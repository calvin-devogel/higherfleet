using Godot;

public partial class HullButton : Control
{
	private Component_Structural hull;
	private PackedScene hullScene;
	private int hullIndex = 0;
	private string pathToBridge;

	public override void _Ready()
	{
		pathToBridge = GetTree().GetNodesInGroup("RootBridge")[0].GetPath().ToString();
		hullScene = GD.Load<PackedScene>("res://data/gameObjects/builder/hull.tscn");
	}
	private void OnPressed()
	{
		hull = hullScene.Instantiate<Component_Structural>();
		hull.Name = "Hull_" + hullIndex++;
		GetTree().Root.GetNode<Component_Bridge>(pathToBridge).AddChild(hull);
		GD.Print(hull.Name + " created.");
	}
}
