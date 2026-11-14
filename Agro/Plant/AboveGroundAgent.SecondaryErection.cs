using System.Numerics;

namespace Agro;

public partial struct AboveGroundAgent
{
    internal static int SecondaryCycle(AgroWorld world, uint tick) => TreeCycleAt(world, tick);
    // Own actuation leaves its attachment fixed. A carrier rotates the offset
    // too; both preserve independent loaded offsets and all local programmes.
    internal void TransportSecondaryMaterial(Quaternion delta, bool carrier)
    {
        if (delta == Quaternion.Identity) return;
        TransportDevelopment(delta, carryAttachment: carrier);
    }
}
