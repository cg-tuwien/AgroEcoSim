using Godot;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;

public partial class Roots : CanvasLayer
{
	static readonly ColorCodingType[] TransferOptions = (ColorCodingType[])Enum.GetValues(typeof(ColorCodingType));

	private RootsVisualisationSettings Parameters;
	public bool UpdateRequest = false;
	public MenuEvent MenuEvent = MenuEvent.None;

	static bool IsVisible(Visibility visibility) => visibility == Visibility.Visible || visibility == Visibility.MakeVisible;
	static Visibility Vis(bool flag) => flag ? Visibility.MakeVisible : Visibility.MakeInvisible;

	public void Load(RootsVisualisationSettings parameters)
	{
		Parameters = parameters;
		var rootsTransferNode = GetNode<OptionButton>("Color/OptionButton");
		for(int i = 0; i < TransferOptions.Length - 3; ++i) //skipping the light related stuff
			rootsTransferNode.AddItem(TransferOptions[i].ToString());
		GetNode<CheckButton>("VisibilityCheckButton").ButtonPressed = IsVisible(parameters.RootsVisibility);
		rootsTransferNode.Select(Array.IndexOf(TransferOptions, parameters.TransferFunc));
		GetNode<CheckButton>("Color/UnshadedButton").ButtonPressed = parameters.IsUnshaded;
	}

	public void RootsVisibility(bool flag)
	{
		Parameters.RootsVisibility = Vis(flag);
		UpdateRequest = true;
	}

	public void RootsTransferFunction(int index)
	{
		System.Diagnostics.Debug.WriteLine($"ROOTS TRANSFER FUNCTION {index}");
		Parameters.TransferFunc = TransferOptions[index];
		UpdateRequest = true;
	}

	public void Unshaded(bool flag)
	{
		Parameters.IsUnshaded = flag;
		UpdateRequest = true;
	}

	public void MenuEntered() => MenuEvent = MenuEvent.Enter;

	public void MenuLeft(bool dummy = false) => MenuEvent = MenuEvent.Leave;
}
