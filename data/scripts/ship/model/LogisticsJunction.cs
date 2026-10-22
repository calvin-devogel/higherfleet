using Godot;
using Higherfleet.Ships.Parts;

namespace Higherfleet.Ships.Model;

public sealed record LogisticsJunction(
	string JunctionId,
	LogisticsSystem System,
	Vector2 Position);
