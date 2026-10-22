using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Higherfleet.Ships.Parts;

namespace Higherfleet.Ships.Model;

public readonly record struct LogisticsEndpoint(Vector2 Position, LogisticsPortReference? Port = null)
{
	public Vector2 GetPosition(IReadOnlyList<ShipPartInstance> parts)
	{
		if (!Port.HasValue)
			return Position;

		LogisticsPortReference reference = Port.Value;
		ShipPartInstance part = parts.FirstOrDefault(part => part.InstanceId == reference.PartInstanceId)
			?? throw new InvalidOperationException($"Missing logistics endpoint part '{reference.PartInstanceId}'.");
		LogisticsPortDefinition port = part.Definition.LogisticsPorts.FirstOrDefault(port => port.PortId == reference.PortId)
			?? throw new InvalidOperationException($"Missing logistics endpoint port '{reference.PortId}'.");
		return LogisticsRoute.SnapToGrid(part.GetWorldLogisticsPortPosition(port));
	}

	public bool ReferencesPart(string partId) => Port?.PartInstanceId == partId;
}
