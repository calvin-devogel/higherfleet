using System;
using System.Linq;
using Godot;
using Higherfleet.Ships.Model;
using Higherfleet.Ships.Parts;
using Higherfleet.Ships.View;

public partial class ShipBuilderRegressionTests : Node
{
	private int _failures;
	private int _suiteCount;

	public override void _Ready()
	{
		Run("component sizes, rotations, ports and mounting", CheckComponents);
		Run("splitting at bends and inside segments", CheckSplitting);
		Run("open conduit, extension, branch and consumer", CheckNetworkEditing);
		Run("joining an existing segment and restoring picked-up endpoints", CheckJoining);
		Run("undo and cancellation leave the network unchanged", CheckCancellation);
		Run("pass-overs and different systems remain isolated", CheckPassOvers);
		Run("invalid paths and duplicate consumers are rejected", CheckInvalidPaths);
		GD.Print($"Ship builder regression suites: {_suiteCount - _failures}/{_suiteCount} passed.");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}

	private void Run(string name, Action<ShipAssembly> test)
	{
		_suiteCount++;
		ShipAssembly ship = new();
		AddChild(ship);
		try
		{
			test(ship);
			GD.Print($"PASS: {name}");
		}
		catch (Exception exception)
		{
			_failures++;
			GD.PushError($"FAIL: {name}\n{exception}");
		}
		finally
		{
			ship.Free();
		}
	}

	private static void Require(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException(message);
	}

	private static ShipPartInstance Drop(ShipAssembly ship)
	{
		Require(ship.TryDropHeldPart(false, out string status), status);
		return ship.Design.Parts[^1];
	}

	private static void RequireSize(ShipPartInstance part, Vector2 size)
	{
		Require(part.Definition.Footprint is RectangleShape2D rectangle && rectangle.Size == size,
			$"{part.Definition.DisplayName} must have footprint {size}.");
	}

	private static void CheckComponents(ShipAssembly ship)
	{
		ship.BeginNewPowerCorePlacement(new Vector2(-64, 0));
		Require(ship.RotateHeldPart(90), "2x1 generator must rotate.");
		ShipPartInstance generator = Drop(ship);
		RequireSize(generator, new Vector2(256, 128));
		Require(generator.RotationDegrees == 90, "Generator rotation must survive placement.");
		Require(ship.Design.Connections.Any(connection =>
			connection.First.PartInstanceId == generator.InstanceId
			|| connection.Second.PartInstanceId == generator.InstanceId),
			"2x1 generator must mount inside a 2x2 hull.");
		Require(generator.Definition.LogisticsPorts.Any(port =>
			port.System == LogisticsSystem.Power && port.Direction == LogisticsPortDirection.Source),
			"Generator must supply power.");
		Require(generator.Definition.LogisticsPorts.Any(port =>
			port.System == LogisticsSystem.Fuel && port.Direction == LogisticsPortDirection.Consumer),
			"Generator must consume fuel.");
		RequireGeneratorPortsAreOpposite(generator);

		ship.BeginNewStructuralHullPlacement(new Vector2(768, 0));
		ShipPartInstance generatorHull = Drop(ship);
		ship.BeginNewLargeGeneratorPlacement(generatorHull.Position);
		Require(ship.RotateHeldPart(90), "2x2 generator must rotate.");
		ShipPartInstance largeGenerator = Drop(ship);
		RequireSize(largeGenerator, new Vector2(256, 256));
		RequireGeneratorPortsAreOpposite(largeGenerator);
		Require(ship.Design.Connections.Any(connection =>
			(connection.First.PartInstanceId == largeGenerator.InstanceId && connection.Second.PartInstanceId == generatorHull.InstanceId)
			|| (connection.Second.PartInstanceId == largeGenerator.InstanceId && connection.First.PartInstanceId == generatorHull.InstanceId)),
			"2x2 generator must mount inside a 2x2 hull.");

		ship.BeginNewStructuralLargePlacement(new Vector2(1536, 0));
		ShipPartInstance hull = Drop(ship);
		ship.BeginNewLargeFuelTankPlacement(hull.Position);
		ShipPartInstance tank = Drop(ship);
		RequireSize(tank, new Vector2(512, 512));
		Require(tank.Definition.Tags.Contains("large"), "4x4 tank must qualify for large-only hulls.");
		Require(ship.Design.Connections.Any(connection =>
			(connection.First.PartInstanceId == tank.InstanceId && connection.Second.PartInstanceId == hull.InstanceId)
			|| (connection.Second.PartInstanceId == tank.InstanceId && connection.First.PartInstanceId == hull.InstanceId)),
			"4x4 fuel tank must mount in the 4x4 hull.");
		Require(tank.Definition.LogisticsPorts.Single().System == LogisticsSystem.Fuel,
			"Large tank must have a fuel port.");
		ship.BeginNewFuelTankPlacement(new Vector2(2304, 0));
		RequireSize(Drop(ship), new Vector2(256, 128));
	}

	private static void RequireGeneratorPortsAreOpposite(ShipPartInstance generator)
	{
		LogisticsPortDefinition power = generator.Definition.LogisticsPorts.Single(port =>
			port.System == LogisticsSystem.Power);
		LogisticsPortDefinition fuel = generator.Definition.LogisticsPorts.Single(port =>
			port.System == LogisticsSystem.Fuel);
		Vector2 size = ((RectangleShape2D)generator.Definition.Footprint).Size;
		Require(power.LocalPosition == new Vector2(-size.X / 2, 0),
			"Generator power output must be centered on its left face.");
		Require(fuel.LocalPosition == new Vector2(size.X / 2, 0),
			"Generator fuel input must be centered on the opposite, right face.");
		Require(generator.GetWorldLogisticsPortPosition(power)
			!= generator.GetWorldLogisticsPortPosition(fuel),
			"Rotated generator logistics ports must remain separated.");
	}

	private static void CheckSplitting(ShipAssembly ship)
	{
		ShipDesign design = ship.Design;
		design.AddLogisticsRoute(new LogisticsRoute(
			"bent", LogisticsSystem.Power,
			new LogisticsEndpoint(new Vector2(-128, 0)), new LogisticsEndpoint(new Vector2(0, 128)),
			new[] { Vector2.Zero }));
		design.SplitLogisticsRoutesAt(LogisticsSystem.Power, Vector2.Zero);
		Require(design.LogisticsRoutes.Count == 2, "Splitting at a bend must create two sections.");
		Require(design.LogisticsRoutes.All(route => route.Waypoints.Count == 0),
			"A split bend must not produce a duplicate zero-length vertex.");
		design.SplitLogisticsRoutesAt(LogisticsSystem.Power, new Vector2(-64, 0));
		Require(design.LogisticsRoutes.Count == 3, "An interior grid split must add one section.");
		design.SplitLogisticsRoutesAt(LogisticsSystem.Power, new Vector2(-64, 0));
		design.SplitLogisticsRoutesAt(LogisticsSystem.Fuel, new Vector2(0, 64));
		Require(design.LogisticsRoutes.Count == 3, "Endpoint splits and other systems must not split again.");
		RequireValidLogistics(design);
		design.AddLogisticsJunction(new LogisticsJunction(
			"split-junction", LogisticsSystem.Power, new Vector2(-64, 0)));
		Require(design.RemoveLogisticsRoute("bent"), "First split section must retain the original ID.");
		Require(design.LogisticsJunctions.Count == 0, "Removing a section must prune an orphaned junction.");
		RequireValidLogistics(design);
	}

	private static ShipPartInstance PrepareRouting(ShipAssembly ship)
	{
		ShipPartDefinition hull = new()
		{
			PartId = "test-hull",
			DisplayName = "Test hull",
			Footprint = new RectangleShape2D { Size = new Vector2(2048, 2048) }
		};
		hull.Capabilities.Add(new StructuralCapability());
		ship.Design.AddPart(new ShipPartInstance("test-hull", hull, Vector2.Zero, 0));
		ship.BeginNewPowerCorePlacement(new Vector2(-256, -384));
		ShipPartInstance source = Drop(ship);
		ship.BeginLogisticsRouting(LogisticsSystem.Power);
		return source;
	}

	private static void Click(ShipAssembly ship, Vector2 position)
	{
		Require(ship.HandleLogisticsRoutingClick(position, false, out string status), status);
	}

	private static void CreateTrunk(ShipAssembly ship)
	{
		Click(ship, new Vector2(-384, -384));
		Click(ship, new Vector2(0, -384));
		Require(ship.FinishLogisticsConduit(out string status), status);
	}

	private static void CheckNetworkEditing(ShipAssembly ship)
	{
		ShipPartInstance source = PrepareRouting(ship);
		CreateTrunk(ship);
		LogisticsRoute trunk = ship.Design.LogisticsRoutes.Single();
		Require(trunk.Start.Port?.PartInstanceId == source.InstanceId, "Trunk must retain its source port.");
		Require(!trunk.End.Port.HasValue && trunk.End.Position == new Vector2(0, -384),
			"Enter must save an open end at the last clicked vertex.");
		Require(trunk.Waypoints.Count == 0, "Open end must not be duplicated as a waypoint.");

		Click(ship, new Vector2(0, -384));
		Click(ship, new Vector2(128, -384));
		Require(ship.FinishLogisticsConduit(out string status), status);
		Require(ship.Design.LogisticsRoutes.Count == 2, "Extending must retain the original conduit.");
		Require(ship.Design.LogisticsJunctions.Count == 0, "A two-way extension needs no branch junction.");

		Click(ship, new Vector2(-192, -384));
		Require(ship.Design.LogisticsRoutes.Count == 2, "Selecting a branch start must not split before commit.");
		Click(ship, new Vector2(-192, -128));
		Require(ship.FinishLogisticsConduit(out status), status);
		Require(ship.Design.LogisticsRoutes.Count == 4, "Branching must split the trunk and add one branch.");
		Require(ship.Design.LogisticsJunctions.Single().Position == new Vector2(-192, -384),
			"A branch must automatically receive a junction.");

		Click(ship, new Vector2(128, -384));
		Click(ship, new Vector2(128, 0));
		Click(ship, Vector2.Zero);
		Require(ship.Design.LogisticsRoutes.Any(route => route.End.Port == new LogisticsPortReference("root", "power-in")),
			"An extension must be able to finish at a consumer.");
		RequireValidLogistics(ship.Design);

		Require(ship.HandleLogisticsRoutingClick(new Vector2(-192, -256), true, out status), status);
		Require(ship.Design.LogisticsRoutes.Count == 4, "Right-click must remove only the selected branch section.");
		RequireValidLogistics(ship.Design);
	}

	private static void CheckCancellation(ShipAssembly ship)
	{
		PrepareRouting(ship);
		CreateTrunk(ship);
		Click(ship, new Vector2(-192, -384));
		Click(ship, new Vector2(-192, -256));
		Require(ship.HandleLogisticsRoutingClick(Vector2.Zero, true, out string status), status);
		Require(!ship.FinishLogisticsConduit(out _), "Undo must remove the user-authored vertex.");
		ship.CancelLogisticsRouting();
		Require(ship.Design.LogisticsRoutes.Count == 1 && ship.Design.LogisticsJunctions.Count == 0,
			"Cancelling a branch must preserve the existing route exactly.");
	}

	private static void CheckJoining(ShipAssembly ship)
	{
		ShipPartInstance source = PrepareRouting(ship);
		CreateTrunk(ship);
		ship.Design.AddLogisticsRoute(new LogisticsRoute(
			"join-target", LogisticsSystem.Power,
			new LogisticsEndpoint(new Vector2(128, -512)),
			new LogisticsEndpoint(new Vector2(128, -128)), Array.Empty<Vector2>()));
		Click(ship, new Vector2(0, -384));
		Click(ship, new Vector2(128, -384));
		Require(ship.Design.LogisticsRoutes.Count == 4,
			"Finishing on a conduit must split the target and add the connecting section.");
		Require(ship.Design.LogisticsJunctions.Single().Position == new Vector2(128, -384),
			"Joining the middle of a section must add a junction.");
		RequireValidLogistics(ship.Design);

		ship.CancelLogisticsRouting();
		Require(ship.TryPickUpPartAt(source.Position, false, out string status), status);
		Require(ship.Design.LogisticsRoutes.Count == 3, "Picking up a source must remove only its incident section.");
		ship.CancelHeldPartPlacement();
		Require(ship.Design.LogisticsRoutes.Count == 4, "Cancelling pickup must restore the incident section.");
		RequireValidLogistics(ship.Design);
		Require(ship.TryPickUpPartAt(source.Position, false, out status), status);
		Require(ship.DeleteHeldPart(out status), status);
		Require(ship.Design.LogisticsRoutes.Count == 3, "Deleting a source must leave the rest of the network intact.");
		RequireValidLogistics(ship.Design);
	}

	private static void CheckPassOvers(ShipAssembly ship)
	{
		PrepareRouting(ship);
		CreateTrunk(ship);
		Vector2 crossing = new(-192, -384);
		ship.Design.AddLogisticsRoute(new LogisticsRoute(
			"crossing", LogisticsSystem.Power,
			new LogisticsEndpoint(new Vector2(-192, -512)),
			new LogisticsEndpoint(new Vector2(-192, -256)), Array.Empty<Vector2>()));
		Require(ship.TryAddLogisticsPassOver(crossing, out string status), status);
		Click(ship, crossing);
		Require(!ship.FinishLogisticsConduit(out _), "Pass-over must not start a connected branch.");
		Require(!ship.TryAddLogisticsJunction(crossing, out _), "Pass-over must block a junction.");
		ship.BeginLogisticsRouting(LogisticsSystem.Fuel);
		Click(ship, new Vector2(0, -384));
		Require(!ship.FinishLogisticsConduit(out _), "Fuel must not extend a power conduit.");
		RequireValidLogistics(ship.Design);
	}

	private static void CheckInvalidPaths(ShipAssembly ship)
	{
		PrepareRouting(ship);
		Click(ship, new Vector2(-384, -384));
		Click(ship, new Vector2(-256, -320));
		Require(!ship.FinishLogisticsConduit(out _), "A non-45-degree segment must be rejected.");
		Click(ship, new Vector2(2048, -384));
		Require(!ship.FinishLogisticsConduit(out _), "A segment outside the hull must be rejected.");
		Click(ship, new Vector2(0, -384));
		Require(ship.FinishLogisticsConduit(out string status), status);
		Click(ship, new Vector2(0, -384));
		Click(ship, new Vector2(-192, -384));
		Require(ship.Design.LogisticsRoutes.Count == 1, "Retracing a trunk must not duplicate or split it.");
		ship.CancelLogisticsRouting();
		ship.BeginLogisticsRouting(LogisticsSystem.Power);
		Click(ship, new Vector2(0, -384));
		Click(ship, Vector2.Zero);
		int routeCount = ship.Design.LogisticsRoutes.Count;
		Click(ship, new Vector2(-384, -384));
		Click(ship, Vector2.Zero);
		Require(ship.Design.LogisticsRoutes.Count == routeCount, "Consumer must not accept a second direct conduit.");
		RequireValidLogistics(ship.Design);
	}

	private static void RequireValidLogistics(ShipDesign design)
	{
		DesignIssue[] issues = ShipDesignValidator.Validate(design)
			.Where(issue => issue.Code.Contains("logistics", StringComparison.Ordinal))
			.ToArray();
		Require(issues.Length == 0, string.Join("; ", issues.Select(issue => issue.Message)));
	}
}
