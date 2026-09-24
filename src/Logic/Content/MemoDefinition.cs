namespace UnderWarden.Logic.Content;

/// <summary>
/// Whether the memo is addressed directly to Sasha ("direct") or is an internal note
/// that Sasha is cc'd on ("internal_cc"). Determines how MemoRenderer frames the letter.
/// </summary>
public enum MemoRegister { Direct, InternalCc }

/// <summary>
/// A single Under-Warden memo — canonical public representation after YAML deserialization.
///
/// body[0] is the canonical first-fire text (persisted cross-run).
/// body[1+] are repeat variants; if no variant exists for the requested fireIndex,
/// body[0] is returned as the fallback.
///
/// Slot syntax in subject and body: {slot_name} — resolved by MemoFormatter at render time.
/// Bold emphasis: **text** — parsed by MemoRenderer, not here.
/// </summary>
public sealed class MemoDefinition
{
    public MemoRegister Register { get; init; }

    /// <summary>Destination department name — populated only for internal_cc memos.</summary>
    public string? To { get; init; }

    public string Subject { get; init; } = "";

    /// <summary>
    /// Body paragraphs. Index 0 is the canonical first-fire version.
    /// Indices 1+ are repeat variants (e.g. second or third incident).
    /// </summary>
    public List<string> Body { get; init; } = new();
}
