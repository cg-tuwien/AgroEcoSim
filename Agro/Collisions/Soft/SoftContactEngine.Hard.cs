using System.Numerics;

namespace Agro.Collisions;

public sealed partial class SoftContactEngine
{
    /// <summary>
    /// Bounded pushes out of obstacles and soil through the same joint compliance, then one
    /// closed-form clip of this tick's elongation for anything still inside an obstacle. No
    /// admissibility test, no redirect search, no rollback.
    /// </summary>
    void ResolveHard(PlantFormation2 plant)
    {
        var ag = plant.AG; var work = Work[plant];
        ag.EnsureClearanceCapacity(ag.Count);
        if (work.SolidConfinement.Length < ag.Count)
            Array.Resize(ref work.SolidConfinement, Math.Max(ag.Count, work.SolidConfinement.Length * 2));
        Array.Clear(work.SolidConfinement, 0, ag.Count);
        if (work.ObstacleTouch.Length < ag.Count) { work.ObstacleTouch = new StaticTouch[Math.Max(ag.Count, work.ObstacleTouch.Length * 2)]; work.SoilTouch = new StaticTouch[work.ObstacleTouch.Length]; }
        for (int i = 0; i < ag.Count; ++i) { work.ObstacleTouch[i].Round = -1; work.SoilTouch[i].Round = -1; }
        long T() => TraceTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var t0 = T();
        ag.RefreshAnchors(Static);
        ag.ClearSupport();
        var t1 = T(); work.HardAnchorTicks += t1 - t0;
        for (int round = 0; round < CollisionSettings.SoftHardPushRounds; ++round)
        {
            ag.BeginContactSweep(resetDiagnostics: false);
            for (int i = 0; i < ag.Count; ++i)
            {
                if (!CollisionWorld.Eligible(ag, i)) continue;
                var pose = ag.GetContactPose(i);
                var trusted = CollisionWorld.TrustedPoint(plant, i);
                var ta = T();
                if (CollisionSettings.ObstaclePass && !ClearOfObstacles(ag, i, pose, work))
                {
                    var tb = T(); work.HardClearTicks += tb - ta;
                    Correct(ag, i, pose, trusted, StaticSurface.Obstacle, work, round);
                    ta = T(); work.HardObstacleTicks += ta - tb;
                }
                else { var tb = T(); work.HardClearTicks += tb - ta; ta = tb; }
                if (CollisionSettings.SoilPass && World.SoilEligible(ag, i, pose))
                {
                    var tb = T(); work.HardSoilEligibleTicks += tb - ta;
                    Correct(ag, i, pose, trusted, StaticSurface.Soil, work, round);
                    work.HardSoilTicks += T() - tb;
                }
                else work.HardSoilEligibleTicks += T() - ta;
            }
            var te = T();
            var moved = ag.EndContactSweep(CollisionSettings.MaxCorrectionRadians);
            work.HardSweepTicks += T() - te; ++work.HardRounds;
            if (!moved) break;
        }
        var tc = T();
        if (CollisionSettings.SolidElongationClip && CollisionSettings.ObstaclePass) ClipElongation(plant, work);
        var tl = T(); work.HardClipTicks += tl - tc;
        RefreshContactLoads(plant, work);
        work.HardLoadTicks += T() - tl;
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
        var ag = plant.AG;
        ag.ClearSupport();
        ag.RefreshMechanicalLoads();
        var supportReach = CollisionSettings.ContactTolerance + 2 * FoliageGeometry.Skin;
        for (int i = 0; i < ag.Count; ++i)
        {
            if (!CollisionWorld.Eligible(ag, i)) continue;
            var pose = ag.GetContactPose(i);
            // An organ pushed against a solid this tick is resting on it if the push came from
            // below: the push already found the witness and the normal.
            ref var obstacle = ref work.ObstacleTouch[i];
            if (obstacle.Round >= 0) { if (obstacle.Normal.Y > 0f) ag.RecordSupportReaction(i, obstacle.Point, obstacle.Normal); }
            else if (!ProvablyClear(ag, i, pose, supportReach, work)) World.RecordSupport(ag, i, StaticSurface.Obstacle);
            ref var soil = ref work.SoilTouch[i];
            if (soil.Round >= 0) { if (soil.Normal.Y > 0f) ag.RecordSupportReaction(i, soil.Point, soil.Normal); }
            else if (World.SoilEligible(ag, i, pose)) World.RecordSupport(ag, i, StaticSurface.Soil);
        }
        ag.UpdateSupportedLoads();
        ag.RefreshAnchors(Static);
    }

    /// <summary>Whether the clearance cache proves no obstacle within <paramref name="margin"/> of the
    /// organ at its current pose, without issuing a query.</summary>
    bool ProvablyClear(PlantSubFormation<AboveGroundAgent> ag, int i, in OrganPose pose, float margin, SoftWork work)
    {
        if (Static.ObstacleTriangles == 0) return true;
        var template = ag.GetLaminaTemplate(i);
        var travel = template?.MotionBound(ag.ClearancePose[i], pose) ?? ag.ClearancePose[i].MotionBound(pose);
        if (ag.StaticClearance[i] > travel + margin) { ++work.SupportSkips; return true; }
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
        work.MaxSolidPenetration = MathF.Max(work.MaxSolidPenetration, depth);
        if (!ag.QueueContactCorrection(i, point, normal, depth + CollisionSettings.ContactTolerance)) return;
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
        if (ag.StaticClearance[i] > travel + CollisionSettings.ContactTolerance) { ++work.Skips; return true; }
        if (travel < 1e-12f && ag.StaticClearance[i] <= CollisionSettings.ContactTolerance) return false;
        ++work.Queries;
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
