using System.Numerics;

namespace Agro.Collisions;

public sealed partial class SoftContactEngine
{
    /// <summary>
    /// Bounded pushes out of obstacles and soil through the same joint compliance, then one
    /// closed-form clip of this tick's elongation for anything still inside an obstacle. Under the
    /// soft engine that is all: no admissibility test, no redirect search, no rollback, and an
    /// organ a push could not free stays inside until the next tick's pushes. Under the mixed
    /// engine the certified engine's admissibility pass follows
    /// (<see cref="CollisionWorld.EnforceStaticAdmissibility"/>): whatever is still in contact with
    /// a solid, or crossed one on its way, is redirected, shortened or rolled back to last tick's
    /// geometry and retried, so no organ ends a tick inside an obstacle or the soil.
    /// </summary>
    void ResolveHard(PlantFormation2 plant)
    {
        var ag = plant.AG; var work = Work[plant];
        ag.EnsureClearanceCapacity(ag.Count);
        if (work.SolidConfinement.Length < ag.Count)
            Array.Resize(ref work.SolidConfinement, Math.Max(ag.Count, work.SolidConfinement.Length * 2));
        Array.Clear(work.SolidConfinement, 0, ag.Count);
        if (work.ObstacleTouch.Length < ag.Count) { work.ObstacleTouch = new StaticTouch[Math.Max(ag.Count, work.ObstacleTouch.Length * 2)]; work.SoilTouch = new StaticTouch[work.ObstacleTouch.Length]; }
        if (work.SolidAnswers.Length < ag.Count) work.SolidAnswers = new byte[Math.Max(ag.Count, work.SolidAnswers.Length * 2)];
        for (int i = 0; i < ag.Count; ++i) { work.ObstacleTouch[i].Round = -1; work.SoilTouch[i].Round = -1; }
        long T() => TraceTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var t0 = T();
        ag.RefreshAnchors(Static);
        ag.ClearSupport();
        var t1 = T(); work.HardAnchorTicks += t1 - t0;
        // The first round asks about every organ. A later round asks about the organs whose
        // answer can have changed: the ones the round before moved, and the ones that have
        // touched a solid this tick, whose contact is carried from round to round. For any other
        // organ the round would repeat the question it asked last time, of the same pose, and
        // get the same answer - that it touches nothing - so it queues nothing either way.
        var asked = work.RoundOrgans; var all = true;
        work.Touched.Clear();
        for (int round = 0; round < CollisionSettings.SoftHardPushRounds; ++round)
        {
            ag.BeginContactSweep(resetDiagnostics: false);
            // Whether an organ needs a push is a question about that organ alone, and is asked
            // of a large plant on all cores. The pushes queue turns on joints the organs share,
            // so they are made one after another, in organ order.
            var ta = T();
            if (all) AskAboutSolids(plant, work, round);
            else foreach (var i in asked) work.SolidAnswers[i] = SolidQuestions(plant, i, work, round);
            work.HardAskTicks += T() - ta;
            if (all) { for (int i = 0; i < ag.Count; ++i) PushOutOfSolids(plant, i, work, round); }
            else foreach (var i in asked) PushOutOfSolids(plant, i, work, round);
            var te = T();
            var moved = ag.EndContactSweep(CollisionSettings.MaxCorrectionRadians);
            if (TraceSolids) Console.WriteLine($"SOLID_PUSH t={plant.World.Timestep} round={round} moved={moved}");
            work.HardSweepTicks += T() - te; ++work.HardRounds;
            if (!moved) break;
            asked.Clear();
            all = !ag.CollectSweptOrgans(asked, ag.Count / 4);
            if (all) continue;
            // In organ order, once each: the order in which corrections are queued on a shared joint.
            asked.AddRange(work.Touched);
            asked.Sort();
            var kept = 0;
            for (int k = 0; k < asked.Count; ++k)
                if (kept == 0 || asked[k] != asked[kept - 1]) asked[kept++] = asked[k];
            asked.RemoveRange(kept, asked.Count - kept);
        }
        var tc = T();
        if (CollisionSettings.SolidElongationClip && CollisionSettings.ObstaclePass) ClipElongation(plant, work);
        var ta2 = T(); work.HardClipTicks += ta2 - tc;
        if (CertifiedSolids)
        {
            // Both steps start by asking every organ whether it is admissible. A large plant is
            // asked once, on all cores; when nothing is inadmissible neither step has a walk to do.
            var allAdmissible = World.AllStaticallyAdmissible(plant, work.Admission);
            var moved = ExactPushRounds(plant, work, allAdmissible);
            if (World.EnforceStaticAdmissibility(plant, work.Admission, shrinkNewborns: true, out work.UnresolvedSolids, allAdmissible)) { moved = true; ++work.Enforced; }
            // A push, a redirect, a shortening or a rollback moved organs after the carried pushes
            // measured their witnesses, so the support detection below asks the scene again instead
            // of reading a witness taken at a pose the organ no longer has.
            if (moved) for (int i = 0; i < ag.Count; ++i) { work.ObstacleTouch[i].Round = -1; work.SoilTouch[i].Round = -1; }
        }
        var tl = T(); work.HardAdmitTicks += tl - ta2;
        RefreshContactLoads(plant, work);
        work.HardLoadTicks += T() - tl;
    }

    const byte ObstacleQuestion = 1, SoilQuestion = 2, ObstacleReaction = 4, SoilReaction = 8;
    /// <summary>Organs from which the per-organ questions of the solid pass are asked on all cores.</summary>
    const int ParallelQuestionsMinimum = 4096;

    /// <summary>Which solids an organ has to be pushed out of this round, if any: the obstacles
    /// unless the clearance cache proves it clear of them, the soil unless it is out of its reach.</summary>
    byte SolidQuestions(PlantFormation2 plant, int i, SoftWork work, int round)
    {
        var ag = plant.AG;
        if (!CollisionWorld.Eligible(ag, i)) return 0;
        var pose = ag.GetContactPose(i);
        byte answer = 0;
        if (CollisionSettings.ObstaclePass && !ClearOfObstacles(ag, i, pose, work)) answer |= ObstacleQuestion;
        // Out of the soil's reach the push would ask the scene and find nothing (see
        // StaticCollider.AboveSoilReach). A push that carries a contact from the round
        // before does not ask, so it is left alone.
        var carried = round > 0 && work.SoilTouch[i].Round == round - 1;
        if (CollisionSettings.SoilPass
            && (carried || !Static.AboveSoilReach(new ContactShape(pose, ag.GetLaminaTemplate(i))))
            && World.SoilEligible(ag, i, pose)) answer |= SoilQuestion;
        return answer;
    }

    void AskAboutSolids(PlantFormation2 plant, SoftWork work, int round)
    {
        var count = plant.AG.Count;
        if (count >= ParallelQuestionsMinimum) { AskOnAllCores(plant, work, round, float.NaN); return; }
        for (int i = 0; i < count; ++i) work.SolidAnswers[i] = SolidQuestions(plant, i, work, round);
    }

    /// <summary>The push questions of a round, or - given a support reach - the support questions,
    /// for every organ of a large plant. Kept apart so that a small plant pays for no closure.</summary>
    void AskOnAllCores(PlantFormation2 plant, SoftWork work, int round, float supportReach)
    {
        var answers = work.SolidAnswers;
        plant.AG.WarmLaminaCache(); // filled lazily, which only one thread at a time may do
        Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, plant.AG.Count, 2048), range =>
        {
            for (int i = range.Item1; i < range.Item2; ++i)
                answers[i] = float.IsNaN(supportReach) ? SolidQuestions(plant, i, work, round) : SupportQuestions(plant, i, supportReach, work);
        });
    }

    /// <summary>One organ's pushes out of the obstacles and out of the soil, in one push round,
    /// as <see cref="SolidQuestions"/> answered for it.</summary>
    void PushOutOfSolids(PlantFormation2 plant, int i, SoftWork work, int round)
    {
        var answer = work.SolidAnswers[i];
        if (answer == 0) return;
        long T() => TraceTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var ag = plant.AG;
        var pose = ag.GetContactPose(i);
        var trusted = CollisionWorld.TrustedPoint(plant, i);
        var ta = T();
        if ((answer & ObstacleQuestion) != 0) Correct(ag, i, pose, trusted, StaticSurface.Obstacle, work, round);
        var tb = T(); work.HardObstacleTicks += tb - ta;
        if ((answer & SoilQuestion) != 0) Correct(ag, i, pose, trusted, StaticSurface.Soil, work, round);
        work.HardSoilTicks += T() - tb;
    }

    /// <summary>
    /// Mixed engine: the certified engine's push rounds, exact and for the organs the carried
    /// pushes left inadmissible only. The rounds above carry a push's witness and normal forward
    /// as a plane, which is exact for a rod against a tray wall and cheap on the facade, but not
    /// for a blade that widened or turned since the witness was taken: its deepest point moved,
    /// the carried depth reads clear, and the blade stays a millimetre inside. Left there, the
    /// admissibility pass would roll the whole branch's tick back - growth, gravity and all -
    /// although a push could have freed it. So before that pass the organs still inadmissible
    /// get what the certified engine gives every touching organ: up to <see cref="CollisionSettings.HardRounds"/>
    /// rounds, each re-measured. Returns whether anything moved. When every eligible organ is
    /// known to be admissible as the plant stands (<paramref name="allAdmissible"/>), the first
    /// round has nothing to queue and its walk is left out.
    /// </summary>
    bool ExactPushRounds(PlantFormation2 plant, SoftWork work, bool allAdmissible = false)
    {
        var ag = plant.AG; var moved = false;
        var previousDepth = double.PositiveInfinity;
        for (int round = 0; round < CollisionSettings.HardRounds; ++round)
        {
            ag.BeginContactSweep(resetDiagnostics: false);
            var queued = 0; var pushedDepth = 0.0;
            for (int i = 0; !allAdmissible && i < ag.Count; ++i)
            {
                if (!CollisionWorld.Eligible(ag, i) || World.StaticAdmissible(plant, i, work.Admission)) continue;
                var shape = CollisionWorld.Shape(ag, i);
                var trusted = CollisionWorld.TrustedPoint(plant, i);
                if (CollisionSettings.ObstaclePass && Static.ShapeContact(shape, trusted, StaticSurface.Obstacle, out var point, out var normal, out var depth)
                    && ag.QueueContactCorrection(i, point, normal, depth + CollisionSettings.ContactTolerance)) { ++queued; ++work.Obstacle; pushedDepth += depth; }
                if (CollisionSettings.SoilPass && World.SoilEligible(ag, i, shape.Pose)
                    && Static.ShapeContact(shape, trusted, StaticSurface.Soil, out point, out normal, out depth)
                    && ag.QueueContactCorrection(i, point, normal, depth + CollisionSettings.ContactTolerance)) { ++queued; ++work.Soil; pushedDepth += depth; }
                if (TraceSolids) Console.WriteLine($"SOLID_EXACT t={plant.World.Timestep} round={round} organ={i} {ag.GetOrgan(i)} queued={queued}");
            }
            ++work.ExactRounds;
            if (queued == 0 || !ag.EndContactSweep(CollisionSettings.MaxCorrectionRadians)) break;
            moved = true;
            // Optional (off by default): a plant whose pushes no longer shrink - the same organs
            // pushed by about the same total depth as the round before - stops pushing; the
            // admissibility pass that follows deals with what is left, as it does after the last
            // round anyway.
            if (CollisionSettings.ExactRoundsStallRatio > 0f && round >= CollisionSettings.ExactRoundsBeforeStall
                && pushedDepth > CollisionSettings.ExactRoundsStallRatio * previousDepth) break;
            previousDepth = pushedDepth;
        }
        return moved;
    }

    /// <summary>
    /// The certified engine's <see cref="CollisionWorld.RefreshContactLoads"/>, minus one query per
    /// organ per tick. Support detection asked the static scene about every eligible organ every
    /// tick - on the facade that was 70,000 queries against two million triangles, 2.2 s of CPU a
    /// tick and half of the contact pass - although the clearance cache already knew, for nine
    /// organs in ten, that no obstacle was within reach. An organ whose cached clearance exceeds
    /// its travel since the measurement plus the support reach cannot be resting on anything, so
    /// it is not asked.
    /// </summary>
    void RefreshContactLoads(PlantFormation2 plant, SoftWork work)
    {
        var ag = plant.AG; var count = ag.Count; var answers = work.SolidAnswers;
        ag.ClearSupport(); // the loads are summed when the first resting organ needs them
        var supportReach = CollisionSettings.ContactTolerance + 2 * FoliageGeometry.Skin;
        // As in the push rounds: what there is to record is asked per organ, on all cores for a
        // large plant; recording adds to moments along the stem and stays in organ order.
        if (count < ParallelQuestionsMinimum)
            for (int i = 0; i < count; ++i) answers[i] = SupportQuestions(plant, i, supportReach, work);
        else AskOnAllCores(plant, work, 0, supportReach);
        for (int i = 0; i < count; ++i)
        {
            var answer = answers[i];
            if (answer == 0) continue;
            if ((answer & ObstacleReaction) != 0) ag.RecordSupportReaction(i, work.ObstacleTouch[i].Point, work.ObstacleTouch[i].Normal);
            else if ((answer & ObstacleQuestion) != 0) World.RecordSupport(ag, i, StaticSurface.Obstacle);
            if ((answer & SoilReaction) != 0) ag.RecordSupportReaction(i, work.SoilTouch[i].Point, work.SoilTouch[i].Normal);
            else if ((answer & SoilQuestion) != 0) World.RecordSupport(ag, i, StaticSurface.Soil);
        }
        ag.UpdateSupportedLoads();
        ag.RefreshAnchors(Static);
    }

    /// <summary>
    /// What an organ may be resting on. An organ pushed against a solid this tick is resting on
    /// it if the push came from below - the push already found the witness and the normal; any
    /// other organ has to ask the scene, unless it is provably out of reach.
    /// </summary>
    byte SupportQuestions(PlantFormation2 plant, int i, float supportReach, SoftWork work)
    {
        var ag = plant.AG;
        if (!CollisionWorld.Eligible(ag, i)) return 0;
        var pose = ag.GetContactPose(i);
        byte answer = 0;
        ref var obstacle = ref work.ObstacleTouch[i];
        if (obstacle.Round >= 0) { if (obstacle.Normal.Y > 0f) answer |= ObstacleReaction; }
        else if (!ProvablyClear(ag, i, pose, supportReach, work)) answer |= ObstacleQuestion;
        ref var soil = ref work.SoilTouch[i];
        if (soil.Round >= 0) { if (soil.Normal.Y > 0f) answer |= SoilReaction; }
        else if (!Static.AboveSoilReach(new ContactShape(pose, ag.GetLaminaTemplate(i))) && World.SoilEligible(ag, i, pose))
            answer |= SoilQuestion;
        return answer;
    }

    /// <summary>Whether the clearance cache proves no obstacle within <paramref name="margin"/> of the
    /// organ at its current pose, without issuing a query.</summary>
    bool ProvablyClear(PlantSubFormation<AboveGroundAgent> ag, int i, in OrganPose pose, float margin, SoftWork work)
    {
        if (Static.ObstacleTriangles == 0) return true;
        var template = ag.GetLaminaTemplate(i);
        var travel = template?.MotionBound(ag.ClearancePose[i], pose) ?? ag.ClearancePose[i].MotionBound(pose);
        if (ag.StaticClearance[i] > travel + margin) { Interlocked.Increment(ref work.SupportSkips); return true; }
        return false;
    }

    /// <summary>A solid contact found in a push round this tick: the witness in the organ's own frame,
    /// the surface normal, and the depth, so that the later rounds and the support detection can
    /// carry it forward without asking the static scene again.</summary>
    internal struct StaticTouch { public Vector3 Local, Point, Normal; public float Depth; public int Round; }

    /// <summary>
    /// One push against a solid. The first round asks the static scene; a later round of the same
    /// tick carries the first round's witness and normal forward and measures the remaining depth
    /// against the plane through them. On the facade the second and later rounds were re-querying
    /// the same tray wall for the same 11,000 organs, two million triangles a query, for 1.2 s of
    /// CPU a tick; a tray wall is a plane and the push is a millimetre, so the plane is exact for
    /// it and within the push for anything curved.
    /// </summary>
    void Correct(PlantSubFormation<AboveGroundAgent> ag, int i, in OrganPose pose, Vector3 trusted, StaticSurface surface, SoftWork work, int round)
    {
        ref var touch = ref (surface == StaticSurface.Obstacle ? ref work.ObstacleTouch[i] : ref work.SoilTouch[i]);
        Vector3 point, normal; float depth;
        if (round > 0 && touch.Round == round - 1)
        {
            point = pose.Base + Vector3.Transform(touch.Local, pose.Rotation);
            normal = touch.Normal;
            depth = touch.Depth - Vector3.Dot(point - touch.Point, normal);
            if (depth <= 0f) return;
        }
        else
        {
            if (!Static.ShapeContactUncached(CollisionWorld.Shape(ag, i), trusted, surface, out point, out normal, out depth)) return;
            touch.Local = Vector3.Transform(point - pose.Base, Quaternion.Conjugate(pose.Rotation));
        }
        touch.Point = point; touch.Normal = normal; touch.Depth = depth; touch.Round = round;
        work.Touched.Add(i);
        work.MaxSolidPenetration = MathF.Max(work.MaxSolidPenetration, depth);
        var queued = ag.QueueContactCorrection(i, point, normal, depth + CollisionSettings.ContactTolerance);
        if (TraceSolids) Console.WriteLine($"SOLID_PUSH t={ag.Plant.World.Timestep} round={round} organ={i} {ag.GetOrgan(i)} {surface} depth={depth * 1000:F3}mm n={normal} queued={queued}");
        if (!queued) return;
        if (surface == StaticSurface.Obstacle) ++work.Obstacle; else ++work.Soil;
    }

    /// <summary>
    /// Temporal coherence for obstacle queries, as in the certified engine: an organ measured well
    /// clear of every wall is not measured again until it has moved far enough to matter.
    /// </summary>
    bool ClearOfObstacles(PlantSubFormation<AboveGroundAgent> ag, int i, in OrganPose pose, SoftWork work)
    {
        if (Static.ObstacleTriangles == 0) return true;
        var previous = ag.PreviousGeometry[i];
        var template = ag.GetLaminaTemplate(i);
        float Travel(OrganPose a, OrganPose b) => template?.MotionBound(a, b) ?? a.MotionBound(b);
        var travel = Travel(ag.ClearancePose[i], pose);
        if (previous.Exists) travel = MathF.Max(travel, Travel(ag.ClearancePose[i], previous.Pose));
        // Counted with interlocked steps: the questions of a large plant are asked on all cores.
        if (ag.StaticClearance[i] > travel + CollisionSettings.ContactTolerance) { Interlocked.Increment(ref work.Skips); return true; }
        if (travel < 1e-12f && ag.StaticClearance[i] <= CollisionSettings.ContactTolerance) return false;
        Interlocked.Increment(ref work.Queries);
        var deviation = template == null ? 0f : template.WideReach * pose.Width + template.ReliefAt(pose)
            + MathF.Max(0f, template.LongReach - 1f) * pose.Length;
        deviation += template?.DeformationReach(pose) ?? ContactDeformation.MotionBound(pose.Length, 0, Vector4.Zero, pose.Deformation);
        var probe = deviation + pose.Radius + CollisionSettings.ClearanceProbeDistance;
        var found = Static.ClosestObstacleSurface(pose.Base, pose.Tip, Vector3.Zero, probe, out var hit);
        var clearance = MathF.Max(0f, (found ? hit.Distance : probe) - pose.Radius - deviation);
        ag.ClearancePose[i] = pose; ag.StaticClearance[i] = clearance;
        return clearance > CollisionSettings.ContactTolerance
            && (!previous.Exists || clearance > Travel(previous.Pose, pose) + CollisionSettings.ContactTolerance);
    }

    /// <summary>
    /// An organ that elongated into an obstacle this tick and is still inside it after the pushes
    /// loses the part of this tick's elongation that lies behind the surface, once. A shoot under a
    /// tray therefore stops at the tray and grows along it as the pushes deflect it, instead of
    /// waiting for a certificate that never comes. Never below last tick's length.
    /// </summary>
    void ClipElongation(PlantFormation2 plant, SoftWork work)
    {
        var ag = plant.AG;
        if (Static.ObstacleTriangles == 0) return;
        for (int i = 0; i < ag.Count; ++i)
        {
            if (!CollisionWorld.Eligible(ag, i)) continue;
            var previous = ag.PreviousGeometry[i];
            if (!previous.Exists) continue;
            var length = ag.GetLength(i);
            var grown = length - previous.Pose.Length;
            if (grown <= 1e-7f) continue;
            var pose = ag.GetContactPose(i);
            if (ClearOfObstacles(ag, i, pose, work)) continue;
            if (!Static.ShapeContactUncached(CollisionWorld.Shape(ag, i), CollisionWorld.TrustedPoint(plant, i), StaticSurface.Obstacle,
                out _, out var normal, out var depth) || depth <= CollisionSettings.ContactTolerance) continue;
            work.SolidConfinement[i] = depth;
            // The normal is the push-out direction; the axis points into the solid when it opposes it.
            var axis = Vector3.Transform(Vector3.UnitX, pose.Rotation);
            var into = -Vector3.Dot(axis, normal);
            if (into < .1f) continue;
            var clip = MathF.Min(grown, (depth - CollisionSettings.ContactTolerance) / into);
            if (clip <= 0f) continue;
            ag.ClipLength(i, length - clip);
            ++work.Clips;
            work.ClippedMetres += clip;
        }
    }

    /// <summary>Residual obstacle penetration of an organ after this tick's pushes, metres; zero when clear.</summary>
    public float GetSolidConfinement(PlantFormation2 plant, int organ)
        => Work.TryGetValue(plant, out var work) && organ < work.SolidConfinement.Length ? work.SolidConfinement[organ] : 0f;
}
