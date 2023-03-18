using Godot;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;

public partial class Shoots : CanvasLayer
{
	static readonly ColorCodingType[] TransferOptions = (ColorCodingType[])Enum.GetValues(typeof(Agro.ColorCodingType));

	private ShootsVisualisationSettings Parameters;
	public bool UpdateRequest = false;
	public MenuEvent MenuEvent = MenuEvent.None;

	Control LightCutOffControl;

	static bool IsVisible(Visibility visibility) => visibility == Visibility.Visible || visibility == Visibility.MakeVisible;
	static Visibility Vis(bool flag) => flag ? Visibility.MakeVisible : Visibility.MakeInvisible;
	const float LightCutoffMin = 10f;
	const float LightCutoffMax = 1000f;
	const float LightCutoffDiff = LightCutoffMax - LightCutoffMin;

	public void Load(ShootsVisualisationSettings parameters)
	{
		Parameters = parameters;
		var rootsTransferNode = GetNode<OptionButton>("Color/OptionButton");
		foreach(var item in TransferOptions)
			rootsTransferNode.AddItem(item.ToString());
		GetNode<CheckButton>("Visibility/StemsCheckButton").ButtonPressed = IsVisible(parameters.StemsVisibility);
		GetNode<CheckButton>("Visibility/LeafsCheckButton").ButtonPressed = IsVisible(parameters.LeafsVisibility);
		GetNode<CheckButton>("Visibility/BudsCheckButton").ButtonPressed = IsVisible(parameters.BudsVisibility);
		rootsTransferNode.Select(Array.IndexOf(TransferOptions, parameters.TransferFunc));
		GetNode<CheckButton>("Color/UnshadedButton").ButtonPressed = parameters.IsUnshaded;
		LightCutOffControl = GetNode<Control>("LightCutOff");
		GetNode<HSlider>("LightCutOff/HSlider").Value = Math.Clamp(((1f / parameters.LightCutOff) - LightCutoffMin) / LightCutoffDiff, 0f, 1f);

		if (!(parameters.TransferFunc == ColorCodingType.Light || parameters.TransferFunc == ColorCodingType.DailyLightExposure || parameters.TransferFunc == ColorCodingType.All))
			LightCutOffControl.Hide();
	}

	public void StemsVisibility(bool flag)
	{
		Parameters.StemsVisibility = Vis(flag);
		UpdateRequest = true;
	}

	public void LeafsVisibility(bool flag)
	{
		Parameters.LeafsVisibility = Vis(flag);
		UpdateRequest = true;
	}

	public void BudsVisibility(bool flag)
	{
		Parameters.BudsVisibility = Vis(flag);
		UpdateRequest = true;
	}

	public void ShootsTransferFunction(int index)
	{
		Parameters.TransferFunc = TransferOptions[index];
		if (Parameters.TransferFunc == ColorCodingType.Light || Parameters.TransferFunc == ColorCodingType.DailyLightExposure || Parameters.TransferFunc == ColorCodingType.All)
			LightCutOffControl.Show();
		else
			LightCutOffControl.Hide();

		UpdateRequest = true;
	}

	public void Unshaded(bool flag)
	{
		Parameters.IsUnshaded = flag;
		UpdateRequest = true;
	}

	public void LightCutOff(float value)
	{
		Parameters.LightCutOff = 1f / (LightCutoffMin + LightCutoffDiff * value);
		//System.Diagnostics.Debug.WriteLine($"Light CutOff {value} -> {Parameters.LightCutOff}");
		UpdateRequest = true;
	}

	public void MenuEntered() => MenuEvent = MenuEvent.Enter;

	public void MenuLeft(bool dummy = false) => MenuEvent = MenuEvent.Leave;
}
