using System.Numerics;

namespace Agro;
public partial struct AboveGroundAgent
{
    // A developmental share is a transfer limit, not additional tissue capacity.
    // The caller retains this returned water in the donor or the plant reserve.
    internal double ReleaseWaterAboveCapacity()
    {
        var capacity = MathF.Max(0f, WaterStorageCapacity_g());
        if (Water_g <= capacity) return 0d;
        var excess = (double)Water_g - capacity;
        Water_g = capacity;
        return excess;
    }

    // Ordinary organ-local state: copied by growth transactions and preserved by checkpoints.
    int TreeShootCycle, TreeLeafCycle, TreeFlowerCycle;
    float TreeShootExtension, TreeDevelopmentHours, TreeRipeningHours;
    enum TreeBudRole : byte { Axillary, Terminal, Relay }
    TreeBudRole TreeRole;
    readonly bool TreeIsTerminal => TreeRole != TreeBudRole.Axillary;
    byte TreeOrder;
    bool TreeCrown, TreeBranchDecided, TreeFruitChosen, TreeFruitSetDecided, TreeFruitSetAccepted;
    uint TreePhaseStarted;
    // Gravity-relative inclination belongs to the whole long shoot. Re-sampling it
    // at each internode would let repeated phototropism erase the branching habit.
    float TreeAxisSetPointAngle;
    // Active-season hours of sustained, asymmetric shade. Dormancy preserves
    // the exposure history; it does not itself age this physiological budget.
    float TreeShadedHours;

    internal bool IsTreeShootContinuation(in AboveGroundAgent parent)
        => Organ == OrganTypes.Meristem && TreeIsTerminal && TreeOrder == parent.TreeOrder
            && TreeShootCycle == parent.TreeShootCycle && TreeShootExtension == parent.TreeShootExtension;

    internal void RefundTreeShootExtension(float rejectedLength)
    {
        TreeShootExtension = MathF.Max(0f, TreeShootExtension - rejectedLength);
        // A newborn continuation begins after its support's accepted extension,
        // not after the discarded part of that proposal. Existing internode starts
        // are already below the refunded total and remain unchanged.
        TreeUnitStartProgress = MathF.Min(TreeUnitStartProgress, TreeShootExtension);
    }

    internal void AcceptTreeShootExtension(float acceptedExtension)
    {
        TreeShootExtension = MathF.Max(0f, acceptedExtension);
        TreeUnitStartProgress = MathF.Min(TreeUnitStartProgress, TreeShootExtension);
    }
    /// <summary>Establish a small germinated tree, conserving its supplied reserves.</summary>
    internal static bool TryEstablishWoody(PlantFormation2 plant, float reserve, float water)
    {
        var s = plant.Parameters;
        if (s.Behavior != Behavior.Woody)
            return false;
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, plant.RNG.NextFloat(-MathF.PI, MathF.PI));
        plant.UGBirth(new(plant, plant.World.Timestep, -1, yaw * UnderGroundAgent.OrientationDown, reserve * .35f, initialWater_g: water * .35f, radius: .002f, length: .012f, initialResources: 1f));
        if (plant.UG is VirtualRootsFormation virtualRoots)
            virtualRoots.InitializeWoody(.03f, .04f);
        // Root collar is the model's soil-anchored basal support, not a spreading rhizome.
        var collar = new AboveGroundAgent(plant, -1, OrganTypes.Stem, yaw, reserve * .25f, radius: .003f, length: .001f, rizome: true)
        {
            Water_g = water * .25f,
            WoodFactor = .6f
        };
        var root = plant.AG.Birth(collar);
        var count = s.TreeStemCount;
        for (int i = 0; i < count; i++)
        {
            var tilt = count == 1 ? 0f : .13f;
            var orientation = yaw * OrientationUp * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.Tau * i / count) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -tilt);
            var shoot = new AboveGroundAgent(plant, root, OrganTypes.Meristem, orientation, reserve * .4f / count, radius: .0012f, length: .002f, initialResources: 1f, initialProduction: 1f)
            {
                Water_g = water * .4f / count,
                TreeOrder = 1,
                TreeAxisSetPointAngle = TreeInclination(orientation),
                TreeRole = TreeBudRole.Terminal,
                TreeCrown = s.TreeGraftHeight <= 0f,
                TreeShootCycle = TreeCycle(plant.World),
                TreeBranchDecided = true
            };
            var crownBase = plant.Position + plant.AG.FieldOrigin + Vector3.Transform(Vector3.UnitX, yaw) * collar.Length;
            var groundHeight = plant.WoodyGroundHeight + plant.AG.FieldOrigin.Y;
            if (crownBase.Y < groundHeight)
                shoot.SetEmergenceTarget(groundHeight - crownBase.Y + .005f);
            if (s.TreeMainlineModulesEnabled) plant.AG.BirthInitialTreeModule(shoot);
            else plant.AG.Birth(shoot);
        }

        return true;
    }

    static int TreeCycle(AgroWorld world)
        => TreeCycleAt(world, world.Timestep);

    static int TreeCycleAt(AgroWorld world, uint tick)
    {
        var date = world.GetTime(tick);
        return date.Month >= 3 ? date.Year : date.Year - 1;
    }

    void TickWoody(PlantSubFormation<AboveGroundAgent> formation, int id, uint timestep)
    {
        var plant = formation.Plant;
        var s = plant.Parameters;
        var world = plant.World;
        var hours = world.HoursPerTick;
        var cycle = TreeCycle(world);
        Energy -= LifeSupportPerHour(s) * hours;
        Photosynthesize(formation, id, s, world);
        formation.MarkDevelopmentPhysiology(this);
        if (Organ is OrganTypes.Fruit or OrganTypes.FruitWing or OrganTypes.FlowerBud or OrganTypes.FlowerPadel or OrganTypes.FlowerPedicel or OrganTypes.FlowerStem)
        {
            TickTreeFlower(formation, id, timestep);
            return;
        }

        var season = world.Season;
        var leafSeason = season >= s.TreeBudBreakSeason && season < s.SeasonalSenescence;
        var growSeason = leafSeason && season < s.TreeGrowthEndSeason;
        var thermal = plant.VegetativeTemperatureFactor(timestep);
        var hydrated = Math.Clamp(plant.WaterBalance, 0f, 1f);
        var funded = Energy > EnoughEnergy(s);
        if (Organ is OrganTypes.Leaf or OrganTypes.Petiole)
        {
            if (!leafSeason && SenescenceStart == 0)
                CommitDroughtSenescence(timestep, SenescenceTicks(s.LeafSenescenceSeasonalDuration, hours));
            if (SenescenceStart != 0)
            {
                if (Senescence(timestep) >= 1f)
                    formation.Death(Organ == OrganTypes.Leaf ? LeafUnitRoot(formation, id) : id);
                return;
            }

            if (!funded || GrowthBlocked)
                return;
            var limit = Organ == OrganTypes.Leaf ? new Vector2(s.LeafLength + LengthVar, s.LeafRadius + RadiusVar) : new Vector2(s.PetioleLength + LengthVar, s.PetioleRadius + RadiusVar);
            var current = new Vector2(Length, Radius);
            var growth = limit * GrowthTimeVar * thermal * hydrated;
            growth = Organ == OrganTypes.Leaf ? LimitLeafGrowthToPetioleSupport(formation, Parent, current, limit, growth, new(s.PetioleLength, s.PetioleRadius)) : LimitPetioleGrowthToSupport(current, limit, growth, Parent >= 0 ? formation.GetBaseRadius(Parent) : float.MaxValue);
            var next = Vector2.Min(limit, current + Vector2.Max(Vector2.Zero, growth));
            Length = MathF.Max(Length, next.X);
            Radius = MathF.Max(Radius, next.Y);
            if (Organ == OrganTypes.Leaf && s.TreeLeafDeclinationDegrees > 0f && TreeAxisSetPointAngle > 0f)
                DevelopTreeLeafPose(hours, thermal * hydrated, limit);
            return;
        }

        // Perennial wood survives dormancy and is never replaced by a leaf's abscission.
        // Maintenance shortages are conserved; genuine prolonged starvation kills the axis.
        if (Energy < 0f)
        {
            TreeDevelopmentHours += hours;
            if (TreeDevelopmentHours > 24f * 60f)
                formation.Death(id);
            return;
        }

        TreeDevelopmentHours = 0f;
        if (IsRizome)
        {
            if (growSeason && funded)
                Radius += s.TreeAnnualRadiusGrowth * hours / (24f * 150f) * thermal * hydrated;
            WoodFactor = 1f;
            return;
        }

        var ageYears = plant.GetAge_h() / (24f * 365f);
        var maturity = Math.Clamp(ageYears / s.TreeMatureAgeYears, 0f, 1f);
        if (growSeason && funded && !GrowthBlocked)
        {
            var parentRadius = Parent >= 0 ? formation.GetBaseRadius(Parent) : s.ShootMaximumRadius;
            var increment = s.TreeAnnualRadiusGrowth * (1f - .6f * maturity) * SpeciesSettings.TreeRadiusByOrder(TreeOrder) * hours / (24f * 150f) * thermal * hydrated;
            if (Organ == OrganTypes.Bud)
                parentRadius = MathF.Min(parentRadius, MathF.Max(.002f, s.PetioleRadius * 1.5f));
            Radius = MathF.Max(Radius, MathF.Min(parentRadius, Radius + increment));
            if (Organ == OrganTypes.Stem)
                WoodFactor = MathF.Min(1f, WoodFactor + hours / MathF.Max(1f, s.WoodGrowthTime));
        }

        if (Organ == OrganTypes.Stem)
        {
            formation.TryReissueTreeModule(ref this, id, growSeason && thermal > 0f && hydrated > 0f && funded && !GrowthBlocked);
            return;
        }
        if (Organ == OrganTypes.Bud && s.TreeGrowthUnitsEnabled && TreeRole == TreeBudRole.Relay)
        {
            // The appressed overwintering bud is a different organ from the spent
            // apex. Only its new flushing tissue opens; the bearer never rotates.
            if (TreeShootCycle == cycle || !growSeason || thermal <= 0f || hydrated <= 0f || !funded || GrowthBlocked)
                return;
            Organ = OrganTypes.Meristem;
            TreeShootCycle = cycle;
            BeginTreeGrowthUnit(plant);
            LengthVar = MathF.Max(s.MinimumInternodeLength, s.NodeDistance + plant.RNG.NextFloatVar(s.NodeDistanceVar));
        }
        if (Organ == OrganTypes.Bud)
        {
            // A newly formed axil remains an overwintering capsule. BirthTime
            // distinguishes it from an older spur whose annual branch trial
            // already failed (and also set TreeShootCycle to the current year).
            if (!TreeIsTerminal && (s.TreeGrowthUnitsEnabled || s.TreeAxillaryFlushProbability < 1f)
                && TreeShootCycle == cycle && TreeFlushDecisionCycle != cycle
                && TreeCycleAt(world, BirthTime) == cycle)
                return;
            if (!TreeIsTerminal && s.TreeAxillaryFlushProbability < 1f)
            {
                // A dormant axillary bud either flushes as one whole unit this
                // year or remains closed. The decision precedes spur elongation,
                // flowers and leaves and is retained through contact retries.
                if (TreeFlushDecisionCycle != cycle
                    && (season < MathF.Min(s.FloweringSeasonStart, s.TreeBudBreakSeason)
                        || season >= s.TreeGrowthEndSeason || thermal <= 0f || !funded || GrowthBlocked))
                    return;
                if (!TreeFlushDecision(formation, cycle))
                    return;
            }
            if (!TreeIsTerminal && s.TreeShortShootShadeDiebackDays > 0f)
            {
                // A persistently suppressed spur does not renew indefinitely on
                // carbon from bright parts of its crown. Retire at the next spring
                // renewal, preserving the current year's fruit and normal leaf fall.
                if (TreeShadedHours >= 24f * s.TreeShortShootShadeDiebackDays
                    && TreeLeafCycle != cycle
                    && season >= MathF.Min(s.FloweringSeasonStart, s.TreeBudBreakSeason))
                {
                    formation.Death(id);
                    return;
                }

                if (season >= s.TreeBudBreakSeason + .16f && leafSeason
                    && TreeLeafCycle == cycle && TreeFoliageObserved(formation, id, timestep))
                {
                    // Woody establishment has one permanent root collar at index0.
                    // Its daily leaf mean provides the crown's common light context.
                    // Uniform whole-tree shade must not look like a branch losing
                    // competition to brighter parts of the same tree.
                    var crownLight = formation.GetDailyResourcesInv(0);
                    var threshold = MathF.Min(s.TreeShortShootShadeLightFraction * s.VegetativeGrowthLightReferenceWhM2,
                        s.TreeShortShootShadeRelativeFraction * crownLight);
                    var previousShadeHours = TreeShadedHours;
                    TreeShadedHours = threshold > 0f && PreviousDayEnvResourcesInvariant < threshold
                        ? TreeShadedHours + hours : MathF.Max(0f, TreeShadedHours - hours);
                    if (TreeShadedHours != previousShadeHours)
                        formation.MarkDevelopmentPhysiology(this);
                }
            }
            // A perennial short shoot carries several compressed nodes. Expand its support
            // before admitting leaves or flowers, so they do not emerge inside older wood.
            if (!TreeIsTerminal && season >= MathF.Min(s.FloweringSeasonStart, s.TreeBudBreakSeason) && season < s.TreeGrowthEndSeason && funded && !GrowthBlocked)
                Length = MathF.Min(s.TreeShortShootLength, Length + s.TreeShortShootLength * hours / (24f * 18f) * thermal * hydrated);
            var spurReady = TreeIsTerminal || Length >= s.TreeShortShootLength * .99f;
            var exposedSpur = TreeIsTerminal || Parent < 0 || formation.GetBaseRadius(Parent) < Length;
            var budAgeYears = (timestep - BirthTime) * hours / (24f * 365f);
            if (TreeCrown && spurReady && exposedSpur && budAgeYears >= .5f && TreeFlowerCycle != cycle && season >= s.FloweringSeasonStart && season <= s.FloweringSeasonEnd && (s.TreeGraftHeight > 0f || ageYears >= s.TreeFloweringAgeYears) && funded && !GrowthBlocked && !formation.HasPendingDevelopment(id))
            {
                CreateTreeFlowers(formation, id, cycle);
                return;
            }

            if (!growSeason || thermal <= 0f || !funded || GrowthBlocked)
                return;
            if (TreeLeafCycle != cycle && spurReady && exposedSpur && TreeFoliageCanEmerge(formation, id))
            {
                TreeLeafCycle = cycle;
                CreateTreeLeaves(formation, id);
            }

            if (TreeShootCycle == cycle)
                return;
            // Decide after renewed foliage has supplied a measured exposure. Sampling on
            // the first leafless spring tick otherwise systematically treats every bud
            // as shaded, even under constant ambient light.
            var branchLight = Math.Clamp(PreviousDayEnvResourcesInvariant / MathF.Max(1f, s.VegetativeGrowthLightReferenceWhM2), 0f, 1f);
            if (!TreeIsTerminal && (!spurReady || TreeLeafCycle != cycle || branchLight <= .05f || !TreeFoliageObserved(formation, id, timestep)))
                return;
            TreeShootCycle = cycle;
            TreeShootExtension = 0f;
            if (!TreeIsTerminal)
            {
                // A failed flush does not disable a dormant bud for its entire lifetime.
                // The annual trial fades on older wood; shaded buds retain their cheaper
                // short-shoot foliage without producing a costly long extension.
                var probability = s.TreeBranchProbability * MathF.Exp(-budAgeYears / 3f) / MathF.Sqrt(Math.Max(1, (int)TreeOrder));
                if (TreeOrder >= s.TreeMaximumBranchOrder || !exposedSpur || plant.RNG.NextFloat() > probability * branchLight)
                {
                    // Saskatoon short shoots can themselves branch into clusters. A
                    // daughter starts at a leaf axil and renews foliage next spring;
                    // retaining the parent spur avoids replacing foliage with a long axis.
                    // One woody daughter per spur bounds this to a chain rather than an
                    // exponentially branching new crown. The annual rate is provisional.
                    if (s.TreeShortShootBranchProbability > 0f && exposedSpur
                        && !TreeHasWoodyDaughter(formation, id)
                        && plant.RNG.NextFloat() < s.TreeShortShootBranchProbability * branchLight)
                        CreateTreeShortShoot(formation, id, cycle);
                    return;
                }
                TreeBranchDecided = true;
            }

            Organ = OrganTypes.Meristem;
            LengthVar = MathF.Max(s.MinimumInternodeLength, s.NodeDistance + plant.RNG.NextFloatVar(s.NodeDistanceVar));
            if (!TreeIsTerminal)
                TreeOrder++;
            if (!TreeIsTerminal)
                TreeRole = TreeBudRole.Terminal;
            if (s.TreeGrowthUnitsEnabled)
                BeginTreeGrowthUnit(plant);
        }

        if (Organ != OrganTypes.Meristem)
            return;
        if (TreeShootCycle != cycle)
        {
            if (s.TreeSympodialRenewal && TreeShootExtension > 0f)
            {
                CloseTreeGrowthUnit(formation, id, cycle - 1);
                return;
            }
            TreeShootCycle = cycle;
            TreeShootExtension = 0f;
            if (s.TreeGrowthUnitsEnabled)
            {
                if (s.TreeSympodialRenewal)
                    TreeShootAllowance = TreeUnitStartProgress = 0f;
                else
                    BeginTreeGrowthUnit(plant);
            }
        }

        if (!growSeason)
        {
            if (season >= s.TreeGrowthEndSeason)
            {
                if (s.TreeSympodialRenewal)
                {
                    CloseTreeGrowthUnit(formation, id, cycle);
                    return;
                }
                CloseTreeTerminalShoot(formation, id, cycle);
            }

            return;
        }

        if (!funded || GrowthBlocked || thermal <= 0f)
            return;
        var renewFoliage = TreeLeafCycle != cycle && TreeFoliageCanEmerge(formation, id);
        if (renewFoliage && !s.TreeGrowthUnitsEnabled)
        {
            TreeLeafCycle = cycle;
            CreateTreeLeaves(formation, id);
        }

        var crownBaseHeight = s.TreeGraftHeight;
        var basePosition = formation.GetBaseCenter(id);
        var baseHeight = basePosition.Y - plant.WoodyGroundHeight;
        var release = TreeCrown ? s.TreeApicalControlRelease(ageYears) : 0f;
        var annual = (TreeCrown ? s.TreeAnnualExtension : MathF.Max(.8f, s.TreeAnnualExtension))
            * (1f - .8f * maturity) * s.TreeExtensionByOrder(TreeOrder, release);
        if (s.TreeGrowthUnitsEnabled)
        {
            if (TreeShootAllowance <= 0f)
                BeginTreeGrowthUnit(plant);
            annual = TreeShootAllowance;
        }
        if (TreeShootExtension >= annual)
        {
            if (s.TreeSympodialRenewal)
            {
                CloseTreeGrowthUnit(formation, id, cycle);
                return;
            }
            CloseTreeTerminalShoot(formation, id, cycle);
            return;
        }

        // Reserve-supported flushing bridges spring and initial primary-axis emergence.
        // A slow seedling may still be below its first leaf attachment when the spring
        // interval ends. Keep its funded emergence moving until that attachment clears
        // soil; established shoots still require measured exposure afterwards.
        var light = Math.Clamp(PreviousDayEnvResourcesInvariant / MathF.Max(1f, s.VegetativeGrowthLightReferenceWhM2), 0f, 1f);
        var emerging = TreeOrder == 1 && TreeIsTerminal && TreeLeafCycle == 0
            && !TreeFoliageCanEmerge(formation, id);
        var opening = emerging || season < s.TreeBudBreakSeason + .055f || plant.GetAge_h() < 24f * 25f;
        var exposure = opening ? MathF.Max(.35f, light) : light;
        var incrementLength = annual * hours / (24f * 105f) * thermal * hydrated * exposure * TreeEnvelopeGrowthFactor(plant, basePosition);
        var target = LengthVar;
        var verticalDirection = Vector3.Transform(Vector3.UnitX, Orientation).Y;
        if (!TreeCrown && verticalDirection > 1e-6f)
            target = MathF.Min(target, MathF.Max(.001f, (crownBaseHeight - baseHeight) / verticalDirection));
        var delta = MathF.Max(0f, MathF.Min(MathF.Min(incrementLength, target - Length), annual - TreeShootExtension));
        var lightStartLength = Length;
        var lightStartExtension = TreeShootExtension;
        Length += delta;
        TreeShootExtension += delta;
        if (s.TreeGrowthUnitsEnabled && delta > 0f)
        {
            var module = formation.TreeModuleStepFor(this);
            var lightStep = AdvanceTreeLightResponse(s, lightStartExtension,
                TreeShootExtension, formation.TreeUnitLightCue(id), module);
            // A new unit can open from an appressed relay or last year's
            // retained apex. Zero accepted extension must retain that old pose.
            var unitOpening = lightStartExtension <= 0f
                ? TreeRelayOpeningTurn(s, null, module) : Quaternion.Identity;
            formation.RecordTreeLightStep(lightStep, unitOpening, lightStartLength, module,
                CorrectsOrdinaryLateralUnitTrial(s) ? CaptureTreeUnitFrame() : default);
            if (module.IsDorsiventral)
                TurnTreeModuleMaterial(s, lightStartExtension, TreeShootExtension, lightStep, module);
            else TurnTreeUnitRestFrame(formation.TreeUnitGrowthTarget(id, this));
        }
        if (renewFoliage && s.TreeGrowthUnitsEnabled)
        {
            // Newborns are captured in their final parent frame. The normal
            // subtree transport below Tick carries committed descendants only.
            TreeLeafCycle = cycle;
            CreateTreeLeaves(formation, id);
        }
        if (s.TreeSympodialRenewal && TreeShootExtension >= annual - 1e-6f)
        {
            // A real module closes on the next tick, after this increment and
            // its actual leaf axil have passed normal funding/contact/Census.
            if (TreeModuleRootStableId != 0) return;
            CloseTreeGrowthUnit(formation, id, cycle);
            return;
        }
        if (Length + 1e-6f < target)
            return;
        Organ = OrganTypes.Stem;
        WoodFactor = MathF.Max(WoodFactor, .15f);
        if (!TreeCrown && baseHeight + Length * verticalDirection >= crownBaseHeight - .002f)
        {
            TreeCrown = true;
            CreateGraftedCrown(formation, id, cycle);
            return;
        }

        CreateTreeNodeBuds(formation, id);
        var continuationModule = formation.TreeModuleStepFor(this);
        var direction = continuationModule.IsDorsiventral
            ? TreeModuleProgramFrame(s, TreeShootExtension, CaptureTreeLightMaterial(), continuationModule, atTip: true)
            : s.TreeGrowthUnitsEnabled
            ? TreeFrameToward(baseOrientation, formation.TreeUnitGrowthTarget(id, this, atTip: true))
            : TreeCrown ? RandomTreeOrientation(plant, Orientation, TreeOrder) : OrientationUp;
        var child = new AboveGroundAgent(plant, id, OrganTypes.Meristem, direction, Energy * .12f, radius: MathF.Min(Radius, .0012f), length: .0002f, initialResources: MathF.Max(PreviousDayEnvResourcesInvariant, formation.DailyResourceMax * .25f), initialProduction: MathF.Max(PreviousDayProductionInvariant, formation.DailyProductionMax * .25f))
        {
            Water_g = Water_g * .12f,
            TreeOrder = TreeOrder,
            TreeAxisSetPointAngle = TreeAxisSetPointAngle,
            TreeCrown = TreeCrown,
            TreeRole = TreeRole,
            TreeShootCycle = cycle,
            TreeShootExtension = TreeShootExtension,
            TreeModuleRootStableId = TreeModuleRootStableId,
            LateralAngle = LateralAngle + s.LateralRoll
        };
        if (s.TreeGrowthUnitsEnabled)
        {
            child.TreeShootAllowance = TreeShootAllowance;
            child.TreeUnitStartProgress = TreeShootExtension;
            child.TreeUnitReferenceLocal = Quaternion.Normalize(Quaternion.Conjugate(direction) * TreeUnitReferenceFrame());
            child.InheritTreeUnitLight(this);
        }
        formation.Birth(child);
        Energy *= .88f;
        Water_g *= .88f;
    }

    void CloseTreeTerminalShoot(PlantSubFormation<AboveGroundAgent> formation, int id, int cycle)
    {
        if (GrowthBlocked || formation.HasPendingDevelopment(id)) return;
        var plant = formation.Plant;
        // The elongating organ includes its accepted internode, not just the
        // apical cells. Dormancy retains that wood and its existing leaf pair;
        // replacing the whole organ with a bud hides and shrinks the shoot.
        var bud = new AboveGroundAgent(plant, id, OrganTypes.Bud, Orientation,
            Energy * .12f, radius: MathF.Min(Radius, .001f), length: .002f,
            initialResources: MathF.Max(PreviousDayEnvResourcesInvariant, formation.DailyResourceMax * .25f),
            initialProduction: MathF.Max(PreviousDayProductionInvariant, formation.DailyProductionMax * .25f))
        {
            Water_g = Water_g * .12f,
            TreeOrder = TreeOrder,
            TreeRole = TreeBudRole.Terminal,
            TreeCrown = TreeCrown,
            TreeShootCycle = cycle,
            TreeLeafCycle = cycle,
            TreeAxisSetPointAngle = TreeAxisSetPointAngle,
            TreeUnitReferenceLocal = Quaternion.Normalize(Quaternion.Conjugate(Orientation) * TreeUnitReferenceFrame()),
            LateralAngle = LateralAngle,
        };
        bud.InheritTreeUnitLight(this, newUnit: true);
        Organ = OrganTypes.Stem;
        WoodFactor = MathF.Max(WoodFactor, .15f);
        formation.Birth(bud);
        Energy *= .88f;
        Water_g *= .88f;
    }

    static bool TreeFoliageObserved(PlantSubFormation<AboveGroundAgent> formation, int id, uint timestep)
    {
        // Birth-time resource estimates keep new leaves funded, but are not measured
        // local light. After two daily boundaries at least one complete day's exposure
        // has replaced them. Only this short shoot's bounded leaf units are inspected.
        var species = formation.Plant.Parameters;
        var observationTicks = 2u * formation.Plant.World.TicksPerDay;
        foreach (var child in formation.GetChildren(id))
        {
            if (formation.GetOrgan(child) != OrganTypes.Petiole)
                continue;
            foreach (var leaf in formation.GetChildren(child))
                if (formation.GetOrgan(leaf) == OrganTypes.Leaf)
                {
                    ref readonly var blade = ref formation.GetAgent(leaf);
                    // Tiny spring leaves see an almost empty crown. A measured day
                    // at that stage is not the exposure of the developed canopy.
                    // Primary terminals still flush from reserves; an axillary
                    // extension waits for actual, substantially expanded foliage.
                    if (timestep - blade.BirthTime >= observationTicks
                        && blade.Length >= .8f * MathF.Max(.0005f, species.LeafLength + blade.LengthVar)
                        && blade.Radius >= .8f * MathF.Max(.0003f, species.LeafRadius + blade.RadiusVar))
                        return true;
                }
        }

        return false;
    }

    void CreateGraftedCrown(PlantSubFormation<AboveGroundAgent> formation, int id, int cycle)
    {
        var plant = formation.Plant;
        // A standard's rootstock stops at the graft. The mature scion makes several
        // co-dominant framework axes; retaining the stock's leader makes a cone.
        const int leaders = 4;
        var phase = plant.RNG.NextFloat(-MathF.PI, MathF.PI);
        for (int j = 0; j < leaders; j++)
        {
            var orientation = OrientationUp * Quaternion.CreateFromAxisAngle(Vector3.UnitX, phase + MathF.Tau * j / leaders) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -.65f);
            formation.Birth(new AboveGroundAgent(plant, id, OrganTypes.Meristem, orientation, Energy * .12f, radius: MathF.Min(Radius, .0012f), length: .0002f, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax)
            {
                Water_g = Water_g * .12f,
                TreeOrder = 1,
                TreeAxisSetPointAngle = TreeInclination(orientation),
                TreeCrown = true,
                TreeRole = TreeBudRole.Terminal,
                TreeShootCycle = cycle,
                LateralAngle = plant.Parameters.TreeDorsiventralShoots ? 0f : plant.RNG.NextFloat(-MathF.PI, MathF.PI)
            });
            Energy *= .88f;
            Water_g *= .88f;
        }
    }

    static float TreeEnvelopeGrowthFactor(PlantFormation2 plant, Vector3 tip)
    {
        var s = plant.Parameters;
        var p = tip - plant.Position;
        p.Y = tip.Y - plant.WoodyGroundHeight;
        var radius = s.TreeCrownRadius;
        if (s.TreeCrownHabit == TreeCrownHabit.Globe && p.Y > s.TreeGraftHeight)
        {
            var half = (s.Height - s.TreeGraftHeight) * .5f;
            var y = (p.Y - s.TreeGraftHeight - half) / MathF.Max(.01f, half);
            radius *= MathF.Sqrt(MathF.Max(.08f, 1f - y * y));
        }

        // Typical dimensions are calibration envelopes, not clipping planes. Extension
        // gradually slows with height and crown reach; no organ is deleted or shortened.
        var reach = (p.X * p.X + p.Z * p.Z) / (radius * radius);
        var height = MathF.Max(0, p.Y) / s.Height;
        return 1f / (1f + reach * reach * reach + MathF.Pow(height, 8));
    }

    static Quaternion RandomTreeOrientation(PlantFormation2 plant, Quaternion orientation, int order)
    {
        var variation = plant.Parameters.TreeCrownHabit == TreeCrownHabit.Columnar ? .025f : .06f;
        return Quaternion.Normalize(orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, plant.RNG.NextFloatVar(variation)) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, plant.RNG.NextFloatVar(variation)));
    }

    static float TreeInclination(Quaternion orientation)
        => MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitX, orientation).Y, -1f, 1f));

    internal readonly Vector3 WoodyGrowthLightTarget(Vector3 light, float phototropicWeight)
    {
        var lightInclination = MathF.Acos(Math.Clamp(light.Y, -1f, 1f));
        var inclination = TreeAxisSetPointAngle + (lightInclination - TreeAxisSetPointAngle) * phototropicWeight;
        var current = Vector3.Transform(Vector3.UnitX, Orientation);
        var horizontal = phototropicWeight > 0f ? new Vector3(light.X, 0f, light.Z) : Vector3.Zero;
        // An open sky has no meaningful azimuth. Preserve the shoot's own heading
        // instead of amplifying near-zero floating-point horizontal components.
        if (horizontal.LengthSquared() < 1e-6f)
            horizontal = new Vector3(current.X, 0f, current.Z);
        if (horizontal.LengthSquared() < 1e-8f)
        {
            var across = Vector3.Transform(Vector3.UnitZ, Orientation);
            horizontal = new Vector3(across.X, 0f, across.Z);
        }
        if (horizontal.LengthSquared() < 1e-8f)
            horizontal = Vector3.UnitX;
        return Vector3.Normalize(horizontal) * MathF.Sin(inclination) + Vector3.UnitY * MathF.Cos(inclination);
    }

    bool TreeFoliageCanEmerge(PlantSubFormation<AboveGroundAgent> formation, int id)
        // Germination starts below soil. A petiole born there can pin its support at the
        // surface during solid certification. This also covers primary-axis continuation
        // nodes in deeper planting, rather than checking only the collar's direct child.
        => formation.GetTipPosition(id).Y >= formation.Plant.WoodyGroundHeight + .005f;

    void DevelopTreeLeafPose(float hours, float activity, Vector2 dimensions)
    {
        // A young blade starts in its emergence frame. Its biological rest pose
        // approaches the pendent mature inclination as the blade expands. This
        // moves the rest frame, while retaining the elastic offset and the
        // petiole's azimuth. The sampled inclination uses the existing persisted
        // set-point field (unused by leaves on the original path).
        var expansion = Math.Clamp(MathF.Min(Length / MathF.Max(.0005f, dimensions.X),
            Radius / MathF.Max(.0003f, dimensions.Y)), 0f, 1f);
        var response = 1f - MathF.Pow(.98f, hours * MathF.Max(0f, activity) * expansion);
        if (response <= 0f) return;
        var target = GravitropicSetPointOrientation(baseOrientation, TreeAxisSetPointAngle);
        if (target == baseOrientation) return;
        var next = Quaternion.Normalize(Quaternion.Slerp(baseOrientation, target, response));
        var carry = Quaternion.Normalize(next * Quaternion.Conjugate(baseOrientation));
        baseOrientation = next;
        restOrientation = Quaternion.Normalize(carry * restOrientation);
        targetOrientation = Quaternion.Normalize(carry * targetOrientation);
        SetOrientation(Quaternion.Normalize(carry * Orientation));
    }

    void CreateTreeLeaves(PlantSubFormation<AboveGroundAgent> formation, int id)
    {
        var plant = formation.Plant;
        var s = plant.Parameters;
        var height = formation.GetBaseCenter(id).Y - plant.WoodyGroundHeight;
        var crownHeight = formation.Height + plant.Position.Y - plant.WoodyGroundHeight;
        if (TreeCrown && crownHeight > s.TreeClearTrunkHeight + .25f && plant.GetAge_h() > 24f * 365f * 3f && height < s.TreeClearTrunkHeight - .1f && TreeOrder == 1)
            return;
        var nodes = TreeIsTerminal ? 1 : s.TreeShortShootNodes;
        var count = nodes * s.LateralsPerNode;
        for (int j = 0; j < count; j++)
        {
            var moduleDorsiventral = formation.IsDorsiventralModule(this);
            var node = j / s.LateralsPerNode;
            var roll = TreeRankRoll(s, node, j % s.LateralsPerNode, moduleDorsiventral);
            var direction = TreeAxillaryFrame(s, roll, s.LateralPitch, moduleDorsiventral);
            var share = Energy * .035f;
            var shareWater = Water_g * .035f;
            var petiole = new AboveGroundAgent(plant, id, OrganTypes.Petiole, direction, share, radius: MathF.Min(Radius, .0005f), length: .0002f, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax)
            {
                Water_g = shareWater,
                TreeOrder = TreeOrder,
                BaseOffset = Vector3.Transform(Vector3.UnitX, Orientation) * (Length * ((node + 1f) / nodes - 1f))
            };
            var petioleId = formation.Birth(petiole);
            var leaf = new AboveGroundAgent(plant, petioleId, OrganTypes.Leaf, direction * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -s.LeafPitch), share, radius: .0003f, length: .0005f, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax)
            {
                Water_g = shareWater,
                TreeOrder = TreeOrder,
                TreeAxisSetPointAngle = s.TreeLeafDeclinationDegrees > 0f
                    ? s.TreeLeafInclinationRadians + (s.TreeLeafInclinationVariationRadians > 0f
                        ? plant.RNG.NextFloatVar(s.TreeLeafInclinationVariationRadians) : 0f) : 0f
            };
            formation.Birth(leaf);
            Energy -= 2 * share;
            Water_g -= 2 * shareWater;
        }
    }

    static bool TreeHasWoodyDaughter(PlantSubFormation<AboveGroundAgent> formation, int id)
    {
        foreach (var child in formation.GetChildren(id))
            if (formation.GetOrgan(child) is OrganTypes.Bud or OrganTypes.Meristem or OrganTypes.Stem)
                return true;
        return false;
    }

    void CreateTreeShortShoot(PlantSubFormation<AboveGroundAgent> formation, int id, int cycle)
    {
        var plant = formation.Plant;
        var s = plant.Parameters;
        // BaseOffset is a world-space offset from the support tip, already used by
        // its leaf petioles. Use an existing leaf axil, not a new unmodelled support.
        var axil = -1;
        var distalOffset = float.NegativeInfinity;
        var axis = Vector3.Transform(Vector3.UnitX, Orientation);
        foreach (var child in formation.GetChildren(id))
        {
            if (formation.GetOrgan(child) != OrganTypes.Petiole)
                continue;
            var offset = Vector3.Dot(formation.GetAgent(child).BaseOffset, axis);
            if (offset > distalOffset)
            {
                distalOffset = offset;
                axil = child;
            }
        }
        if (axil >= 0)
        {
            // The distal existing axil stays outside older supporting wood. Reuse
            // its direction so alternate ranks and opposite pairs remain coherent.
            var direction = formation.GetAgent(axil).Orientation;
            if (s.TreeCrownHabit == TreeCrownHabit.Columnar)
                direction = Quaternion.Slerp(direction, PlantFormation2.AdjustUpBase(direction, true), .5f);
            // A new ordinary daughter establishes its dorsal plane once. Its
            // heading/axil remain unchanged; established sprays keep their frame.
            if (s.TreeDorsiventralShoots)
                direction = TurnUpwards(direction);
            var bud = new AboveGroundAgent(plant, id, OrganTypes.Bud, direction, Energy * .025f, radius: MathF.Min(.001f, Radius), length: .002f, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax)
            {
                Water_g = Water_g * .025f,
                TreeOrder = TreeOrder,
                TreeAxisSetPointAngle = TreeInclination(direction),
                TreeCrown = true,
                TreeShootCycle = cycle,
                TreeLeafCycle = cycle,
                BaseOffset = formation.GetAgent(axil).BaseOffset,
                LateralAngle = s.TreeDorsiventralShoots ? 0f : plant.RNG.NextFloat(-MathF.PI, MathF.PI),
                WoodFactor = .5f
            };
            formation.Birth(bud);
            Energy *= .975f;
            Water_g *= .975f;
            return;
        }
    }

    void CreateTreeNodeBuds(PlantSubFormation<AboveGroundAgent> formation, int id)
    {
        if (!TreeCrown)
            return;
        var plant = formation.Plant;
        var s = plant.Parameters;
        var height = formation.GetTipPosition(id).Y - plant.WoodyGroundHeight;
        if (TreeOrder == 1 && height < s.TreeClearTrunkHeight)
            return;
        for (int j = 0; j < s.LateralsPerNode; j++)
        {
            var moduleDorsiventral = formation.IsDorsiventralModule(this);
            var pitch = TreeOrder == 1 ? s.TreeFrameworkPitch(s.TreeApicalControlRelease(plant.GetAge_h() / (24f * 365f))) : s.LateralPitch;
            var direction = TreeAxillaryFrame(s, TreeRankRoll(s, 0, j, moduleDorsiventral), pitch, moduleDorsiventral);
            if (s.TreeCrownHabit == TreeCrownHabit.Columnar)
                direction = Quaternion.Slerp(direction, PlantFormation2.AdjustUpBase(direction, true), .5f);
            if (s.TreeDorsiventralShoots && (moduleDorsiventral
                || !(TreeOrder == 1 && TreeIsTerminal && s.TreeCrownHabit != TreeCrownHabit.Columnar)))
                direction = TurnUpwards(direction);
            var bud = new AboveGroundAgent(plant, id, OrganTypes.Bud, direction, Energy * .025f, radius: MathF.Min(.001f, Radius), length: .002f, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax)
            {
                Water_g = Water_g * .025f,
                TreeOrder = TreeOrder,
                TreeAxisSetPointAngle = TreeInclination(direction),
                TreeCrown = true,
                TreeShootCycle = TreeCycle(plant.World),
                TreeLeafCycle = TreeCycle(plant.World),
                // The two ranks/opposite pairs remain local to this axis. Distinct
                // daughter-axis phases prevent the entire tree becoming one plane.
                LateralAngle = s.TreeDorsiventralShoots ? 0f : plant.RNG.NextFloat(-MathF.PI, MathF.PI),
                WoodFactor = .5f
            };
            formation.Birth(bud);
            Energy *= .975f;
            Water_g *= .975f;
        }
    }

    void CreateTreeFlowers(PlantSubFormation<AboveGroundAgent> formation, int id, int cycle)
    {
        var plant = formation.Plant;
        var s = plant.Parameters;
        TreeFlowerCycle = cycle;
        // Only a subset of eligible short shoots carry a cluster; the draw occurs once a year.
        if (plant.RNG.NextFloat() > .28f)
            return;
        // A deterministic site allocation avoids another probabilistic draw. The one-third
        // male share is a model coefficient, not a measured sex ratio for Celtis.
        var male = s.TreeMaleFlowersPerCluster > 0 && (StableId + (ulong)cycle) % 3 == 0;
        var count = male ? s.TreeMaleFlowersPerCluster : s.TreeFlowersPerCluster;
        var solitary = count == 1;
        var raceme = s.FruitSettings?.FruitType == FruitType.Pome;
        var axisLength = male ? .012f : raceme ? .035f : .025f;
        var axisId = id;
        // Keep emerging flower centres separated even before the axis elongates; the
        // certified contact engine cannot admit several coincident embryonic buds.
        var initialAxisLength = MathF.Min(axisLength, .0005f + count * .0006f);
        if (!solitary)
        {
            var axis = new AboveGroundAgent(plant, id, OrganTypes.FlowerStem, Orientation, Energy * .08f, radius: .0003f, length: initialAxisLength)
            {
                Water_g = Water_g * .08f,
                TreePhaseStarted = plant.World.Timestep,
                TreeCrown = true,
                flowerSupport = true,
                LengthVar = axisLength,
                RadiusVar = .0009f,
                GrowthTimeVar = plant.World.HoursPerTick / s.FlowerSettings.FlowerStemSegmentGrowthDurationHours,
                WoodFactor = 0f
            };
            axisId = formation.Birth(axis);
            Energy *= .92f;
            Water_g *= .92f;
        }

        for (int j = 0; j < count; j++)
        {
            var inclination = solitary ? .35f : raceme ? .7f : .8f;
            var direction = Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.Tau * j / count) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -inclination);
            var attachment = solitary ? 1f : .2f + .8f * j / Math.Max(1, count - 1);
            var pedicelLength = solitary || raceme ? s.FlowerSettings.FlowerPedicelLength : male ? .006f : (axisLength * (1 - attachment) + .012f) / MathF.Cos(inclination);
            var pedicel = new AboveGroundAgent(plant, axisId, OrganTypes.FlowerPedicel, direction, Energy * .015f, radius: .0002f, length: .0006f)
            {
                Water_g = Water_g * .015f,
                TreePhaseStarted = plant.World.Timestep,
                LengthVar = pedicelLength,
                RadiusVar = s.FlowerSettings.FlowerPedicelRadius,
                GrowthTimeVar = plant.World.HoursPerTick / s.FlowerSettings.PetalAndPedicelGrowthDurationHours,
                LateralAngle = attachment,
                flowerSupport = true
            };
            // Erect racemes distribute flowers along the axis; corymbs hold their heads near a plane.
            pedicel.BaseOffset = Vector3.Transform(Vector3.UnitX, Orientation) * (initialAxisLength * (attachment - 1f));
            var pedicelId = formation.Birth(pedicel);
            Energy *= .985f;
            Water_g *= .985f;
            var bud = new AboveGroundAgent(plant, pedicelId, OrganTypes.FlowerBud, direction, Energy * .015f, radius: .0002f, length: .0003f)
            {
                Water_g = Water_g * .015f,
                TreePhaseStarted = plant.World.Timestep,
                TreeFruitSetDecided = male,
                TreeFruitSetAccepted = false,
                flowerSupport = true
            };
            formation.Birth(bud);
            Energy *= .985f;
            Water_g *= .985f;
        }
    }

    void TickTreeFlower(PlantSubFormation<AboveGroundAgent> formation, int id, uint timestep)
    {
        var plant = formation.Plant;
        var s = plant.Parameters;
        var f = s.FlowerSettings;
        var fruit = s.FruitSettings;
        var age = (timestep - TreePhaseStarted) * plant.World.HoursPerTick;
        var hydrated = Math.Clamp(plant.WaterBalance, 0f, 1f);
        var thermal = plant.VegetativeTemperatureFactor(timestep);
        var exposure = Math.Clamp(MathF.Max(PreviousDayEnvResourcesInvariant, formation.DailyResourceMax) / MathF.Max(1, s.VegetativeGrowthLightReferenceWhM2), 0f, 1f);
        // Stored reserves can fund the pre-leaf spring blossom, but no new energy is created.
        if (plant.World.Season < s.TreeBudBreakSeason + .055f)
            exposure = MathF.Max(.35f, exposure);
        var development = !GrowthBlocked && Energy > EnoughEnergy(s) ? thermal * hydrated * exposure : 0f;
        if (Organ is OrganTypes.FlowerStem or OrganTypes.FlowerPedicel)
        {
            if (Organ == OrganTypes.FlowerPedicel && Parent >= 0 && formation.GetOrgan(Parent) == OrganTypes.FlowerStem)
                BaseOffset = Vector3.Transform(Vector3.UnitX, formation.GetDirection(Parent)) * (formation.GetLength(Parent) * (LateralAngle - 1f));
            if (development > 0f)
            {
                // The shared helper already applies water to pedicels; stems historically
                // do not, so add it here exactly once for the woody support path.
                var environment = thermal * exposure * (Organ == OrganTypes.FlowerStem ? hydrated : 1f);
                new FlowerHelper(f).grow(ref this, id, formation, environment);
            }

            // The stalk stays while any flower or fruit needs it, including a fruit delayed
            // by drought. Child shedding removes supports from the tip back to the twig.
            if (formation.GetChildren(id).Count == 0 && !formation.HasPendingDevelopment(id))
                formation.Death(id);
            return;
        }

        if (Organ == OrganTypes.FlowerPadel)
        {
            if (age > f.FlowerMaximumAgeHours)
            {
                formation.Death(id);
                return;
            }

            if (development > 0f)
            {
                var grow = plant.World.HoursPerTick / MathF.Max(1, f.PetalAndPedicelGrowthDurationHours) * development;
                Length = MathF.Min(f.PetalLength, Length + f.PetalLength * grow);
                Radius = MathF.Min(f.PetalRadius, Radius + f.PetalRadius * grow);
            }

            return;
        }

        if (Organ == OrganTypes.FlowerBud)
        {
            if (!TreeFruitChosen)
            {
                var allowance = (f.BudBloomDurationHours + f.FlowerMaximumAgeHours) / (365f * 24f);
                var created = plant.World.GetTime(TreePhaseStarted);
                var createdCycle = created.Month >= 3 ? created.Year : created.Year - 1;
                if (createdCycle != TreeCycle(plant.World) || plant.World.Season > s.FloweringSeasonEnd + allowance)
                {
                    formation.Death(id);
                    return;
                }
            }

            if (!TreeFruitChosen && development > 0f)
            {
                var grow = plant.World.HoursPerTick / MathF.Max(1, f.BudBloomDurationHours) * development;
                Length = MathF.Min(f.BudLength, Length + f.BudLength * grow);
                Radius = MathF.Min(f.BudRadius, Radius + f.BudRadius * grow);
            }

            if (!TreeFruitChosen && Length >= f.BudLength * .99f && Radius >= f.BudRadius * .99f && development > 0f && TreeFlowerSupportReady(formation))
            {
                TreeFruitChosen = true;
                TreePhaseStarted = timestep;
                for (int p = 0; p < f.PetalCount; p++)
                {
                    var orientation = Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.Tau * p / f.PetalCount) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI * .5f);
                    formation.Birth(new AboveGroundAgent(plant, id, OrganTypes.FlowerPadel, orientation, Energy * .04f, radius: .0002f, length: .0003f) { Water_g = Water_g * .04f, TreePhaseStarted = timestep });
                    Energy *= .96f;
                    Water_g *= .96f;
                }

                return;
            }

            if (!TreeFruitChosen)
                return;
            if (age < f.FlowerMaximumAgeHours || development <= 0f)
                return;
            if (fruit == null || !fruit.Enabled)
            {
                formation.Death(id);
                return;
            }

            if (!TreeFruitSetDecided)
            {
                TreeFruitSetDecided = true;
                TreeFruitSetAccepted = plant.RNG.NextFloat() <= fruit.FruitPerFlowerProbability;
            }

            if (!TreeFruitSetAccepted)
            {
                formation.Death(id);
                return;
            }

            Organ = OrganTypes.Fruit;
            TreePhaseStarted = timestep;
            TreeDevelopmentHours = 0f;
            Length = fruit.InitialLength;
            Radius = fruit.InitialRadius;
            Color = fruit.UnripeColor;
            if (fruit.FruitsPerFlower == 2)
            {
                // A maple schizocarp comprises two nutlets, each carrying its own wing.
                var pairOrientation = Orientation;
                var across = Vector3.Transform(Vector3.UnitZ, pairOrientation) * fruit.MatureRadius;
                var secondEnergy = Energy * .4f;
                var secondWater = Water_g * .4f;
                var retained = MathF.Pow(.95f, fruit.WingsPerFruit);
                var second = new AboveGroundAgent(plant, Parent, OrganTypes.Fruit, pairOrientation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI), secondEnergy * retained, radius: fruit.InitialRadius, length: fruit.InitialLength)
                {
                    Water_g = secondWater * retained,
                    TreePhaseStarted = timestep,
                    Color = fruit.UnripeColor,
                    BaseOffset = -across
                };
                var secondId = formation.Birth(second);
                Energy *= .6f;
                Water_g *= .6f;
                second.Energy = secondEnergy;
                second.Water_g = secondWater;
                second.CreateTreeFruitWings(formation, secondId);
                BaseOffset = across;
            }

            CreateTreeFruitWings(formation, id);
            return;
        }

        if (fruit == null || !fruit.Enabled)
        {
            formation.Death(id);
            return;
        }

        // Fruit and wing geometry expand independently. A samara wing is part of its
        // parent's mericarp: only the Fruit owns ordinary ripening and retention.
        // The wing's effective phase/colour is synchronized after final admission.
        var samaraWing = Organ == OrganTypes.FruitWing && fruit.FruitType == FruitType.Samara && fruit.WingsPerFruit > 0;
        if (development > 0f)
            TreeDevelopmentHours += plant.World.HoursPerTick * development;
        var matureLength = Organ == OrganTypes.Fruit ? fruit.MatureLength : fruit.WingLength;
        var matureRadius = Organ == OrganTypes.Fruit ? fruit.MatureRadius : fruit.WingRadius;
        if (!samaraWing && Length >= matureLength * .99f && Radius >= matureRadius * .99f)
            TreeRipeningHours += plant.World.HoursPerTick * (TreeRipeningHours >= fruit.RipeningTimeHours ? 1f : thermal);
        var progress = Math.Clamp(TreeDevelopmentHours / MathF.Max(1, fruit.GrowthTimeHours), 0, 1);
        if (development > 0f)
        {
            if (Organ == OrganTypes.Fruit)
            {
                // Admission may retain dimensions from the supporting flower. Expansion
                // must preserve that accepted tissue while its progress target catches up.
                Length = MathF.Max(Length, fruit.InitialLength + (fruit.MatureLength - fruit.InitialLength) * progress);
                Radius = MathF.Max(Radius, fruit.InitialRadius + (fruit.MatureRadius - fruit.InitialRadius) * progress);
            }
            else
            {
                Length = MathF.Max(Length, .001f + (fruit.WingLength - .001f) * progress);
                Radius = MathF.Max(Radius, .0005f + (fruit.WingRadius - .0005f) * progress);
            }
        }

        if (!samaraWing)
        {
            var ripeness = Math.Clamp(TreeRipeningHours / MathF.Max(1, fruit.RipeningTimeHours), 0, 1);
            Color = ripeness < .5f ? Vector3.Lerp(fruit.UnripeColor, fruit.RipeningColor, ripeness * 2) : Vector3.Lerp(fruit.RipeningColor, fruit.MatureColor, (ripeness - .5f) * 2);
            if (TreeRipeningHours > fruit.RipeningTimeHours + fruit.FruitRetentionDays * 24f || age > 24f * 365f)
                formation.Death(id); // Ordinary mericarp shedding carries its wing descendants.
        }
    }

    internal void SynchronizeTreeFruitWingPhase(in AboveGroundAgent fruitBody)
    {
        TreeRipeningHours = fruitBody.TreeRipeningHours;
        Color = fruitBody.Color;
    }

    readonly bool TreeFlowerSupportReady(PlantSubFormation<AboveGroundAgent> formation)
    {
        var parent = Parent;
        // There is at most a pedicel and one inflorescence axis below a tree flower.
        for (int level = 0; level < 2 && parent >= 0; level++)
        {
            ref readonly var support = ref formation.GetAgent(parent);
            if (support.Organ is not (OrganTypes.FlowerPedicel or OrganTypes.FlowerStem))
                break;
            if (support.Length < support.LengthVar * .99f || support.Radius < support.RadiusVar * .99f)
                return false;
            parent = support.Parent;
        }

        return true;
    }

    void CreateTreeFruitWings(PlantSubFormation<AboveGroundAgent> formation, int id)
    {
        var plant = formation.Plant;
        var fruit = plant.Parameters.FruitSettings;
        for (int w = 0; w < fruit.WingsPerFruit; w++)
        {
            var orientation = Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI * w) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -1.30f);
            formation.Birth(new AboveGroundAgent(plant, id, OrganTypes.FruitWing, orientation, Energy * .05f, radius: .0005f, length: .001f) { Water_g = Water_g * .05f, TreePhaseStarted = plant.World.Timestep, Color = fruit.UnripeColor });
            Energy *= .95f;
            Water_g *= .95f;
        }
    }

    internal void GrowWoodySuspendedRadius(PlantFormation2 plant)
    {
        if (IsRizome || Organ is not (OrganTypes.Stem or OrganTypes.Meristem))
            return;
        var s = plant.Parameters;
        var season = plant.World.Season;
        if (season < s.TreeBudBreakSeason || season >= s.TreeGrowthEndSeason || Energy <= EnoughEnergy(s))
            return;
        var mature = Math.Clamp(plant.GetAge_h() / (24f * 365f * s.TreeMatureAgeYears), 0f, 1f);
        var increment = s.TreeAnnualRadiusGrowth * (1f - .6f * mature) * SpeciesSettings.TreeRadiusByOrder(TreeOrder) * plant.World.HoursPerTick / (24f * 150f) * plant.VegetativeTemperatureFactor(plant.World.Timestep) * Math.Clamp(plant.WaterBalance, 0f, 1f);
        var limit = Parent >= 0 ? plant.AG.GetBaseRadius(Parent) : s.ShootMaximumRadius;
        Radius = MathF.Max(Radius, MathF.Min(limit, Radius + increment));
    }

    internal void RebaseTreeDevelopmentClock(in AboveGroundAgent before, uint delay)
    {
        if (TreePhaseStarted != before.TreePhaseStarted || TreeFruitChosen && !before.TreeFruitChosen || Organ == OrganTypes.Fruit && before.Organ != OrganTypes.Fruit)
            TreePhaseStarted += delay;
    }
}
