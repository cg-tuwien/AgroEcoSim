using AgentsSystem;

namespace Agro;

public interface IPlantFormation : IFormation
{
	SpeciesSettings Parameters { get; }
	bool SeedAlive { get; }
	bool Send(int recipient, IMessage<SeedAgent> msg);
}