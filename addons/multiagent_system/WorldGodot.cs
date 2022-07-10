using Godot;
using System;

namespace AgentsSystem;

public delegate void GodotChildAction(Node node);

public partial class SimulationWorld
{
	public static GodotChildAction GodotAddChild;
	public static GodotChildAction GodotRemoveChild;
}
