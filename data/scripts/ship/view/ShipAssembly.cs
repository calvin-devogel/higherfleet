using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;
using Higherfleet.Ships.Model;
using Higherfleet.Ships.Parts;

namespace Higherfleet.Ships.View;

public partial class ShipAssembly : Node2D
{
	private const float ViewScale = 0.5f;
	private const float InternalVisualScale = 0.92f;
	private const float SnapRadius = 44;
	private const float ConnectionPositionTolerance = 0.01f;
	private const float InternalCoverageSampleSpacing = 16;
	private const string InternalSupportCompatibilityClass = "internal-support";
	private const string RootDefinitionPath = "res://data/parts/root_bridge.tres";
	private const string StructuralDefinitionPath = "res://data/parts/structural_hull.tres";
	private const string HalfStructuralDefinitionPath = "res://data/parts/structural_hull_half.tres";

	private int _nextPartNumber = 1;
	private ShipPartDefinition _rootDefinition;
	private ShipPartDefinition _structuralDefinition;
	private ShipPartDefinition _halfStructuralDefinition;
	private ShipPartDefinition _halfStructuralLongDefinition;
	private ShipPartDefinition _structuralTriangleDefinition;
	private ShipPartDefinition _halfStructuralTriangleDefinition;
	private ShipPartDefinition _structuralNotchedDefinition;
	private ShipPartDefinition _structuralLargeDefinition;
	private ShipPartDefinition _structuralWideDefinition;
	private ShipPartDefinition _fuelTankDefinition;
	private ShipPartDefinition _largeFuelTankDefinition;
	private ShipPartDefinition _powerCoreDefinition;
	private ShipPartDefinition _largeGeneratorDefinition;
	private ShipPartDefinition _thrusterDefinition;
	private ShipPartDefinition _gimballedThrusterDefinition;
	private bool _isHoldingPart;
	private Vector2 _previewPosition;
	private Vector2 _heldCursorPosition;
	private int _heldRotationDegrees;
	private PlacementTarget? _currentPlacement;
	private string _heldPartInstanceId = string.Empty;
	private ShipPartDefinition _heldPartDefinition;
	private List<ShipPartInstance> _pickedUpParts = new();
	private List<ShipPartInstance> _originalPickedUpParts = new();
	private string _heldAnchorInstanceId = string.Empty;
	private Vector2 _heldAnchorCursorOffset;
	private List<PhysicalConnection> _pickedUpConnections = new();
	private List<LogisticsRoute> _pickedUpLogisticsRoutes = new();
	private LogisticsSystem? _activeRoutingSystem;
	private LogisticsEndpoint? _pendingRouteStart;
	private List<Vector2> _pendingRouteWaypoints = new();
	private Vector2 _routingCursorPosition;
	private int _nextLogisticsRouteNumber = 1;

	public ShipDesign Design { get; private set; }
	public string InitializationError { get; private set; } = string.Empty;
	public bool IsHoldingPart => _isHoldingPart;
	public bool IsRouting => _activeRoutingSystem.HasValue;
	public bool CanAddRootPart =>
		Design != null
		&& !_isHoldingPart
		&& !Design.Parts.Any(part => part.Definition.HasCapability<CommandCapability>());
	public int DetachedSubassemblyCount =>
		Design?.GetSubassemblies().Count(IsDetachedSubassembly) ?? 0;
	public string HeldPartDisplayName =>
		!_isHoldingPart
			? string.Empty
			: _pickedUpParts.Count > 1
				? $"{_pickedUpParts.Count} parts"
				: _heldPartDefinition?.DisplayName ?? string.Empty;

	public float TotalMassKg
	{
		get
		{
			if (Design == null)
				return 0;

			float mass = Design.TotalMassKg;
			if (!_isHoldingPart)
				return mass;

			return mass + (_pickedUpParts.Count > 0
				? _pickedUpParts.Sum(part => part.Definition.Mass)
				: _heldPartDefinition.Mass);
		}
	}

	public override void _Ready()
	{
		_rootDefinition = LoadOrCreateDefinition(
			RootDefinitionPath,
			CreateRootDefinition,
			"root bridge definition");
		_structuralDefinition = LoadOrCreateDefinition(
			StructuralDefinitionPath,
			CreateStructuralDefinition,
			"structural hull definition");
		_halfStructuralDefinition = LoadOrCreateDefinition(
			HalfStructuralDefinitionPath,
			CreateHalfStructuralDefinition,
			"half-size structural hull definition");
		_halfStructuralLongDefinition = CreateHalfStructuralLongDefinition();
		_structuralTriangleDefinition = CreateStructuralTriangleDefinition();
		_halfStructuralTriangleDefinition = CreateHalfStructuralTriangleDefinition();
		_structuralNotchedDefinition = CreateStructuralNotchedDefinition();
		_structuralLargeDefinition = CreateStructuralLargeDefinition();
		_structuralWideDefinition = CreateStructuralWideDefinition();
		_fuelTankDefinition = CreateFuelTankDefinition();
		_largeFuelTankDefinition = CreateFuelTankDefinition(large: true);
		_powerCoreDefinition = CreatePowerCoreDefinition();
		_largeGeneratorDefinition = CreatePowerCoreDefinition(large: true);
		_thrusterDefinition = CreateThrusterDefinition();
		_gimballedThrusterDefinition = CreateGimballedThrusterDefinition();
		_rootDefinition.LogisticsPorts.Add(CreateLogisticsPort(
			"power-in",
			LogisticsSystem.Power,
			LogisticsPortDirection.Consumer,
			Vector2.Zero));
		AddInternalSupportPorts(_fuelTankDefinition);
		AddInternalSupportPorts(_largeFuelTankDefinition);
		AddInternalSupportPorts(_powerCoreDefinition);
		AddInternalSupportPorts(_largeGeneratorDefinition);
		AddInternalSupportPorts(_thrusterDefinition);
		AddInternalSupportPorts(_gimballedThrusterDefinition);
		AddHalfFacePortsIfMissing(_rootDefinition);
		AddHalfFacePortsIfMissing(_structuralDefinition);
		foreach (ShipPartDefinition hullDefinition in new[]
		{
			_rootDefinition,
			_structuralDefinition,
			_halfStructuralDefinition,
			_halfStructuralLongDefinition,
			_structuralTriangleDefinition,
			_halfStructuralTriangleDefinition,
			_structuralNotchedDefinition,
			_structuralLargeDefinition,
			_structuralWideDefinition
		})
		{
			AddInternalSupportPorts(hullDefinition);
		}

		Design = new ShipDesign("player-ship");
		ShipPartInstance root = new("root", _rootDefinition, Vector2.Zero, 0);
		Design.AddPart(root);
		Design.SetRootPart(root.InstanceId);
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (Design == null)
		{
			if (!string.IsNullOrEmpty(InitializationError))
			{
				DrawString(
					ThemeDB.FallbackFont,
					new Vector2(20, 40),
					InitializationError,
					HorizontalAlignment.Left,
					-1,
					16,
					Colors.OrangeRed);
			}

			return;
		}

		DrawConnections();
		List<(ShipPartInstance Part, Color? Color)> internalParts = new();
		int detachedSubassemblyNumber = 1;
		foreach (ShipSubassembly subassembly in Design.GetSubassemblies())
		{
			bool containsOnlyInternalParts = subassembly.Parts.All(
				part => part.Definition.Tags.Contains("internal"));
			Color? subassemblyColor = subassembly.ContainsRoot || containsOnlyInternalParts
				? null
				: new Color(0.58f, 0.37f, 0.30f);
			Vector2 center = Vector2.Zero;
			foreach (ShipPartInstance part in subassembly.Parts)
			{
				if (part.Definition.Tags.Contains("internal"))
					internalParts.Add((part, subassemblyColor));
				else
					DrawPart(part, subassemblyColor);
				center += part.Position * ViewScale;
			}

			if (!subassembly.ContainsRoot && IsDetachedSubassembly(subassembly))
			{
				center /= subassembly.Parts.Count;
				DrawCenteredText($"DETACHED SUBASSEMBLY {detachedSubassemblyNumber++}", center - new Vector2(0, 82), 12);
			}
		}

		if (!_activeRoutingSystem.HasValue)
			DrawLogisticsRoutes();

		foreach (var (part, color) in internalParts)
			DrawPart(part, color);

		if (_activeRoutingSystem.HasValue)
		{
			DrawLogisticsGrid();
			DrawLogisticsRoutes();
		}
		if (_isHoldingPart)
		{
			DrawAvailablePortRanges();
			DrawHeldPart();
		}

		if (_activeRoutingSystem.HasValue)
			DrawLogisticsRoutingState();
	}

	public void BeginNewStructuralHullPlacement(Vector2 cursorPosition)
	{
		BeginNewPartPlacement(_structuralDefinition, cursorPosition);
	}

	public void BeginNewHalfStructuralHullPlacement(Vector2 cursorPosition)
	{
		BeginNewPartPlacement(_halfStructuralDefinition, cursorPosition);
	}

	public void BeginNewHalfStructuralLongPlacement(Vector2 cursorPosition)
	{
		BeginNewPartPlacement(_halfStructuralLongDefinition, cursorPosition);
	}

	public void BeginNewStructuralTrianglePlacement(Vector2 cursorPosition)
	{
		BeginNewPartPlacement(_structuralTriangleDefinition, cursorPosition);
	}

	public void BeginNewHalfStructuralTrianglePlacement(Vector2 cursorPosition)
	{
		BeginNewPartPlacement(_halfStructuralTriangleDefinition, cursorPosition);
	}

	public void BeginNewStructuralNotchedPlacement(Vector2 cursorPosition)
	{
		BeginNewPartPlacement(_structuralNotchedDefinition, cursorPosition);
	}

	public void BeginNewStructuralLargePlacement(Vector2 cursorPosition)
	{
		BeginNewPartPlacement(_structuralLargeDefinition, cursorPosition);
	}

	public void BeginNewStructuralWidePlacement(Vector2 cursorPosition)
	{
		BeginNewPartPlacement(_structuralWideDefinition, cursorPosition);
	}

	public void BeginNewFuelTankPlacement(Vector2 cursorPosition)
	{
		BeginNewPartPlacement(_fuelTankDefinition, cursorPosition);
	}

	public void BeginNewPowerCorePlacement(Vector2 cursorPosition)
	{
		BeginNewPartPlacement(_powerCoreDefinition, cursorPosition);
	}

	public void BeginNewLargeGeneratorPlacement(Vector2 cursorPosition)
	{
		BeginNewPartPlacement(_largeGeneratorDefinition, cursorPosition);
	}

	public void BeginNewLargeFuelTankPlacement(Vector2 cursorPosition)
	{
		BeginNewPartPlacement(_largeFuelTankDefinition, cursorPosition);
	}

	public void BeginNewThrusterPlacement(Vector2 cursorPosition)
	{
		BeginNewPartPlacement(_thrusterDefinition, cursorPosition);
	}

	public void BeginNewGimballedThrusterPlacement(Vector2 cursorPosition)
	{
		BeginNewPartPlacement(_gimballedThrusterDefinition, cursorPosition);
	}

	public void BeginLogisticsRouting(LogisticsSystem system)
	{
		if (_isHoldingPart)
			CancelHeldPartPlacement();

		_activeRoutingSystem = system;
		_pendingRouteStart = null;
		_pendingRouteWaypoints.Clear();
		QueueRedraw();
	}

	public void CancelLogisticsRouting()
	{
		_activeRoutingSystem = null;
		_pendingRouteStart = null;
		_pendingRouteWaypoints.Clear();
		QueueRedraw();
	}

	public void UpdateLogisticsRouteCursor(Vector2 cursorPosition)
	{
		_routingCursorPosition = cursorPosition;
		if (_activeRoutingSystem.HasValue)
			QueueRedraw();
	}

	public bool HandleLogisticsRoutingClick(Vector2 position, bool isRightClick, out string status)
	{
		if (!_activeRoutingSystem.HasValue)
		{
			status = string.Empty;
			return false;
		}

		if (isRightClick)
		{
			if (_pendingRouteWaypoints.Count > 0)
			{
				_pendingRouteWaypoints.RemoveAt(_pendingRouteWaypoints.Count - 1);
				status = "Removed the last route waypoint.";
			}
			else if (_pendingRouteStart.HasValue)
			{
				_pendingRouteStart = null;
				status = "Route selection cleared.";
			}
			else if (TryRemoveLogisticsMarkerAt(position, _activeRoutingSystem.Value))
			{
				status = "Logistics routing marker removed.";
			}
			else if (TryRemoveLogisticsRouteAt(position, _activeRoutingSystem.Value))
			{
				status = "Logistics route removed.";
			}
			else
			{
				status = "Route selection cleared.";
			}

			QueueRedraw();
			return true;
		}

		if (!_pendingRouteStart.HasValue)
		{
			ShipPartInstance sourcePart = FindNearestLogisticsPort(
				position,
				_activeRoutingSystem.Value,
				LogisticsPortDirection.Source,
				out LogisticsPortDefinition sourcePort);
			if (sourcePart == null)
			{
				if (TryGetConduitEndpoint(position, out LogisticsEndpoint endpoint))
				{
					_pendingRouteStart = endpoint;
					status = "Conduit selected. Draw an extension or branch; click a consumer/conduit or press Enter to finish with an open end.";
					QueueRedraw();
					return true;
				}

				status = $"Select a {_activeRoutingSystem.Value} source or an existing conduit to begin. Pass-overs cannot be joined.";
				return true;
			}

			_pendingRouteStart = new LogisticsEndpoint(
				Vector2.Zero, new LogisticsPortReference(sourcePart.InstanceId, sourcePort.PortId));
			_pendingRouteWaypoints.Clear();
			status = $"Source selected: {sourcePart.Definition.DisplayName}. Draw 90°/45° segments; click a consumer/conduit or press Enter for an open end.";
			QueueRedraw();
			return true;
		}

		ShipPartInstance consumerPart = FindNearestLogisticsPort(
			position,
			_activeRoutingSystem.Value,
			LogisticsPortDirection.Consumer,
			out LogisticsPortDefinition consumerPort);
		if (consumerPart != null)
		{
			LogisticsPortReference consumerReference = new(consumerPart.InstanceId, consumerPort.PortId);
			CompletePendingConduit(new LogisticsEndpoint(Vector2.Zero, consumerReference),
				_pendingRouteWaypoints, out status);
			return true;
		}

		if (TryGetConduitEndpoint(position, out LogisticsEndpoint conduitEnd))
		{
			CompletePendingConduit(conduitEnd, _pendingRouteWaypoints, out status);
			return true;
		}

		Vector2 waypoint = LogisticsRoute.SnapToGrid(position);
		Vector2 previousPoint = _pendingRouteWaypoints.Count > 0
			? _pendingRouteWaypoints[^1]
			: _pendingRouteStart.Value.GetPosition(Design.Parts);
		if (!IsGridRouteSegment(previousPoint, waypoint))
		{
			status = "Waypoint must continue horizontally, vertically, or diagonally on the grid.";
			return true;
		}

		if (!CanPlaceConduitSegment(previousPoint, waypoint))
		{
			status = "Keep the conduit inside the hull and avoid retracing existing conduits. Click a conduit to join it instead.";
			return true;
		}

		_pendingRouteWaypoints.Add(waypoint);
		status = $"Vertex added ({_pendingRouteWaypoints.Count}). Continue drawing; click a consumer/conduit or press Enter for an open end.";
		QueueRedraw();
		return true;
	}

	public bool FinishLogisticsConduit(out string status)
	{
		if (!_pendingRouteStart.HasValue || _pendingRouteWaypoints.Count == 0)
		{
			status = "Draw at least one conduit segment before pressing Enter.";
			return false;
		}

		return CompletePendingConduit(
			new LogisticsEndpoint(_pendingRouteWaypoints[^1]),
			_pendingRouteWaypoints.Take(_pendingRouteWaypoints.Count - 1).ToArray(),
			out status);
	}

	private bool TryGetConduitEndpoint(Vector2 position, out LogisticsEndpoint endpoint)
	{
		Vector2 point = LogisticsRoute.SnapToGrid(position);
		endpoint = new LogisticsEndpoint(point);
		return !IsPassOverAt(point)
			&& Design.LogisticsRoutes.Any(route =>
				route.System == _activeRoutingSystem.Value && RouteContainsPoint(route, point));
	}

	private bool IsPassOverAt(Vector2 position) => Design.LogisticsPassOvers.Any(marker =>
		marker.System == _activeRoutingSystem.Value && marker.Position == position);

	private bool CanPlaceConduitSegment(Vector2 start, Vector2 end)
	{
		if (!IsGridRouteSegment(start, end) || !IsRoutePathInsideHull(start, new[] { end }))
			return false;

		foreach (LogisticsRoute route in Design.LogisticsRoutes)
		{
			if (route.System != _activeRoutingSystem.Value)
				continue;

			IReadOnlyList<Vector2> points = route.GetPoints(Design.Parts);
			for (int i = 0; i < points.Count - 1; i++)
				if (LogisticsRoute.SegmentsOverlap(start, end, points[i], points[i + 1]))
					return false;
		}

		return true;
	}

	private bool CompletePendingConduit(
		LogisticsEndpoint end, IEnumerable<Vector2> waypoints, out string status)
	{
		LogisticsSystem system = _activeRoutingSystem.Value;
		LogisticsRoute route = new(
			$"logistics-route-{_nextLogisticsRouteNumber++}",
			system, _pendingRouteStart.Value, end, waypoints);
		IReadOnlyList<Vector2> points = route.GetPoints(Design.Parts);
		for (int i = 0; i < points.Count - 1; i++)
		{
			if (!CanPlaceConduitSegment(points[i], points[i + 1]))
			{
				status = "Each segment must be a nonzero 90°/45° line inside the hull without retracing an existing conduit.";
				return false;
			}
			for (int j = 0; j < i; j++)
			{
				if (LogisticsRoute.SegmentsOverlap(points[i], points[i + 1], points[j], points[j + 1]))
				{
					status = "A conduit cannot double back over itself. Undo the last vertex and choose another path.";
					return false;
				}
			}
		}

		if (IsPassOverAt(points[0]) || IsPassOverAt(points[^1]))
		{
			status = "A pass-over is an unconnected crossing. Remove its marker before joining a conduit there.";
			return false;
		}

		if (end.Port.HasValue && Design.LogisticsRoutes.Any(existing =>
			existing.Start.Port == end.Port || existing.End.Port == end.Port))
		{
			status = "That consumer already has a conduit. Branch from the conduit instead.";
			return false;
		}

		Design.SplitLogisticsRoutesAt(system, points[0]);
		Design.SplitLogisticsRoutesAt(system, points[^1]);
		Design.AddLogisticsRoute(route);
		foreach (Vector2 point in new[] { points[0], points[^1] }.Distinct())
		{
			int incidentRoutes = Design.LogisticsRoutes.Count(candidate =>
				candidate.System == system && RouteContainsPoint(candidate, point));
			if (incidentRoutes >= 3 && !Design.LogisticsJunctions.Any(marker =>
				marker.System == system && marker.Position == point))
			{
				Design.AddLogisticsJunction(new LogisticsJunction(
					$"logistics-junction-{_nextLogisticsRouteNumber++}", system, point));
			}
		}

		_pendingRouteStart = null;
		_pendingRouteWaypoints.Clear();
		status = $"{system} conduit saved. Click an open end to extend, or a segment to branch.";
		QueueRedraw();
		return true;
	}

	public bool TryAddLogisticsJunction(Vector2 position, out string status)
	{
		if (!_activeRoutingSystem.HasValue)
		{
			status = "Select a routing system before adding a junction.";
			return false;
		}

		Vector2 junctionPosition = LogisticsRoute.SnapJunctionToGrid(position);
		int crossingRoutes = Design.LogisticsRoutes.Count(route =>
			route.System == _activeRoutingSystem.Value
			&& RouteContainsPoint(route, junctionPosition));
		if (crossingRoutes < 2)
		{
			status = "A junction can be added only where at least two same-system conduits meet or cross.";
			return false;
		}

		if (Design.LogisticsJunctions.Any(junction =>
			junction.System == _activeRoutingSystem.Value
			&& junction.Position == junctionPosition))
		{
			status = "A junction already exists at this grid point.";
			return false;
		}
		if (Design.LogisticsPassOvers.Any(passOver =>
			passOver.System == _activeRoutingSystem.Value
			&& passOver.Position == junctionPosition))
		{
			status = "This crossing is marked as a pass-over. Remove it before connecting the routes.";
			return false;
		}

		Design.AddLogisticsJunction(new LogisticsJunction(
			$"logistics-junction-{_nextLogisticsRouteNumber++}",
			_activeRoutingSystem.Value,
			junctionPosition));
		status = $"{_activeRoutingSystem.Value} junction added.";
		QueueRedraw();
		return true;
	}

	public bool TryAddLogisticsPassOver(Vector2 position, out string status)
	{
		if (!_activeRoutingSystem.HasValue)
		{
			status = "Select a routing system before adding a pass-over.";
			return false;
		}

		Vector2 markerPosition = LogisticsRoute.SnapJunctionToGrid(position);
		int crossingRoutes = Design.LogisticsRoutes.Count(route =>
			route.System == _activeRoutingSystem.Value
			&& RouteContainsPoint(route, markerPosition));
		if (crossingRoutes < 2)
		{
			status = "A pass-over can be marked only where at least two same-system conduits cross.";
			return false;
		}

		if (Design.LogisticsJunctions.Any(junction =>
			junction.System == _activeRoutingSystem.Value
			&& junction.Position == markerPosition))
		{
			status = "This crossing is already connected by a junction.";
			return false;
		}
		if (Design.LogisticsPassOvers.Any(passOver =>
			passOver.System == _activeRoutingSystem.Value
			&& passOver.Position == markerPosition))
		{
			status = "A pass-over already exists at this crossing.";
			return false;
		}

		Design.AddLogisticsPassOver(new LogisticsPassOver(
			$"logistics-pass-over-{_nextLogisticsRouteNumber++}",
			_activeRoutingSystem.Value,
			markerPosition));
		status = $"{_activeRoutingSystem.Value} pass-over marker added.";
		QueueRedraw();
		return true;
	}

	public bool TryAddLogisticsPassThrough(Vector2 position, out string status)
	{
		if (!_activeRoutingSystem.HasValue)
		{
			status = "Select a routing system before adding a pass-through.";
			return false;
		}

		Vector2 markerPosition = LogisticsRoute.SnapToGrid(position);
		if (!Design.LogisticsRoutes.Any(route =>
			route.System == _activeRoutingSystem.Value
			&& RouteContainsPoint(route, markerPosition)))
		{
			status = "A pass-through must be placed on an existing conduit.";
			return false;
		}

		if (Design.LogisticsPassThroughs.Any(passThrough =>
			passThrough.System == _activeRoutingSystem.Value
			&& passThrough.Position == markerPosition))
		{
			status = "A pass-through already exists at this grid point.";
			return false;
		}

		Design.AddLogisticsPassThrough(new LogisticsPassThrough(
			$"logistics-pass-through-{_nextLogisticsRouteNumber++}",
			_activeRoutingSystem.Value,
			markerPosition));
		status = $"{_activeRoutingSystem.Value} pass-through marker added.";
		QueueRedraw();
		return true;
	}

	private static bool IsGridRouteSegment(Vector2 start, Vector2 end)
	{
		return LogisticsRoute.IsValidSegment(start, end);
	}

	private bool IsRoutePathInsideHull(Vector2 start, IReadOnlyList<Vector2> points)
	{
		if (!IsPointInsideHullUnion(start))
			return false;

		Vector2 previous = start;
		foreach (Vector2 point in points)
		{
			int sampleCount = Mathf.Max(1, Mathf.CeilToInt(
				previous.DistanceTo(point) / InternalCoverageSampleSpacing));
			for (int i = 1; i <= sampleCount; i++)
			{
				Vector2 sample = previous.Lerp(point, i / (float)sampleCount);
				if (!IsPointInsideHullUnion(sample))
					return false;
			}

			previous = point;
		}

		return true;
	}

	private bool IsPointInsideHullUnion(Vector2 position)
	{
		return Design.Parts.Any(part =>
			part.Definition.HasCapability<StructuralCapability>()
			&& ContainsPoint(part, position));
	}

	private bool RouteContainsPoint(LogisticsRoute route, Vector2 position)
	{
		IReadOnlyList<Vector2> routePoints = route.GetPoints(Design.Parts);
		for (int i = 0; i < routePoints.Count - 1; i++)
		{
			if (PointLiesOnSegment(position, routePoints[i], routePoints[i + 1]))
				return true;
		}

		return false;
	}

	private static bool PointLiesOnSegment(Vector2 point, Vector2 start, Vector2 end)
	{
		return LogisticsRoute.PointLiesOnSegment(point, start, end);
	}

	private bool TryRemoveLogisticsMarkerAt(Vector2 position, LogisticsSystem system)
	{
		const float pickRadius = 32;
		LogisticsJunction junction = Design.LogisticsJunctions
			.Where(candidate => candidate.System == system)
			.OrderBy(candidate => candidate.Position.DistanceSquaredTo(position))
			.FirstOrDefault(candidate => candidate.Position.DistanceSquaredTo(position) <= pickRadius * pickRadius);
		if (junction != null)
			return Design.RemoveLogisticsJunction(junction.JunctionId);

		LogisticsPassOver passOver = Design.LogisticsPassOvers
			.Where(candidate => candidate.System == system)
			.OrderBy(candidate => candidate.Position.DistanceSquaredTo(position))
			.FirstOrDefault(candidate => candidate.Position.DistanceSquaredTo(position) <= pickRadius * pickRadius);
		if (passOver != null)
			return Design.RemoveLogisticsPassOver(passOver.MarkerId);

		LogisticsPassThrough passThrough = Design.LogisticsPassThroughs
			.Where(candidate => candidate.System == system)
			.OrderBy(candidate => candidate.Position.DistanceSquaredTo(position))
			.FirstOrDefault(candidate => candidate.Position.DistanceSquaredTo(position) <= pickRadius * pickRadius);
		return passThrough != null && Design.RemoveLogisticsPassThrough(passThrough.MarkerId);
	}

	public bool RotateHeldPart(int degrees)
	{
		if (!_isHoldingPart || !_heldPartDefinition.Tags.Contains("rotatable"))
			return false;

		int normalizedRotation = ((_heldRotationDegrees + degrees) % 360 + 360) % 360;
		if (!_heldPartDefinition.AllowsRotation(normalizedRotation))
			return false;

		if (_pickedUpParts.Count > 0)
		{
			ShipPartInstance anchor = FindPickedUpPart(_heldAnchorInstanceId);
			_pickedUpParts = _pickedUpParts
				.Select(part =>
				{
					Vector2 relativePosition = (part.Position - anchor.Position)
						.Rotated(Mathf.DegToRad(degrees));
					return new ShipPartInstance(
						part.InstanceId,
						part.Definition,
						anchor.Position + relativePosition,
						(part.RotationDegrees + degrees + 360) % 360);
				})
				.ToList();
			_heldAnchorCursorOffset = _heldAnchorCursorOffset.Rotated(Mathf.DegToRad(degrees));
		}

		_heldRotationDegrees = normalizedRotation;
		UpdateHeldPartPlacement(_heldCursorPosition);
		return true;
	}

	public bool BeginNewRootPartPlacement(Vector2 cursorPosition)
	{
		if (!CanAddRootPart || _rootDefinition == null)
			return false;

		BeginNewPartPlacement(_rootDefinition, cursorPosition);
		return _isHoldingPart;
	}

	private void BeginNewPartPlacement(ShipPartDefinition definition, Vector2 cursorPosition)
	{
		if (Design == null || definition == null || _isHoldingPart)
			return;

		_activeRoutingSystem = null;
		_pendingRouteStart = null;
		_pendingRouteWaypoints.Clear();
		_isHoldingPart = true;
		_heldPartInstanceId = $"{definition.PartId}-{_nextPartNumber++}";
		_heldPartDefinition = definition;
		_heldCursorPosition = cursorPosition;
		_heldRotationDegrees = 0;
		_pickedUpParts.Clear();
		_originalPickedUpParts.Clear();
		_heldAnchorInstanceId = string.Empty;
		_pickedUpConnections.Clear();
		_pickedUpLogisticsRoutes.Clear();
		UpdateHeldPartPlacement(cursorPosition);
	}

	public void UpdateHeldPartPlacement(Vector2 cursorPosition)
	{
		if (!_isHoldingPart)
			return;

		_heldCursorPosition = cursorPosition;
		if (_pickedUpParts.Count > 0)
		{
			ShipPartInstance anchor = FindPickedUpPart(_heldAnchorInstanceId);
			Vector2 anchorPosition = cursorPosition - _heldAnchorCursorOffset;
			_currentPlacement = FindGroupPlacementTarget(anchor, anchorPosition);
			_previewPosition = _currentPlacement?.PartPosition ?? anchorPosition;
		}
		else
		{
			_currentPlacement = FindPlacementTarget(_heldPartDefinition, cursorPosition);
			_previewPosition = _currentPlacement?.PartPosition ?? cursorPosition;
		}
		QueueRedraw();
	}

	public bool TryDropHeldPart(bool retainHeldHull, out string reason)
	{
		if (!_isHoldingPart)
		{
			reason = "There is no held part to place.";
			return false;
		}

		PlacementTarget? placement = _currentPlacement;
		List<ShipPartInstance> placedParts = GetHeldPartsAtPreviewPosition();
		List<string> placedPartIds = placedParts.Select(part => part.InstanceId).ToList();
		foreach (ShipPartInstance part in placedParts)
			Design.AddPart(part);

		bool containsRoot = placedParts.Any(part => part.InstanceId == Design.RootPartInstanceId
			|| part.Definition.HasCapability<CommandCapability>());
		if (containsRoot)
		{
			ShipPartInstance rootPart = placedParts.First(part =>
				part.InstanceId == Design.RootPartInstanceId
				|| part.Definition.HasCapability<CommandCapability>());
			Design.SetRootPart(rootPart.InstanceId);
		}

		List<PhysicalConnection> internalConnections = _pickedUpConnections
			.Where(connection =>
				placedPartIds.Contains(connection.First.PartInstanceId)
				&& placedPartIds.Contains(connection.Second.PartInstanceId))
			.ToList();
		foreach (PhysicalConnection connection in internalConnections)
			Design.AddConnection(connection);

		List<PhysicalConnection> newConnections = FindConnectionsFor(placedParts, placedPartIds);
		foreach (PhysicalConnection connection in newConnections)
			Design.AddConnection(connection);
		foreach (LogisticsRoute route in _pickedUpLogisticsRoutes)
		{
			if ((!route.Start.Port.HasValue || FindPart(route.Start.Port.Value.PartInstanceId) != null)
				&& (!route.End.Port.HasValue || FindPart(route.End.Port.Value.PartInstanceId) != null))
				Design.AddLogisticsRoute(route);
		}

		string partName = placedParts.Count == 1
			? placedParts[0].Definition.DisplayName
			: $"{placedParts.Count} connected parts";
		string placementDescription = newConnections.Count == 0
			? "Placed freely in the scene"
			: placement.HasValue
				? $"Attached to {placement.Value.TargetPart.Definition.DisplayName}"
				: $"Attached at {newConnections.Count} connection points";
		bool continuePlacingHull = retainHeldHull
			&& _pickedUpParts.Count == 0
			&& _heldPartDefinition.Tags.Contains("hull");
		if (continuePlacingHull)
		{
			_heldPartInstanceId = $"{_heldPartDefinition.PartId}-{_nextPartNumber++}";
			UpdateHeldPartPlacement(_heldCursorPosition);
		}
		else
		{
			ClearHeldPart();
		}
		QueueRedraw();
		reason = continuePlacingHull
			? $"{placementDescription}: {partName}. Continue placing copies."
			: $"{placementDescription}: {partName}.";
		return true;
	}

	public void CancelHeldPartPlacement()
	{
		if (_pickedUpParts.Count > 0)
		{
			foreach (ShipPartInstance part in _originalPickedUpParts)
				Design.AddPart(part);
			ShipPartInstance rootPart = _originalPickedUpParts.FirstOrDefault(part =>
				part.Definition.HasCapability<CommandCapability>());
			if (rootPart != null)
				Design.SetRootPart(rootPart.InstanceId);
			foreach (PhysicalConnection connection in _pickedUpConnections)
				Design.AddConnection(connection);
			foreach (LogisticsRoute route in _pickedUpLogisticsRoutes)
				Design.AddLogisticsRoute(route);
		}

		ClearHeldPart();
		QueueRedraw();
	}

	public bool TryPickUpPartAt(Vector2 localPosition, bool pickUpConnectedGroup, out string reason)
	{
		if (Design == null || _isHoldingPart)
		{
			reason = "Finish the held part before picking up another one.";
			return false;
		}

		ShipPartInstance hitPart = FindPartAt(localPosition);
		if (hitPart == null)
		{
			reason = "Right-click a placed hull piece to pick it up.";
			return false;
		}

		if (!pickUpConnectedGroup && hitPart.InstanceId == Design.RootPartInstanceId)
		{
			reason = "The bridge/CIC root cannot be picked up by itself.";
			return false;
		}

		ShipSubassembly connectedGroup = Design.GetSubassemblies()
			.First(subassembly => subassembly.Parts.Any(part => part.InstanceId == hitPart.InstanceId));
		bool pickUpGroup = pickUpConnectedGroup
			&& !hitPart.Definition.Tags.Contains("internal");
		_pickedUpParts = pickUpGroup
			? connectedGroup.Parts.ToList()
			: new List<ShipPartInstance> { hitPart };
		_originalPickedUpParts = _pickedUpParts.ToList();
		_heldAnchorInstanceId = hitPart.InstanceId;
		_heldAnchorCursorOffset = localPosition - hitPart.Position;
		HashSet<string> heldPartIds = _pickedUpParts.Select(part => part.InstanceId).ToHashSet();
		_pickedUpConnections = Design.Connections
			.Where(connection =>
				heldPartIds.Contains(connection.First.PartInstanceId)
				|| heldPartIds.Contains(connection.Second.PartInstanceId))
			.ToList();
		_pickedUpLogisticsRoutes = Design.LogisticsRoutes
			.Where(route =>
				heldPartIds.Any(id => route.Start.ReferencesPart(id) || route.End.ReferencesPart(id)))
			.ToList();
		Design.RemoveParts(heldPartIds, preserveLogisticsMarkers: true);
		_heldPartInstanceId = hitPart.InstanceId;
		_heldPartDefinition = hitPart.Definition;
		_heldCursorPosition = localPosition;
		_heldRotationDegrees = hitPart.RotationDegrees;
		_isHoldingPart = true;
		UpdateHeldPartPlacement(localPosition);
		QueueRedraw();
		string heldDescription = pickUpGroup
			? connectedGroup.ContainsRoot
				? "the whole ship"
				: $"the {connectedGroup.Parts.Count}-part subassembly"
			: hitPart.Definition.DisplayName;
		reason = $"Picked up {heldDescription}.";
		return true;
	}

	public bool DeleteHeldPart(out string reason)
	{
		if (!_isHoldingPart)
		{
			reason = "There is no held part to delete.";
			return false;
		}

		string partName = _pickedUpParts.Count > 1
			? $"{_pickedUpParts.Count}-part group"
			: _heldPartDefinition.DisplayName;
		Design.PruneUnusedLogisticsMarkers();
		ClearHeldPart();
		QueueRedraw();
		reason = $"{partName} deleted. Other disconnected sections remain as subassemblies.";
		return true;
	}

	public bool IsMouseOverPart(Vector2 localPosition)
	{
		return Design != null && FindPartAt(localPosition) != null;
	}

	public int GetPartCount()
	{
		return Design?.Parts.Count ?? 0;
	}

	private void DrawConnections()
	{
		foreach (PhysicalConnection connection in Design.Connections)
		{
			ShipPartInstance firstPart = FindPart(connection.First.PartInstanceId);
			ShipPartInstance secondPart = FindPart(connection.Second.PartInstanceId);
			MountPortDefinition firstPort = firstPart?.Definition.FindMountPort(connection.First.PortId);
			MountPortDefinition secondPort = secondPart?.Definition.FindMountPort(connection.Second.PortId);
			if (firstPart == null || secondPart == null || firstPort == null || secondPort == null)
			{
				continue;
			}

			Vector2 firstPosition = firstPart.GetWorldPortPosition(firstPort) * ViewScale;
			Vector2 secondPosition = secondPart.GetWorldPortPosition(secondPort) * ViewScale;
			DrawLine(firstPosition, secondPosition, new Color(0.44f, 0.82f, 0.58f), 4);
		}
	}

	private void DrawLogisticsRoutes()
	{
		foreach (LogisticsRoute route in Design.LogisticsRoutes)
		{
			Color color = GetLogisticsSystemColor(route.System);
			IReadOnlyList<Vector2> points = route.GetPoints(Design.Parts);
			for (int i = 0; i < points.Count - 1; i++)
			{
				DrawLine(points[i] * ViewScale, points[i + 1] * ViewScale, color, 4, true);
			}

			foreach (LogisticsEndpoint endpoint in new[] { route.Start, route.End })
			{
				Vector2 point = endpoint.GetPosition(Design.Parts);
				if (!endpoint.Port.HasValue && Design.LogisticsRoutes.Count(candidate =>
					candidate.System == route.System && RouteContainsPoint(candidate, point)) == 1)
					DrawArc(point * ViewScale, 6, 0, Mathf.Tau, 16, color, 2);
			}
		}
	}

	private void DrawLogisticsRoutingState()
	{
		LogisticsSystem system = _activeRoutingSystem.Value;
		foreach (ShipPartInstance part in Design.Parts)
		{
			foreach (LogisticsPortDefinition port in part.Definition.LogisticsPorts)
			{
				if (port.System != system)
					continue;

				bool isConnectedConsumer = port.Direction == LogisticsPortDirection.Consumer
					&& Design.LogisticsRoutes.Any(route =>
						route.Start.Port == new LogisticsPortReference(part.InstanceId, port.PortId)
						|| route.End.Port == new LogisticsPortReference(part.InstanceId, port.PortId));
				if (isConnectedConsumer)
					continue;

				Color color = port.Direction == LogisticsPortDirection.Source
					? new Color(0.25f, 0.95f, 0.48f, 0.95f)
					: new Color(0.35f, 0.78f, 1f, 0.95f);
				Vector2 marker = LogisticsRoute.SnapToGrid(
					part.GetWorldLogisticsPortPosition(port)) * ViewScale;
				DrawCircle(marker, 8, color);
				DrawArc(marker, 11, 0, Mathf.Tau, 24, Colors.White, 2);
			}
		}

		foreach (LogisticsJunction junction in Design.LogisticsJunctions)
		{
			if (junction.System != system)
				continue;

			Vector2 marker = junction.Position * ViewScale;
			DrawCircle(marker, 7, GetLogisticsSystemColor(system));
			DrawLine(marker + new Vector2(-10, 0), marker + new Vector2(10, 0), Colors.White, 2);
			DrawLine(marker + new Vector2(0, -10), marker + new Vector2(0, 10), Colors.White, 2);
		}

		foreach (LogisticsPassOver passOver in Design.LogisticsPassOvers)
		{
			if (passOver.System != system)
				continue;

			Vector2 marker = passOver.Position * ViewScale;
			DrawArc(marker, 9, 0, Mathf.Tau, 24, Colors.White, 2);
			DrawLine(marker + new Vector2(-12, 0), marker + new Vector2(-4, 0), GetLogisticsSystemColor(system), 3);
			DrawLine(marker + new Vector2(4, 0), marker + new Vector2(12, 0), GetLogisticsSystemColor(system), 3);
		}

		foreach (LogisticsPassThrough passThrough in Design.LogisticsPassThroughs)
		{
			if (passThrough.System != system)
				continue;

			Vector2 marker = passThrough.Position * ViewScale;
			DrawRect(new Rect2(marker - new Vector2(6, 6), new Vector2(12, 12)), Colors.White, false, 2);
			DrawCircle(marker, 3, GetLogisticsSystemColor(system));
		}

		if (!_pendingRouteStart.HasValue)
			return;

		Color routeColor = GetLogisticsSystemColor(system);
		Vector2 lastPoint = _pendingRouteStart.Value.GetPosition(Design.Parts);
		foreach (Vector2 waypoint in _pendingRouteWaypoints)
		{
			DrawLine(lastPoint * ViewScale, waypoint * ViewScale, routeColor, 3, true);
			lastPoint = waypoint;
		}

		Vector2 previewDestination = LogisticsRoute.SnapToGrid(_routingCursorPosition);
		Color previewColor = CanPlaceConduitSegment(lastPoint, previewDestination)
			? routeColor
			: new Color(1f, 0.25f, 0.25f, 0.9f);
		DrawLine(lastPoint * ViewScale, previewDestination * ViewScale, previewColor, 3, true);
	}

	private void DrawLogisticsGrid()
	{
		const float halfCellSize = LogisticsRoute.GridSize / 2;
		ShipPartInstance[] hullParts = Design.Parts
			.Where(part => part.Definition.HasCapability<StructuralCapability>())
			.ToArray();
		if (hullParts.Length == 0)
			return;

		float minimumX = hullParts.Min(part => part.Position.X - GetFootprintSize(part.Definition).X / 2);
		float maximumX = hullParts.Max(part => part.Position.X + GetFootprintSize(part.Definition).X / 2);
		float minimumY = hullParts.Min(part => part.Position.Y - GetFootprintSize(part.Definition).Y / 2);
		float maximumY = hullParts.Max(part => part.Position.Y + GetFootprintSize(part.Definition).Y / 2);
		int minCellX = Mathf.FloorToInt(minimumX / LogisticsRoute.GridSize);
		int maxCellX = Mathf.CeilToInt(maximumX / LogisticsRoute.GridSize);
		int minCellY = Mathf.FloorToInt(minimumY / LogisticsRoute.GridSize);
		int maxCellY = Mathf.CeilToInt(maximumY / LogisticsRoute.GridSize);
		Color gridColor = new(0.7f, 0.88f, 0.76f, 0.22f);
		for (int cellY = minCellY; cellY < maxCellY; cellY++)
		{
			for (int cellX = minCellX; cellX < maxCellX; cellX++)
			{
				Vector2 topLeft = new(cellX * LogisticsRoute.GridSize, cellY * LogisticsRoute.GridSize);
				Vector2 topRight = topLeft + new Vector2(LogisticsRoute.GridSize, 0);
				Vector2 bottomRight = topLeft + new Vector2(LogisticsRoute.GridSize, LogisticsRoute.GridSize);
				Vector2 bottomLeft = topLeft + new Vector2(0, LogisticsRoute.GridSize);
				if (!IsPointInsideHullUnion((topLeft + bottomRight) / 2)
					|| !IsPointInsideHullUnion(topLeft + new Vector2(halfCellSize, halfCellSize))
					|| !IsPointInsideHullUnion(topRight + new Vector2(-halfCellSize, halfCellSize))
					|| !IsPointInsideHullUnion(bottomRight + new Vector2(-halfCellSize, -halfCellSize))
					|| !IsPointInsideHullUnion(bottomLeft + new Vector2(halfCellSize, -halfCellSize)))
					continue;

				Vector2[] corners = { topLeft, topRight, bottomRight, bottomLeft };
				for (int i = 0; i < corners.Length; i++)
				{
					DrawLine(
						corners[i] * ViewScale,
						corners[(i + 1) % corners.Length] * ViewScale,
						gridColor,
						1);
				}
			}
		}
	}

	private static Color GetLogisticsSystemColor(LogisticsSystem system)
	{
		return system switch
		{
			LogisticsSystem.Power => new Color(0.35f, 0.8f, 1f, 0.95f),
			LogisticsSystem.Fuel => new Color(1f, 0.68f, 0.22f, 0.95f),
			_ => Colors.White
		};
	}

	private ShipPartInstance FindNearestLogisticsPort(
		Vector2 position,
		LogisticsSystem system,
		LogisticsPortDirection direction,
		out LogisticsPortDefinition matchingPort)
	{
		const float pickRadius = 34;
		ShipPartInstance nearestPart = null;
		matchingPort = null;
		float nearestDistanceSquared = pickRadius * pickRadius;
		foreach (ShipPartInstance part in Design.Parts)
		{
			foreach (LogisticsPortDefinition port in part.Definition.LogisticsPorts)
			{
				if (port.System != system || port.Direction != direction)
					continue;

				float distanceSquared = LogisticsRoute.SnapToGrid(
					part.GetWorldLogisticsPortPosition(port)).DistanceSquaredTo(position);
				if (distanceSquared > nearestDistanceSquared)
					continue;

				nearestDistanceSquared = distanceSquared;
				nearestPart = part;
				matchingPort = port;
			}
		}

		return nearestPart;
	}

	private bool TryRemoveLogisticsRouteAt(Vector2 position, LogisticsSystem system)
	{
		const float routePickRadius = 24;
		LogisticsRoute nearestRoute = null;
		float nearestDistanceSquared = routePickRadius * routePickRadius;
		foreach (LogisticsRoute route in Design.LogisticsRoutes)
		{
			if (route.System != system)
				continue;

			IReadOnlyList<Vector2> points = route.GetPoints(Design.Parts);
			for (int i = 0; i < points.Count - 1; i++)
			{
				float distanceSquared = DistanceSquaredToSegment(position, points[i], points[i + 1]);
				if (distanceSquared < nearestDistanceSquared)
				{
					nearestDistanceSquared = distanceSquared;
					nearestRoute = route;
				}
			}
		}

		return nearestRoute != null && Design.RemoveLogisticsRoute(nearestRoute.RouteId);
	}

	private static float DistanceSquaredToSegment(Vector2 point, Vector2 start, Vector2 end)
	{
		Vector2 segment = end - start;
		float segmentLengthSquared = segment.LengthSquared();
		if (segmentLengthSquared <= Mathf.Epsilon)
			return point.DistanceSquaredTo(start);

		float projection = Mathf.Clamp((point - start).Dot(segment) / segmentLengthSquared, 0, 1);
		return point.DistanceSquaredTo(start + segment * projection);
	}

	private static LogisticsPortDefinition FindLogisticsPort(ShipPartInstance part, string portId)
	{
		if (part == null)
			return null;

		foreach (LogisticsPortDefinition port in part.Definition.LogisticsPorts)
		{
			if (port.PortId == portId)
				return port;
		}

		return null;
	}

	private void DrawPart(ShipPartInstance part, Color? overrideColor, bool isSelected = false)
	{
		Vector2 center = part.Position * ViewScale;
		Color fillColor = overrideColor
			?? (part.Definition.HasCapability<CommandCapability>()
				? new Color(0.20f, 0.48f, 0.66f)
				: part.Definition.Tags.Contains("fuel")
					? new Color(0.12f, 0.48f, 0.56f)
				: part.Definition.Tags.Contains("power")
					? new Color(0.46f, 0.32f, 0.60f)
				: new Color(0.36f, 0.40f, 0.44f));
		float rotation = Mathf.DegToRad(part.RotationDegrees);
		float visualScale = ViewScale
			* (part.Definition.Tags.Contains("internal") ? InternalVisualScale : 1);
		Vector2[] corners = GetFootprintVertices(part.Definition)
			.Select(point => center + (point * visualScale).Rotated(rotation))
			.ToArray();

		DrawColoredPolygon(corners, fillColor);
		for (int i = 0; i < corners.Length; i++)
			DrawLine(
				corners[i],
				corners[(i + 1) % corners.Length],
				isSelected
					? new Color(0.20f, 0.96f, 1f, 1f)
					: new Color(0.82f, 0.87f, 0.91f, fillColor.A),
				isSelected ? 4 : 2);

		if (part.Definition.Tags.Contains("fuel"))
		{
			DrawLine(
				center + new Vector2(-40, -48).Rotated(rotation) * ViewScale,
				center + new Vector2(-40, 48).Rotated(rotation) * ViewScale,
				new Color(0.54f, 0.88f, 0.9f, fillColor.A),
				2);
			DrawLine(
				center + new Vector2(40, -48).Rotated(rotation) * ViewScale,
				center + new Vector2(40, 48).Rotated(rotation) * ViewScale,
				new Color(0.54f, 0.88f, 0.9f, fillColor.A),
				2);
		}

		if (part.Definition.Tags.Contains("power"))
		{
			Vector2[] bolt =
			{
				new(8, -42), new(-24, 6), new(-2, 6),
				new(-8, 42), new(24, -6), new(2, -6)
			};
			DrawColoredPolygon(
				bolt.Select(point => center + point.Rotated(rotation) * visualScale).ToArray(),
				new Color(1f, 0.88f, 0.38f, fillColor.A));
		}

		if (part.Definition.Tags.Contains("thruster"))
		{
			Vector2 thrustDirection = part.Definition.Tags.Contains("gimballed")
				? Vector2.Down
				: Vector2.Down.Rotated(rotation);
			Vector2 arrowStart = center - thrustDirection * 14;
			Vector2 arrowEnd = center + thrustDirection * 30;
			Color thrustColor = new(1f, 0.72f, 0.32f, fillColor.A);
			DrawLine(arrowStart, arrowEnd, thrustColor, 3);
			Vector2 arrowSide = new(-thrustDirection.Y, thrustDirection.X);
			DrawLine(arrowEnd, arrowEnd - thrustDirection * 10 + arrowSide * 6, thrustColor, 3);
			DrawLine(arrowEnd, arrowEnd - thrustDirection * 10 - arrowSide * 6, thrustColor, 3);
			if (part.Definition.Tags.Contains("gimballed"))
				DrawArc(center, 18, 0, Mathf.Tau, 24, thrustColor, 2);
		}

		if (part.Definition.HasCapability<CommandCapability>())
			DrawCenteredText("Bridge", center, 14, fillColor.A);
	}

	private void DrawAvailablePortRanges()
	{
		if (!_isHoldingPart || _heldPartDefinition.Tags.Contains("internal"))
			return;

		foreach (ShipPartInstance part in Design.Parts)
		{
			if (part.Definition.Tags.Contains("internal"))
				continue;

			DrawPartPortRanges(
				part,
				port => !IsPortUnavailable(part, port) && HasCompatibleHeldPort(part, port));
		}

		foreach (ShipPartInstance heldPart in GetHeldPartsAtPreviewPosition())
		{
			if (heldPart.Definition.Tags.Contains("internal"))
				continue;

			DrawPartPortRanges(
				heldPart,
				port => HasCompatibleTargetPort(heldPart.Definition, port, heldPart.RotationDegrees));
		}
	}

	private void DrawPartPortRanges(
		ShipPartInstance part,
		Func<MountPortDefinition, bool> isAvailable)
	{
		Vector2 footprintSize = GetFootprintSize(part.Definition);
		float depth = SnapRadius;
		foreach (MountFacing facing in Enum.GetValues<MountFacing>())
		{
			List<MountPortDefinition> facePorts = part.Definition.MountPorts
				.Where(port =>
					port.Facing == facing
					&& !IsInternalSupportPort(port)
					&& isAvailable(port))
				.OrderBy(port => facing is MountFacing.North or MountFacing.South
					? port.LocalPosition.X
					: port.LocalPosition.Y)
				.ToList();

			if (facePorts.Count == 0)
				continue;

			float faceLength = facing is MountFacing.North or MountFacing.South
				? footprintSize.X
				: footprintSize.Y;
			MountPortDefinition firstPort = facePorts[0];
			MountPortDefinition lastPort = facePorts[^1];
			float lowerBound = firstPort.FaceRange != Vector2.Zero
				? firstPort.FaceRange.X
				: -faceLength / 2;
			float upperBound = lastPort.FaceRange != Vector2.Zero
				? lastPort.FaceRange.Y
				: faceLength / 2;
			DrawPortRangeBox(
				part,
				facing,
				lowerBound,
				upperBound,
				depth,
				new Color(0.22f, 0.9f, 0.42f, 0.28f));
		}
	}

	private void DrawPortRangeBox(
		ShipPartInstance part,
		MountFacing facing,
		float lowerBound,
		float upperBound,
		float depth,
		Color color)
	{
		Vector2 footprintSize = GetFootprintSize(part.Definition);
		float halfWidth = footprintSize.X / 2;
		float halfHeight = footprintSize.Y / 2;
		Vector2[] localCorners = facing switch
		{
			MountFacing.North => new[]
			{
				new Vector2(lowerBound, -halfHeight),
				new Vector2(upperBound, -halfHeight),
				new Vector2(upperBound, -halfHeight - depth),
				new Vector2(lowerBound, -halfHeight - depth)
			},
			MountFacing.East => new[]
			{
				new Vector2(halfWidth, lowerBound),
				new Vector2(halfWidth + depth, lowerBound),
				new Vector2(halfWidth + depth, upperBound),
				new Vector2(halfWidth, upperBound)
			},
			MountFacing.South => new[]
			{
				new Vector2(lowerBound, halfHeight),
				new Vector2(upperBound, halfHeight),
				new Vector2(upperBound, halfHeight + depth),
				new Vector2(lowerBound, halfHeight + depth)
			},
			MountFacing.West => new[]
			{
				new Vector2(-halfWidth, lowerBound),
				new Vector2(-halfWidth - depth, lowerBound),
				new Vector2(-halfWidth - depth, upperBound),
				new Vector2(-halfWidth, upperBound)
			},
			_ => System.Array.Empty<Vector2>()
		};

		float rotation = Mathf.DegToRad(part.RotationDegrees);
		Vector2 center = part.Position * ViewScale;
		Vector2[] corners = localCorners
			.Select(point => center + point.Rotated(rotation) * ViewScale)
			.ToArray();
		DrawColoredPolygon(corners, color);
	}

	private void DrawHeldPart()
	{
		Color color = _currentPlacement.HasValue
			? new Color(0.36f, 0.72f, 0.48f, 0.72f)
			: new Color(0.85f, 0.34f, 0.30f, 0.55f);
		foreach (ShipPartInstance part in GetHeldPartsAtPreviewPosition())
			DrawPart(part, color, isSelected: true);
	}

	private void DrawCenteredText(string text, Vector2 center, int fontSize, float alpha = 1)
	{
		Font font = ThemeDB.FallbackFont;
		Vector2 textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
		DrawString(font, center - new Vector2(textSize.X / 2, -fontSize / 2), text, HorizontalAlignment.Left, -1, fontSize, new Color(1, 1, 1, alpha));
	}

	private static Vector2 GetFootprintSize(ShipPartDefinition definition)
	{
		if (definition.Footprint is RectangleShape2D rectangle)
		{
			return rectangle.Size;
		}

		return new Vector2(256, 256);
	}

	private static Vector2[] GetFootprintVertices(ShipPartDefinition definition)
	{
		if (definition.FootprintVertices.Count >= 3)
			return definition.FootprintVertices.ToArray();

		Vector2 size = GetFootprintSize(definition);
		return new[]
		{
			new Vector2(-size.X / 2, -size.Y / 2),
			new Vector2(size.X / 2, -size.Y / 2),
			new Vector2(size.X / 2, size.Y / 2),
			new Vector2(-size.X / 2, size.Y / 2)
		};
	}

	private static Vector2 FacingVector(MountFacing facing)
	{
		return facing switch
		{
			MountFacing.North => Vector2.Up,
			MountFacing.East => Vector2.Right,
			MountFacing.South => Vector2.Down,
			MountFacing.West => Vector2.Left,
			_ => Vector2.Zero
		};
	}

	private static MountFacing Opposite(MountFacing facing)
	{
		return (MountFacing)(((int)facing + 2) % 4);
	}

	private static MountPortDefinition FindCompatiblePort(
		ShipPartDefinition definition,
		MountPortDefinition targetPort,
		MountFacing requiredFacing,
		int rotationDegrees)
	{
		foreach (MountPortDefinition port in definition.MountPorts)
		{
			if (RotateFacing(port.Facing, rotationDegrees) == requiredFacing
				&& ArePortTypesCompatible(port, targetPort))
				return port;
		}

		return null;
	}

	private static bool IsDetachedSubassembly(ShipSubassembly subassembly)
	{
		return !subassembly.ContainsRoot
			&& subassembly.Parts.Any(part => !part.Definition.Tags.Contains("internal"));
	}

	private static MountFacing RotateFacing(MountFacing facing, int rotationDegrees)
	{
		int quarterTurns = ((rotationDegrees / 90) % 4 + 4) % 4;
		return (MountFacing)(((int)facing + quarterTurns) % 4);
	}

	private PlacementTarget? FindPlacementTarget(ShipPartDefinition heldDefinition, Vector2 cursorPosition)
	{
		PlacementTarget? nearest = null;
		float nearestDistanceSquared = SnapRadius * SnapRadius;

		foreach (ShipPartInstance targetPart in Design.Parts)
		{
			foreach (MountPortDefinition targetPort in targetPart.Definition.MountPorts)
			{
				bool isInternalPlacement = heldDefinition.Tags.Contains("internal");
				if ((isInternalPlacement && !IsInternalSupportPort(targetPort))
					|| IsPortUnavailable(targetPart, targetPort))
					continue;
				if (isInternalPlacement
					&& targetPart.Definition.Tags.Contains("large")
					&& !heldDefinition.Tags.Contains("large"))
					continue;

				Vector2 targetPosition = targetPart.GetWorldPortPosition(targetPort);
				int rotationDegrees = _heldRotationDegrees;
				if (!heldDefinition.AllowsRotation(rotationDegrees))
					continue;

				MountPortDefinition partPort = FindCompatiblePort(
					heldDefinition,
					targetPort,
					Opposite(targetPart.GetWorldPortFacing(targetPort)),
					rotationDegrees);
				if (partPort == null)
					continue;

				Vector2 rotatedPortPosition = partPort.LocalPosition.Rotated(Mathf.DegToRad(rotationDegrees));
				Vector2 portDelta = targetPosition - (cursorPosition + rotatedPortPosition);
				if (Mathf.Abs(portDelta.X) > SnapRadius || Mathf.Abs(portDelta.Y) > SnapRadius)
					continue;

				Vector2 partPosition = targetPosition - rotatedPortPosition;
				if (heldDefinition.Tags.Contains("internal")
					&& !IsInternalFootprintEnclosed(heldDefinition, rotationDegrees, partPosition))
					continue;

				float distance = portDelta.LengthSquared();
				if (distance > nearestDistanceSquared)
					continue;

				nearestDistanceSquared = distance;
				nearest = new PlacementTarget(
					targetPart,
					targetPort,
					partPort,
					partPosition,
					rotationDegrees);
			}
		}

		return nearest;
	}

	private PlacementTarget? FindGroupPlacementTarget(ShipPartInstance anchor, Vector2 anchorPosition)
	{
		PlacementTarget? nearest = null;
		float nearestDistanceSquared = SnapRadius * SnapRadius;

		foreach (ShipPartInstance heldPart in _pickedUpParts)
		{
			Vector2 relativePosition = heldPart.Position - anchor.Position;
			foreach (MountPortDefinition heldPort in heldPart.Definition.MountPorts)
			{
				Vector2 rotatedPortPosition = heldPort.LocalPosition
					.Rotated(Mathf.DegToRad(heldPart.RotationDegrees));
				foreach (ShipPartInstance targetPart in Design.Parts)
				{
					foreach (MountPortDefinition targetPort in targetPart.Definition.MountPorts)
					{
						if (IsPortUnavailable(targetPart, targetPort)
							|| !ArePortDefinitionsCompatible(
								heldPart.Definition,
								heldPort,
								heldPart.RotationDegrees,
								targetPart,
								targetPort))
							continue;

						Vector2 targetPosition = targetPart.GetWorldPortPosition(targetPort);
						Vector2 portDelta = targetPosition
							- (anchorPosition + relativePosition + rotatedPortPosition);
						if (Mathf.Abs(portDelta.X) > SnapRadius
							|| Mathf.Abs(portDelta.Y) > SnapRadius)
							continue;

						float distanceSquared = portDelta.LengthSquared();
						if (distanceSquared > nearestDistanceSquared)
							continue;

						nearestDistanceSquared = distanceSquared;
						nearest = new PlacementTarget(
							targetPart,
							targetPort,
							heldPort,
							targetPosition - relativePosition - rotatedPortPosition,
							heldPart.RotationDegrees);
					}
				}
			}
		}

		return nearest;
	}

	private bool HasCompatibleTargetPort(
		ShipPartDefinition heldDefinition,
		MountPortDefinition partPort,
		int rotationDegrees)
	{
		foreach (ShipPartInstance targetPart in Design.Parts)
		{
			if (targetPart.Definition.Tags.Contains("internal"))
				continue;

			foreach (MountPortDefinition targetPort in targetPart.Definition.MountPorts)
			{
				if (IsInternalSupportPort(targetPort) || IsPortUnavailable(targetPart, targetPort))
					continue;

				if (ArePortDefinitionsCompatible(
					heldDefinition,
					partPort,
					rotationDegrees,
					targetPart,
					targetPort))
					return true;
			}
		}

		return false;
	}

	private bool HasCompatibleHeldPort(ShipPartInstance targetPart, MountPortDefinition targetPort)
	{
		if (_pickedUpParts.Count > 0)
		{
			foreach (ShipPartInstance heldPart in GetHeldPartsAtPreviewPosition())
			{
				if (heldPart.Definition.Tags.Contains("internal"))
					continue;

				foreach (MountPortDefinition partPort in heldPart.Definition.MountPorts)
				{
					if (ArePortDefinitionsCompatible(
						heldPart.Definition,
						partPort,
						heldPart.RotationDegrees,
						targetPart,
						targetPort))
						return true;
				}
			}

			return false;
		}

		return FindCompatiblePort(
			_heldPartDefinition,
			targetPort,
			Opposite(targetPart.GetWorldPortFacing(targetPort)),
			_heldRotationDegrees) != null;
	}

	private static bool ArePortDefinitionsCompatible(
		ShipPartDefinition firstDefinition,
		MountPortDefinition firstPort,
		int firstRotation,
		ShipPartInstance secondPart,
		MountPortDefinition secondPort)
	{
		return ArePortTypesCompatible(firstPort, secondPort)
			&& RotateFacing(firstPort.Facing, firstRotation)
				== Opposite(secondPart.GetWorldPortFacing(secondPort));
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

	private ShipPartInstance FindPart(string instanceId)
	{
		foreach (ShipPartInstance part in Design.Parts)
		{
			if (part.InstanceId == instanceId)
			{
				return part;
			}
		}

		return null;
	}

	private ShipPartInstance FindPartAt(Vector2 localPosition)
	{
		for (int i = Design.Parts.Count - 1; i >= 0; i--)
		{
			if (ContainsPoint(Design.Parts[i], localPosition))
				return Design.Parts[i];
		}

		return null;
	}

	private ShipPartInstance FindPickedUpPart(string instanceId)
	{
		return _pickedUpParts.First(part => part.InstanceId == instanceId);
	}

	private List<ShipPartInstance> GetHeldPartsAtPreviewPosition()
	{
		if (_pickedUpParts.Count == 0)
		{
			return new List<ShipPartInstance>
			{
				new(_heldPartInstanceId, _heldPartDefinition, _previewPosition, _heldRotationDegrees)
			};
		}

		ShipPartInstance anchor = FindPickedUpPart(_heldAnchorInstanceId);
		Vector2 translation = _previewPosition - anchor.Position;
		return _pickedUpParts
			.Select(part => new ShipPartInstance(
				part.InstanceId,
				part.Definition,
				part.Position + translation,
				part.RotationDegrees))
			.ToList();
	}

	private static bool ContainsPoint(ShipPartInstance part, Vector2 localPosition)
	{
		Vector2 partPosition = part.Position;
		Vector2 pointInPart = (localPosition - partPosition)
			.Rotated(-Mathf.DegToRad(part.RotationDegrees));
		Vector2[] vertices = GetFootprintVertices(part.Definition);
		bool inside = false;
		for (int i = 0, j = vertices.Length - 1; i < vertices.Length; j = i++)
		{
			Vector2 first = vertices[i];
			Vector2 second = vertices[j];
			Vector2 edge = second - first;
			Vector2 fromFirst = pointInPart - first;
			float cross = edge.X * fromFirst.Y - edge.Y * fromFirst.X;
			float dot = fromFirst.Dot(edge);
			if (Mathf.Abs(cross) <= 0.01f && dot >= 0 && dot <= edge.LengthSquared())
				return true;

			if ((first.Y > pointInPart.Y) != (second.Y > pointInPart.Y)
				&& pointInPart.X < (second.X - first.X)
					* (pointInPart.Y - first.Y) / (second.Y - first.Y)
					+ first.X)
				inside = !inside;
		}

		return inside;
	}

	private void ClearHeldPart()
	{
		_isHoldingPart = false;
		_currentPlacement = null;
		_heldPartInstanceId = string.Empty;
		_heldPartDefinition = null;
		_heldCursorPosition = Vector2.Zero;
		_heldRotationDegrees = 0;
		_pickedUpParts.Clear();
		_originalPickedUpParts.Clear();
		_heldAnchorInstanceId = string.Empty;
		_heldAnchorCursorOffset = Vector2.Zero;
		_pickedUpConnections.Clear();
		_pickedUpLogisticsRoutes.Clear();
	}

	private bool IsPortUnavailable(
		ShipPartInstance part,
		MountPortDefinition port,
		IReadOnlyCollection<string> ignoredPartIds = null)
	{
		if (IsInternalSupportPort(port))
		{
			Vector2 internalAnchorPosition = part.GetWorldPortPosition(port);
			return part.Definition.MountPorts.Any(candidatePort =>
				IsInternalSupportPort(candidatePort)
				&& part.GetWorldPortPosition(candidatePort).DistanceTo(internalAnchorPosition)
					<= ConnectionPositionTolerance
				&& IsPortOccupied(part.InstanceId, candidatePort.PortId));
		}

		if (IsPortOccupied(part.InstanceId, port.PortId))
			return true;

		Vector2 portPosition = part.GetWorldPortPosition(port);
		foreach (ShipPartInstance otherPart in Design.Parts)
		{
			if (otherPart.InstanceId != part.InstanceId
				&& !(ignoredPartIds?.Contains(otherPart.InstanceId) ?? false)
				&& ContainsPoint(otherPart, portPosition))
				return true;
		}

		return false;
	}

	private bool IsPortOccupied(string partId, string portId)
	{
		foreach (PhysicalConnection connection in Design.Connections)
		{
			if ((connection.First.PartInstanceId == partId && connection.First.PortId == portId)
				|| (connection.Second.PartInstanceId == partId && connection.Second.PortId == portId))
			{
				return true;
			}
		}

		return false;
	}

	private List<PhysicalConnection> FindConnectionsFor(
		IReadOnlyList<ShipPartInstance> newParts,
		IReadOnlyCollection<string> newPartIds)
	{
		List<PhysicalConnection> connections = new();
		HashSet<(string PartId, string PortId)> occupiedPorts = new();

		foreach (ShipPartInstance newPart in newParts)
		{
			if (newPart.Definition.Tags.Contains("internal")
				&& !IsInternalFootprintEnclosed(
					newPart.Definition,
					newPart.RotationDegrees,
					newPart.Position))
				continue;

			foreach (MountPortDefinition newPartPort in newPart.Definition.MountPorts)
			{
				if (newPart.Definition.Tags.Contains("internal")
					&& !IsInternalSupportPort(newPartPort))
					continue;

				if (occupiedPorts.Contains((newPart.InstanceId, newPartPort.PortId))
					|| IsPortOccupied(newPart.InstanceId, newPartPort.PortId))
					continue;

				Vector2 newPortPosition = newPart.GetWorldPortPosition(newPartPort);
				foreach (ShipPartInstance existingPart in Design.Parts)
				{
					if (newPartIds.Contains(existingPart.InstanceId))
						continue;

					foreach (MountPortDefinition existingPort in existingPart.Definition.MountPorts)
					{
						if (occupiedPorts.Contains((existingPart.InstanceId, existingPort.PortId))
							|| IsPortUnavailable(existingPart, existingPort, newPartIds)
							|| !AreCompatible(newPart, newPartPort, existingPart, existingPort))
							continue;

						if (newPortPosition.DistanceTo(existingPart.GetWorldPortPosition(existingPort))
							> ConnectionPositionTolerance)
							continue;

						connections.Add(new PhysicalConnection(
							new PhysicalPortReference(newPart.InstanceId, newPartPort.PortId),
							new PhysicalPortReference(existingPart.InstanceId, existingPort.PortId)));
						occupiedPorts.Add((newPart.InstanceId, newPartPort.PortId));
						occupiedPorts.Add((existingPart.InstanceId, existingPort.PortId));
						break;
					}
				}
			}
		}

		return connections;
	}

	private bool IsInternalFootprintEnclosed(
		ShipPartDefinition definition,
		int rotationDegrees,
		Vector2 partPosition)
	{
		Vector2[] footprint = GetFootprintVertices(definition);
		float minX = footprint.Min(point => point.X);
		float maxX = footprint.Max(point => point.X);
		float minY = footprint.Min(point => point.Y);
		float maxY = footprint.Max(point => point.Y);
		float rotation = Mathf.DegToRad(rotationDegrees);
		for (float y = minY; y <= maxY; y += InternalCoverageSampleSpacing)
		{
			for (float x = minX; x <= maxX; x += InternalCoverageSampleSpacing)
			{
				Vector2 localPoint = new(x, y);
				if (!ContainsPointInPolygon(footprint, localPoint))
					continue;

				Vector2 worldPoint = partPosition + localPoint.Rotated(rotation);
				if (!IsPointCoveredByEligibleHull(definition, worldPoint))
					return false;
			}
		}

		for (int i = 0; i < footprint.Length; i++)
		{
			Vector2 start = footprint[i];
			Vector2 end = footprint[(i + 1) % footprint.Length];
			int sampleCount = Mathf.CeilToInt(
				start.DistanceTo(end) / InternalCoverageSampleSpacing);
			for (int sample = 0; sample <= sampleCount; sample++)
			{
				Vector2 localPoint = start.Lerp(end, sample / (float)sampleCount);
				Vector2 worldPoint = partPosition + localPoint.Rotated(rotation);
				if (!IsPointCoveredByEligibleHull(definition, worldPoint))
					return false;
			}
		}

		return true;
	}

	private bool IsPointCoveredByEligibleHull(ShipPartDefinition internalDefinition, Vector2 worldPoint)
	{
		return Design.Parts.Any(hullPart =>
			hullPart.Definition.HasCapability<StructuralCapability>()
			&& !hullPart.Definition.Tags.Contains("internal")
			&& (!hullPart.Definition.Tags.Contains("large")
				|| internalDefinition.Tags.Contains("large"))
			&& ContainsPoint(hullPart, worldPoint));
	}

	private static bool ContainsPointInPolygon(IReadOnlyList<Vector2> vertices, Vector2 point)
	{
		bool inside = false;
		for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
		{
			Vector2 first = vertices[i];
			Vector2 second = vertices[j];
			Vector2 edge = second - first;
			Vector2 fromFirst = point - first;
			float cross = edge.X * fromFirst.Y - edge.Y * fromFirst.X;
			float dot = fromFirst.Dot(edge);
			if (Mathf.Abs(cross) <= 0.01f && dot >= 0 && dot <= edge.LengthSquared())
				return true;

			if ((first.Y > point.Y) != (second.Y > point.Y)
				&& point.X < (second.X - first.X) * (point.Y - first.Y)
					/ (second.Y - first.Y) + first.X)
				inside = !inside;
		}

		return inside;
	}

	private static bool IsInternalSupportPort(MountPortDefinition port)
	{
		return port.CompatibilityClass == InternalSupportCompatibilityClass;
	}
	private static bool AreCompatible(
		ShipPartInstance firstPart,
		MountPortDefinition firstPort,
		ShipPartInstance secondPart,
		MountPortDefinition secondPort)
	{
		return ArePortTypesCompatible(firstPort, secondPort)
			&& ((int)firstPart.GetWorldPortFacing(firstPort) + 2) % 4
				== (int)secondPart.GetWorldPortFacing(secondPort);
	}

	private static ShipPartDefinition LoadOrCreateDefinition(
		string path,
		Func<ShipPartDefinition> createFallback,
		string description)
	{
		ShipPartDefinition definition = GD.Load<ShipPartDefinition>(path);
		if (IsUsableDefinition(definition))
		{
			return definition;
		}

		GD.PushWarning($"Using a temporary {description}; '{path}' is missing or incomplete.");
		return createFallback();
	}

	private static bool IsUsableDefinition(ShipPartDefinition definition)
	{
		return definition != null
			&& !string.IsNullOrWhiteSpace(definition.PartId)
			&& !string.IsNullOrWhiteSpace(definition.DisplayName)
			&& definition.Footprint != null
			&& definition.MountPorts.Count > 0;
	}

	private static ShipPartDefinition CreateRootDefinition()
	{
		ShipPartDefinition definition = CreateBaseDefinition("root_bridge", "Bridge");
		definition.Mass = 1600;
		definition.Tags.Add("root");
		definition.Tags.Add("command");
		definition.Capabilities.Add(new CommandCapability());
		definition.Capabilities.Add(new StructuralCapability());
		AddCardinalPorts(definition);
		return definition;
	}

	private static ShipPartDefinition CreateStructuralDefinition()
	{
		ShipPartDefinition definition = CreateBaseDefinition("structural_hull", "2×2 Hull");
		definition.Description = "A 2×2 structural hull part with 2×2 center and paired 1×1 mounting ports.";
		definition.Mass = 1600;
		definition.Tags.Add("structural");
		definition.Tags.Add("hull");
		definition.Capabilities.Add(new StructuralCapability());
		AddCardinalPorts(definition);
		return definition;
	}

	private static ShipPartDefinition CreateHalfStructuralDefinition()
	{
		ShipPartDefinition definition = CreateBaseDefinition("structural_hull_half", "1×1 Hull");
		definition.Description = "A 1×1 structural hull part that mounts to smaller structural ports.";
		definition.Tags.Add("structural");
		definition.Tags.Add("hull");
		definition.Tags.Add("half-size");
		definition.Capabilities.Add(new StructuralCapability());
		definition.Mass = 700;
		definition.Footprint = new RectangleShape2D { Size = new Vector2(128, 128) };
		definition.MountPorts.Add(CreatePort(
			"north", new Vector2(0, -64), MountFacing.North, "structural-half"));
		definition.MountPorts.Add(CreatePort(
			"east", new Vector2(64, 0), MountFacing.East, "structural-half"));
		definition.MountPorts.Add(CreatePort(
			"south", new Vector2(0, 64), MountFacing.South, "structural-half"));
		definition.MountPorts.Add(CreatePort(
			"west", new Vector2(-64, 0), MountFacing.West, "structural-half"));
		AddInternalSupportPorts(definition);
		return definition;
	}

	private static ShipPartDefinition CreateHalfStructuralLongDefinition()
	{
		ShipPartDefinition definition = CreateBaseDefinition(
			"structural_hull_half_2x1",
			"2×1 Hull");
		definition.Description = "A 2×1 structural hull part.";
		definition.Tags.Add("structural");
		definition.Tags.Add("hull");
		definition.Tags.Add("half-size");
		definition.Tags.Add("rotatable");
		definition.Capabilities.Add(new StructuralCapability());
		definition.Mass = 1100;
		definition.Footprint = new RectangleShape2D { Size = new Vector2(256, 128) };
		definition.MountPorts.Add(CreatePort(
			"north-left", new Vector2(-64, -64), MountFacing.North, "structural-half"));
		definition.MountPorts.Add(CreatePort(
			"north-right", new Vector2(64, -64), MountFacing.North, "structural-half"));
		definition.MountPorts.Add(CreatePort(
			"east", new Vector2(128, 0), MountFacing.East, "structural-half"));
		definition.MountPorts.Add(CreatePort(
			"south-left", new Vector2(-64, 64), MountFacing.South, "structural-half"));
		definition.MountPorts.Add(CreatePort(
			"south-right", new Vector2(64, 64), MountFacing.South, "structural-half"));
		definition.MountPorts.Add(CreatePort(
			"west", new Vector2(-128, 0), MountFacing.West, "structural-half"));
		return definition;
	}

	private static ShipPartDefinition CreateStructuralTriangleDefinition()
	{
		ShipPartDefinition definition = CreateBaseDefinition(
			"structural_hull_triangle",
			"2×2 Triangle Hull");
		definition.Description = "A 2×2 right-triangle structural hull part.";
		definition.Tags.Add("structural");
		definition.Tags.Add("hull");
		definition.Tags.Add("triangle");
		definition.Tags.Add("rotatable");
		definition.Capabilities.Add(new StructuralCapability());
		definition.Mass = 1200;
		definition.FootprintVertices.Add(new Vector2(-128, -128));
		definition.FootprintVertices.Add(new Vector2(128, -128));
		definition.FootprintVertices.Add(new Vector2(-128, 128));
		AddFullTriangleFacePorts(definition);
		return definition;
	}

	private static ShipPartDefinition CreateHalfStructuralTriangleDefinition()
	{
		ShipPartDefinition definition = CreateBaseDefinition(
			"structural_hull_half_triangle",
			"1×1 Triangle Hull");
		definition.Description = "A 1×1 right-triangle structural hull part.";
		definition.Tags.Add("structural");
		definition.Tags.Add("hull");
		definition.Tags.Add("half-size");
		definition.Tags.Add("triangle");
		definition.Tags.Add("rotatable");
		definition.Capabilities.Add(new StructuralCapability());
		definition.Mass = 600;
		definition.Footprint = new RectangleShape2D { Size = new Vector2(128, 128) };
		definition.FootprintVertices.Add(new Vector2(-64, -64));
		definition.FootprintVertices.Add(new Vector2(64, -64));
		definition.FootprintVertices.Add(new Vector2(-64, 64));
		definition.MountPorts.Add(CreatePort(
			"north", new Vector2(0, -64), MountFacing.North, "structural-half"));
		definition.MountPorts.Add(CreatePort(
			"west", new Vector2(-64, 0), MountFacing.West, "structural-half"));
		return definition;
	}

	private static ShipPartDefinition CreateStructuralNotchedDefinition()
	{
		ShipPartDefinition definition = CreateBaseDefinition(
			"structural_hull_notched",
			"2×2 Notched Hull");
		definition.Description = "A 2×2 structural hull part with its top-right 1×1 triangle corner removed.";
		definition.Tags.Add("structural");
		definition.Tags.Add("hull");
		definition.Tags.Add("notched");
		definition.Tags.Add("rotatable");
		definition.Capabilities.Add(new StructuralCapability());
		definition.Mass = 1200;
		definition.FootprintVertices.Add(new Vector2(-128, -128));
		definition.FootprintVertices.Add(new Vector2(0, -128));
		definition.FootprintVertices.Add(new Vector2(128, 0));
		definition.FootprintVertices.Add(new Vector2(128, 128));
		definition.FootprintVertices.Add(new Vector2(-128, 128));
		definition.MountPorts.Add(CreatePort(
			"north-half",
			new Vector2(-64, -128),
			MountFacing.North,
			"structural-half",
			new Vector2(-128, 0)));
		definition.MountPorts.Add(CreatePort(
			"east-half",
			new Vector2(128, 64),
			MountFacing.East,
			"structural-half",
			new Vector2(0, 128)));
		AddFullFacePorts(definition, MountFacing.South);
		AddFullFacePorts(definition, MountFacing.West);
		return definition;
	}

	private static ShipPartDefinition CreateStructuralLargeDefinition()
	{
		ShipPartDefinition definition = CreateBaseDefinition(
			"structural_hull_2x2",
			"4×4 Hull");
		definition.Description = "A rotatable 4×4 structural hull panel with 2×2 and 1×1 ports along each straight face.";
		definition.Tags.Add("structural");
		definition.Tags.Add("hull");
		definition.Tags.Add("large");
		definition.Tags.Add("rotatable");
		definition.Capabilities.Add(new StructuralCapability());
		definition.Mass = 6400;
		definition.Footprint = new RectangleShape2D { Size = new Vector2(512, 512) };
		AddLargeFacePorts(definition, MountFacing.North);
		AddLargeFacePorts(definition, MountFacing.East);
		AddLargeFacePorts(definition, MountFacing.South);
		AddLargeFacePorts(definition, MountFacing.West);
		return definition;
	}

	private static ShipPartDefinition CreateStructuralWideDefinition()
	{
		ShipPartDefinition definition = CreateBaseDefinition(
			"structural_hull_2x1",
			"4×2 Hull");
		definition.Description = "A rotatable 4×2 structural hull panel with 2×2 and 1×1 ports on every straight face.";
		definition.Tags.Add("structural");
		definition.Tags.Add("hull");
		definition.Tags.Add("long");
		definition.Tags.Add("rotatable");
		definition.Capabilities.Add(new StructuralCapability());
		definition.Mass = 3200;
		definition.Footprint = new RectangleShape2D { Size = new Vector2(512, 256) };
		AddWideFacePorts(definition, MountFacing.North);
		AddWideFacePorts(definition, MountFacing.South);
		AddWideEndPorts(definition, MountFacing.East);
		AddWideEndPorts(definition, MountFacing.West);
		return definition;
	}

	private static ShipPartDefinition CreateFuelTankDefinition(bool large = false)
	{
		ShipPartDefinition definition = CreateBaseDefinition(
			large ? "fuel_tank_4x4" : "fuel_tank_half_2x1",
			large ? "4×4 Fuel Tank" : "2×1 Fuel Tank");
		definition.Description = "An internal fuel tank. Place freely or mount fully enclosed by eligible hull pieces. Fuel capacity is not yet simulated.";
		definition.Tags.Add("internal");
		definition.Tags.Add("fuel");
		definition.Tags.Add("rotatable");
		if (large)
			definition.Tags.Add("large");
		definition.Mass = large ? 8800 : 1100;
		definition.Footprint = new RectangleShape2D
		{
			Size = large ? new Vector2(512, 512) : new Vector2(256, 128)
		};
		if (!large)
		{
			definition.MountPorts.Add(CreatePort(
				"support-left", new Vector2(-64, 0), MountFacing.South, InternalSupportCompatibilityClass));
			definition.MountPorts.Add(CreatePort(
				"support-right", new Vector2(64, 0), MountFacing.South, InternalSupportCompatibilityClass));
		}
		definition.LogisticsPorts.Add(CreateLogisticsPort(
			"fuel-out",
			LogisticsSystem.Fuel,
			LogisticsPortDirection.Source,
			Vector2.Zero));
		return definition;
	}

	private static ShipPartDefinition CreatePowerCoreDefinition(bool large = false)
	{
		ShipPartDefinition definition = CreateBaseDefinition(
			large ? "generator_2x2" : "power_core",
			large ? "2×2 Generator" : "2×1 Generator");
		definition.Description = "A rotatable internal generator: supplies power and consumes fuel. Output and consumption rates are not yet simulated.";
		definition.Tags.Add("internal");
		definition.Tags.Add("power");
		definition.Tags.Add("rotatable");
		definition.Mass = large ? 3600 : 1800;
		definition.Footprint = new RectangleShape2D
		{
			Size = new Vector2(256, large ? 256 : 128)
		};
		const float portOffset = 128;
		definition.LogisticsPorts.Add(CreateLogisticsPort(
			"power-out",
			LogisticsSystem.Power,
			LogisticsPortDirection.Source,
			new Vector2(-portOffset, 0)));
		definition.LogisticsPorts.Add(CreateLogisticsPort(
			"fuel-in",
			LogisticsSystem.Fuel,
			LogisticsPortDirection.Consumer,
			new Vector2(portOffset, 0)));
		return definition;
	}

	private static ShipPartDefinition CreateThrusterDefinition()
	{
		ShipPartDefinition definition = CreateBaseDefinition("thruster_2x2", "2×2 Thruster");
		definition.Description = "A 2×2 interior thruster with fixed directional thrust. Rotate it in 90-degree steps while placing; prototype logistics consumer requiring power and fuel.";
		definition.Tags.Add("internal");
		definition.Tags.Add("rotatable");
		definition.Tags.Add("thruster");
		definition.Mass = 1200;
		definition.Footprint = new RectangleShape2D { Size = new Vector2(256, 256) };
		definition.LogisticsPorts.Add(CreateLogisticsPort(
			"power-in",
			LogisticsSystem.Power,
			LogisticsPortDirection.Consumer,
			new Vector2(-64, 0)));
		definition.LogisticsPorts.Add(CreateLogisticsPort(
			"fuel-in",
			LogisticsSystem.Fuel,
			LogisticsPortDirection.Consumer,
			new Vector2(64, 0)));
		return definition;
	}

	private static ShipPartDefinition CreateGimballedThrusterDefinition()
	{
		ShipPartDefinition definition = CreateBaseDefinition(
			"gimballed_thruster_2x2",
			"2×2 Gimballed Thruster");
		definition.Description = "A 2×2 interior gimballed thruster that faces downward in the editor and is intended to aim in flight. Prototype logistics consumer requiring power and fuel.";
		definition.Tags.Add("internal");
		definition.Tags.Add("thruster");
		definition.Tags.Add("gimballed");
		definition.Mass = 1200;
		definition.Footprint = new RectangleShape2D { Size = new Vector2(256, 256) };
		definition.LogisticsPorts.Add(CreateLogisticsPort(
			"power-in",
			LogisticsSystem.Power,
			LogisticsPortDirection.Consumer,
			new Vector2(-64, 0)));
		definition.LogisticsPorts.Add(CreateLogisticsPort(
			"fuel-in",
			LogisticsSystem.Fuel,
			LogisticsPortDirection.Consumer,
			new Vector2(64, 0)));
		return definition;
	}

	private static LogisticsPortDefinition CreateLogisticsPort(
		string portId,
		LogisticsSystem system,
		LogisticsPortDirection direction,
		Vector2 localPosition)
	{
		return new LogisticsPortDefinition
		{
			PortId = portId,
			System = system,
			Direction = direction,
			LocalPosition = localPosition
		};
	}

	private static void AddWideFacePorts(ShipPartDefinition definition, MountFacing facing)
	{
		Vector2 center = facing == MountFacing.North
			? new Vector2(0, -128)
			: new Vector2(0, 128);
		Vector2 tangent = Vector2.Right;
		string side = facing.ToString().ToLowerInvariant();
		foreach (int offset in new[] { -128, 128 })
		{
			definition.MountPorts.Add(CreatePort(
				$"{side}-{offset}",
				center + tangent * offset,
				facing));
		}

		foreach (int offset in new[] { -192, -64, 64, 192 })
		{
			definition.MountPorts.Add(CreatePort(
				$"{side}-half-{offset}",
				center + tangent * offset,
				facing,
				"structural-half"));
		}
	}

	private static void AddWideEndPorts(ShipPartDefinition definition, MountFacing facing)
	{
		float x = facing == MountFacing.East ? 256 : -256;
		string side = facing.ToString().ToLowerInvariant();
		definition.MountPorts.Add(CreatePort(
			side,
			new Vector2(x, 0),
			facing));
		definition.MountPorts.Add(CreatePort(
			$"{side}-half-top",
			new Vector2(x, -64),
			facing,
			"structural-half"));
		definition.MountPorts.Add(CreatePort(
			$"{side}-half-bottom",
			new Vector2(x, 64),
			facing,
			"structural-half"));
	}

	private static void AddLargeFacePorts(ShipPartDefinition definition, MountFacing facing)
	{
		Vector2 center = facing switch
		{
			MountFacing.North => new Vector2(0, -256),
			MountFacing.East => new Vector2(256, 0),
			MountFacing.South => new Vector2(0, 256),
			MountFacing.West => new Vector2(-256, 0),
			_ => Vector2.Zero
		};
		Vector2 tangent = facing is MountFacing.North or MountFacing.South
			? Vector2.Right
			: Vector2.Down;
		string side = facing.ToString().ToLowerInvariant();
		foreach (int offset in new[] { -128, 128 })
		{
			Vector2 position = center + tangent * offset;
			string suffix = offset < 0 ? "left" : "right";
			definition.MountPorts.Add(CreatePort($"{side}-{suffix}", position, facing));
		}

		foreach (int offset in new[] { -192, -64, 64, 192 })
		{
			Vector2 position = center + tangent * offset;
			string suffix = offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
			definition.MountPorts.Add(CreatePort(
				$"{side}-half-{suffix}",
				position,
				facing,
				"structural-half"));
		}
	}

	private static void AddFullTriangleFacePorts(ShipPartDefinition definition)
	{
		AddFullFacePorts(definition, MountFacing.North);
		AddFullFacePorts(definition, MountFacing.West);
	}

	private static void AddFullFacePorts(ShipPartDefinition definition, MountFacing facing)
	{
		Vector2 center = facing switch
		{
			MountFacing.North => new Vector2(0, -128),
			MountFacing.East => new Vector2(128, 0),
			MountFacing.South => new Vector2(0, 128),
			MountFacing.West => new Vector2(-128, 0),
			_ => Vector2.Zero
		};
		Vector2 tangent = facing is MountFacing.North or MountFacing.South
			? Vector2.Right
			: Vector2.Down;
		string side = facing.ToString().ToLowerInvariant();
		definition.MountPorts.Add(CreatePort(side, center, facing));
		definition.MountPorts.Add(CreatePort(
			$"{side}-half-left", center - tangent * 64, facing, "structural-half"));
		definition.MountPorts.Add(CreatePort(
			$"{side}-half-right", center + tangent * 64, facing, "structural-half"));
	}

	private static ShipPartDefinition CreateBaseDefinition(string partId, string displayName)
	{
		return new ShipPartDefinition
		{
			PartId = partId,
			DisplayName = displayName,
			Description = $"Temporary visualization for {displayName}.",
			Tags = new Array<string>(),
			Mass = 0,
			BuildCost = 0,
			AllowedRotationsDegrees = new Array<int> { 0, 90, 180, 270 },
			Footprint = new RectangleShape2D { Size = new Vector2(256, 256) },
			MountPorts = new Array<MountPortDefinition>(),
			Capabilities = new Array<PartCapability>()
		};
	}

	private static void AddCardinalPorts(ShipPartDefinition definition)
	{
		definition.MountPorts.Add(CreatePort("north", new Vector2(0, -128), MountFacing.North));
		definition.MountPorts.Add(CreatePort("east", new Vector2(128, 0), MountFacing.East));
		definition.MountPorts.Add(CreatePort("south", new Vector2(0, 128), MountFacing.South));
		definition.MountPorts.Add(CreatePort("west", new Vector2(-128, 0), MountFacing.West));
		AddHalfFacePortsIfMissing(definition);
	}

	private static void AddHalfFacePortsIfMissing(ShipPartDefinition definition)
	{
		AddHalfPortIfMissing(definition, "north-half-left", new Vector2(-64, -128), MountFacing.North);
		AddHalfPortIfMissing(definition, "north-half-right", new Vector2(64, -128), MountFacing.North);
		AddHalfPortIfMissing(definition, "east-half-top", new Vector2(128, -64), MountFacing.East);
		AddHalfPortIfMissing(definition, "east-half-bottom", new Vector2(128, 64), MountFacing.East);
		AddHalfPortIfMissing(definition, "south-half-left", new Vector2(-64, 128), MountFacing.South);
		AddHalfPortIfMissing(definition, "south-half-right", new Vector2(64, 128), MountFacing.South);
		AddHalfPortIfMissing(definition, "west-half-top", new Vector2(-128, -64), MountFacing.West);
		AddHalfPortIfMissing(definition, "west-half-bottom", new Vector2(-128, 64), MountFacing.West);
	}

	private static void AddInternalSupportPorts(ShipPartDefinition definition)
	{
		Vector2 size = GetFootprintSize(definition);
		for (float y = -size.Y / 2 + 64; y < size.Y / 2; y += 128)
		{
			for (float x = -size.X / 2 + 64; x < size.X / 2; x += 128)
			{
				Vector2 position = new(x, y);
				if (!ContainsPointInPolygon(GetFootprintVertices(definition), position))
					continue;

				foreach (MountFacing facing in Enum.GetValues<MountFacing>())
				{
					string id = $"internal-support-{x}-{y}-{facing.ToString().ToLowerInvariant()}";
					if (definition.FindMountPort(id) == null)
						definition.MountPorts.Add(CreatePort(
							id,
							position,
							facing,
							InternalSupportCompatibilityClass));
				}
			}
		}
	}

	private static void AddHalfPortIfMissing(
		ShipPartDefinition definition,
		string id,
		Vector2 position,
		MountFacing facing)
	{
		if (definition.FindMountPort(id) == null)
			definition.MountPorts.Add(CreatePort(id, position, facing, "structural-half"));
	}

	private static MountPortDefinition CreatePort(
		string id,
		Vector2 position,
		MountFacing facing,
		string compatibilityClass = "structural",
		Vector2 faceRange = default)
	{
		return new MountPortDefinition
		{
			PortId = id,
			CompatibilityClass = compatibilityClass,
			Size = 1,
			LocalPosition = position,
			FaceRange = faceRange,
			Facing = facing
		};
	}

	private readonly record struct PlacementTarget(
		ShipPartInstance TargetPart,
		MountPortDefinition TargetPort,
		MountPortDefinition PartPort,
		Vector2 PartPosition,
		int RotationDegrees);
}
