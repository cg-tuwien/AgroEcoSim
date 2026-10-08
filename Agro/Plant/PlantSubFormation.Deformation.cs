using System.Numerics;
using Agro.Collisions;

namespace Agro;

public partial class PlantSubFormation<T>
{
    // Allocated only for a formation opting into deformable geometry. Stable keys survive
    // compaction; accepted coordinates alone are sufficient to rebuild all geometry caches.
    sealed class DeformationState
    {
        public Vector4 Angles, Pending, Free;
        public uint Revision;
        public float ReactionNewtons, Residual, FreeLength, FreeWidth;
        public bool FreeKnown;
        public bool GradientKnown, ContactIsOwn;
        public OrganPose GradientPose;
        public Vector3 ContactPoint, ContactNormal;
        public Vector4 Gradient, Response;
        public bool ResponseKnown;
    }
    Dictionary<ulong, DeformationState>? Deformations;
    public long RejectedDeformationIncrements { get; private set; }
    public long DeformationSolveTicks { get; private set; }
    public long ReusedFreeDeformations { get; private set; }
    /// <summary>Diagnostic snapshot; force increments are N, residuals are rejected radians.</summary>
    public IEnumerable<(ulong OrganId, Vector4 Angles, uint Revision, float MaximumForceIncrementNewtons, float RejectedAngleRadians)> DeformationDiagnostics()
    {
        if (Deformations == null) yield break;
        foreach (var (id,state) in Deformations)
            yield return (id,state.Angles,state.Revision,state.ReactionNewtons,state.Residual);
    }
    public bool HasContactDeformation => IsAboveGround && Plant.Parameters.ContactDeformationMode == ContactDeformationMode.BroadLeafV1;
    bool Deformable(int index) => HasContactDeformation && (GetOrgan(index) == OrganTypes.Petiole
        || GetOrgan(index) == OrganTypes.Leaf && Plant.Parameters.ContactLeafFoldingEnabled);

    public Vector4 GetDeformation(int index) => Deformations != null
        && Deformations.TryGetValue(GetOrganId(index), out var state) ? state.Angles : Vector4.Zero;

    DeformationState State(int index)
    {
        var states = Deformations ??= [];
        var id = GetOrganId(index);
        if (!states.TryGetValue(id, out var state)) states.Add(id, state = new());
        return state;
    }

    void SetDeformation(int index, Vector4 value)
    {
        if (value == Vector4.Zero && (Deformations == null || !Deformations.ContainsKey(GetOrganId(index)))) return;
        var state = State(index);
        if (state.Angles == value) return;
        state.Angles = value;
        ++state.Revision;
    }

    internal Vector3 GetAttachmentOffset(int index)
    {
        var q = GetDeformation(index);
        if (q == Vector4.Zero || GetOrgan(index) != OrganTypes.Petiole)
            return Vector3.Transform(Vector3.UnitX, GetDirection(index)) * GetLength(index);
        return Vector3.Transform(ContactDeformation.RodPoint(1, GetLength(index), q), GetDirection(index));
    }

    static Quaternion AttachmentRotation(in OrganPose pose) => pose.Width == 0 && pose.Deformation != Vector4.Zero
        ? Quaternion.Normalize(pose.Rotation * ContactDeformation.RodRotation(pose.Deformation, 2)) : pose.Rotation;

    void PruneDeformationState()
    {
        if (Deformations == null) return;
        RemovedGrowthIds.Clear();
        foreach (var id in Deformations.Keys) if (!OrganIndices.ContainsKey(id)) RemovedGrowthIds.Add(id);
        foreach (var id in RemovedGrowthIds) Deformations.Remove(id);
    }

    // Recompute the unloaded equilibrium at every growth tick. Accepted contact angles
    // never become a new rest shape; neither hours-per-tick nor relaxation rounds alter EI.
    void ResetElasticDeformation()
    {
        if (Deformations == null) return;
        for (var i=0;i<Count;++i)
        {
            var old=GetDeformation(i);
            if(old==Vector4.Zero)continue;
            var before=AttachmentRotation(GetContactPose(i));
            SetDeformation(i,Vector4.Zero);
            var after=AttachmentRotation(GetContactPose(i));
            if(before!=after)PropagateToSubtree(Src(),i,Quaternion.Normalize(after*Quaternion.Conjugate(before)));
        }
        TreeCache.UpdateBases(this);
    }

    Vector4 LimitedShape(int index,Vector4 q)
    {
        var blade=GetOrgan(index)==OrganTypes.Leaf;
        q=ContactDeformation.Clamp(q,blade,ContactBendLimit(index));
        if(blade)return q;
        ref var a=ref Src()[index];
        var delta=Quaternion.Normalize(a.Orientation*Quaternion.Conjugate(a.baseOrientation));
        var baseAngle=2*MathF.Atan2(new Vector3(delta.X,delta.Y,delta.Z).Length(),MathF.Abs(delta.W));
        var internalAngle=new Vector2(q.X,q.Y).Length()+new Vector2(q.Z,q.W).Length();
        var available=MathF.Max(0,ContactBendLimit(index)-baseAngle);
        return internalAngle>available ? q*(available/internalAngle) : q;
    }

    /// <param name="relaxation">Fraction of the way from the current shape to the unloaded
    /// equilibrium taken this tick. One rebuilds the free shape outright (certified engine);
    /// the soft engine passes its per-tick response so contact folds persist and relax.</param>
    void GravityDeformation(float relaxation = 1f)
    {
        if(!HasContactDeformation)return;
        var started=System.Diagnostics.Stopwatch.GetTimestamp();
        for(var i=0;i<Count;++i)
        {
            if(!Deformable(i))continue;
            var pose=GetContactPose(i);
            if(pose.Length<=0 || Src()[i].Radius<=0)continue;
            var state=State(i);state.ReactionNewtons=0;state.Residual=0;
            var weight=GetWeight(i,Plant.Parameters)*.001f*9.81f;
            var load=Vector4.Zero;
            if(GetOrgan(i)==OrganTypes.Leaf)
            {
                for(var side=-1;side<=1;side+=2)
                {
                    var point=pose.Base+Vector3.Transform(new Vector3(pose.Length*.5f,0,side*pose.Width*.5f),pose.Rotation);
                    load+=ShapeGradient(i,i,point,-Vector3.UnitY)*(weight*.5f);
                }
            }
            else
            {
                for(var span=0;span<3;++span)
                {
                    var point=pose.Base+Vector3.Transform(new Vector3(pose.Length*(span+.5f)/3f,0,0),pose.Rotation);
                    load+=ShapeGradient(i,i,point,-Vector3.UnitY)*(weight/3f);
                }
                var carried=Weights[i]-weight;
                if(carried>0)
                {
                    var own=GetBaseCenter(i)+Vector3.Transform(new Vector3(pose.Length*.5f,0,0),pose.Rotation);
                    var point=(WeightedCentroids[i]-own*weight)/carried+FieldOrigin;
                    load+=ShapeGradient(i,-1,point,-Vector3.UnitY)*carried;
                }
            }
            var q=LimitedShape(i,load*ShapeCompliance(i));
            if (state.FreeKnown && state.Free == q && state.FreeLength == pose.Length && state.FreeWidth == pose.Width)
                ++ReusedFreeDeformations;
            else if(GetLaminaTemplate(i) is { } blade)
            {
                for(var attempt=0;attempt<16 && !blade.DeformationAdmissible(pose,q);++attempt)q*=.5f;
                if(!blade.DeformationAdmissible(pose,q))q=Vector4.Zero;
            }
            state.Free=q;state.FreeLength=pose.Length;state.FreeWidth=pose.Width;state.FreeKnown=true;
            if(relaxation<1f)
            {
                q=LimitedShape(i,Vector4.Lerp(state.Angles,q,relaxation));
                if(GetLaminaTemplate(i) is { } shell)
                    for(var attempt=0;attempt<16 && !shell.DeformationAdmissible(pose,q);++attempt)q=Vector4.Lerp(state.Free,q,.5f);
            }
            var before=AttachmentRotation(pose);
            SetDeformation(i,q);
            var after=AttachmentRotation(GetContactPose(i));
            if(before!=after)PropagateToSubtree(Src(),i,Quaternion.Normalize(after*Quaternion.Conjugate(before)));
            if(q!=Vector4.Zero)TreeCache.UpdateBases(this);
        }
        DeformationSolveTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-started;
    }

    void BeginDeformationSweep()
    {
        if (Deformations == null) return;
        foreach (var state in Deformations.Values) state.Pending = Vector4.Zero;
    }

    bool ApplyDeformationIncrement(int index)
    {
        if (Deformations == null || !Deformations.TryGetValue(GetOrganId(index), out var state) || state.Pending == Vector4.Zero) return false;
        var old = state.Angles;
        var step = Vector4.Clamp(state.Pending, new(-CollisionSettings.MaxCorrectionRadians), new(CollisionSettings.MaxCorrectionRadians));
        var candidate = LimitedShape(index, old + step);
        if (GetLaminaTemplate(index) is { } blade)
        {
            var pose = GetContactPose(index);
            for (var n = 0; n < 10 && !blade.DeformationAdmissible(pose, candidate); ++n)
            { candidate = Vector4.Lerp(old, candidate, .5f); ++RejectedDeformationIncrements; }
            if (!blade.DeformationAdmissible(pose, candidate))
            { state.Residual = MathF.Max(state.Residual,state.Pending.Length()); return false; }
        }
        state.Residual = MathF.Max(state.Residual,(state.Pending-(candidate-old)).Length());
        if (Vector4.DistanceSquared(candidate, old) < 1e-16f) return false;
        SetDeformation(index, candidate);
        return true;
    }

    // Angular compliance in radians / (N m). Three L/3 sections sum to the original
    // L/EI compliance under a pure end moment, rather than tripling that compliance.
    Vector4 ShapeCompliance(int index)
    {
        ref var a = ref Src()[index];
        if (a.Organ == OrganTypes.Petiole)
        {
            var ei = Plant.Parameters.PetioleElasticModulus * MathF.PI * MathF.Pow(a.Radius, 4) / 4f;
            return new(a.Length / MathF.Max(1e-20f, 3f * ei));
        }
        var e = MathF.Max(1, Plant.Parameters.LeafElasticModulus > 0 ? Plant.Parameters.LeafElasticModulus : Plant.Parameters.GreenElasticModulus);
        var longitudinal = a.Length / MathF.Max(1e-20f, 3f * e * BladeInertia(a.Radius, BladeKeelAt(index)));
        // Transverse plate bending uses lamina tissue; the effective midrib keel is
        // reserved for longitudinal bending. Neither value is a measured Bergenia fit.
        var thickness = AboveGroundAgent.LeafThickness;
        var fold = a.Radius / MathF.Max(1e-20f, e * a.Length * thickness * thickness * thickness / 12f);
        return new(longitudinal, longitudinal, fold, fold);
    }

    // Material coordinates are recovered once per contact, then four local derivatives
    // avoid cancellation at large facade world coordinates. Supports transport the actual
    // distal point, so their internal bends include the blade's leverage too.
    Vector4 ShapeGradient(int index, int contacted, Vector3 point, Vector3 normal)
    {
        if (!Deformable(index)) return Vector4.Zero;
        var pose = GetContactPose(index);
        var cached=State(index);
        if(cached.GradientKnown && cached.GradientPose==pose && cached.ContactIsOwn==(index==contacted)
            && cached.ContactPoint==point && cached.ContactNormal==normal) return cached.Gradient;
        cached.GradientKnown=true;cached.GradientPose=pose;cached.ContactIsOwn=index==contacted;
        cached.ContactPoint=point;cached.ContactNormal=normal;cached.ResponseKnown=false;
        var local = Vector3.Transform(point - pose.Base, Quaternion.Conjugate(pose.Rotation));
        var direction = Vector3.Transform(normal, Quaternion.Conjugate(pose.Rotation));
        var blade = GetOrgan(index) == OrganTypes.Leaf;
        float hingeHeight = 0;
        var material = blade ? GetLaminaTemplate(index)!.MaterialPoint(pose, point, out hingeHeight) : local;
        var fraction = 1f;
        Vector3 offset = default;
        if (!blade)
        {
            if (index == contacted)
                fraction = ContactDeformation.ClosestRodFraction(local, pose.Length, pose.Deformation);
            else offset = Vector3.Transform(local - ContactDeformation.RodPoint(1, pose.Length, pose.Deformation),
                Quaternion.Conjugate(ContactDeformation.RodRotation(pose.Deformation, 2)));
        }
        var result = Vector4.Zero;
        const float epsilon = .001f;
        for (var axis = 0; axis < 4; ++axis)
        {
            var plus = pose.Deformation; var minus = plus;
            plus[axis] += epsilon; minus[axis] -= epsilon;
            var p = blade ? ContactDeformation.BladePoint(material, pose.Length, plus, hingeHeight)
                : ContactDeformation.RodPoint(fraction, pose.Length, plus) + Vector3.Transform(offset, ContactDeformation.RodRotation(plus, 2));
            var m = blade ? ContactDeformation.BladePoint(material, pose.Length, minus, hingeHeight)
                : ContactDeformation.RodPoint(fraction, pose.Length, minus) + Vector3.Transform(offset, ContactDeformation.RodRotation(minus, 2));
            result[axis] = Vector3.Dot(p - m, direction) / (2 * epsilon);
        }
        return cached.Gradient=result;
    }

    Vector4 ShapeResponse(int index,Vector4 gradient)
    {
        var cached=State(index);
        var pose=GetContactPose(index);
        if(cached.ResponseKnown && cached.GradientPose==pose && cached.Gradient==gradient)return cached.Response;
        var response=ShapeCompliance(index)*gradient;
        var blade=GetLaminaTemplate(index);
        for(var axis=0;axis<4;++axis)
        {
            if(response[axis]==0)continue;
            var trial=pose.Deformation;trial[axis]+=MathF.CopySign(.001f,response[axis]);
            var limited=LimitedShape(index,trial);
            if(MathF.Abs(limited[axis]-trial[axis])>1e-5f || (blade!=null && !blade.WithinStrainTolerance(pose,trial)))response[axis]=0;
        }
        if(cached.GradientPose==pose && cached.Gradient==gradient)
        {cached.ResponseKnown=true;cached.Response=response;}
        return response;
    }

    double ShapeMobility(int index, int contacted, Vector3 point, Vector3 normal)
    {
        if (!Deformable(index)) return 0;
        var g = ShapeGradient(index, contacted, point, normal);
        return Vector4.Dot(g, ShapeResponse(index,g));
    }

    void QueueShapeCorrection(int index, int contacted, Vector3 point, Vector3 normal, float force)
    {
        if (!Deformable(index)) return;
        var state = State(index);
        state.Pending += ShapeResponse(index,ShapeGradient(index, contacted, point, force<0?-normal:normal)) * MathF.Abs(force);
        state.ReactionNewtons = MathF.Max(state.ReactionNewtons, MathF.Abs(force));
    }

    internal double SharedShapeMobility(int common,int first,Vector3 a,int second,Vector3 b,Vector3 normal)
    {
        if(common<0 || !Deformable(common) || IsMechanicallyAnchored(common))return 0;
        var g=ShapeGradient(common,first,a,normal)+ShapeGradient(common,second,b,-normal);
        return Vector4.Dot(g,ShapeResponse(common,g));
    }

    internal void QueueSharedShapeCorrection(int common,int first,Vector3 a,int second,Vector3 b,Vector3 normal,float force)
    {
        if(common<0 || !Deformable(common) || IsMechanicallyAnchored(common))return;
        if(force<0){normal=-normal;force=-force;}
        var g=ShapeGradient(common,first,a,normal)+ShapeGradient(common,second,b,-normal);
        State(common).Pending+=ShapeResponse(common,g)*force;
    }

    void WriteDeformationState(BinaryWriter writer)
    {
        writer.Write(RejectedDeformationIncrements);
        writer.Write(Deformations?.Count ?? 0);
        if (Deformations == null) return;
        foreach (var (id, state) in Deformations.OrderBy(x => x.Key))
        { writer.Write(id); WriteValue(writer, state.Angles); writer.Write(state.Revision); }
    }

    void ReadDeformationState(BinaryReader reader)
    {
        RejectedDeformationIncrements = reader.ReadInt64();
        var count = reader.ReadInt32();
        if (count < 0 || count > Count || (!HasContactDeformation && count != 0)) throw new InvalidDataException("Invalid deformation state.");
        Deformations = count == 0 ? null : new(count);
        for (var i = 0; i < count; ++i)
        {
            var id = reader.ReadUInt64(); var q = ReadValue<Vector4>(reader); var revision = reader.ReadUInt32();
            var organ = FindOrgan(id);
            if (organ < 0 || !Deformable(organ) || !float.IsFinite(q.X + q.Y + q.Z + q.W)
                || Vector4.DistanceSquared(ContactDeformation.Clamp(q,GetOrgan(organ)==OrganTypes.Leaf,ContactBendLimit(organ)),q)>1e-12f)
                throw new InvalidDataException("Invalid deformable organ.");
            if (GetLaminaTemplate(organ) is { } blade && !blade.WithinStrainTolerance(GetContactPose(organ),q))
                throw new InvalidDataException("Checkpoint blade exceeds the surface strain tolerance.");
            if (!Deformations!.TryAdd(id, new() { Angles = q, Revision = revision }))
                throw new InvalidDataException("Duplicate deformable organ identity.");
        }
    }
}
