using Godot;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;

public enum MenuStatus{
	EnteredWaiting,
	LeftWaiting,
	Left,
	Entered
}

public class HUD : CanvasLayer
{
	Simulation SimulationSceneInstance;
	Soil SoilSceneInstance;

	public void Load(Simulation simulation, Soil soil)
	{
		SimulationSceneInstance = simulation;
		SoilSceneInstance = soil;
	}

	public override void _Ready()
	{
		AddChild(SimulationSceneInstance);
		AddChild(SoilSceneInstance);
		SoilSceneInstance.Hide();
	}

	private void _on_OptionButton_item_selected(int index)
	{
		switch(index)
		{
			case 0:
				SimulationSceneInstance.Show();
				SoilSceneInstance.Hide();
			break;
			case 1:
				SimulationSceneInstance.Hide();
				SoilSceneInstance.Show();
			break;
		}
	}
}
