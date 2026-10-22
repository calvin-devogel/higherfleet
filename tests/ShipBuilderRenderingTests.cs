using System;
using Godot;
using Higherfleet.Ships.Model;
using Higherfleet.Ships.Parts;
using Higherfleet.Ships.View;

public partial class ShipBuilderRenderingTests : Node
{
	public override async void _Ready()
	{
		SubViewport viewport = new()
		{
			Size = new Vector2I(512, 512),
			OwnWorld3D = true,
			World2D = new World2D(),
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always
		};
		AddChild(viewport);
		ShipAssembly ship = new() { Position = new Vector2(256, 256) };
		viewport.AddChild(ship);
		try
		{
			ship.BeginNewStructuralLargePlacement(Vector2.Zero);
			Require(ship.TryDropHeldPart(false, out string status), status);
			ship.BeginNewLargeGeneratorPlacement(Vector2.Zero);
			Require(ship.TryDropHeldPart(false, out status), status);

			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			using Image baseline = viewport.GetTexture().GetImage();
			Require(baseline != null && !baseline.IsEmpty(), "Rendering test requires a non-headless renderer.");
			Color generator = baseline.GetPixel(286, 266);
			Color hull = baseline.GetPixel(352, 266);
			Require(!Similar(generator, hull), "Generator body must be visually distinct from hull.");
			Require(Similar(baseline.GetPixel(318, 266), hull),
				"Internal visual inset must expose hull just inside the unchanged 2x2 mounting footprint.");
			Require(Similar(baseline.GetPixel(256, 256), new Color(1f, 0.88f, 0.38f)),
				"Generator must display its lightning-bolt symbol.");

			ship.Design.AddLogisticsRoute(new LogisticsRoute(
				"render-test", LogisticsSystem.Power,
				new LogisticsEndpoint(new Vector2(-192, 0)),
				new LogisticsEndpoint(new Vector2(192, 0)),
				Array.Empty<Vector2>()));
			ship.QueueRedraw();
			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			using Image normal = viewport.GetTexture().GetImage();
			Require(Similar(normal.GetPixel(286, 256), baseline.GetPixel(286, 256)),
				"Internal components must occlude conduits outside routing mode.");
			Require(!Similar(normal.GetPixel(336, 256), baseline.GetPixel(336, 256)),
				"Conduits must remain visible above generic hulls.");

			ship.BeginLogisticsRouting(LogisticsSystem.Power);
			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			using Image routing = viewport.GetTexture().GetImage();
			Require(!Similar(routing.GetPixel(286, 256), normal.GetPixel(286, 256)),
				"Routing mode must bring conduits above internal components.");
			ship.CancelLogisticsRouting();
			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			using Image restored = viewport.GetTexture().GetImage();
			Require(Similar(restored.GetPixel(286, 256), normal.GetPixel(286, 256)),
				"Leaving routing mode must restore occlusion.");
			GD.Print("PASS: rendered generator appearance, internal inset, hull visibility, and routing occlusion.");
			GetTree().Quit();
		}
		catch (Exception exception)
		{
			GD.PushError($"FAIL: rendering regression\n{exception}");
			GetTree().Quit(1);
		}
		finally
		{
			viewport.QueueFree();
		}
	}

	private static bool Similar(Color first, Color second) =>
		Mathf.Abs(first.R - second.R) < 0.04f
		&& Mathf.Abs(first.G - second.G) < 0.04f
		&& Mathf.Abs(first.B - second.B) < 0.04f;

	private static void Require(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException(message);
	}
}
