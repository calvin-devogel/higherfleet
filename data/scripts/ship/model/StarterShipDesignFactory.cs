using System;
using Godot;
using Higherfleet.Ships.Parts;

namespace Higherfleet.Ships.Model;

public static class StarterShipDesignFactory
{
	private const string RootDefinitionPath = "res://data/parts/root_bridge.tres";
	private const string StructuralHullDefinitionPath = "res://data/parts/structural_hull.tres";

	public static ShipDesign Create()
	{
		ShipPartDefinition rootDefinition = LoadDefinition(RootDefinitionPath);
		ShipPartDefinition hullDefinition = LoadDefinition(StructuralHullDefinitionPath);

		ShipPartInstance root = new("root", rootDefinition, Vector2.Zero, 0);
		ShipPartInstance hull = new("hull-1", hullDefinition, new Vector2(0, -256), 0);

		ShipDesign design = new("starter-ship");
		design.AddPart(root);
		design.AddPart(hull);
		design.SetRootPart(root.InstanceId);
		design.AddConnection(new PhysicalConnection(
			new PhysicalPortReference(root.InstanceId, "north"),
			new PhysicalPortReference(hull.InstanceId, "south")));

		return design;
	}

	private static ShipPartDefinition LoadDefinition(string path)
	{
		return GD.Load<ShipPartDefinition>(path)
			?? throw new InvalidOperationException($"Could not load ship part definition at '{path}'.");
	}
}
