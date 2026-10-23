using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using Higherfleet.Ships.Model;
using Higherfleet.Ships.Parts;

namespace Higherfleet.Ships.View;

public partial class ShipBuilderDemo : Node2D
{
	private const float ViewScale = 0.5f;
	private const float MinimumZoom = 0.25f;
	private const float MaximumZoom = 2.5f;
	private const float ZoomStep = 1.1f;
	private ShipAssembly _ship;
	private Label _statusLabel;
	private Label _massLabel;
	private Label _selectedPartLabel;
	private MenuButton _commandMenuButton;
	private Button _saveButton;
	private Button _loadButton;
	private FileDialog _saveDialog;
	private FileDialog _loadDialog;
	private ConfirmationDialog _loadConfirmation;
	private string _pendingLoadPath = string.Empty;
	private readonly List<BaseButton> _catalogButtons = new();
	private bool _isPanning;

	public override void _Ready()
	{
		_ship = GetNode<ShipAssembly>("ShipAssembly");
		BuildInterface();
		CenterShip();
		UpdateCatalogButtons();
		UpdateStatus(string.IsNullOrEmpty(_ship.InitializationError)
			? "Place components, then use ROUTING to draw explicit grid-based power or fuel conduits inside the hull."
			: _ship.InitializationError);
		try
		{
			ShipDesignPersistence.EnsureSaveDirectory();
		}
		catch (IOException exception)
		{
			GD.PushError($"Could not prepare ship save directory: {exception}");
			UpdateStatus($"Ship saving is unavailable: {exception.Message}");
		}
		UpdateCatalogButtons();
	}

	public override void _Process(double delta)
	{
		if (_ship.IsHoldingPart)
			_ship.UpdateHeldPartPlacement(GetShipLocalMousePosition());
		if (_ship.IsRouting)
			_ship.UpdateLogisticsRouteCursor(GetShipLocalMousePosition());
		UpdateSelectedPartReadout();
		UpdateMassReadout();
		UpdateCatalogButtons();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false } finishKey
			&& _ship.IsRouting
			&& finishKey.Keycode is Key.Enter or Key.KpEnter)
		{
			_ship.FinishLogisticsConduit(out string finishStatus);
			UpdateStatus(finishStatus);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.J }
			&& _ship.IsRouting)
		{
			_ship.TryAddLogisticsJunction(GetShipLocalMousePosition(), out string junctionStatus);
			UpdateStatus(junctionStatus);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event is InputEventKey { Pressed: true, Echo: false } markerKey
			&& _ship.IsRouting
			&& markerKey.Keycode is Key.O or Key.T)
		{
			string markerStatus;
			if (markerKey.Keycode == Key.O)
				_ship.TryAddLogisticsPassOver(GetShipLocalMousePosition(), out markerStatus);
			else
				_ship.TryAddLogisticsPassThrough(GetShipLocalMousePosition(), out markerStatus);
			UpdateStatus(markerStatus);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape }
			&& _ship.IsRouting)
		{
			_ship.CancelLogisticsRouting();
			UpdateStatus("Logistics routing cancelled.");
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event is InputEventKey { Pressed: true, Echo: false } keyEvent
			&& _ship.IsHoldingPart
			&& keyEvent.Keycode is Key.Q or Key.E)
		{
			int rotationDegrees = keyEvent.Keycode == Key.Q ? -90 : 90;
			bool rotated = _ship.RotateHeldPart(rotationDegrees);
			UpdateStatus(rotated
				? $"Rotated held part {(rotationDegrees > 0 ? "clockwise" : "counterclockwise")} by 90 degrees. Click to place it."
				: "This part cannot be rotated.");
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape }
			&& _ship.IsHoldingPart)
		{
			_ship.CancelHeldPartPlacement();
			UpdateStatus("Placement cancelled.");
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event is InputEventMouseMotion motionEvent && _isPanning)
		{
			_ship.Position += motionEvent.Relative;
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event is not InputEventMouseButton mouseEvent)
			return;

		if (mouseEvent.ButtonIndex == MouseButton.Middle)
		{
			_isPanning = mouseEvent.Pressed;
			GetViewport().SetInputAsHandled();
			return;
		}

		if (mouseEvent.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
		{
			if (mouseEvent.Pressed)
			{
				if (_ship.IsHoldingPart)
				{
					bool rotated = _ship.RotateHeldPart(
						mouseEvent.ButtonIndex == MouseButton.WheelUp ? 90 : -90);
					UpdateStatus(rotated
						? "Rotated held part by 90 degrees. Click to place it."
						: "This part cannot be rotated with the scroll wheel.");
				}
				else
				{
					ZoomAtCursor(mouseEvent.ButtonIndex == MouseButton.WheelUp ? ZoomStep : 1 / ZoomStep);
				}
			}
			GetViewport().SetInputAsHandled();
			return;
		}

		if (!mouseEvent.Pressed
			|| (mouseEvent.ButtonIndex != MouseButton.Left && mouseEvent.ButtonIndex != MouseButton.Right))
		{
			return;
		}

		Vector2 shipPosition = GetShipLocalMousePosition();
		if (_ship.IsRouting
			&& mouseEvent.Pressed
			&& mouseEvent.ButtonIndex is MouseButton.Left or MouseButton.Right)
		{
			GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
			_ship.HandleLogisticsRoutingClick(
				shipPosition,
				mouseEvent.ButtonIndex == MouseButton.Right,
				out string routeStatus);
			UpdateStatus(routeStatus);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (mouseEvent.ButtonIndex == MouseButton.Right)
		{
			if (_ship.IsHoldingPart)
			{
				if (_ship.DeleteHeldPart(out string deleteReason))
					UpdateStatus($"{deleteReason} Detached subassemblies: {_ship.DetachedSubassemblyCount}.");
				UpdateCatalogButtons();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (_ship.TryPickUpPartAt(shipPosition, true, out string pickupReason))
			{
				UpdateStatus($"{pickupReason} Detached subassemblies: {_ship.DetachedSubassemblyCount}.");
			}
			else
			{
				UpdateStatus(pickupReason);
			}

			UpdateCatalogButtons();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_ship.IsHoldingPart)
		{
			_ship.UpdateHeldPartPlacement(shipPosition);
			bool retainHeldHull = OS.GetName() == "macOS"
				? mouseEvent.MetaPressed
				: mouseEvent.CtrlPressed;

			if (_ship.TryDropHeldPart(retainHeldHull, out string reason))
			{
				UpdateStatus($"{reason} Ship parts: {_ship.GetPartCount()}. Detached subassemblies: {_ship.DetachedSubassemblyCount}.");
				UpdateCatalogButtons();
				GetViewport().SetInputAsHandled();
			}
			else
			{
				UpdateStatus(reason);
				GetViewport().SetInputAsHandled();
			}
		}
		else
		{
			bool copyModifierPressed = OS.GetName() == "macOS"
				? mouseEvent.MetaPressed
				: mouseEvent.CtrlPressed;
			string pickupReason;
			bool pickedUp = copyModifierPressed
				? _ship.TryCopyPartAt(shipPosition, out pickupReason)
				: _ship.TryPickUpPartAt(shipPosition, false, out pickupReason);
			if (pickedUp)
			{
				UpdateStatus(copyModifierPressed
					? pickupReason
					: $"{pickupReason} Detached subassemblies: {_ship.DetachedSubassemblyCount}.");
			}
			else
			{
				UpdateStatus(pickupReason);
			}

			UpdateCatalogButtons();
			GetViewport().SetInputAsHandled();
		}
	}

	private void BuildInterface()
	{
		CanvasLayer layer = new();
		AddChild(layer);

		PanelContainer panel = new();
		panel.Position = new Vector2(20, 20);
		panel.CustomMinimumSize = new Vector2(280, 0);
		layer.AddChild(panel);

		VBoxContainer content = new();
		content.AddThemeConstantOverride("separation", 12);
		panel.AddChild(content);

		Label title = new()
		{
			Text = "SHIP BUILDER",
			ThemeTypeVariation = "HeaderSmall"
		};
		content.AddChild(title);

		Label help = new()
		{
			Text = "Save and load ship designs as JSON files. Choose a part from the catalog, then click to place or snap it. Control-click a placed part to copy it. Press Q/E or scroll while holding to rotate. Use ROUTING to draw conduits and place routing markers. Middle-drag to pan; scroll otherwise to zoom."
		};
		help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(help);

		HBoxContainer persistenceButtons = new();
		persistenceButtons.AddThemeConstantOverride("separation", 8);
		content.AddChild(persistenceButtons);
		_saveButton = new Button { Text = "SAVE SHIP", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_saveButton.Pressed += () => _saveDialog.PopupCentered();
		persistenceButtons.AddChild(_saveButton);
		_loadButton = new Button { Text = "LOAD SHIP", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_loadButton.Pressed += () => _loadDialog.PopupCentered();
		persistenceButtons.AddChild(_loadButton);
		BuildPersistenceDialogs(layer);

		Label statsTitle = new()
		{
			Text = "DESIGN STATS",
			ThemeTypeVariation = "HeaderSmall"
		};
		content.AddChild(statsTitle);
		Label selectedPartTitle = new()
		{
			Text = "SELECTED COMPONENT",
			ThemeTypeVariation = "HeaderSmall"
		};
		content.AddChild(selectedPartTitle);
		_selectedPartLabel = new Label
		{
			Text = "None",
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		_selectedPartLabel.AddThemeFontSizeOverride("font_size", 20);
		_selectedPartLabel.AddThemeColorOverride("font_color", new Color(0.35f, 0.9f, 1f));
		content.AddChild(_selectedPartLabel);
		_massLabel = new Label();
		content.AddChild(_massLabel);
		UpdateMassReadout();

		_statusLabel = new Label();
		_statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(_statusLabel);

		BuildCatalogDock(layer);
	}

	private void BuildCatalogDock(CanvasLayer layer)
	{
		PanelContainer dock = new();
		dock.AnchorLeft = 1;
		dock.AnchorTop = 0;
		dock.AnchorRight = 1;
		dock.AnchorBottom = 1;
		dock.OffsetLeft = -300;
		dock.OffsetTop = 16;
		dock.OffsetRight = -16;
		dock.OffsetBottom = -16;
		layer.AddChild(dock);

		ScrollContainer scroll = new();
		scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		dock.AddChild(scroll);

		VBoxContainer categories = new();
		categories.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		categories.AddThemeConstantOverride("separation", 20);
		scroll.AddChild(categories);

		CreateHullCategoryMenu(categories);
		CreateInternalCategoryMenu(categories);
		CreateCommandCategoryMenu(categories);
		CreateRoutingCategoryMenu(categories);
	}

	private void BuildPersistenceDialogs(CanvasLayer layer)
	{
		_saveDialog = new FileDialog
		{
			Title = "Save Ship Design",
			FileMode = FileDialog.FileModeEnum.SaveFile,
			Access = FileDialog.AccessEnum.Userdata,
			CurrentDir = ShipDesignPersistence.SaveDirectory,
			CurrentFile = "ship.json"
		};
		_saveDialog.AddFilter("*.json", "Ship designs");
		_saveDialog.FileSelected += SaveShipDesign;
		layer.AddChild(_saveDialog);

		_loadDialog = new FileDialog
		{
			Title = "Load Ship Design",
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Access = FileDialog.AccessEnum.Userdata,
			CurrentDir = ShipDesignPersistence.SaveDirectory
		};
		_loadDialog.AddFilter("*.json", "Ship designs");
		_loadDialog.FileSelected += ConfirmLoadShipDesign;
		layer.AddChild(_loadDialog);

		_loadConfirmation = new ConfirmationDialog
		{
			Title = "Load Ship Design",
			DialogText = "Loading this design will replace the current ship."
		};
		_loadConfirmation.Confirmed += LoadPendingShipDesign;
		layer.AddChild(_loadConfirmation);
	}

	private void CreateHullCategoryMenu(Control parent)
	{
		MenuButton button = new() { Text = "HULL" };
		button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		button.CustomMinimumSize = new Vector2(0, 40);
		button.TooltipText = "Choose a hull shape sized in 1×1 grid cells.";
		PopupMenu popup = button.GetPopup();
		Dictionary<int, Action> actions = new()
		{
			[0] = StartHullPlacement,
			[1] = StartTrianglePlacement,
			[2] = StartNotchedHullPlacement,
			[3] = StartHalfHullPlacement,
			[4] = StartLongHalfHullPlacement,
			[5] = StartHalfTrianglePlacement,
			[6] = StartLargeHullPlacement,
			[7] = StartWideHullPlacement
		};
		popup.AddItem("2×2 hull", 0);
		popup.AddItem("2×2 triangle hull", 1);
		popup.AddItem("2×2 notched hull", 2);
		popup.AddSeparator();
		popup.AddItem("1×1 hull", 3);
		popup.AddItem("2×1 hull", 4);
		popup.AddItem("1×1 triangle hull", 5);
		popup.AddSeparator();
		popup.AddItem("4×4 hull", 6);
		popup.AddItem("4×2 hull", 7);
		popup.IdPressed += id =>
		{
			if (actions.TryGetValue((int)id, out Action action))
				action();
		};
		parent.AddChild(button);
		_catalogButtons.Add(button);
	}

	private void CreateInternalCategoryMenu(Control parent)
	{
		MenuButton button = CreateCategoryMenu(parent, "INTERNAL");
		button.TooltipText = "Choose an internal component.";
		PopupMenu popup = button.GetPopup();
		popup.AddItem("2×1 Fuel Tank", 0);
		popup.AddItem("2×1 Generator", 1);
		popup.AddItem("2×2 Thruster", 2);
		popup.AddItem("2×2 Gimballed Thruster", 3);
		popup.AddItem("2×2 Generator", 4);
		popup.AddItem("4×4 Fuel Tank", 5);
		popup.AddSeparator();
		popup.IdPressed += id =>
		{
			switch ((int)id)
			{
				case 0:
					StartFuelTankPlacement();
					break;
				case 1:
					StartPowerCorePlacement();
					break;
				case 2:
					StartThrusterPlacement();
					break;
				case 3:
					StartGimballedThrusterPlacement();
					break;
				case 4:
					_ship.BeginNewLargeGeneratorPlacement(GetShipLocalMousePosition());
					StartCatalogPlacementStatus("2×2 Generator");
					break;
				case 5:
					_ship.BeginNewLargeFuelTankPlacement(GetShipLocalMousePosition());
					StartCatalogPlacementStatus("4×4 Fuel Tank");
					break;
			}
		};
	}

	private void CreateCommandCategoryMenu(Control parent)
	{
		_commandMenuButton = CreateCategoryMenu(parent, "COMMAND");
		_commandMenuButton.TooltipText = "Choose a command component.";
		PopupMenu popup = _commandMenuButton.GetPopup();
		popup.AddItem("Bridge", 0);
		popup.IdPressed += _ => StartRootPlacement();
	}

	private void CreateRoutingCategoryMenu(Control parent)
	{
		MenuButton button = CreateCategoryMenu(parent, "ROUTING");
		button.TooltipText = "Create manual power or fuel routes.";
		PopupMenu popup = button.GetPopup();
		popup.AddItem("Route Power", 0);
		popup.AddItem("Route Fuel", 1);
		popup.IdPressed += id =>
		{
			LogisticsSystem system = (int)id == 0
				? LogisticsSystem.Power
				: LogisticsSystem.Fuel;
			_ship.BeginLogisticsRouting(system);
			UpdateStatus($"Routing {system}: click a source, open end, or conduit segment to start/branch. Draw 90°/45° segments. Click a consumer/conduit to join, or Enter for an open end. J joins crossings; O marks pass-overs; T marks pass-throughs. Right-click undoes/removes; Esc exits.");
		};
	}

	private MenuButton CreateCategoryMenu(Control parent, string category)
	{
		MenuButton button = new() { Text = category };
		button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		button.CustomMinimumSize = new Vector2(0, 40);
		parent.AddChild(button);
		_catalogButtons.Add(button);
		return button;
	}

	private void StartHullPlacement()
	{
		_ship.BeginNewStructuralHullPlacement(GetShipLocalMousePosition());
		StartCatalogPlacementStatus("2×2 hull");
	}

	private void SaveShipDesign(string path)
	{
		if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			path += ".json";

		try
		{
			ShipDesignPersistence.SaveToFile(path, _ship.Design);
			UpdateStatus($"Ship saved to {path}.");
		}
		catch (IOException exception)
		{
			GD.PushError($"Could not save ship design '{path}': {exception}");
			UpdateStatus($"Could not save ship: {exception.Message}");
		}
	}

	private void ConfirmLoadShipDesign(string path)
	{
		_pendingLoadPath = path;
		_loadConfirmation.DialogText =
			$"Load '{Path.GetFileName(path)}' and replace the current ship? This cannot be undone.";
		_loadConfirmation.PopupCentered();
	}

	private void LoadPendingShipDesign()
	{
		string path = _pendingLoadPath;
		_pendingLoadPath = string.Empty;
		if (string.IsNullOrWhiteSpace(path))
			return;

		try
		{
			ShipDesign design = ShipDesignPersistence.LoadFromFile(path, _ship.ResolvePartDefinition);
			_ship.ReplaceDesign(design);
			UpdateStatus($"Loaded ship from {path}.");
			UpdateCatalogButtons();
		}
		catch (IOException exception)
		{
			GD.PushError($"Could not load ship design '{path}': {exception}");
			UpdateStatus($"Could not load ship: {exception.Message}");
		}
		catch (InvalidDataException exception)
		{
			GD.PushError($"Could not load ship design '{path}': {exception}");
			UpdateStatus($"Could not load ship: {exception.Message}");
		}
	}

	private void StartHalfHullPlacement()
	{
		_ship.BeginNewHalfStructuralHullPlacement(GetShipLocalMousePosition());
		StartCatalogPlacementStatus("1×1 hull");
	}

	private void StartLongHalfHullPlacement()
	{
		_ship.BeginNewHalfStructuralLongPlacement(GetShipLocalMousePosition());
		StartCatalogPlacementStatus("2×1 hull");
	}

	private void StartTrianglePlacement()
	{
		_ship.BeginNewStructuralTrianglePlacement(GetShipLocalMousePosition());
		StartCatalogPlacementStatus("2×2 triangle hull");
	}

	private void StartHalfTrianglePlacement()
	{
		_ship.BeginNewHalfStructuralTrianglePlacement(GetShipLocalMousePosition());
		StartCatalogPlacementStatus("1×1 triangle hull");
	}

	private void StartNotchedHullPlacement()
	{
		_ship.BeginNewStructuralNotchedPlacement(GetShipLocalMousePosition());
		StartCatalogPlacementStatus("2×2 notched hull");
	}

	private void StartLargeHullPlacement()
	{
		_ship.BeginNewStructuralLargePlacement(GetShipLocalMousePosition());
		StartCatalogPlacementStatus("4×4 hull");
	}

	private void StartWideHullPlacement()
	{
		_ship.BeginNewStructuralWidePlacement(GetShipLocalMousePosition());
		StartCatalogPlacementStatus("4×2 hull");
	}

	private void StartFuelTankPlacement()
	{
		_ship.BeginNewFuelTankPlacement(GetShipLocalMousePosition());
		UpdateStatus("2×1 fuel tank selected. Place it freely, or mount it fully inside hull pieces; press Q/E or scroll to rotate.");
		UpdateCatalogButtons();
	}

	private void StartPowerCorePlacement()
	{
		_ship.BeginNewPowerCorePlacement(GetShipLocalMousePosition());
		StartCatalogPlacementStatus("2×1 Generator");
	}

	private void StartThrusterPlacement()
	{
		_ship.BeginNewThrusterPlacement(GetShipLocalMousePosition());
		StartCatalogPlacementStatus("2×2 Thruster");
	}

	private void StartGimballedThrusterPlacement()
	{
		_ship.BeginNewGimballedThrusterPlacement(GetShipLocalMousePosition());
		StartCatalogPlacementStatus("2×2 Gimballed Thruster");
	}

	private void StartCatalogPlacementStatus(string partName)
	{
		UpdateStatus($"{partName} selected. Click to place freely or snap to a compatible port.");
		UpdateCatalogButtons();
	}

	private void StartRootPlacement()
	{
		if (_ship.BeginNewRootPartPlacement(GetShipLocalMousePosition()))
		{
			UpdateStatus("Bridge selected. Click to place freely or snap to a compatible port.");
		}
		else
		{
			UpdateStatus("A Bridge already exists in the scene, or another part is being held.");
		}

		UpdateCatalogButtons();
	}

	private Vector2 GetShipLocalMousePosition()
	{
		return _ship.ToLocal(GetGlobalMousePosition()) / ViewScale;
	}

	private void CenterShip()
	{
		if (_ship != null)
		{
			Vector2 viewportSize = GetViewportRect().Size;
			_ship.Position = new Vector2((viewportSize.X - 300) / 2, viewportSize.Y / 2);
		}
	}

	private void ZoomAtCursor(float zoomFactor)
	{
		float currentZoom = _ship.Scale.X;
		float newZoom = Mathf.Clamp(currentZoom * zoomFactor, MinimumZoom, MaximumZoom);
		if (Mathf.IsEqualApprox(currentZoom, newZoom))
			return;

		Vector2 cursorPosition = ToLocal(GetGlobalMousePosition());
		Vector2 shipLocalPosition = _ship.ToLocal(GetGlobalMousePosition());
		_ship.Scale = Vector2.One * newZoom;
		_ship.Position = cursorPosition - shipLocalPosition * newZoom;
	}

	private void UpdateCatalogButtons()
	{
		if (_ship == null || _commandMenuButton == null)
			return;

		bool catalogDisabled = !string.IsNullOrEmpty(_ship.InitializationError) || _ship.IsHoldingPart;
		foreach (BaseButton button in _catalogButtons)
			button.Disabled = catalogDisabled;
		_commandMenuButton.Disabled = catalogDisabled || !_ship.CanAddRootPart;
		bool persistenceDisabled = catalogDisabled || _ship.IsRouting;
		_saveButton.Disabled = persistenceDisabled;
		_loadButton.Disabled = persistenceDisabled;
	}

	private void UpdateMassReadout()
	{
		if (_ship == null || _massLabel == null)
			return;

		float massKg = _ship.TotalMassKg;
		_massLabel.Text = $"Total mass: {massKg.ToString("N0", CultureInfo.InvariantCulture)} kg "
			+ $"({(massKg / 1000).ToString("N2", CultureInfo.InvariantCulture)} t)";
	}

	private void UpdateSelectedPartReadout()
	{
		if (_selectedPartLabel == null || _ship == null)
			return;

		_selectedPartLabel.Text = _ship.IsHoldingPart
			? _ship.HeldPartDisplayName
			: "None";
	}

	private void UpdateStatus(string text)
	{
		if (_statusLabel != null)
		{
			_statusLabel.Text = text;
		}
	}
}
