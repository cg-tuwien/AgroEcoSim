using System.Numerics;

namespace Agro.Collisions;

public sealed partial class CollisionWorld
{
    readonly List<PlantFormation2> ActivePlants = [];
    [ThreadStatic] static List<int>? BlockedOrgans;

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
                if (CollisionSettings.ObstaclePass && !ClearOfObstacles(ag, i, pose))
                    Correct(ag, i, trusted, StaticSurface.Obstacle);
                if (CollisionSettings.SoilPass && SoilEligible(ag, i, pose))
                    Correct(ag, i, trusted, StaticSurface.Soil);
            }
            if (!ag.EndContactSweep(MaxCorrectionRadians)) break;
        }

        var blocked = BlockedOrgans ??= [];
        blocked.Clear();
        for (int i = 0; i < ag.Count; ++i)
        {
            if (!Eligible(ag, i)) continue;
            if (!StaticAdmissible(plant, i))
            {
                var affected = new List<int> { i };
                for (int j = 0; j < affected.Count; ++j) affected.AddRange(ag.GetChildren(affected[j]));
                bool Accept() => affected.All(k => StaticAdmissible(plant, k));
                var shape = Shape(ag, i);
                Static.ShapeContact(shape, TrustedPoint(plant, i), StaticSurface.Obstacle, out _, out var normal, out _);
                if (!ag.TryRedirectGrowth(i, normal, Accept))
                {
                    if (!ag.TryShortenGrowth(i, Accept)) blocked.Add(i);
                    ag.SetObstruction(i, true);
                }
            }
        }
        if (blocked.Count > 0)
        {
            ag.RestorePreviousGeometry(blocked);
            // A local rollback cannot undo transport by an obstructed parent. Escalate only
            // the causally affected branch; unrelated crowns keep their accepted growth.
            var restored=new HashSet<int>();
            for (int pass = 0; pass < ag.Count; ++pass)
            {
                var remaining = Enumerable.Range(0,ag.Count).Where(i => Eligible(ag,i) && !StaticAdmissible(plant, i)).ToArray();
                if (remaining.Length == 0) break;
                var parents = remaining.SelectMany(ag.ChangedContactComponent).Distinct().ToArray();
                ag.RestorePreviousGeometry(parents);
                blocked.Clear(); blocked.AddRange(parents);
                var changed=false;
                foreach(var parent in parents) changed |= restored.Add(parent);
                if(!changed) break; // Only an already inadmissible previous configuration remains.
            }
        }
        RefreshContactLoads(plant);
    }

    void RefreshContactLoads(PlantFormation2 plant)
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

    bool StaticAdmissible(PlantFormation2 plant, int i)
    {
        var ag = plant.AG; var shape = Shape(ag, i);
        if (!Eligible(ag, i)) return true;
        var trusted = TrustedPoint(plant, i);
        var previous = Previous(ag, i, shape);
        if (CollisionSettings.ObstaclePass && !ClearOfObstacles(ag, i, shape.Pose)
            && (Static.ShapeContact(shape, trusted, StaticSurface.Obstacle, out _, out _, out _)
                || !Static.ShapeSweepClear(previous, shape, StaticSurface.Obstacle))) return false;
        return !CollisionSettings.SoilPass || !SoilEligible(ag, i, shape.Pose)
            || (!Static.ShapeContact(shape, trusted, StaticSurface.Soil, out _, out _, out _)
                && (Static.InsideSoil(previous.Pose.Base, 1f) || Static.ShapeSweepClear(previous, shape, StaticSurface.Soil)));
    }

    static bool Eligible(PlantSubFormation<AboveGroundAgent> ag, int i)
        => ag.GetOrgan(i) != OrganTypes.Seed && (ag.GetLength(i) > 0f || ag.GetBaseRadius(i) > 0f);

    bool SoilEligible(PlantSubFormation<AboveGroundAgent> ag, int i, in OrganPose pose)
    {
        if (ag.GetIsRizome(i)) return false;
        var previous = ag.PreviousGeometry[i];
        if (pose.Tip.Y > pose.Base.Y && Static.InsideSoil(pose.Base, 1f)
            && (!previous.Exists || Static.InsideSoil(previous.Pose.Base, 1f))) return false;
        var parent = ag.GetParent(i);
        return !((parent < 0 || ag.GetIsRizome(parent)) && Static.InsideSoil(pose.Base, 1f));
    }

    static Vector3 TrustedPoint(PlantFormation2 plant, int i)
    {
        var ag = plant.AG;
        var previous = ag.PreviousGeometry[i];
        if (previous.Exists) return (previous.Pose.Base + previous.Pose.Tip) * 0.5f;
        var parent = ag.GetParent(i);
        return parent >= 0 ? ag.GetBaseCenterWorld(parent) : plant.Position + ag.FieldOrigin;
    }

    bool ClearOfObstacles(PlantSubFormation<AboveGroundAgent> ag, int i, in OrganPose pose)
    {
        if (Static.ObstacleTriangles == 0) return true;
        var previous = ag.PreviousGeometry[i];
        var template = ag.GetLaminaTemplate(i);
        float Travel(OrganPose a, OrganPose b) => template?.MotionBound(a, b) ?? a.MotionBound(b);
        var travel = Travel(ag.ClearancePose[i], pose);
        if (previous.Exists) travel = MathF.Max(travel, Travel(ag.ClearancePose[i], previous.Pose));
        if (ag.StaticClearance[i] > travel + CollisionSettings.ContactTolerance)
        { System.Threading.Interlocked.Increment(ref ClearanceSkips); return true; }
        if (travel < 1e-12f && ag.StaticClearance[i] <= CollisionSettings.ContactTolerance) return false;
        System.Threading.Interlocked.Increment(ref StaticQueries);
        var deviation = template == null ? 0f : template.WideReach * pose.Width + template.ReliefAt(pose)
            + MathF.Max(0f, template.LongReach - 1f) * pose.Length;
        var probe = deviation + pose.Radius + CollisionSettings.ClearanceProbeDistance;
        var found = Static.ClosestObstacleSurface(pose.Base, pose.Tip, Vector3.Zero, probe, out var hit);
        var clearance = MathF.Max(0f, (found ? hit.Distance : probe) - pose.Radius - deviation);
        ag.ClearancePose[i] = pose; ag.StaticClearance[i] = clearance;
        return clearance > CollisionSettings.ContactTolerance
            && (!previous.Exists || clearance > Travel(previous.Pose, pose) + CollisionSettings.ContactTolerance);
    }

    void Correct(PlantSubFormation<AboveGroundAgent> ag, int i, Vector3 trusted, StaticSurface surface)
    {
        if (!Static.ShapeContact(Shape(ag, i), trusted, surface, out var point, out var normal, out var depth)) return;
        if (!ag.QueueContactCorrection(i, point, normal, depth + CollisionSettings.ContactTolerance)) return;
        if (surface == StaticSurface.Obstacle) System.Threading.Interlocked.Increment(ref ObstacleContacts);
        else System.Threading.Interlocked.Increment(ref SoilContacts);
    }

    void RecordSupport(PlantSubFormation<AboveGroundAgent> ag, int i, StaticSurface surface)
    {
        var pose = ag.GetContactPose(i);
        if (ag.GetLaminaTemplate(i) is { } lamina)
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
