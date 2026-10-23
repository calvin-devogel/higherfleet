using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Higherfleet.Ships.Parts;

namespace Higherfleet.Ships.Model;

public sealed class ShipDesign
{
	private readonly List<ShipPartInstance> _parts = new();
	private readonly List<PhysicalConnection> _connections = new();
	private readonly List<LogisticsRoute> _logisticsRoutes = new();
	private readonly List<LogisticsJunction> _logisticsJunctions = new();
	private readonly List<LogisticsPassOver> _logisticsPassOvers = new();
	private readonly List<LogisticsPassThrough> _logisticsPassThroughs = new();
	private IReadOnlyList<ShipSubassembly> _subassemblies;

	public string DesignId { get; }
	internal int StructureRevision { get; private set; }
	public string RootPartInstanceId { get; private set; } = string.Empty;
	public IReadOnlyList<ShipPartInstance> Parts => _parts;
	public IReadOnlyList<PhysicalConnection> Connections => _connections;
	public IReadOnlyList<LogisticsRoute> LogisticsRoutes => _logisticsRoutes;
	public IReadOnlyList<LogisticsJunction> LogisticsJunctions => _logisticsJunctions;
	public IReadOnlyList<LogisticsPassOver> LogisticsPassOvers => _logisticsPassOvers;
	public IReadOnlyList<LogisticsPassThrough> LogisticsPassThroughs => _logisticsPassThroughs;
	public float TotalMassKg => _parts.Sum(part => part.Definition.Mass);

	public ShipDesign(string designId)
	{
		if (string.IsNullOrWhiteSpace(designId))
		{
			throw new ArgumentException("A ship design must have a stable ID.", nameof(designId));
		}

		DesignId = designId;
	}

	public void AddPart(ShipPartInstance part)
	{
		ArgumentNullException.ThrowIfNull(part);

		if (string.IsNullOrWhiteSpace(part.InstanceId))
		{
			throw new ArgumentException("A part instance must have a stable ID.", nameof(part));
		}

		if (_parts.Exists(existing => existing.InstanceId == part.InstanceId))
		{
			throw new InvalidOperationException($"Part instance ID '{part.InstanceId}' is already in this design.");
		}

		_parts.Add(part);
		InvalidateStructure();
	}

	public void SetRootPart(string instanceId)
	{
		if (!_parts.Exists(part => part.InstanceId == instanceId))
		{
			throw new ArgumentException($"Part instance '{instanceId}' is not in this design.", nameof(instanceId));
		}

		RootPartInstanceId = instanceId;
		InvalidateStructure();
	}

	public void ClearRootPart()
	{
		RootPartInstanceId = string.Empty;
		InvalidateStructure();
	}

	public void AddConnection(PhysicalConnection connection)
	{
		ArgumentNullException.ThrowIfNull(connection);
		_connections.Add(connection);
		InvalidateStructure();
	}

	public bool RemoveConnection(PhysicalConnection connection)
	{
		ArgumentNullException.ThrowIfNull(connection);
		bool removed = _connections.Remove(connection);
		if (removed)
			InvalidateStructure();
		return removed;
	}

	public void AddLogisticsRoute(LogisticsRoute route)
	{
		ArgumentNullException.ThrowIfNull(route);
		if (_logisticsRoutes.Exists(existing => existing.RouteId == route.RouteId))
			throw new InvalidOperationException($"Logistics route ID '{route.RouteId}' is already in this design.");

		_logisticsRoutes.Add(route);
	}

	public bool RemoveLogisticsRoute(string routeId)
	{
		bool removed = _logisticsRoutes.RemoveAll(route => route.RouteId == routeId) > 0;
		if (removed)
			PruneUnusedLogisticsMarkers();
		return removed;
	}

	public void SplitLogisticsRoutesAt(LogisticsSystem system, Vector2 position)
	{
		foreach (LogisticsRoute route in _logisticsRoutes.Where(route => route.System == system).ToArray())
		{
			IReadOnlyList<Vector2> points = route.GetPoints(_parts);
			if (points[0] == position || points[^1] == position)
				continue;

			for (int i = 0; i < points.Count - 1; i++)
			{
				if (!LogisticsRoute.PointLiesOnSegment(position, points[i], points[i + 1]))
					continue;

				LogisticsEndpoint split = new(position);
				_logisticsRoutes.Remove(route);
				AddLogisticsRoute(new LogisticsRoute(
					route.RouteId, system, route.Start, split,
					points.Skip(1).Take(i).Where(point => point != position)));
				AddLogisticsRoute(new LogisticsRoute(
					$"logistics-route-{Guid.NewGuid():N}", system, split, route.End,
					points.Skip(i + 1).Take(points.Count - i - 2).Where(point => point != position)));
				break;
			}
		}
	}

	public void PruneUnusedLogisticsMarkers()
	{
		int RouteCount(LogisticsSystem system, Vector2 position) =>
			_logisticsRoutes.Count(route =>
			{
				if (route.System != system)
					return false;
				IReadOnlyList<Vector2> points = route.GetPoints(_parts);
				for (int i = 0; i < points.Count - 1; i++)
					if (LogisticsRoute.PointLiesOnSegment(position, points[i], points[i + 1]))
						return true;
				return false;
			});

		_logisticsJunctions.RemoveAll(marker => RouteCount(marker.System, marker.Position) < 2);
		_logisticsPassOvers.RemoveAll(marker => RouteCount(marker.System, marker.Position) < 2);
		_logisticsPassThroughs.RemoveAll(marker => RouteCount(marker.System, marker.Position) == 0);
	}

	public void AddLogisticsJunction(LogisticsJunction junction)
	{
		ArgumentNullException.ThrowIfNull(junction);
		if (_logisticsJunctions.Exists(existing => existing.JunctionId == junction.JunctionId))
			throw new InvalidOperationException($"Logistics junction ID '{junction.JunctionId}' is already in this design.");
		if (_logisticsJunctions.Exists(existing =>
			existing.System == junction.System && existing.Position == junction.Position))
			throw new InvalidOperationException("A logistics junction already exists at this position for this system.");

		_logisticsJunctions.Add(junction);
	}

	public bool RemoveLogisticsJunction(string junctionId)
	{
		return _logisticsJunctions.RemoveAll(junction => junction.JunctionId == junctionId) > 0;
	}

	public void AddLogisticsPassOver(LogisticsPassOver passOver)
	{
		ArgumentNullException.ThrowIfNull(passOver);
		if (_logisticsPassOvers.Exists(existing => existing.MarkerId == passOver.MarkerId))
			throw new InvalidOperationException($"Logistics pass-over ID '{passOver.MarkerId}' is already in this design.");
		if (_logisticsPassOvers.Exists(existing =>
			existing.System == passOver.System && existing.Position == passOver.Position))
			throw new InvalidOperationException("A logistics pass-over already exists at this position for this system.");

		_logisticsPassOvers.Add(passOver);
	}

	public bool RemoveLogisticsPassOver(string markerId)
	{
		return _logisticsPassOvers.RemoveAll(passOver => passOver.MarkerId == markerId) > 0;
	}

	public void AddLogisticsPassThrough(LogisticsPassThrough passThrough)
	{
		ArgumentNullException.ThrowIfNull(passThrough);
		if (_logisticsPassThroughs.Exists(existing => existing.MarkerId == passThrough.MarkerId))
			throw new InvalidOperationException($"Logistics pass-through ID '{passThrough.MarkerId}' is already in this design.");
		if (_logisticsPassThroughs.Exists(existing =>
			existing.System == passThrough.System && existing.Position == passThrough.Position))
			throw new InvalidOperationException("A logistics pass-through already exists at this position for this system.");

		_logisticsPassThroughs.Add(passThrough);
	}

	public bool RemoveLogisticsPassThrough(string markerId)
	{
		return _logisticsPassThroughs.RemoveAll(passThrough => passThrough.MarkerId == markerId) > 0;
	}

	public bool RemovePart(string instanceId)
	{
		if (instanceId == RootPartInstanceId)
		{
			return false;
		}

		return RemoveParts(new[] { instanceId }) > 0;
	}

	public int RemoveParts(IEnumerable<string> instanceIds, bool preserveLogisticsMarkers = false)
	{
		ArgumentNullException.ThrowIfNull(instanceIds);
		HashSet<string> idsToRemove = new(instanceIds);
		int removedCount = _parts.RemoveAll(part => idsToRemove.Contains(part.InstanceId));
		if (removedCount == 0)
		{
			return 0;
		}
		InvalidateStructure();

		if (idsToRemove.Contains(RootPartInstanceId))
		{
			ClearRootPart();
		}

		_connections.RemoveAll(connection =>
			idsToRemove.Contains(connection.First.PartInstanceId)
			|| idsToRemove.Contains(connection.Second.PartInstanceId));
		_logisticsRoutes.RemoveAll(route =>
			idsToRemove.Any(id => route.Start.ReferencesPart(id) || route.End.ReferencesPart(id)));
		if (!preserveLogisticsMarkers)
			PruneUnusedLogisticsMarkers();
		return removedCount;
	}

	public IReadOnlyList<ShipSubassembly> GetSubassemblies()
	{
		if (_subassemblies != null)
			return _subassemblies;

		Dictionary<string, ShipPartInstance> partsById = _parts.ToDictionary(part => part.InstanceId);
		Dictionary<string, List<string>> neighbors = _parts.ToDictionary(
			part => part.InstanceId,
			_ => new List<string>());

		foreach (PhysicalConnection connection in _connections)
		{
			if (!neighbors.ContainsKey(connection.First.PartInstanceId)
				|| !neighbors.ContainsKey(connection.Second.PartInstanceId))
				continue;

			neighbors[connection.First.PartInstanceId].Add(connection.Second.PartInstanceId);
			neighbors[connection.Second.PartInstanceId].Add(connection.First.PartInstanceId);
		}

		HashSet<string> visited = new();
		List<ShipSubassembly> subassemblies = new();
		foreach (ShipPartInstance part in _parts)
		{
			if (!visited.Add(part.InstanceId))
				continue;

			List<ShipPartInstance> componentParts = new();
			Queue<string> pending = new();
			pending.Enqueue(part.InstanceId);
			bool containsRoot = false;

			while (pending.Count > 0)
			{
				string currentId = pending.Dequeue();
				ShipPartInstance currentPart = partsById[currentId];
				componentParts.Add(currentPart);
				containsRoot |= currentId == RootPartInstanceId;

				foreach (string neighborId in neighbors[currentId])
				{
					if (visited.Add(neighborId))
						pending.Enqueue(neighborId);
				}
			}

			subassemblies.Add(new ShipSubassembly(componentParts, containsRoot));
		}

		_subassemblies = subassemblies;
		return _subassemblies;
	}

	private void InvalidateStructure()
	{
		_subassemblies = null;
		StructureRevision++;
	}
}
