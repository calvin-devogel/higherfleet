using System;
using System.Collections.Generic;
using Godot;
using Higherfleet.Ships.Parts;

namespace Higherfleet.Ships.Model;

public sealed class LogisticsRoute
{
	public const float GridSize = 64;
	public const float JunctionGridSize = GridSize / 2;

	public string RouteId { get; }
	public LogisticsSystem System { get; }
	public LogisticsEndpoint Start { get; }
	public LogisticsEndpoint End { get; }
	public IReadOnlyList<Vector2> Waypoints { get; }

	public IReadOnlyList<Vector2> GetPoints(IReadOnlyList<ShipPartInstance> parts)
	{
		List<Vector2> points = new() { Start.GetPosition(parts) };
		points.AddRange(Waypoints);
		points.Add(End.GetPosition(parts));
		return points;
	}

	public static bool IsValidSegment(Vector2 start, Vector2 end)
	{
		Vector2 delta = end - start;
		return delta != Vector2.Zero
			&& (Mathf.IsZeroApprox(delta.X) || Mathf.IsZeroApprox(delta.Y)
				|| Mathf.IsEqualApprox(Mathf.Abs(delta.X), Mathf.Abs(delta.Y)));
	}

	public static bool PointLiesOnSegment(Vector2 point, Vector2 start, Vector2 end)
	{
		Vector2 segment = end - start;
		Vector2 offset = point - start;
		if (segment.IsZeroApprox())
			return point.IsEqualApprox(start);

		float cross = segment.X * offset.Y - segment.Y * offset.X;
		return Mathf.Abs(cross) <= 0.01f
			&& offset.Dot(segment) >= 0
			&& offset.Dot(segment) <= segment.LengthSquared();
	}

	public static bool SegmentsOverlap(Vector2 firstStart, Vector2 firstEnd, Vector2 secondStart, Vector2 secondEnd)
	{
		Vector2 axis = firstEnd - firstStart;
		if (axis.IsZeroApprox())
			return false;

		Vector2 offset = secondStart - firstStart;
		Vector2 direction = secondEnd - secondStart;
		if (!Mathf.IsZeroApprox(axis.Cross(offset)) || !Mathf.IsZeroApprox(axis.Cross(direction)))
			return false;

		float start = offset.Dot(axis);
		float end = (secondEnd - firstStart).Dot(axis);
		return Mathf.Min(axis.LengthSquared(), Mathf.Max(start, end))
			- Mathf.Max(0, Mathf.Min(start, end)) > 0.01f;
	}

	public static Vector2 SnapToGrid(Vector2 position)
	{
		return new Vector2(
			Mathf.Round(position.X / GridSize) * GridSize,
			Mathf.Round(position.Y / GridSize) * GridSize);
	}

	public static Vector2 SnapJunctionToGrid(Vector2 position)
	{
		return new Vector2(
			Mathf.Round(position.X / JunctionGridSize) * JunctionGridSize,
			Mathf.Round(position.Y / JunctionGridSize) * JunctionGridSize);
	}

	public LogisticsRoute(
		string routeId,
		LogisticsSystem system,
		LogisticsPortReference source,
		LogisticsPortReference consumer,
		IEnumerable<Vector2> waypoints)
		: this(routeId, system, new LogisticsEndpoint(Vector2.Zero, source),
			new LogisticsEndpoint(Vector2.Zero, consumer), waypoints)
	{
	}

	public LogisticsRoute(
		string routeId,
		LogisticsSystem system,
		LogisticsEndpoint start,
		LogisticsEndpoint end,
		IEnumerable<Vector2> waypoints)
	{
		if (string.IsNullOrWhiteSpace(routeId))
			throw new ArgumentException("A logistics route must have a stable ID.", nameof(routeId));

		ArgumentNullException.ThrowIfNull(waypoints);
		RouteId = routeId;
		System = system;
		Start = start;
		End = end;
		Waypoints = new List<Vector2>(waypoints).AsReadOnly();
	}
}
