namespace UnderWarden.Logic.Core;

/// <summary>
/// Game difficulty level, primarily affecting the identification system's pre-identification rates.
///
/// | Category | Easy | Medium (default) | Hard |
/// | Potions  | 80%  |  50%             |  5%  |
/// | Scrolls  | 80%  |  40%             |  5%  |
/// | Wands    | 75%  |  30%             |  0%  |
/// | Rings    | 90%  |  40%             |  0%  |
/// | Weapons/Armor | 100% | 100%        | 100% |
/// </summary>
public enum Difficulty
{
    Easy,
    Medium,
    Hard,
}
