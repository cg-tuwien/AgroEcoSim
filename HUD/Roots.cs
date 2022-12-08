using Godot;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;

public class Roots : CanvasLayer
{
	static readonly Agro.ColorCodingType[] TransferOptions = (Agro.ColorCodingType[])Enum.GetValues(typeof(Agro.ColorCodingType));

	private RootsVisualisationSettings Parameters;
	public bool UpdateRequest = false;
	public MenuEvent MenuEvent = MenuEvent.None;

	bool IsVisible(Visibility visibility) => visibility == Visibility.Visible || visibility == Visibility.MakeVisible;
	Visibility Vis(bool flag) => flag ? Visibility.MakeVisible : Visibility.MakeInvisible;

	public void Load(RootsVisualisationSettings parameters)
	{
		Parameters = parameters;
		var rootsTransferNode = GetNode<OptionButton>("Roots/Color/ColorCombo");
		for(int i = 0; i < TransferOptions.Length - 3; ++i) //skipping the light related stuff
			rootsTransferNode.AddItem(TransferOptions[i].ToString());
		GetNode<CheckButton>("Roots/Visibility/CheckButton").Pressed = IsVisible(parameters.RootsVisibility);
		rootsTransferNode.Select(Array.IndexOf(TransferOptions, parameters.TransferFunc));
		GetNode<CheckButton>("Roots/Color/UnshadedButton").Pressed = parameters.Unshaded;
	}

	public void RootsVisibility(bool flag)
	{
		Parameters.RootsVisibility = Vis(flag);
		UpdateRequest = true;
	}

	public void RootsTransferFunction(int index)
	{
		Parameters.TransferFunc = TransferOptions[index];
		UpdateRequest = true;
	}

	public void Unshaded(bool flag)
	{
		Parameters.Unshaded = flag;
		UpdateRequest = true;
	}

	public void MenuEntered() => MenuEvent = MenuEvent.Enter;

	public void MenuLeft() => MenuEvent = MenuEvent.Leave;
}
