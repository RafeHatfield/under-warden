namespace UnderWarden.Logic.ECS;

/// <summary>
/// One arm of an L-shaped corridor connecting two rooms.
/// Width 1 = standard single-tile passage. Width 2 = wide corridor (also carves one extra row/column).
/// </summary>
public sealed record CorridorSegment(int X1, int Y1, int X2, int Y2, int Width = 1);
