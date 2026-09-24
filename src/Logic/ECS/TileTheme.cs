namespace UnderWarden.Logic.ECS;

/// <summary>
/// Visual theme assigned to a map tile. Set by DungeonFloorBuilder when carving rooms.
/// Drives tile sprite selection in DungeonRenderer — same theme = coherent visual area.
/// </summary>
public enum TileTheme
{
    Grey,   // Default stone dungeon — grey walls, tile floors
    Crypt,  // Darker ornate stone — deeper floors
    Moss,   // Overgrown — very deep floors
    Dirt,   // Earthy — corridors and natural areas
}
