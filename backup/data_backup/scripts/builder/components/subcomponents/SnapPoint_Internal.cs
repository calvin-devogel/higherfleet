using Godot;

public partial class SnapPoint_Internal : Marker2D
{
	// marker for internal snap points (ones that internal components can snap to)
	public bool IsOccupied;
	public bool isInternal;
	public bool isExternal;

	public override void _Ready()
	{
		isInternal = true;
		isExternal = false;
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
}
