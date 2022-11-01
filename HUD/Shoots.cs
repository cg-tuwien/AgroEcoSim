using Godot;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;

public class Shoots : CanvasLayer
{
static readonly Agro.ColorCodingType[] TransferOptions = (Agro.ColorCodingType[])Enum.GetValues(typeof(Agro.ColorCodingType));

	private ShootsVisualisationSettings Parameters;
	public bool UpdateRequest = false;
	public MenuEvent MenuEvent = MenuEvent.None;

	Control LightCutOffControl;

	bool IsVisible(Visibility visibility) => visibility == Visibility.Visible || visibility == Visibility.MakeVisible;
	Visibility Vis(bool flag) => flag ? Visibility.MakeVisible : Visibility.MakeInvisible;

	public void Load(ShootsVisualisationSettings parameters)
	{
		Parameters = parameters;
		var rootsTransferNode = GetNode<OptionButton>("Shoots/Color/ColorCombo");
		foreach(var item in TransferOptions)
			rootsTransferNode.AddItem(item.ToString());
		GetNode<CheckButton>("Shoots/Visibility/StemsCheckButton").Pressed = IsVisible(parameters.StemsVisibility);
		GetNode<CheckButton>("Shoots/Visibility/LeafsCheckButton").Pressed = IsVisible(parameters.LeafsVisibility);
		GetNode<CheckButton>("Shoots/Visibility/BudsCheckButton").Pressed = IsVisible(parameters.BudsVisibility);
		rootsTransferNode.Select(Array.IndexOf(TransferOptions, parameters.TransferFunc));
		LightCutOffControl = GetNode<Control>("Shoots/Color/LightCutOff");
		GetNode<HSlider>("Shoots/Color/LightCutOff/HSlider").Value = Math.Clamp(((1f / parameters.LightCutOff) - 100f) / 900f, 0f, 1f);

		if (!(parameters.TransferFunc == ColorCodingType.Light || parameters.TransferFunc == ColorCodingType.All))
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
		if (Parameters.TransferFunc == ColorCodingType.Light || Parameters.TransferFunc == ColorCodingType.All)
			LightCutOffControl.Show();
		else
			LightCutOffControl.Hide();

		UpdateRequest = true;
	}

	public void LightCutOff(float value)
	{
		Parameters.LightCutOff = 1f / (100f + 900f * value);
		UpdateRequest = true;
	}

	public void MenuEntered() => MenuEvent = MenuEvent.Enter;

	public void MenuLeft() => MenuEvent = MenuEvent.Leave;
}
