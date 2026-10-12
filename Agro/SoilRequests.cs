using AgentsSystem;

namespace Agro;

/// <summary>A soil that accepts water requests routed from the plants' buffers.</summary>
public interface ISoilRequestSink
{
	void Enqueue(PlantFormation2 plant, int cell, int part, float amount_g);
}

/// <summary>
/// Moves the plants' buffered water requests into the soil before the post is delivered.
///
/// Plants tick in parallel and each root asks the soil for water as it goes. Appending into the
/// soil directly needed one lock for every root of every plant and left the order of the
/// requests - and so the outcome of a scarce cell shared by several roots - to the thread pool.
/// Each plant now records its requests on itself (<see cref="PlantFormation2.WaterRequests"/>)
/// and this walk, which runs once per tick on one thread, hands them over in formation order.
/// The parallel tick is reproducible because of that order; nothing else in a tick crosses
/// formations.
/// </summary>
internal static class SoilRequestRouter
{
	public static void Gather(AgroWorld world, ISoilFormation root)
	{
		var formations = world.FormationList;
		for (int i = 0; i < formations.Count; ++i)
		{
			if (formations[i] is not PlantFormation2 plant || plant.WaterRequests.Count == 0) continue;
			var requests = plant.WaterRequests;
			var sink = root.RequestSink(plant.SoilIndex);
			if (sink != null)
				for (int r = 0; r < requests.Count; ++r)
					sink.Enqueue(plant, requests[r].Cell, requests[r].Part, requests[r].Amount_g);
			requests.Clear();
		}
	}
}
