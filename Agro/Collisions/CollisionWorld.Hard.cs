using System.Numerics;

namespace Agro.Collisions;

public sealed partial class CollisionWorld
{
    readonly List<PlantFormation2> ActivePlants = [];
    [ThreadStatic] static List<int>? BlockedOrgans;
    static readonly bool TraceSolids = Environment.GetEnvironmentVariable("AGRO_TRACE_SOLID_ENFORCE") != null;

    void ResolveHard(PlantFormation2 plant)
    {
        var ag = plant.AG;
        ag.EnsureClearanceCapacity(ag.Count);
        ag.RefreshAnchors(Static);
        ag.ClearSupport();
        for (int round = 0; round < CollisionSettings.HardRounds; ++round)
        {
            ag.BeginContactSweep();
            for (int i = 0; i < ag.Count; ++i)
            {
                if (!Eligible(ag, i)) continue;
                var pose = ag.GetContactPose(i);
                var trusted = TrustedPoint(plant, i);
                if (CollisionSettings.ObstaclePass && (HasStaticReaction(ag,i,StaticSurface.Obstacle) || !ClearOfObstacles(ag, i, pose, Work[plant].Admission)))
                    Correct(ag, i, trusted, StaticSurface.Obstacle);
                if (CollisionSettings.SoilPass && (HasStaticReaction(ag,i,StaticSurface.Soil) || SoilEligible(ag, i, pose)))
                    Correct(ag, i, trusted, StaticSurface.Soil);
            }
            if (!ag.EndContactSweep(MaxCorrectionRadians)) break;
        }

        EnforceStaticAdmissibility(plant, Work[plant].Admission, shrinkNewborns: false, out _);
        RefreshContactLoads(plant);
    }

    /// <summary>
    /// The second half of the solid pass: every eligible organ must be admissible against the
    /// obstacles and the soil - no contact, and a clear swept path from last tick's geometry.
    /// One that is not is redirected along the surface, shortened to the admissible part of this
    /// tick's growth, or rolled back to last tick's geometry and marked obstructed so that it
    /// retries later; a rollback that does not help is escalated to the moving supports that
    /// carried the organ there. Shared with the mixed engine, which runs it after the soft
    /// foliage pass, so a solid is a constraint under both and a tolerance only under the soft
    /// engine. Returns whether any organ had to be touched.
    /// </summary>
    /// <param name="shrinkNewborns">The mixed engine's births are not staged, so an organ born
    /// this tick can already stand inside a solid with no previous geometry to fall back to;
    /// it is shortened toward its base instead, and when even its base is inside the solid the
    /// birth is withdrawn (the organ dies unborn and its parent waits a day before growing on),
    /// which is what the certified engine's staged birth would have refused. Left in place, such
    /// an organ can never become admissible and the escalation below would roll back every
    /// moving ancestor every tick to no effect, freezing the branch against the wall. The
    /// certified engine admits births before they exist and passes false, which leaves its
    /// behaviour exactly as it was.</param>
    /// <param name="unresolved">Organs still inadmissible when the escalation gave up: only an
    /// already inadmissible previous configuration remained to fall back to.</param>
    internal bool EnforceStaticAdmissibility(PlantFormation2 plant, StaticAdmission admission, bool shrinkNewborns, out int unresolved)
    {
        var ag = plant.AG;
        var blocked = BlockedOrgans ??= [];
        blocked.Clear();
        var touched = false;
        unresolved = 0;
        for (int i = 0; i < ag.Count; ++i)
        {
            if (!Eligible(ag, i) || shrinkNewborns && ag.IsDying(i)) continue;
            if (!StaticAdmissible(plant, i, admission))
            {
                touched = true;
                var affected = new List<int> { i };
                for (int j = 0; j < affected.Count; ++j) affected.AddRange(ag.GetChildren(affected[j]));
                bool Accept() => affected.All(k => StaticAdmissible(plant, k, admission));
                var shape = Shape(ag, i);
                Static.ShapeContact(shape, TrustedPoint(plant, i), StaticSurface.Obstacle, out _, out var normal, out _);
                var outcome = "redirected";
                if (!ag.TryRedirectGrowth(i, normal, Accept))
                {
                    outcome = "shortened";
                    if (!ag.TryShortenGrowth(i, Accept) && !(shrinkNewborns && ag.TryShrinkNewborn(i, Accept)))
                    {
                        if (shrinkNewborns && !ag.PreviousGeometry[i].Exists)
                        {
                            if (TraceSolids) Trace(plant, i, "withdrawn birth");
                            ag.Death(i, force: true, reason: OrganDeathReason.Obstruction);
                            var parent = ag.GetParent(i);
                            if (parent >= 0) ag.SetObstruction(parent, true);
                            continue;
                        }
                        outcome = "blocked";
                        blocked.Add(i);
                    }
                    ag.SetObstruction(i, true);
                }
                if (TraceSolids) Trace(plant, i, outcome);
            }
        }
        if (blocked.Count > 0)
        {
            ag.RestorePreviousGeometry(blocked);
            // Mixed engine: an organ whose previous configuration is itself inside a solid - a floral
            // segment whose base sits on the surface it was born against - can never be freed by a
            // rollback, and escalating on its behalf would restore its moving ancestors every tick
            // to no effect, freezing the branch. The escalation runs once in full; whatever it cannot
            // resolve is then set aside and the escalation is redone without it, from this same
            // state, so the ancestors keep the tick's growth and the residual stays with the organ
            // that owns it. The certified engine never reaches this case and takes the single pass.
            var trial = shrinkNewborns ? ag.SaveContactTrial() : null;
            var hopeless = Escalate(plant, admission, shrinkNewborns, null, blocked);
            if (hopeless.Count > 0 && trial != null)
            {
                ag.RestoreContactTrial(trial);
                Escalate(plant, admission, shrinkNewborns, hopeless, blocked);
            }
            unresolved = hopeless.Count;
        }
        return touched;
    }

    /// <summary>
    /// A local rollback cannot undo transport by an obstructed parent. Escalate only the causally
    /// affected branch; unrelated crowns keep their accepted growth. Returns the organs still
    /// inadmissible when only an already inadmissible previous configuration remained.
    /// </summary>
    List<int> Escalate(PlantFormation2 plant, StaticAdmission admission, bool shrinkNewborns, List<int>? ignore, List<int> blocked)
    {
        var ag = plant.AG;
        var restored = new HashSet<int>();
        var remaining = new List<int>();
        for (int pass = 0; pass < ag.Count; ++pass)
        {
            remaining.Clear();
            for (int i = 0; i < ag.Count; ++i)
                if (Eligible(ag, i) && !(shrinkNewborns && ag.IsDying(i)) && ignore?.Contains(i) != true && !StaticAdmissible(plant, i, admission))
                    remaining.Add(i);
            if (remaining.Count == 0) break;
            var parents = remaining.SelectMany(ag.ChangedContactComponent).Distinct().ToArray();
            if (TraceSolids) foreach (var r in remaining) Trace(plant, r, $"pass {pass} remaining; component [{string.Join(",", parents)}]");
            ag.RestorePreviousGeometry(parents);
            blocked.Clear(); blocked.AddRange(parents);
            var changed = false;
            foreach (var parent in parents) changed |= restored.Add(parent);
            if (!changed) break; // Only an already inadmissible previous configuration remains.
        }
        return remaining;
    }

    internal void RefreshContactLoads(PlantFormation2 plant)
    {
        var ag = plant.AG;
        ag.ClearSupport();
        ag.RefreshMechanicalLoads();
        for (int i = 0; i < ag.Count; ++i)
        {
            if (!Eligible(ag, i)) continue;
            RecordSupport(ag, i, StaticSurface.Obstacle);
            if (SoilEligible(ag, i, ag.GetContactPose(i))) RecordSupport(ag, i, StaticSurface.Soil);
        }
        ag.UpdateSupportedLoads();
        ag.RefreshAnchors(Static);
    }

    internal struct StaticEvaluation
    {
        public bool Used, Admissible, Rhizome, ParentRhizome, ObstaclePass, SoilPass;
        public int Parent;
        public long Scene;
        public ulong Id;
        public ContactShape Shape, Previous;
        public Vector3 Trusted;
    }

    /// <summary>The per-plant memo of <see cref="StaticAdmissible"/>, owned by whichever engine's
    /// per-plant work runs the solid pass.</summary>
    internal sealed class StaticAdmission
    {
        public StaticEvaluation[] Evaluations = [];
        /// <summary>Admissibility verdicts served from the memo, and the clearance cache's skips and
        /// scene queries made on this object's behalf; the owner folds them into its own counters.</summary>
        public long Reused, Skips, Queries;
    }

    bool StaticAdmissible(PlantFormation2 plant, int i) => StaticAdmissible(plant, i, Work[plant].Admission);

    internal bool StaticAdmissible(PlantFormation2 plant, int i, StaticAdmission work)
    {
        var ag = plant.AG; var shape = Shape(ag, i);
        if (!Eligible(ag, i)) return true;
        var trusted = TrustedPoint(plant, i);
        var previous = Previous(ag, i, shape);
        if (work.Evaluations.Length < ag.Count)
            Array.Resize(ref work.Evaluations, Math.Max(ag.Count, work.Evaluations.Length * 3 / 2 + 8));
        ref var cached = ref work.Evaluations[i];
        var parent = ag.GetParent(i); var rhizome = ag.GetIsRizome(i);
        var parentRhizome = parent >= 0 && ag.GetIsRizome(parent);
        var id = ag.GetOrganId(i);
        if (cached.Used && cached.Scene == StaticRevision && cached.Id == id
            && cached.ObstaclePass == CollisionSettings.ObstaclePass && cached.SoilPass == CollisionSettings.SoilPass
            && cached.Shape == shape && cached.Previous == previous && cached.Trusted == trusted
            && cached.Parent == parent && cached.Rhizome == rhizome && cached.ParentRhizome == parentRhizome)
        { ++work.Reused; return cached.Admissible; }
        var admissible = StaticAdmissibleUncached(plant, i, shape, previous, trusted, work);
        cached = new() { Used = true, Scene = StaticRevision, Id = id, Shape = shape,
            Previous = previous, Trusted = trusted, Parent = parent, Rhizome = rhizome,
            ParentRhizome = parentRhizome, Admissible = admissible,
            ObstaclePass = CollisionSettings.ObstaclePass, SoilPass = CollisionSettings.SoilPass };
        return admissible;
    }

    bool StaticAdmissibleUncached(PlantFormation2 plant, int i, in ContactShape shape,
        in ContactShape previous, Vector3 trusted, StaticAdmission work)
    {
        var ag = plant.AG;
        if (ag.HasContactDeformation && ag.GetOrgan(i) == OrganTypes.Leaf && shape.Lamina is { } blade
            && (!blade.WithinStrainTolerance(shape.Pose,shape.Pose.Deformation)
                || !blade.SelfSweepAdmissible(previous.Pose,shape.Pose))) return false;
        if (!AdmissionClocks)
        {
            if (CollisionSettings.ObstaclePass && !ClearOfObstacles(ag, i, shape.Pose, work)
                && (Static.ShapeContact(shape, trusted, StaticSurface.Obstacle, out _, out _, out _)
                    || !Static.ShapeSweepClear(previous, shape, StaticSurface.Obstacle))) return false;
            return !CollisionSettings.SoilPass || !SoilEligible(ag, i, shape.Pose)
                || (!Static.ShapeContact(shape, trusted, StaticSurface.Soil, out _, out _, out _)
                    && (Static.InsideSoil(previous.Pose.Base, 1f) || Static.ShapeSweepClear(previous, shape, StaticSurface.Soil)));
        }
        // The same tests, each stage on its own clock (AGRO_COLLISION_TIMING).
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (CollisionSettings.ObstaclePass && !ClearOfObstacles(ag, i, shape.Pose, work))
        {
            var t1 = System.Diagnostics.Stopwatch.GetTimestamp(); Interlocked.Add(ref AdmitClearTicks, t1 - t0);
            var contact = Static.ShapeContact(shape, trusted, StaticSurface.Obstacle, out _, out _, out _);
            var t2 = System.Diagnostics.Stopwatch.GetTimestamp(); Interlocked.Add(ref AdmitObstacleContactTicks, t2 - t1); Interlocked.Increment(ref AdmitObstacleContacts);
            if (contact) return false;
            var clear = Static.ShapeSweepClear(previous, shape, StaticSurface.Obstacle);
            t0 = System.Diagnostics.Stopwatch.GetTimestamp(); Interlocked.Add(ref AdmitObstacleSweepTicks, t0 - t2); Interlocked.Increment(ref AdmitObstacleSweeps);
            if (!clear) return false;
        }
        else { var t1 = System.Diagnostics.Stopwatch.GetTimestamp(); Interlocked.Add(ref AdmitClearTicks, t1 - t0); t0 = t1; }
        if (!CollisionSettings.SoilPass || !SoilEligible(ag, i, shape.Pose)) { Interlocked.Add(ref AdmitSoilEligibleTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t0); return true; }
        var t3 = System.Diagnostics.Stopwatch.GetTimestamp(); Interlocked.Add(ref AdmitSoilEligibleTicks, t3 - t0);
        var soilContact = Static.ShapeContact(shape, trusted, StaticSurface.Soil, out _, out _, out _);
        var t4 = System.Diagnostics.Stopwatch.GetTimestamp(); Interlocked.Add(ref AdmitSoilContactTicks, t4 - t3); Interlocked.Increment(ref AdmitSoilContacts);
        if (soilContact) return false;
        if (Static.InsideSoil(previous.Pose.Base, 1f)) { Interlocked.Add(ref AdmitSoilSweepTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t4); return true; }
        var soilClear = Static.ShapeSweepClear(previous, shape, StaticSurface.Soil);
        Interlocked.Add(ref AdmitSoilSweepTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t4); Interlocked.Increment(ref AdmitSoilSweeps);
        return soilClear;
    }

    /// <summary>Stage clocks of the admissibility test, summed over all threads; only under
    /// <c>AGRO_COLLISION_TIMING</c>, and read by the mixed engine's timing line.</summary>
    internal static readonly bool AdmissionClocks = TraceTiming;
    internal static long AdmitClearTicks, AdmitObstacleContactTicks, AdmitObstacleSweepTicks, AdmitSoilEligibleTicks, AdmitSoilContactTicks, AdmitSoilSweepTicks;
    internal static long AdmitObstacleContacts, AdmitObstacleSweeps, AdmitSoilContacts, AdmitSoilSweeps;
    internal static string AdmissionStages()
    {
        var k = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        var s = $"admitCpuMs: clear={AdmitClearTicks * k:F0} obstacleContact={AdmitObstacleContactTicks * k:F0}/{AdmitObstacleContacts} obstacleSweep={AdmitObstacleSweepTicks * k:F0}/{AdmitObstacleSweeps}"
            + $" soilEligible={AdmitSoilEligibleTicks * k:F0} soilContact={AdmitSoilContactTicks * k:F0}/{AdmitSoilContacts} soilSweep={AdmitSoilSweepTicks * k:F0}/{AdmitSoilSweeps}";
        AdmitClearTicks = AdmitObstacleContactTicks = AdmitObstacleSweepTicks = AdmitSoilEligibleTicks = AdmitSoilContactTicks = AdmitSoilSweepTicks = 0;
        AdmitObstacleContacts = AdmitObstacleSweeps = AdmitSoilContacts = AdmitSoilSweeps = 0;
        return s;
    }

    void Trace(PlantFormation2 plant, int i, string outcome)
    {
        var ag = plant.AG; var previous = ag.PreviousGeometry[i];
        var hit = Static.ShapeContactUncached(Shape(ag, i), TrustedPoint(plant, i), StaticSurface.Obstacle, out var point, out var normal, out var depth);
        var pose = ag.GetContactPose(i);
        Console.WriteLine($"SOLID_ENFORCE t={plant.World.Timestep} organ={i} {ag.GetOrgan(i)} len={ag.GetLength(i) * 1000:F3}mm r={ag.GetBaseRadius(i) * 1000:F3}mm depth={depth * 1000:F3}mm hit={hit} at={point} n={normal} base={pose.Base} tip={pose.Tip} trusted={TrustedPoint(plant, i)} soilDepth={(Static.ShapeContactUncached(Shape(ag, i), TrustedPoint(plant, i), StaticSurface.Soil, out _, out _, out var sd) ? sd * 1000 : 0):F3}"
            + $" previous={(previous.Exists ? $"len {previous.Pose.Length * 1000:F3} r {previous.Radius * 1000:F3}" : "none")} parent={ag.GetParent(i)} dying={ag.IsDying(i)}: {outcome}");
    }

    /// <summary>
    /// For probes: how deep this organ currently sits in the obstacles or the soil, in metres,
    /// zero when clear. Read-only and uncached; not part of either engine's pass.
    /// </summary>
    public float StaticPenetration(PlantFormation2 plant, int organ, StaticSurface surface)
    {
        var ag = plant.AG;
        if (!StaticReady || organ < 0 || organ >= ag.Count || !Eligible(ag, organ)) return 0f;
        if (surface == StaticSurface.Soil && !SoilEligible(ag, organ, ag.GetContactPose(organ))) return 0f;
        return Static.ShapeContactUncached(Shape(ag, organ), TrustedPoint(plant, organ), surface, out _, out _, out var depth)
            ? MathF.Max(0f, depth) : 0f;
    }

    internal static bool Eligible(PlantSubFormation<AboveGroundAgent> ag, int i)
        => ag.GetOrgan(i) != OrganTypes.Seed && (ag.GetLength(i) > 0f || ag.GetBaseRadius(i) > 0f);

    internal bool SoilEligible(PlantSubFormation<AboveGroundAgent> ag, int i, in OrganPose pose)
    {
        if (ag.GetIsRizome(i)) return false;
        var previous = ag.PreviousGeometry[i];
        if (pose.Tip.Y > pose.Base.Y && Static.InsideSoil(pose.Base, 1f)
            && (!previous.Exists || Static.InsideSoil(previous.Pose.Base, 1f))) return false;
        var parent = ag.GetParent(i);
        return !((parent < 0 || ag.GetIsRizome(parent)) && Static.InsideSoil(pose.Base, 1f));
    }

    internal static Vector3 TrustedPoint(PlantFormation2 plant, int i)
    {
        var ag = plant.AG;
        var previous = ag.PreviousGeometry[i];
        if (previous.Exists) return (previous.Pose.Base + previous.Pose.Tip) * 0.5f;
        var parent = ag.GetParent(i);
        return parent >= 0 ? ag.GetBaseCenterWorld(parent) : plant.Position + ag.FieldOrigin;
    }

    bool ClearOfObstacles(PlantSubFormation<AboveGroundAgent> ag, int i, in OrganPose pose, StaticAdmission work)
    {
        if (Static.ObstacleTriangles == 0) return true;
        var previous = ag.PreviousGeometry[i];
        var template = ag.GetLaminaTemplate(i);
        float Travel(OrganPose a, OrganPose b) => template?.MotionBound(a, b) ?? a.MotionBound(b);
        var travel = Travel(ag.ClearancePose[i], pose);
        if (previous.Exists) travel = MathF.Max(travel, Travel(ag.ClearancePose[i], previous.Pose));
        if (ag.StaticClearance[i] > travel + CollisionSettings.ContactTolerance)
        { ++work.Skips; return true; }
        if (travel < 1e-12f && ag.StaticClearance[i] <= CollisionSettings.ContactTolerance) return false;
        ++work.Queries;
        var deviation = template == null ? 0f : template.WideReach * pose.Width + template.ReliefAt(pose)
            + MathF.Max(0f, template.LongReach - 1f) * pose.Length;
        deviation += template?.DeformationReach(pose) ?? ContactDeformation.MotionBound(pose.Length,0,Vector4.Zero,pose.Deformation);
        var probe = deviation + pose.Radius + CollisionSettings.ClearanceProbeDistance;
        var found = Static.ClosestObstacleSurface(pose.Base, pose.Tip, Vector3.Zero, probe, out var hit);
        var clearance = MathF.Max(0f, (found ? hit.Distance : probe) - pose.Radius - deviation);
        ag.ClearancePose[i] = pose; ag.StaticClearance[i] = clearance;
        return clearance > CollisionSettings.ContactTolerance
            && (!previous.Exists || clearance > Travel(previous.Pose, pose) + CollisionSettings.ContactTolerance);
    }

    bool HasStaticReaction(PlantSubFormation<AboveGroundAgent> ag,int i,StaticSurface surface)
        => Work[ag.Plant].StaticElastic is { } reactions && reactions.TryGetValue((ag.GetOrganId(i),surface),out var c) && c.Force>0;

    void Correct(PlantSubFormation<AboveGroundAgent> ag, int i, Vector3 trusted, StaticSurface surface)
    {
        if(ag.HasContactDeformation)
        {
            var reactions=Work[ag.Plant].StaticElastic ??= [];
            var key=(ag.GetOrganId(i),surface);
            reactions.TryGetValue(key,out var reaction);
            var shape=Shape(ag,i);
            var padding=reaction.Force*reaction.Mobility+CollisionSettings.ContactTolerance;
            var query=shape with { Pose=shape.Pose with { Radius=shape.Pose.Radius+padding } };
            var found=Static.ShapeContact(query,trusted,surface,out var elasticPoint,out var elasticNormal,out var elasticDepth);
            if(!found && reaction.Force<=0)return;
            if(!found){elasticPoint=reaction.Point;elasticNormal=reaction.Normal;}
            var signedGap=elasticDepth-padding+CollisionSettings.ContactTolerance;
            if(found)Work[ag.Plant].HardCorrectionDepth=MathF.Max(Work[ag.Plant].HardCorrectionDepth,MathF.Max(0,signedGap));
            var responseNormal=!found || signedGap<0 ? -elasticNormal : elasticNormal;
            var mobility=ag.ContactMobility(i,elasticPoint,responseNormal);
            if(mobility<=1e-20 || !double.IsFinite(mobility))return;
            var increment=found ? (float)((elasticDepth-padding+CollisionSettings.ContactTolerance)/mobility) : -reaction.Force;
            var limit=ag.ContactForceStepLimit(i,elasticPoint,responseNormal);
            increment=Math.Clamp(MathF.Max(-reaction.Force,increment),-limit,limit);
            reactions[key]=new(MathF.Max(0,reaction.Force+increment),(float)mobility,elasticPoint,elasticNormal);
            if(increment==0)return;
            ag.QueueContactCorrection(i,elasticPoint,elasticNormal,increment*(float)mobility);
            if(surface==StaticSurface.Obstacle)++Work[ag.Plant].Obstacle;else ++Work[ag.Plant].Soil;
            return;
        }
        if (!Static.ShapeContact(Shape(ag, i), trusted, surface, out var point, out var normal, out var depth)) return;
        if (!ag.QueueContactCorrection(i, point, normal, depth + CollisionSettings.ContactTolerance)) return;
        if (surface == StaticSurface.Obstacle) ++Work[ag.Plant].Obstacle;
        else ++Work[ag.Plant].Soil;
    }

    internal void RecordSupport(PlantSubFormation<AboveGroundAgent> ag, int i, StaticSurface surface)
    {
        var pose = ag.GetContactPose(i);
        var lamina = ag.GetLaminaTemplate(i);
        if (lamina != null || pose.CurvedRod)
        {
            // A supported blade also transmits a reaction through its petiole. Expand only
            // this proximity query, never the accepted geometry or its physical thickness.
            var nearby = new ContactShape(pose with
                { Radius=pose.Radius+CollisionSettings.ContactTolerance+2*FoliageGeometry.Skin },lamina);
            if (Static.ShapeContact(nearby,TrustedPoint(ag.Plant,i),surface,out var point,out var direction,out _)
                && direction.Y>0)
                ag.RecordSupportReaction(i,point,direction);
            return;
        }
        StaticHit hit = default;
        var found = surface == StaticSurface.Soil
            && Static.ClosestSoilSurface(pose.Base, pose.Tip, pose.Radius + CollisionSettings.ContactTolerance, out hit);
        if (surface == StaticSurface.Obstacle)
            found = Static.ClosestObstacleSurface(pose.Base, pose.Tip, Vector3.Zero, pose.Radius + CollisionSettings.ContactTolerance, out hit);
        if (!found) return;
        var normal = hit.Normal;
        if (surface == StaticSurface.Obstacle && Vector3.Dot(TrustedPoint(ag.Plant, i) - hit.Point, normal) < 0) normal = -normal;
        if (normal.Y > 0f && hit.Distance <= pose.Radius + CollisionSettings.ContactTolerance)
            ag.RecordSupportReaction(i, hit.OnQuery, normal);
    }
}
