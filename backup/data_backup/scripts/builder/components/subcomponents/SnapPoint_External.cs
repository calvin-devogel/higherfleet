using Godot;

public partial class SnapPoint_External : Area2D
{
	// marker for external snap points (ones that can connect to other components)
	public bool IsOccupied;
	public bool isInternal;
	public bool isExternal;

	public override void _Ready()
	{
		isInternal = false;
		isExternal = true;
		IsOccupied = false;
	}
	public void SetIsOccupied()
	{
		IsOccupied = true;
	}
	
	public void SetIsUnoccupied()
	{
		IsOccupied = false;
	}

	public void _OnAreaEntered(Area2D area)
	{
		if (area is SnapPoint_External snapPoint && snapPoint.GetParent<Component>() != this.GetParent<Component>())
		{
			SetIsOccupied();
		}
	}

	public void _OnAreaExited(Area2D area)
	{
		if (area is SnapPoint_External snapPoint && snapPoint.GetParent<Component>() != this.GetParent<Component>())
		{
			SetIsUnoccupied();
		}
	}
}
