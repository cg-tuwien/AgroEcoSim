using System.Numerics;

namespace Agro;

internal enum TreeModuleKind : byte { None, Trained, DorsiventralMainline }
internal enum TreeModuleBirthState : byte { Queued, CertifiedPending, Provisional, Live, Withdrawn }
internal enum TreeClosureState : byte { Sampled, CertifiedPending, ImmediateAttempt, Live, SiteLost, WithdrawnReady }

internal sealed class TreeModuleRecord
{
    internal ulong RootStableId, ClosureFateId, PredecessorRootStableId, SiteOwnerStableId, OriginPetioleStableId;
    internal TreeModuleKind Kind;
    internal TreeModuleBirthState BirthState;
    internal uint CreatedTick, BirthCommitTick, AdmittedTick;
    internal bool ImmediatePath, CommittedByNormalCensus;
    internal float SiteRollRadians, OpeningLengthM, FrozenAllowanceM;
    internal int AllowanceCycle = int.MinValue;
    internal Quaternion TargetFrameInOwnerLocal = Quaternion.Identity, OpeningStartLocal = Quaternion.Identity;
}

internal sealed class TreeClosureFate
{
    internal ulong FateId, PredecessorRootStableId, SiteOwnerStableId, OriginPetioleStableId, AttemptRootStableId;
    internal uint SampledTick, AttemptTick;
    internal int SampledShootCycle, AttemptCount;
    internal float SiteRollRadians;
    internal Quaternion OwnerFrameAtSample, TargetFrameInOwnerLocal, OpeningStartLocal;
    internal Vector3 SiteOffsetOwnerLocal;
    internal TreeModuleKind Kind;
    internal TreeClosureState State;
    internal AboveGroundAgent Template;
}

internal readonly record struct TreeModuleStep(ulong RootStableId, TreeModuleKind Kind,
    Quaternion OpeningStartLocal, float OpeningLengthM, float AllowanceM)
{
    internal bool IsDorsiventral => Kind == TreeModuleKind.DorsiventralMainline;
    internal bool HasProgram => IsDorsiventral && OpeningLengthM > 0f && AllowanceM > OpeningLengthM;
}
