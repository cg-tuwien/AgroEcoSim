using System;
using System.Numerics;
using AgentsSystem;

namespace Agro;

public interface IPlantFormation : IFormation
{
	SpeciesSettings Parameters { get; }
	bool SeedAlive { get; }
	bool Send(int recipient, IMessage<SeedAgent> msg);
	#if HISTORY_LOG || TICK_LOG
	ulong GetID();
	#endif
}