namespace Higherfleet.Ships.Model;

public readonly record struct PhysicalPortReference(string PartInstanceId, string PortId);

public sealed record PhysicalConnection(
	PhysicalPortReference First,
	PhysicalPortReference Second);
