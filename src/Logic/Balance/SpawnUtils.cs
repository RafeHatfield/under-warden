using UnderWarden.Logic.Content;

namespace UnderWarden.Logic.Balance;

public static class SpawnUtils
{
    /// <summary>
    /// Port of Python prototype's from_dungeon_level() in random_utils.py.
    /// table: rows of {Weight, MinDepth} sorted ascending by MinDepth.
    /// Returns the Weight of the last row whose MinDepth &lt;= depth.
    /// Returns 0 if no row qualifies — monster is excluded from the pool at this depth.
    ///
    /// IMPORTANT: table must be sorted ascending by MinDepth. Unsorted tables
    /// produce silent wrong results. ContentLoader validates order at load time.
    /// </summary>
    public static int FromDungeonLevel(IReadOnlyList<DepthWeightEntry> table, int depth)
    {
        int result = 0;
        foreach (var entry in table)
        {
            if (depth >= entry.MinDepth)
                result = entry.Weight;
        }
        return result;
    }
}
