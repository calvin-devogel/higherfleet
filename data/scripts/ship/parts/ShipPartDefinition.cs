using Godot;
using Godot.Collections;

namespace Higherfleet.Ships.Parts;

[GlobalClass]
public partial class ShipPartDefinition : Resource
{
	[Export]
	public string PartId { get; set; } = string.Empty;

	[Export]
	public string DisplayName { get; set; } = string.Empty;

	[Export(PropertyHint.MultilineText)]
	public string Description { get; set; } = string.Empty;

	[Export]
	public Array<string> Tags { get; set; } = new();

	[Export]
	public float Mass { get; set; }

	[Export]
	public float BuildCost { get; set; }

	[Export]
	public Array<int> AllowedRotationsDegrees { get; set; } = new() { 0, 90, 180, 270 };

	[Export]
	public Shape2D Footprint { get; set; } = new RectangleShape2D();

	[Export]
	public Array<Vector2> FootprintVertices { get; set; } = new();

	[Export]
	public Array<MountPortDefinition> MountPorts { get; set; } = new();

	[Export]
	public Array<LogisticsPortDefinition> LogisticsPorts { get; set; } = new();

	[Export]
	public Array<PartCapability> Capabilities { get; set; } = new();

	[Export]
	public PackedScene VisualScene { get; set; }

	public bool AllowsRotation(int degrees)
	{
		int normalizedDegrees = ((degrees % 360) + 360) % 360;

		foreach (int allowedDegrees in AllowedRotationsDegrees)
		{
			if (((allowedDegrees % 360) + 360) % 360 == normalizedDegrees)
			{
				return true;
			}
		}

		return false;
	}

	public bool HasCapability<TCapability>() where TCapability : PartCapability
	{
		foreach (PartCapability capability in Capabilities)
		{
			if (capability is TCapability)
			{
				return true;
			}
		}

		return false;
	}

	public MountPortDefinition FindMountPort(string portId)
	{
		foreach (MountPortDefinition port in MountPorts)
		{
			if (port.PortId == portId)
			{
				return port;
			}
		}

		return null;
	}
}
