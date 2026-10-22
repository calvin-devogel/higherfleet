using Godot;
using Builder.Components.External;

public partial class Component_Bridge : Component
{
	[Export]
	public NodePath ComponentPath;

	public override void _Ready()
	{
		ComponentPath = GetPath();
		isBeingDragged = false;
		_IsMouseOver = false;
		_CollectExternalSnapPoints();
	}

	public override void _Input(InputEvent @event)
	{
		if (!_IsMouseOver) return;

		if (@event is InputEventMouseButton mouseEvent)
		{
			if (mouseEvent.ButtonIndex == MouseButton.Right && mouseEvent.Pressed)
			{
				isBeingDragged = !isBeingDragged;
				if (isBeingDragged)
                {
                    _FollowMouse();
                }
			}
		}

		if (@event is InputEventMouseMotion && isBeingDragged)
		{
			_FollowMouse();
		}
	}
}