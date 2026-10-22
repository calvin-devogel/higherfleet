using Godot;

namespace Higherfleet.Ships.Parts;

[GlobalClass]
public partial class StructuralCapability : PartCapability
{
	[Export]
	public float StructuralStrength { get; set; }
}
