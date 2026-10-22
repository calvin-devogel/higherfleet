using System.Linq;
using Builder.Components.External;
using Godot;

public partial class Component : Area2D
{
	// possibly not needed, we'll see
	public GraphNode GraphNode = null;
	
	public bool isBeingDragged = false;
	protected bool _IsMouseOver = false;
	protected bool _IsSnapped = false;
	public SnapPoint_Directional ExternalSnapPoints = new SnapPoint_Directional();

	protected void _OnMouseEntered()
	{
		_IsMouseOver = true;
	}

	protected void _OnMouseExited()
	{
		_IsMouseOver = false;
	}

	protected void _FollowMouse()
	{
		if (isBeingDragged)
		{
			GlobalPosition = GetGlobalMousePosition();
		}
	}

	protected void _CollectExternalSnapPoints()
    {
        foreach (var child in GetChildren().OfType<SnapPoint_External>())
		{
			switch (child.Name)
			{
				case "SnapPoint_External_North": ExternalSnapPoints.North = child; break;
				case "SnapPoint_External_South": ExternalSnapPoints.South = child; break;
				case "SnapPoint_External_East": ExternalSnapPoints.East = child; break;
				case "SnapPoint_External_West": ExternalSnapPoints.West = child; break;
				case "SnapPoint_External_NorthWest": ExternalSnapPoints.NorthWest = child; break;
				case "SnapPoint_External_NorthEast": ExternalSnapPoints.NorthEast = child; break;
				case "SnapPoint_External_EastNorth": ExternalSnapPoints.EastNorth = child; break;
				case "SnapPoint_External_EastSouth": ExternalSnapPoints.EastSouth = child; break;
				case "SnapPoint_External_SouthEast": ExternalSnapPoints.SouthEast = child; break;
				case "SnapPoint_External_SouthWest": ExternalSnapPoints.SouthWest = child; break;
				case "SnapPoint_External_WestNorth": ExternalSnapPoints.WestNorth = child; break;
				case "SnapPoint_External_WestSouth": ExternalSnapPoints.WestSouth = child; break;
			}
		}
    }
}
