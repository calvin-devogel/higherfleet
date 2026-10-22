using System.Collections.Generic;

namespace Higherfleet.Ships.Model;

public sealed record ShipSubassembly(
	IReadOnlyList<ShipPartInstance> Parts,
	bool ContainsRoot);
