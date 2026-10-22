using Godot;
using Higherfleet.Ships.Parts;

namespace Higherfleet.Ships.Model;

public sealed record LogisticsPassOver(
	string MarkerId,
	LogisticsSystem System,
	Vector2 Position);
