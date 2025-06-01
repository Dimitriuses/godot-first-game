using Godot;
using System;

public partial class Player : Node2D
{
	public int CellIndex = 0;

	public void MoveToCell(Vector2 position)
	{
		Position = position;
	}
	
}
