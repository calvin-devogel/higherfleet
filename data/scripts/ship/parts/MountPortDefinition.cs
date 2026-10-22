using Godot;

namespace Higherfleet.Ships.Parts;

public enum MountFacing
{
	North,
	East,
	South,
	West
}

[GlobalClass]
public partial class MountPortDefinition : Resource
{
	[Export]
	public string PortId { get; set; } = string.Empty;

	[Export]
	public string CompatibilityClass { get; set; } = string.Empty;

	[Export]
	public int Size { get; set; } = 1;

	[Export]
	public float LoadRating { get; set; }

	[Export]
	public Vector2 LocalPosition { get; set; }

	[Export]
	public Vector2 FaceRange { get; set; }

	[Export]
	public MountFacing Facing { get; set; }
}
