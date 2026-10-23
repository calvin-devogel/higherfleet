# Ship Builder Development Roadmap

## Purpose

Rebuild the ship builder as a dependable, extensible 2D side-view design tool. The builder should make ship construction enjoyable while exposing engineering tradeoffs that can later matter in combat. This roadmap covers the ship builder only; it does not plan combat, the campaign overworld, or trade.

The existing builder implementation is not a design constraint. Reuse useful art and part scenes where they fit, but keep the new ship model and editor behavior independent of the current builder logic.

## Product direction

The first useful builder should let a player assemble and edit a ship with a small part catalog. It should communicate what is attached, what is questionable, and how design choices affect the ship. It should not require a complete engineering simulation before the basic editing loop is usable.

### Decisions already made

- **View:** 2D side view.
- **Physical attachment:** Parts connect at named snap points/ports.
- **Structural geometry and terminology:** A half-size grid cell is 1×1 (128×128 design units); a full-size hull part is 2×2 (256×256). The catalog also includes 2×1, triangular, notched, 4×2, and 4×4 hull footprints. Polygonal footprints govern drawing and hit-testing. The 4×2 hull uses a 512×256 footprint and an estimated 3.2 t mass; the 4×4 hull uses a 512×512 footprint and an estimated 6.4 t mass, with centered 2×2 attachment ports on each face. Structural ports are named by their grid scale: 2×2 and 1×1.
- **Initial ship scale:** One full square tile represents a 10 m × 10 m side-view compartment; a 0.5×1 tile compartment is 5 m × 10 m and has a 25-person capacity at 2 m² per person.
- **Initial hull mass estimate:** A full tile is approximately 1.6 metric tonnes and a 0.5×1 tile frame approximately 1.1 tonnes, using a hollow steel-frame estimate. These are tuning estimates, not simulation-grade engineering values.
- **Catalog layout:** The component catalog sits in a vertically scrollable right sidebar, grouped by component class. Hull choices are listed directly in the HULL pop-up by 1×1/2×2 grid scale; design help and stats remain in the upper-left panel. The ship canvas is centered in the area left of the sidebar, and the currently held component is named in the selection readout and gets a bright outline.
- **First internal component:** A rotatable 2×1 fuel tank can be freely placed horizontally or vertically, or mounted wherever its full footprint is enclosed by eligible hull pieces. A 4×4 hull is reserved for large-scale internal components. Free-placed tanks remain unconnected and do not count/display as detached structural subassemblies; mounted tanks connect to their supporting hull pieces. Its current 1.1 t mass is a provisional component estimate; stored fuel capacity and fuel mass are not modeled yet.
- **Generators and larger storage:** The former Power Core is now a rotatable 2×1 Generator (256×128 design units); a 2×2 Generator (256×256) is also available. Both supply power and consume fuel. The 4×4 Fuel Tank (512×512) supplies fuel and qualifies for large-only hull support. New masses are provisional area-scaled estimates: 3.6 t for the 2×2 generator and 8.8 t for the 4×4 tank. Generator output, consumption rates, and tank capacities are not simulated yet.
- **Interior visualization:** Internal components are drawn at 92% of their mounting footprint size, leaving a visible hull border without changing placement, hit-testing, support, or logistics-port coordinates. Generators have purple bodies and a lightning-bolt symbol. Outside routing mode, conduits are drawn above hulls but below internal components; routing mode brings the grid and conduits above the interiors.
- **Prototype propulsion:** The regular 2×2 thruster points in its placed orientation and is rotated in 90° increments while being placed. The 2×2 gimballed thruster faces downward in the editor; in-flight aiming is a future gameplay behavior. Both are currently prototype power/fuel consumers.
- **Catalog interaction:** Each component class in the right sidebar is a dropdown menu whose entries select individual parts.
- **Part orientation:** Allowed rotations are specified per part rather than being assumed universal.
- **Placement interaction:** Choose a part, then place it freely or snap it to a compatible port.
- **Structural port sizes:** 2×2 parts expose center ports and paired 1×1 ports on each face. 1×1 parts use only 1×1 ports.
- **Validation style:** Advisory. Show clear warnings and permit questionable designs where practical instead of blocking every placement.
- **First prototype goal:** Place, rotate, move, and delete parts. Control-click (Command-click on macOS) an already-placed non-command part to hold a copy while keeping the source in place. Multi-select and copy/paste are also desired editing controls.
- **First engineering layer:** Staged, explainable summaries and warnings, followed by deeper systems.
- **Physical and logistical connections:** Physical snap points mount parts only. System routes are a separate, later builder layer.
- **Future logistics direction:** Players route systems through compartments, junctions, and shared networks. Dedicated or backup routes should be possible, and route/part placement should have engineering and gameplay consequences.
- **Project approach:** Replace builder logic and architecture; retain useful existing art and part scenes.
- **Design priority:** Detailed engineering and tradeoffs that can lead to meaningfully different combat designs.

### Ship persistence

Save and load versioned JSON designs from the game's persistent `user://ship_designs` directory. Saves preserve parts, rotations, physical connections, logistics routes, and routing markers; loading validates references before replacing the current design.

### Recommended, but not yet confirmed

- Add undo/redo early, after the basic placement model is stable. Group movement, deletion, and paste are safer and easier to learn when reversible.
- Use a small starter catalog for the first usable version. Expand content after the editing and data model have been exercised.
- Keep ship definitions independent of Godot scene nodes so they can be saved, validated, tested, and handed to future combat code.

## Scope boundaries

### Included

- Physical ship assembly and editing.
- Part catalog and part definitions.
- Design-time feedback, statistics, and warnings.
- Ship design persistence and a stable handoff format for future game systems.
- A later, separate logistics-routing layer inside the builder.

### Not included in this roadmap

- Combat implementation or combat balancing.
- Campaign map, travel, trade, and economy.
- Realistic fluid/electrical simulation beyond what is needed to make builder feedback useful.
- A requirement to replace existing art. Reuse it where it supports the new interaction and data model.

## Roadmap at a glance

| Phase | Outcome | Gate |
|---|---|---|
| 0. Product and model foundation | A small, explicit model for parts, physical attachments, and design feedback | A ship can be described without editor nodes |
| 1. Placement prototype | Parts can be chosen and attached through snap points | A basic ship can be assembled and invalid placements are explained |
| 2. Editing workflow | Rotate, move, delete, multi-select, and copy/paste work predictably | Common edits preserve valid attachment relationships |
| 3. Advisory engineering | Summaries and warnings explain the consequences of a design | Changing a part changes the relevant feedback |
| 4. Save/load and handoff | Designs persist and have a stable representation for future systems | A design survives restart and can be consumed outside the editor |
| 5. Logistics-routing layer | Players create compartment-based system networks and redundancy | Connectivity and route choices affect reported capability |
| 6. Builder validation and polish | The builder is robust, understandable, and ready to grow | A representative design workflow passes usability and regression checks |

## Phase 0 — Product and model foundation

**Goal:** Set the model boundaries before building editor interactions, avoiding a second scene-driven builder that is difficult to test or extend.

### Work

1. Inventory existing part scenes, scripts, artwork, snap points, and collision/overlap helpers. Mark each as reusable, adaptable, or legacy-only; do not migrate code just because it exists.
2. Define a data representation for:
   - A part definition: stable ID, display metadata, scene/art reference, allowed orientations, physical attachment ports, and engineering properties.
   - A part instance: definition ID, stable instance ID, orientation, and placement.
   - A physical attachment: the two part/port references that are connected.
   - A ship design: its part instances and physical attachment relationships.
3. Establish a single coordinate and orientation convention for the side-view ship. Define port identity and compatibility—including 2×2 and 1×1 structural ports—so placement, rotation, validation, and rendering agree.
4. Keep editor selection, preview, and UI state separate from the ship design itself.
5. Decide how to represent a structurally attached group and what happens to dependent parts when a support part is removed. Record unresolved policy choices rather than hiding them in editor code.
6. Define the builder's warning categories and severity language. At minimum distinguish informational feedback, design warnings, and hard data/operation errors.

### Acceptance criteria

- A small example ship can be created and inspected as data without instantiating the editor.
- A physical connection references two known compatible ports.
- Part orientation and port transforms are deterministic and testable.
- Reusable assets are identified without requiring the current builder scripts to remain in the new architecture.

## Phase 1 — Placement prototype

**Goal:** Make the core interaction clear and reliable before adding advanced engineering.

### Work

1. Create a builder screen with a side-view canvas, a bottom-docked catalog grouped by component class and scale, and a selected-part/placement preview. Put alternate shapes in the matching scale group's submenu.
2. Let the player select a part, rotate it through only its allowed orientations, and see the resulting port locations.
3. Highlight compatible snap points on the ship. Let the player place the preview freely or snap it to a compatible port, then create the physical attachment in the ship model when snapped.
4. Show a clear invalid-placement preview and reason for nonmatching ports, occupied ports, or prohibited overlap. Preserve advisory behavior: questionable designs should normally remain editable.
5. Add selection and a compact part/ship information panel. Keep feedback near the relevant part or port where possible.
6. Support an empty ship/root part and a small representative catalog: 2×2 and 1×1 structural pieces, a bridge or control part, a propulsion part, and one weapon or utility part.
7. Support canvas panning and cursor-centered zoom so large designs and precise port placement remain practical.
8. Let held triangular parts rotate in 90-degree steps with the scroll wheel, while the wheel zooms when no part is held.

### Acceptance criteria

- A player can assemble a small ship without dragging parts to pixel-perfect positions.
- 1×1 pieces snap only to paired 1×1 structural ports; 2×2 pieces can use either 2×2 center or paired 1×1 ports.
- Snap previews, final positions, and port orientation agree at all supported rotations.
- The player can tell why a connection is unavailable.
- Placement does not silently create duplicate or dangling physical links.
- The prototype remains usable at different window sizes and zoom levels.

## Phase 2 — Editing workflow

**Goal:** Make iteration on a design faster than rebuilding it.

### Work

1. Rotate an existing part within its allowed orientations. Define whether connected neighbors move, reconnect, or become warnings; use one consistent and visible rule.
2. Move a part and reattach it through the same placement/port rules used for new parts.
3. Delete parts and explicitly handle attached descendants or disconnected groups. Show the player what will be affected before destructive group changes.
4. Add multi-select, group move, and copy/paste. Preserve relative positions and internal physical links when copying a group; validate its external connections when pasting.
5. Add undo/redo if adopted. Prefer meaningful user operations as history entries (for example, one group move is one undo), and ensure undo/redo updates both the design model and its visual representation.
6. Show selection, attachment state, and placement mode distinctly; provide keyboard shortcuts only alongside visible UI affordances.

### Acceptance criteria

- Move, rotate, delete, and paste do not leave stale attachment links.
- A copied group retains its internal connections and can be attached to the ship using normal rules.
- Undo/redo, if included, restores the complete design state rather than only its visual position.
- The player can recover from common editing mistakes without restarting the ship.

## Phase 3 — Advisory engineering summaries

**Goal:** Make part choices matter through legible, traceable feedback without requiring full network simulation.

The live readout shows total design mass. It sums part masses in kilograms and includes parts currently being held, so pickup and group movement do not make the displayed total appear to drop. The initial structural scale assumes a 100 mm × 100 mm × 8 mm hollow steel section (about 23 kg/m), a full-tile perimeter plus two cross-members, and 15% additional mass for joints and fittings. Round the result to about 1.6 t for a 10 m × 10 m full tile; for a 5 m × 10 m half-width compartment with one cross-member, round to about 1.1 t. These are tuning estimates, not simulation-grade engineering values, and should be revisited when structural properties and material choices become data-driven.

### Work

1. Add typed, unit-bearing properties to part definitions and calculate ship summaries from the design model.
2. Start with design-time totals and simple derivations. Clearly label installed capacity versus connected/usable capability; before logistics routing exists, do not suggest that unconnected systems can deliver their capacity.
3. Add warnings for meaningful design problems while allowing the player to continue editing. Each warning should identify affected part(s), explain the reason, and suggest what kind of change could resolve it.
4. Add a ship summary panel with the initial requested categories:
   - Mass and center of mass.
   - Thrust and maneuverability indicators.
   - Power generation and demand/capacity.
   - Structural connectivity and unsupported/disconnected parts.
   - Weapon locations and firing arcs.
   - Build cost.
   - Sensor/radar range and EWAR capability.
   - Cargo and fuel capacity.
   - Crew capacity and requirements.
   - Survivability indicators, such as armor or critical components exposed to damage.
5. Present uncertainty honestly: use simple indicators or “not yet simulated” where the builder lacks enough information; do not imply combat outcomes that are not calculated.
6. Recalculate summaries after every design change, and keep the calculation logic testable without UI scenes.

### Acceptance criteria

- Adding, removing, or moving a part updates all affected summaries.
- Each summary has a defined unit or an explicitly qualitative label.
- Warnings are actionable and can be traced to the design condition that produced them.
- Identical designs yield identical summary values and warning sets.
- The UI distinguishes actual current calculations from planned future simulation.

## Phase 4 — Persistence and future-system handoff

**Goal:** Make a completed design durable and independent of the current editor session.

### Work

1. Define a versioned save format using stable part IDs and explicit attachment references, not scene-tree paths or transient node IDs.
2. Save and load designs. Validate missing part definitions, malformed references, unsupported versions, and invalid values; report failures instead of silently substituting parts.
3. Add a small design list or equivalent open/save workflow and communicate unsaved changes.
4. Define a stable read-only design representation that future combat systems can consume without depending on editor controls or scene structure.
5. Add migration policy for save-format changes before introducing incompatible content.

### Acceptance criteria

- Save → close/reopen → load reproduces the same part instances, orientations, physical links, and summaries.
- Invalid or unsupported files produce useful errors and do not corrupt another design.
- A consumer can read the ship representation without instantiating the builder UI.

## Phase 5 — Manual logistics-routing layer

**Goal:** Make system placement and redundancy meaningful through player-authored networks, while keeping logistics separate from physical attachment.

This phase follows the physical editor and basic summaries. Do not make it a prerequisite for the first usable builder.

**Initial prototype:** The builder has a separate grid-routing mode for power and fuel. Generators supply power and consume fuel; thrusters consume both; fuel tanks supply fuel; and the Bridge consumes power. Conduit sections have typed part-port or grid-point endpoints and can be left unfinished. Players click snapped 64-unit grid vertices to author each straight 90- or 45-degree segment explicitly; paths are constrained to the hull-footprint union. Different systems are color-coded.

**Extending and branching:** In ROUTING mode, click a source port or existing conduit to begin. Click an open end to extend it, or a segment to start a branch. Click grid vertices to steer, then click a consumer or existing conduit to finish; Enter saves at the last vertex with an open end, shown as a small ring. Committing a branch splits the existing conduit section at that point and automatically adds a junction where three or more sections meet. The original trunk is shared, not copied into each branch. Right-click undoes one authored vertex while drawing, clears the pending start if no vertices remain, or removes a marker/section when no route is pending. Esc discards the pending edit and leaves committed conduits unchanged.

**Crossings and scope:** Same-system crossings remain unconnected unless explicitly joined; shared section endpoints represent deliberate connections. J adds a junction, O marks an intentional unconnected pass-over, and T marks a pass-through point on an existing conduit. Pass-over markers must be removed before branching/joining at that point. Different systems cannot be joined. Existing conduits may not be retraced by overlapping same-system sections. Markers are authoring/visualization aids, not physical ship parts or flow simulation. The prototype does not yet model flow/capacity, compartments, shared-capacity calculations, redundancy behavior, route-length consequences, or usable-capability calculations.

**Regression checks:** After `dotnet build Higherfleet.csproj --no-restore`, run `godot --headless --path . res://tests/ship_builder_regressions.tscn` using the Godot .NET executable. The scene exits nonzero on failure and covers component sizes/mounting, segment splits, open ends, extensions, branches, consumer attachment, cancellation, invalid geometry, and pass-over/system isolation.

**Rendering checks:** Run `godot --path . --rendering-method gl_compatibility res://tests/ship_builder_rendering.tscn` with a display-enabled Godot .NET runtime (not `--headless`). Pixel checks verify generator appearance, the internal visual inset, conduits visible through hulls, and occlusion toggling with routing mode.

### Work

1. Define the compartment model: how compartments are authored or derived, how parts occupy them, and how compartment boundaries constrain routes.
2. Define routing elements such as source/consumer ports, junctions, shared segments, and route capacity. Keep these distinct from physical snap points.
3. Add a separate routing/edit mode so players can place and inspect routes without confusing them with part mounting.
4. Begin with a small set of systems—recommended initial candidates are electrical power, fuel, and ammunition—then add other logistics only when their gameplay purpose is clear.
5. Support dedicated and backup supply lines. Report disconnected consumers, insufficient capacity, bottlenecks, and whether redundancy actually survives a route/element failure, as appropriate to the simulation level.
6. Recompute usable capability from the networks. Show how a generator, tank, or magazine that is installed but not routed differs from one that is connected.
7. Make the location of compartments, junctions, and supply sources affect route length, capacity, vulnerability, or other explicitly defined engineering consequences.

### Acceptance criteria

- A player can create, inspect, edit, and remove a route independently of physical part connections.
- Network results are deterministic and explainable from the authored graph.
- Dedicated and backup lines are distinguishable and their benefits are visible in feedback.
- The builder explains disconnected or constrained consumers and does not count unavailable supply as usable capability.
- Existing designs without routes remain loadable and editable.

## Phase 6 — Builder validation and polish

**Goal:** Harden the builder for continued content and feature development.

### Work

- Exercise representative designs: minimal craft, asymmetrical craft, rotated components, large copied groups, disconnected structures, and routed systems.
- Add focused automated tests for port compatibility, transforms, attachment integrity, group operations, summary calculations, persistence, and routing.
- Review warnings for clarity, severity, and consistency; ensure warnings do not obscure ordinary creative iteration.
- Improve catalog browsing and ship-summary layout as the part count grows.
- Profile only after representative ships exist; optimize demonstrated bottlenecks in selection, placement preview, validation, or rendering.
- Document the part-definition authoring workflow and the rules for adding properties, ports, and future network interfaces.

### Acceptance criteria

- Core design operations and data calculations have regression coverage.
- A representative ship can be edited, saved, loaded, and inspected without broken references.
- Adding a new part using the documented workflow does not require bespoke editor logic.
- Validation issues are visible and recoverable rather than silently ignored.

## Cross-cutting implementation principles

- **One source of truth:** The ship design model owns parts and connections; scenes render and interact with that model.
- **Separate layers:** Physical mounting, design-time calculations, and logistical networks are related but distinct concepts.
- **Advisory by default:** Warnings guide and explain; only malformed operations or data should be hard failures unless a later rule is deliberately specified.
- **Explain every derived value:** Show where a number comes from or which parts contribute to it.
- **Stable identity:** Persist stable IDs and explicit references, not editor-specific object paths.
- **Test logic outside the UI:** Keep port transforms, validation, calculations, and network logic independently testable.
- **Grow in playable increments:** Finish the placement loop before expanding the catalog or simulating every engineering detail.

## Open design questions

Resolve these when the relevant phase begins; they do not block the placement prototype:

1. **Port compatibility:** Are ports matched by exact type, size/class, direction, or a combination? Can adapters bridge types?
2. **Occupied ports:** Can one port host multiple connections, or is each physical port single-use?
3. **Overlap policy:** Which overlaps are hard-invalid, advisory, or intentional (for example, armor covering another part)?
4. **Part movement:** When a part moves or rotates, should attached components follow automatically, reconnect where possible, or detach with a preview?
5. **Deletion and support:** Does deleting a structural part delete its attached subtree, leave a floating group, or prompt the player?
6. **Rotation semantics:** What is each part's allowed-orientation metadata, and how are asymmetric ports handled?
7. **Ship coordinate system:** What is the ship's origin, and how should center of mass and bounds be displayed for an asymmetric ship?
8. **Compartment definition:** Are compartments explicit buildable units, inferred from hull geometry, or a hybrid?
9. **Route construction:** How do players create junctions and shared lines, and how are capacities, route length, and failure/redundancy represented?
10. **Undo/redo:** Include it in the first usable release, or defer until the basic edit operations stabilize?
11. **Save location and UX:** Where are designs stored, how are they named, and is ship duplication/versioning needed?
12. **Warnings and thresholds:** Which conditions are warnings versus hard errors, and what numeric limits are balance rules rather than editor rules?

## Suggested first milestone

Deliver Phases 0 and 1 with a deliberately small catalog. The milestone is complete when a player can start with a root/bridge, choose a structural or functional part, preview its allowed orientations, attach it at a compatible port, and understand why an attempted invalid connection is unavailable. Do not wait for save/load, combat, or logistics simulation to validate whether the new builder interaction feels right.
