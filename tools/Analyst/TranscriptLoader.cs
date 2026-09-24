using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnderWarden.Analyst;

/// <summary>
/// A scalar field value read from a transcript record. Predicates evaluate against these.
/// Objects and arrays (e.g. the `events` array) are intentionally NOT lifted into the field
/// map — predicate-mechanism checks operate on the flat scalar fields of one record.
/// </summary>
public readonly struct FieldValue
{
    public enum Kind { Number, Bool, Str }

    public Kind Type { get; }
    public double Num { get; }
    public bool Bool { get; }
    public string? Str { get; }

    private FieldValue(Kind type, double num, bool b, string? str)
    {
        Type = type; Num = num; Bool = b; Str = str;
    }

    public static FieldValue Number(double n) => new(Kind.Number, n, false, null);
    public static FieldValue Boolean(bool b) => new(Kind.Bool, 0, b, null);
    public static FieldValue String(string s) => new(Kind.Str, 0, false, s);

    public override string ToString() => Type switch
    {
        Kind.Number => Num == Math.Floor(Num) ? ((long)Num).ToString() : Num.ToString("0.####"),
        Kind.Bool   => Bool ? "true" : "false",
        _           => Str ?? "",
    };
}

/// <summary>One TurnRecord, flattened to its scalar fields plus turn/floor for reporting.</summary>
public sealed class LoadedTurn
{
    public int Turn { get; init; }
    public int Floor { get; init; }
    public required IReadOnlyDictionary<string, FieldValue> Fields { get; init; }
}

/// <summary>The RunSummary record: its scalar fields plus the raw node for echoing coverage.</summary>
public sealed class LoadedSummary
{
    public required IReadOnlyDictionary<string, FieldValue> Fields { get; init; }
    public JsonObject? SystemTriggers { get; init; }
    public required JsonNode Raw { get; init; }
}

/// <summary>A fully loaded enriched transcript, ready for evaluation.</summary>
public sealed class LoadedTranscript
{
    public required int SchemaVersion { get; init; }
    public required string RunId { get; init; }
    public required string Persona { get; init; }
    public required string PlayerType { get; init; }
    public string? LlmModel { get; init; }
    public bool ReplayAvailable { get; init; }
    public required IReadOnlyList<LoadedTurn> Turns { get; init; }
    public LoadedSummary? Summary { get; init; }
}

/// <summary>Thrown when a transcript cannot be loaded or fails a mandatory schema check.</summary>
public sealed class TranscriptLoadException(string message) : Exception(message);

/// <summary>
/// Reads enriched JSONL transcripts (see docs/llm-testing/shared-transcript-schema.md) into a
/// <see cref="LoadedTranscript"/>. Parses each line as generic JSON and lifts top-level scalar
/// fields into a name → value map, so the predicate evaluator reads fields by their schema name
/// and new scalar fields are picked up automatically (forward-compatible).
///
/// Schema-version mismatch on a mandatory field is fatal; unknown optional fields are ignored.
/// Old-format transcripts (no full action_taken) are not rejected — the header's
/// replay_available flag is carried through verbatim.
/// </summary>
public static class TranscriptLoader
{
    public const int SupportedSchemaVersion = 1;

    public static LoadedTranscript LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new TranscriptLoadException($"Transcript not found: {path}");
        return LoadFromText(File.ReadAllText(path), path);
    }

    public static LoadedTranscript LoadFromText(string jsonl, string source = "<text>")
    {
        var lines = jsonl.Split('\n').Where(l => l.Trim().Length > 0).ToArray();
        if (lines.Length == 0)
            throw new TranscriptLoadException($"{source}: empty transcript.");

        JsonObject? header = null;
        LoadedSummary? summary = null;
        var turns = new List<LoadedTurn>();

        // Running streak counter for consecutive_blocked_moves.
        // Maintained across turns so each turn knows how many prior blocked-move turns
        // preceded it without a break — no sequential-eval plumbing required.
        int blockedStreak = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            JsonObject obj;
            try
            {
                obj = JsonNode.Parse(lines[i]) as JsonObject
                      ?? throw new TranscriptLoadException($"{source} line {i + 1}: not a JSON object.");
            }
            catch (JsonException ex)
            {
                throw new TranscriptLoadException($"{source} line {i + 1}: malformed JSON — {ex.Message}");
            }

            var recordType = (obj["record_type"]?.GetValue<string>())
                ?? throw new TranscriptLoadException($"{source} line {i + 1}: missing record_type.");

            switch (recordType)
            {
                case "header":
                    header = obj;
                    break;
                case "turn":
                {
                    var fields = ScalarFields(obj);
                    // Derived fields: move_was_blocked + consecutive_blocked_moves.
                    //
                    // move_was_blocked: true when a Move action was issued, no Move event fired,
                    // AND no known productive reason accounts for the non-movement.
                    //
                    // Exclusion categories:
                    //   Hard blocking (no-choice):  EntangleMoveBlocked, TrapTriggered, SkipTurn
                    //   Productive interactions:    DoorOpened, ChestOpened, ChestUnlocked,
                    //                               PropDestroyed, MuralExamined, SignpostRead
                    //
                    // consecutive_blocked_moves: running streak of move_was_blocked turns, reset
                    // to 0 on any non-blocked turn. Predicate threshold >=5 excludes the healthy
                    // 4-turn stuck-detect-and-reroute cycle (max streak after the fix = 4).
                    var actionTaken = obj["action_taken"]?.AsObject();
                    string actionKind = actionTaken?["kind"]?.GetValue<string>() ?? "";
                    var evtArray = obj["events"]?.AsArray();
                    bool thisBlocked = false;
                    if (actionKind == "Move" && evtArray != null)
                    {
                        bool hasMoveEvent = false, hasBlockingReason = false, hasProductiveInteraction = false;
                        foreach (var ev in evtArray)
                        {
                            string et = ev?.AsObject()?["event_type"]?.GetValue<string>() ?? "";
                            if (et == "Move") hasMoveEvent = true;
                            if (et is "EntangleMoveBlocked" or "TrapTriggered" or "SkipTurn")
                                hasBlockingReason = true;
                            if (et is "DoorOpened" or "ChestOpened" or "ChestUnlocked"
                                    or "PropDestroyed" or "MuralExamined" or "SignpostRead")
                                hasProductiveInteraction = true;
                        }
                        thisBlocked = !hasMoveEvent && !hasBlockingReason && !hasProductiveInteraction;
                    }
                    fields["move_was_blocked"] = FieldValue.Boolean(thisBlocked);

                    // Running streak: reset to 0 on any non-blocked turn.
                    blockedStreak = thisBlocked ? blockedStreak + 1 : 0;
                    fields["consecutive_blocked_moves"] = FieldValue.Number(blockedStreak);

                    turns.Add(new LoadedTurn
                    {
                        Turn   = (int)(ReadScalar(obj, "turn")?.Num ?? 0),
                        Floor  = (int)(ReadScalar(obj, "floor")?.Num ?? 0),
                        Fields = fields,
                    });
                    break;
                }
                case "summary":
                    summary = new LoadedSummary
                    {
                        Fields = ScalarFields(obj),
                        SystemTriggers = obj["system_triggers"] as JsonObject,
                        Raw = obj,
                    };
                    break;
                // Unknown record types are ignored (forward-compatible).
            }
        }

        if (header == null)
            throw new TranscriptLoadException($"{source}: no header record (line 1 must be record_type=header).");

        int schemaVersion = (int)(ReadScalar(header, "schema_version")?.Num
            ?? throw new TranscriptLoadException($"{source}: header missing schema_version."));
        if (schemaVersion != SupportedSchemaVersion)
            throw new TranscriptLoadException(
                $"{source}: schema_version {schemaVersion} is unsupported (this Analyst supports v{SupportedSchemaVersion}).");

        return new LoadedTranscript
        {
            SchemaVersion   = schemaVersion,
            RunId           = header["run_id"]?.GetValue<string>() ?? "",
            Persona         = header["persona"]?.GetValue<string>() ?? "",
            PlayerType      = header["player_type"]?.GetValue<string>() ?? "",
            LlmModel        = header["llm_model"]?.GetValue<string>(),
            ReplayAvailable = ReadScalar(header, "replay_available")?.Bool ?? false,
            Turns           = turns,
            Summary         = summary,
        };
    }

    /// <summary>Lift every top-level primitive (number/bool/string) into a field map.</summary>
    private static Dictionary<string, FieldValue> ScalarFields(JsonObject obj)
    {
        var map = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        foreach (var (key, node) in obj)
        {
            var v = AsScalar(node);
            if (v.HasValue) map[key] = v.Value;
        }
        return map;
    }

    private static FieldValue? ReadScalar(JsonObject obj, string key)
        => obj.TryGetPropertyValue(key, out var node) ? AsScalar(node) : null;

    private static FieldValue? AsScalar(JsonNode? node)
    {
        if (node is not JsonValue val) return null;
        if (val.TryGetValue<bool>(out var b)) return FieldValue.Boolean(b);
        if (val.TryGetValue<double>(out var d)) return FieldValue.Number(d);
        if (val.TryGetValue<string>(out var s)) return FieldValue.String(s);
        return null;
    }
}
