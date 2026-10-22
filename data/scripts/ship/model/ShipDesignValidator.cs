using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Higherfleet.Ships.Parts;

namespace Higherfleet.Ships.Model;

public static class ShipDesignValidator
{
	private const float PortPositionTolerance = 0.01f;

	public static IReadOnlyList<DesignIssue> Validate(ShipDesign design)
	{
		ArgumentNullException.ThrowIfNull(design);

		List<DesignIssue> issues = new();
		Dictionary<string, ShipPartInstance> partsById = design.Parts.ToDictionary(part => part.InstanceId);

		ValidateRoot(design, partsById, issues);
		ValidateParts(design.Parts, issues);
		ValidateConnections(design.Connections, partsById, issues);
		ValidateLogisticsRoutes(design.LogisticsRoutes, design.Parts, partsById, issues);
		ValidateLogisticsJunctions(
			design.LogisticsJunctions,
			design.LogisticsRoutes,
			partsById,
			issues);
		ValidateLogisticsPassOvers(
			design.LogisticsPassOvers,
			design.LogisticsJunctions,
			design.LogisticsRoutes,
			partsById,
			issues);
		ValidateLogisticsPassThroughs(
			design.LogisticsPassThroughs,
			design.LogisticsRoutes,
			partsById,
			issues);

		return issues;
	}

	private static void ValidateRoot(
		ShipDesign design,
		IReadOnlyDictionary<string, ShipPartInstance> partsById,
		List<DesignIssue> issues)
	{
		List<ShipPartInstance> commandParts = design.Parts
			.Where(part => part.Definition.HasCapability<CommandCapability>())
			.ToList();

		if (string.IsNullOrWhiteSpace(design.RootPartInstanceId)
			|| !partsById.TryGetValue(design.RootPartInstanceId, out ShipPartInstance rootPart))
		{
			issues.Add(new DesignIssue(
				DesignIssueSeverity.Error,
				"missing-root",
				"Ship design must identify its bridge/CIC as the root part."));
			return;
		}

		if (commandParts.Count != 1 || commandParts[0].InstanceId != rootPart.InstanceId)
		{
			issues.Add(new DesignIssue(
				DesignIssueSeverity.Error,
				"invalid-root-count",
				"Ship design must contain exactly one command-capable part, and it must be the root part.",
				rootPart.InstanceId));
		}
	}

	private static void ValidateParts(
		IReadOnlyList<ShipPartInstance> parts,
		List<DesignIssue> issues)
	{
		foreach (ShipPartInstance part in parts)
		{
			if (string.IsNullOrWhiteSpace(part.Definition.PartId))
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"missing-definition-id",
					"Part definition must have a stable ID.",
					part.InstanceId));
			}

			if (part.Definition.Footprint == null)
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"missing-footprint",
					$"{part.Definition.DisplayName} must define an authoritative footprint.",
					part.InstanceId));
			}

			if (!part.Definition.AllowsRotation(part.RotationDegrees))
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"unsupported-rotation",
					$"{part.Definition.DisplayName} does not allow rotation {part.RotationDegrees} degrees.",
					part.InstanceId));
			}

			HashSet<string> portIds = new(StringComparer.Ordinal);
			foreach (MountPortDefinition port in part.Definition.MountPorts)
			{
				if (string.IsNullOrWhiteSpace(port.PortId) || !portIds.Add(port.PortId))
				{
					issues.Add(new DesignIssue(
						DesignIssueSeverity.Error,
						"invalid-port-id",
						$"{part.Definition.DisplayName} has a missing or duplicate mount port ID.",
						part.InstanceId));
				}

				if (string.IsNullOrWhiteSpace(port.CompatibilityClass) || port.Size <= 0)
				{
					issues.Add(new DesignIssue(
						DesignIssueSeverity.Error,
						"invalid-port-compatibility",
						$"Mount port '{port.PortId}' must define a compatibility class and positive size.",
						part.InstanceId));
				}

				if ((int)port.Facing < 0 || (int)port.Facing > 3)
				{
					issues.Add(new DesignIssue(
						DesignIssueSeverity.Error,
						"invalid-port-facing",
						$"Mount port '{port.PortId}' has an unsupported facing.",
						part.InstanceId));
				}
			}
		}
	}

	private static void ValidateConnections(
		IReadOnlyList<PhysicalConnection> connections,
		IReadOnlyDictionary<string, ShipPartInstance> partsById,
		List<DesignIssue> issues)
	{
		HashSet<PhysicalPortReference> occupiedPorts = new();

		foreach (PhysicalConnection connection in connections)
		{
			if (!partsById.TryGetValue(connection.First.PartInstanceId, out ShipPartInstance firstPart)
				|| !partsById.TryGetValue(connection.Second.PartInstanceId, out ShipPartInstance secondPart))
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"missing-connection-part",
					"Physical connection refers to a part instance that is not in this design."));
				continue;
			}

			if (firstPart.InstanceId == secondPart.InstanceId)
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"self-connection",
					"A part cannot be physically connected to itself.",
					firstPart.InstanceId));
				continue;
			}

			MountPortDefinition firstPort = firstPart.Definition.FindMountPort(connection.First.PortId);
			MountPortDefinition secondPort = secondPart.Definition.FindMountPort(connection.Second.PortId);

			if (firstPort == null || secondPort == null)
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"missing-connection-port",
					"Physical connection refers to a mount port that does not exist."));
				continue;
			}

			bool firstPortOccupied = !occupiedPorts.Add(connection.First);
			bool secondPortOccupied = !occupiedPorts.Add(connection.Second);
			if (firstPortOccupied || secondPortOccupied)
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"occupied-port",
					"A physical mount port can participate in only one connection.",
					firstPart.InstanceId));
				continue;
			}

			if (!AreCompatible(firstPart, firstPort, secondPart, secondPort))
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"incompatible-ports",
					"Connected mount ports must have matching compatibility class and size, and face one another.",
					firstPart.InstanceId));
				continue;
			}

			Vector2 firstPosition = firstPart.GetWorldPortPosition(firstPort);
			Vector2 secondPosition = secondPart.GetWorldPortPosition(secondPort);
			if (firstPosition.DistanceTo(secondPosition) > PortPositionTolerance)
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"misaligned-ports",
					"Connected mount ports must occupy the same ship-design position.",
					firstPart.InstanceId));
			}
		}
	}

	private static void ValidateLogisticsRoutes(
		IReadOnlyList<LogisticsRoute> routes,
		IReadOnlyList<ShipPartInstance> parts,
		IReadOnlyDictionary<string, ShipPartInstance> partsById,
		List<DesignIssue> issues)
	{
		HashSet<string> routeIds = new(StringComparer.Ordinal);
		HashSet<LogisticsPortReference> connectedConsumers = new();
		foreach (LogisticsRoute route in routes)
		{
			if (string.IsNullOrWhiteSpace(route.RouteId) || !routeIds.Add(route.RouteId))
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"invalid-logistics-route-id",
					"Logistics routes must have unique, non-empty IDs."));
			}

			bool validEndpoints = true;
			foreach (LogisticsEndpoint endpoint in new[] { route.Start, route.End })
			{
				if (!endpoint.Port.HasValue)
					continue;

				LogisticsPortReference reference = endpoint.Port.Value;
				if (!partsById.TryGetValue(reference.PartInstanceId, out ShipPartInstance part))
				{
					issues.Add(new DesignIssue(
						DesignIssueSeverity.Error,
						"missing-logistics-route-part",
						$"Logistics route '{route.RouteId}' refers to a missing part."));
					validEndpoints = false;
					continue;
				}

				LogisticsPortDefinition port = FindLogisticsPort(part, reference.PortId);
				if (port == null || port.System != route.System)
				{
					issues.Add(new DesignIssue(
						DesignIssueSeverity.Error,
						"invalid-logistics-route-endpoints",
						$"Conduit '{route.RouteId}' must use matching system ports."));
					validEndpoints = false;
				}
				else if (port.Direction == LogisticsPortDirection.Consumer && !connectedConsumers.Add(reference))
				{
					issues.Add(new DesignIssue(
						DesignIssueSeverity.Error,
						"duplicate-logistics-consumer-route",
						$"Consumer port '{reference.PortId}' can have only one incident conduit."));
				}
			}

			if (validEndpoints)
				ValidateLogisticsRouteGeometry(route, parts, issues);
		}
	}

	private static void ValidateLogisticsRouteGeometry(
		LogisticsRoute route,
		IReadOnlyList<ShipPartInstance> parts,
		List<DesignIssue> issues)
	{
		IReadOnlyList<Vector2> points = route.GetPoints(parts);
		bool validGeometry = points.All(point => LogisticsRoute.SnapToGrid(point) == point);
		for (int i = 0; validGeometry && i < points.Count - 1; i++)
		{
			validGeometry = LogisticsRoute.IsValidSegment(points[i], points[i + 1]);
		}

		if (!validGeometry)
		{
			issues.Add(new DesignIssue(
				DesignIssueSeverity.Error,
				"invalid-logistics-route-geometry",
				$"Logistics route '{route.RouteId}' must follow the {LogisticsRoute.GridSize}-unit grid using only 90- or 45-degree segments."));
		}
	}

	private static LogisticsPortDefinition FindLogisticsPort(ShipPartInstance part, string portId)
	{
		foreach (LogisticsPortDefinition port in part.Definition.LogisticsPorts)
		{
			if (port.PortId == portId)
				return port;
		}

		return null;
	}

	private static void ValidateLogisticsJunctions(
		IReadOnlyList<LogisticsJunction> junctions,
		IReadOnlyList<LogisticsRoute> routes,
		IReadOnlyDictionary<string, ShipPartInstance> partsById,
		List<DesignIssue> issues)
	{
		HashSet<string> junctionIds = new(StringComparer.Ordinal);
		HashSet<(LogisticsSystem System, Vector2 Position)> occupiedPositions = new();
		foreach (LogisticsJunction junction in junctions)
		{
			if (string.IsNullOrWhiteSpace(junction.JunctionId) || !junctionIds.Add(junction.JunctionId))
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"invalid-logistics-junction-id",
					"Logistics junctions must have unique, non-empty IDs."));
			}

			Vector2 snappedPosition = LogisticsRoute.SnapJunctionToGrid(junction.Position);
			if (snappedPosition != junction.Position
				|| !occupiedPositions.Add((junction.System, junction.Position)))
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"invalid-logistics-junction-position",
					"Logistics junctions must occupy unique points on the routing grid."));
			}

			int crossingRoutes = routes.Count(route =>
				route.System == junction.System
				&& RouteContainsPoint(route, junction.Position, partsById));
			if (crossingRoutes < 2)
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Warning,
					"orphan-logistics-junction",
					"Logistics junction does not connect at least two routes."));
			}
		}
	}

	private static bool RouteContainsPoint(
		LogisticsRoute route,
		Vector2 point,
		IReadOnlyDictionary<string, ShipPartInstance> partsById)
	{
		foreach (LogisticsEndpoint endpoint in new[] { route.Start, route.End })
		{
			if (endpoint.Port.HasValue
				&& (!partsById.TryGetValue(endpoint.Port.Value.PartInstanceId, out ShipPartInstance part)
					|| FindLogisticsPort(part, endpoint.Port.Value.PortId) == null))
				return false;
		}

		IReadOnlyList<Vector2> points = route.GetPoints(partsById.Values.ToArray());
		for (int i = 0; i < points.Count - 1; i++)
		{
			if (PointLiesOnSegment(point, points[i], points[i + 1]))
				return true;
		}

		return false;
	}

	private static bool PointLiesOnSegment(Vector2 point, Vector2 start, Vector2 end)
	{
		return LogisticsRoute.PointLiesOnSegment(point, start, end);
	}

	private static void ValidateLogisticsPassOvers(
		IReadOnlyList<LogisticsPassOver> passOvers,
		IReadOnlyList<LogisticsJunction> junctions,
		IReadOnlyList<LogisticsRoute> routes,
		IReadOnlyDictionary<string, ShipPartInstance> partsById,
		List<DesignIssue> issues)
	{
		HashSet<string> markerIds = new(StringComparer.Ordinal);
		HashSet<(LogisticsSystem System, Vector2 Position)> positions = new();
		foreach (LogisticsPassOver passOver in passOvers)
		{
			if (string.IsNullOrWhiteSpace(passOver.MarkerId) || !markerIds.Add(passOver.MarkerId))
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"invalid-logistics-pass-over-id",
					"Logistics pass-over markers must have unique, non-empty IDs."));
			}

			if (LogisticsRoute.SnapJunctionToGrid(passOver.Position) != passOver.Position
				|| !positions.Add((passOver.System, passOver.Position)))
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"invalid-logistics-pass-over-position",
					"Logistics pass-over markers must occupy unique crossing points on the routing grid."));
			}

			bool hasJunction = junctions.Any(junction =>
				junction.System == passOver.System && junction.Position == passOver.Position);
			int crossingRoutes = routes.Count(route =>
				route.System == passOver.System
				&& RouteContainsPoint(route, passOver.Position, partsById));
			if (hasJunction || crossingRoutes < 2)
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Warning,
					"orphan-logistics-pass-over",
					"Logistics pass-over marker must mark an unconnected crossing of at least two routes."));
			}
		}
	}

	private static void ValidateLogisticsPassThroughs(
		IReadOnlyList<LogisticsPassThrough> passThroughs,
		IReadOnlyList<LogisticsRoute> routes,
		IReadOnlyDictionary<string, ShipPartInstance> partsById,
		List<DesignIssue> issues)
	{
		HashSet<string> markerIds = new(StringComparer.Ordinal);
		HashSet<(LogisticsSystem System, Vector2 Position)> positions = new();
		foreach (LogisticsPassThrough passThrough in passThroughs)
		{
			if (string.IsNullOrWhiteSpace(passThrough.MarkerId) || !markerIds.Add(passThrough.MarkerId))
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"invalid-logistics-pass-through-id",
					"Logistics pass-through markers must have unique, non-empty IDs."));
			}

			if (LogisticsRoute.SnapToGrid(passThrough.Position) != passThrough.Position
				|| !positions.Add((passThrough.System, passThrough.Position)))
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Error,
					"invalid-logistics-pass-through-position",
					"Logistics pass-through markers must occupy unique points on the routing grid."));
			}

			if (!routes.Any(route =>
				route.System == passThrough.System
				&& RouteContainsPoint(route, passThrough.Position, partsById)))
			{
				issues.Add(new DesignIssue(
					DesignIssueSeverity.Warning,
					"orphan-logistics-pass-through",
					"Logistics pass-through marker must lie on a route of the same system."));
			}
		}
	}

	private static bool AreCompatible(
		ShipPartInstance firstPart,
		MountPortDefinition firstPort,
		ShipPartInstance secondPart,
		MountPortDefinition secondPort)
	{
		return ArePortTypesCompatible(firstPort, secondPort)
			&& AreOpposing(
				firstPart.GetWorldPortFacing(firstPort),
				secondPart.GetWorldPortFacing(secondPort));
	}

	private static bool ArePortTypesCompatible(
		MountPortDefinition firstPort,
		MountPortDefinition secondPort)
	{
		return firstPort.Size == secondPort.Size
			&& string.Equals(
				firstPort.CompatibilityClass,
				secondPort.CompatibilityClass,
				StringComparison.Ordinal);
	}

	private static bool AreOpposing(MountFacing first, MountFacing second)
	{
		return ((int)first + 2) % 4 == (int)second;
	}
}
