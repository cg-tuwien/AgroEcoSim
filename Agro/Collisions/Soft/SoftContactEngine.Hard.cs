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
        ag.RefreshAnchors(Static);
        ag.ClearSupport();
        for (int round = 0; round < CollisionSettings.SoftHardPushRounds; ++round)
        {
            ag.BeginContactSweep();
            for (int i = 0; i < ag.Count; ++i)
            {
                if (!CollisionWorld.Eligible(ag, i)) continue;
                var pose = ag.GetContactPose(i);
                var trusted = CollisionWorld.TrustedPoint(plant, i);
                if (CollisionSettings.ObstaclePass && !ClearOfObstacles(ag, i, pose, work))
                    Correct(ag, i, trusted, StaticSurface.Obstacle, work);
                if (CollisionSettings.SoilPass && World.SoilEligible(ag, i, pose))
                    Correct(ag, i, trusted, StaticSurface.Soil, work);
            }
            if (!ag.EndContactSweep(CollisionSettings.MaxCorrectionRadians)) break;
        }
        if (CollisionSettings.SolidElongationClip && CollisionSettings.ObstaclePass) ClipElongation(plant, work);
        World.RefreshContactLoads(plant);
    }

    void Correct(PlantSubFormation<AboveGroundAgent> ag, int i, Vector3 trusted, StaticSurface surface, SoftWork work)
    {
        if (!Static.ShapeContactUncached(CollisionWorld.Shape(ag, i), trusted, surface, out var point, out var normal, out var depth)) return;
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
