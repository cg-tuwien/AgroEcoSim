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
	Simulation SimulationControlsInstance;
	Soil SoilControlsInstance;
	Roots RootsControlsInstance;
	Shoots ShootsControlsInstance;

	public void Load(Simulation simulation, Soil soil, Roots roots, Shoots shoots)
	{
		SimulationControlsInstance = simulation;
		SoilControlsInstance = soil;
		RootsControlsInstance = roots;
		ShootsControlsInstance = shoots;
	}

	public override void _Ready()
	{
		AddChild(SimulationControlsInstance);
		AddChild(SoilControlsInstance);
		AddChild(RootsControlsInstance);
		AddChild(ShootsControlsInstance);
		SoilControlsInstance.Hide();
		RootsControlsInstance.Hide();
		ShootsControlsInstance.Hide();
	}

	private void _on_OptionButton_item_selected(int index)
	{
		switch(index)
		{
			case 0:
				SimulationControlsInstance.Show();
				SoilControlsInstance.Hide();
				RootsControlsInstance.Hide();
				ShootsControlsInstance.Hide();
			break;
			case 1:
				SimulationControlsInstance.Hide();
				SoilControlsInstance.Show();
				RootsControlsInstance.Hide();
				ShootsControlsInstance.Hide();
			break;
			case 2:
				SimulationControlsInstance.Hide();
				SoilControlsInstance.Hide();
				RootsControlsInstance.Show();
				ShootsControlsInstance.Hide();
			break;
			case 3:
				SimulationControlsInstance.Hide();
				SoilControlsInstance.Hide();
				RootsControlsInstance.Hide();
				ShootsControlsInstance.Show();
			break;
		}
	}
}
