using System.Text.Json;
using UnderWarden.Logic.Persistence.Namespaces;

namespace UnderWarden.Logic.Persistence;

/// <summary>
/// Runtime mirror of the persistence file. Loaded once at app start; never discarded.
/// All reads and writes go through this object. See spec §1 Single-source rule.
///
/// Consumers access namespaces via typed convenience properties (e.g. RunCounter, PastSashas).
/// When a consumer mutates a namespace, it calls MarkDirty() to signal that a flush is needed.
/// Flush() writes the file atomically per spec §4 and §5.
/// </summary>
public sealed class PersistentRunState
{
    private readonly PersistenceFile _file;
    private bool _isDirty;

    private PersistentRunState(PersistenceFile file) => _file = file;

    /// <summary>
    /// Create a fresh in-memory state with all namespaces at their defaults — no disk I/O.
    /// Used by headless tooling (e.g. the LLM-testing transcript harness) that needs to run
    /// post-run systems like MemoDeliveryEvaluator against a clean slate.
    /// </summary>
    public static PersistentRunState CreateEmpty() => new(new PersistenceFile());

    // ── Dirty tracking ───────────────────────────────────────────────────────

    public bool IsDirty => _isDirty;

    public void MarkDirty() => _isDirty = true;

    // ── Convenience accessors (consumers never touch _file directly) ─────────

    public RunCounterData RunCounter      => _file.Namespaces.RunCounter.Data;
    public PastSashasData PastSashas      => _file.Namespaces.PastSashas.Data;
    public FactionsData Factions          => _file.Namespaces.Factions.Data;
    public BorrekData Borrek              => _file.Namespaces.Borrek.Data;
    public VeshData Vesh                  => _file.Namespaces.Vesh.Data;
    public HaelData Hael                  => _file.Namespaces.Hael.Data;
    public MaryaFragmentsData MaryaFragments     => _file.Namespaces.MaryaFragments.Data;
    public HaelHintsData HaelHints        => _file.Namespaces.HaelHints.Data;
    public FreedPastSelvesData FreedPastSelves   => _file.Namespaces.FreedPastSelves.Data;
    public UnshrivenGeasData UnshrivenGeas       => _file.Namespaces.UnshrivenGeas.Data;
    public HollowmarkMetaData HollowmarkMeta     => _file.Namespaces.HollowmarkMeta.Data;
    public AchievementsData Achievements  => _file.Namespaces.Achievements.Data;
    public EncountersData Encounters      => _file.Namespaces.Encounters.Data;
    public HollowmarkSpanData HollowmarkSpan     => _file.Namespaces.HollowmarkSpan.Data;
    public UnderWardenData UnderWarden    => _file.Namespaces.UnderWarden.Data;

    // ── Load ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Load from disk. If the file is missing, returns a fresh default state without writing.
    /// If the file is corrupted or any namespace fails migration, falls back to defaults for
    /// that namespace (spec §3 OQ-1 resolution B) and logs the error.
    ///
    /// latestVersionOverride: inject a custom version table for testing migration paths.
    /// </summary>
    public static PersistentRunState LoadFromDisk(
        IPersistencePathProvider provider,
        Action<string>? errorLogger = null,
        IReadOnlyDictionary<string, int>? latestVersionOverride = null)
    {
        var path = provider.GetMainSaveFilePath();
        if (!File.Exists(path))
            return new PersistentRunState(new PersistenceFile());

        try
        {
            var raw = File.ReadAllText(path);
            // Pre-deserialization migration pass: applies forward migrations and falls back
            // future-namespaces to defaults before the typed DTO sees the JSON.
            var json = Migrations.ApplyMigrations(raw, latestVersionOverride, errorLogger);
            var file = JsonSerializer.Deserialize(json, PersistenceJsonContext.Default.PersistenceFile)
                       ?? new PersistenceFile();
            return new PersistentRunState(file);
        }
        catch (Exception ex)
        {
            errorLogger?.Invoke($"[Persistence] Failed to load save file: {ex.Message}. Starting with defaults.");
            return new PersistentRunState(new PersistenceFile());
        }
    }

    /// <summary>
    /// Load daily seeds from disk. Returns an empty file if missing.
    /// </summary>
    public static DailySeedsFile LoadDailySeedsFromDisk(IPersistencePathProvider provider,
        Action<string>? errorLogger = null)
    {
        var path = provider.GetDailySeedsFilePath();
        if (!File.Exists(path))
            return new DailySeedsFile();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, PersistenceJsonContext.Default.DailySeedsFile)
                   ?? new DailySeedsFile();
        }
        catch (Exception ex)
        {
            errorLogger?.Invoke($"[Persistence] Failed to load daily seeds: {ex.Message}. Starting with empty.");
            return new DailySeedsFile();
        }
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Flush dirty state to disk using atomic write (write-to-.tmp, then File.Move).
    /// Also rotates backup snapshots. Clears dirty flag on success.
    /// Safe to call when not dirty — just skips if called unnecessarily (rare overhead).
    /// </summary>
    public void Flush(IPersistencePathProvider provider, Action<string>? errorLogger = null)
    {
        _file.SavedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(_file, PersistenceJsonContext.Default.PersistenceFile);

        var mainPath = provider.GetMainSaveFilePath();
        var tmpPath = mainPath + ".tmp";

        try
        {
            // Backup existing file before overwriting.
            if (File.Exists(mainPath))
                RotateBackup(mainPath, provider.GetBackupDirectory());

            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, mainPath, overwrite: true);
            _isDirty = false;
        }
        catch (Exception ex)
        {
            errorLogger?.Invoke($"[Persistence] Failed to flush save: {ex.Message}");
            try { File.Delete(tmpPath); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Flush daily seeds to disk using atomic write.
    /// </summary>
    public static void FlushDailySeeds(DailySeedsFile seeds, IPersistencePathProvider provider,
        Action<string>? errorLogger = null)
    {
        seeds.SavedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(seeds, PersistenceJsonContext.Default.DailySeedsFile);

        var mainPath = provider.GetDailySeedsFilePath();
        var tmpPath = mainPath + ".tmp";

        try
        {
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, mainPath, overwrite: true);
        }
        catch (Exception ex)
        {
            errorLogger?.Invoke($"[Persistence] Failed to flush daily seeds: {ex.Message}");
            try { File.Delete(tmpPath); } catch { /* best-effort cleanup */ }
        }
    }

    // ── Backup rotation ──────────────────────────────────────────────────────

    private const int MaxBackups = 5;

    private static void RotateBackup(string mainPath, string backupDir)
    {
        try
        {
            Directory.CreateDirectory(backupDir);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var backupPath = Path.Combine(backupDir, $"yarl_persistence.{timestamp}.json");
            File.Copy(mainPath, backupPath, overwrite: false);

            // Prune oldest backups beyond the limit.
            var backups = Directory.GetFiles(backupDir, "yarl_persistence.*.json")
                .OrderBy(File.GetLastWriteTimeUtc)
                .ToArray();

            foreach (var old in backups.Take(Math.Max(0, backups.Length - MaxBackups)))
                File.Delete(old);
        }
        catch
        {
            // Backup failure is non-fatal. Log silently; main file write continues.
        }
    }
}
