using Godot;

namespace Higherfleet.Ships.Parts;

[GlobalClass]
public partial class CommandCapability : PartCapability
{
	[Export]
	public int MinimumCrew { get; set; }
}
