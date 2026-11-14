using System.Runtime.CompilerServices;

namespace Agro;

public partial class PlantSubFormation<T>
{
    // A separate wing mesh is fruit-wall tissue of its parent mericarp. This
    // completed-boundary pass derives phase only; it never grows, moves, funds,
    // sheds or reparents an organ, and never advances an elapsed-time clock.
    internal void SynchronizeSamaraUnitPhenology()
    {
        if (!IsAboveGround || Plant.Parameters.Behavior != Behavior.Woody
            || Plant.Parameters.FruitSettings is not { Enabled: true, FruitType: FruitType.Samara, WingsPerFruit: > 0 })
            return;

        var live = Src();
        for (int i = 0; i < live.Length; ++i)
        {
            ref var wing = ref Unsafe.As<T, AboveGroundAgent>(ref live[i]);
            if (wing.Organ != OrganTypes.FruitWing || Deaths.Contains(i)) continue;
            int parent = wing.Parent;
            // Structural orphans/support loss remain the responsibility of the
            // normal topology/death paths, rather than a fabricated ripeness event.
            if (parent < 0 || parent >= live.Length || Deaths.Contains(parent)) continue;
            ref readonly var body = ref Unsafe.As<T, AboveGroundAgent>(ref live[parent]);
            if (body.Organ == OrganTypes.Fruit)
                wing.SynchronizeTreeFruitWingPhase(body);
        }
    }
}
