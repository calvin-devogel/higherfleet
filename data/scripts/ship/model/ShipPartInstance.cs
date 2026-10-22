using Godot;
using Higherfleet.Ships.Parts;

namespace Higherfleet.Ships.Model;

public sealed class ShipPartInstance
{
	public string InstanceId { get; }
	public ShipPartDefinition Definition { get; }
	public Vector2 Position { get; }
	public int RotationDegrees { get; }

	public ShipPartInstance(
		string instanceId,
		ShipPartDefinition definition,
		Vector2 position,
		int rotationDegrees)
	{
		InstanceId = instanceId;
		Definition = definition;
		Position = position;
		RotationDegrees = ((rotationDegrees % 360) + 360) % 360;
	}

	public Vector2 GetWorldPortPosition(MountPortDefinition port)
	{
		return Position + port.LocalPosition.Rotated(Mathf.DegToRad(RotationDegrees));
	}

	public MountFacing GetWorldPortFacing(MountPortDefinition port)
	{
		int facingDegrees = (int)port.Facing * 90;
		int worldFacingDegrees = (facingDegrees + RotationDegrees) % 360;
		return (MountFacing)(worldFacingDegrees / 90);
	}

	public Vector2 GetWorldLogisticsPortPosition(LogisticsPortDefinition port)
	{
		return Position + port.LocalPosition.Rotated(Mathf.DegToRad(RotationDegrees));
	}
}
