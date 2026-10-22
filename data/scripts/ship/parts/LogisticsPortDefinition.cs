using Godot;

namespace Higherfleet.Ships.Parts;

[GlobalClass]
public partial class LogisticsPortDefinition : Resource
{
	[Export]
	public string PortId { get; set; } = string.Empty;

	[Export]
	public LogisticsSystem System { get; set; }

	[Export]
	public LogisticsPortDirection Direction { get; set; }

	[Export]
	public Vector2 LocalPosition { get; set; }
}
