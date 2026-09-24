using System.Text;
using System.Text.Json;

namespace UnderWarden.Logic.Persistence.MidRun;

/// <summary>Outcome of loading a mid-run save file. A truncated/garbage/version-mismatched file
/// fails to a defined status — never a silent new-game, never an unhandled crash. The caller (4b)
/// decides what to do (discard the save, warn, restart the floor, …).</summary>
public enum MidRunLoadStatus
{
    Ok,
    FileNotFound,
    Corrupt,          // unreadable / truncated / not valid JSON for this schema
    SchemaMismatch,   // parsed, but a version this build does not support
}

public sealed record MidRunLoadResult(MidRunLoadStatus Status, MidRunSaveDto? Save, string? Error)
{
    public bool IsOk => Status == MidRunLoadStatus.Ok && Save != null;
}

/// <summary>
/// Atomic file I/O for the mid-run save (M1.4 §File + write discipline): write to `.tmp` then
/// File.Move over the target, matching the PersistentRunState pattern, so a crash mid-write cannot
/// corrupt an existing save. WHEN saves happen (background/kill) is presentation-side (4b).
/// </summary>
public static class MidRunFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void SaveMidRunToFile(MidRunSaveDto dto, string path)
    {
        string json = JsonSerializer.Serialize(dto, MidRunSaveJsonContext.Default.MidRunSaveDto);
        string tmp = path + ".tmp";
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(tmp, json, Utf8NoBom);
        File.Move(tmp, path, overwrite: true);
    }

    public static MidRunLoadResult LoadMidRunFromFile(string path)
    {
        if (!File.Exists(path))
            return new MidRunLoadResult(MidRunLoadStatus.FileNotFound, null, $"No save at '{path}'.");

        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex) { return new MidRunLoadResult(MidRunLoadStatus.Corrupt, null, $"read failed: {ex.Message}"); }

        MidRunSaveDto? dto;
        try { dto = JsonSerializer.Deserialize(json, MidRunSaveJsonContext.Default.MidRunSaveDto); }
        catch (JsonException ex) { return new MidRunLoadResult(MidRunLoadStatus.Corrupt, null, $"parse failed: {ex.Message}"); }

        if (dto is null)
            return new MidRunLoadResult(MidRunLoadStatus.Corrupt, null, "deserialized to null.");
        if (dto.SchemaVersion != MidRunSchema.Version)
            return new MidRunLoadResult(MidRunLoadStatus.SchemaMismatch, null,
                $"schema {dto.SchemaVersion}, this build supports {MidRunSchema.Version}.");

        return new MidRunLoadResult(MidRunLoadStatus.Ok, dto, null);
    }

    /// <summary>Delete the mid-run save (and any stray .tmp). Used by the RECORD-then-DELETE run-end
    /// flow — call ONLY after the run's outcome is committed to the cross-run save.</summary>
    public static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        var tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
    }

    /// <summary>Move a corrupt save aside (never delete) so it can be inspected. Returns the archived
    /// path, or null if there was nothing to archive.</summary>
    public static string? ArchiveCorrupt(string path)
    {
        if (!File.Exists(path)) return null;
        var archived = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
        File.Move(path, archived);
        return archived;
    }
}
