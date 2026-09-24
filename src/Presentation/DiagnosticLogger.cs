using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Godot;

namespace UnderWarden.Presentation;

/// <summary>
/// Writes timestamped breadcrumbs to both GD.Print (Godot Output panel) and a
/// log file on disk. The file survives even if Godot crashes or the main thread
/// locks. Tail it from a terminal:
///
///   tail -f ~/Library/Application\ Support/Godot/app_userdata/Catacombs\ of\ YARL/diag.log
///
/// All writes are AutoFlush — every line hits disk immediately.
/// Only active when OS.IsDebugBuild() is true.
///
/// Structured output:
///   diag_structured.jsonl — one JSON object per Event() call, machine-readable.
///   Format: {"seq":N,"frame":N,"time":N.NNN,"tag":"...","heap_mb":N,"data":{...}}
/// </summary>
public static class Diag
{
    private static StreamWriter? _writer;
    private static StreamWriter? _structuredWriter;
    private static int _counter;
    private static bool _enabled;

    public static void Init()
    {
        if (!OS.IsDebugBuild()) return;
        _enabled = true;

        var dir = OS.GetUserDataDir();

        var path = Path.Combine(dir, "diag.log");
        _writer = new StreamWriter(path, append: false) { AutoFlush = true };

        var structuredPath = Path.Combine(dir, "diag_structured.jsonl");
        _structuredWriter = new StreamWriter(structuredPath, append: false) { AutoFlush = true };

        GD.Print($"[DIAG] Logging to: {path}");
        GD.Print($"[DIAG] Structured log: {structuredPath}");
        Log("=== Session started ===");
    }

    public static void Log(string msg)
    {
        if (!_enabled) return;

        // Frame counter: Engine.GetProcessFrames() is safe after _Ready but may throw
        // if called during early static init or before the engine loop starts.
        long frame;
        try { frame = (long)Engine.GetProcessFrames(); }
        catch { frame = -1; }

        double tSec = Time.GetTicksMsec() / 1000.0;
        var line = $"[DIAG {_counter++:D5} F{frame} T{tSec:F3}] {msg}";
        GD.Print(line);
        _writer?.WriteLine(line);
    }

    public static void Mem(string label)
    {
        if (!_enabled) return;
        long mb = GC.GetTotalMemory(false) / (1024 * 1024);
        Log($"{label} | heap={mb}MB");
    }

    /// <summary>
    /// Writes a structured event to both the human-readable log and diag_structured.jsonl.
    ///
    /// Human log: same format as Log() — "[DIAG NNNNN F... T...] tag: data"
    /// JSON line: {"seq":N,"frame":N,"time":N.NNN,"tag":"...","heap_mb":N,"data":{...}}
    ///
    /// The data parameter accepts anonymous objects, primitives, or any type with
    /// public readable properties. No System.Text.Json dependency — uses reflection.
    /// </summary>
    public static void Event(string tag, object? data = null)
    {
        if (!_enabled) return;

        // Human-readable output via the existing Log path (also bumps _counter).
        string humanMsg = data != null ? $"{tag}: {data}" : tag;
        Log(humanMsg);

        // Structured JSON output — seq is _counter-1 because Log() already incremented it.
        long frame = -1;
        try { frame = (long)Engine.GetProcessFrames(); } catch { }
        double time = Time.GetTicksMsec() / 1000.0;
        long heapMb = GC.GetTotalMemory(false) / (1024 * 1024);

        string dataJson = data != null ? SerializeSimple(data) : "null";
        string json = $"{{\"seq\":{_counter - 1},\"frame\":{frame},\"time\":{time:F3},\"tag\":\"{EscapeJson(tag)}\",\"heap_mb\":{heapMb},\"data\":{dataJson}}}";
        _structuredWriter?.WriteLine(json);
    }

    /// <summary>
    /// Flush and close both log files. Call on application exit if needed.
    /// Safe to call even if Init() was never called.
    /// </summary>
    public static void Shutdown()
    {
        _writer?.Flush();
        _writer?.Close();
        _writer = null;

        _structuredWriter?.Flush();
        _structuredWriter?.Close();
        _structuredWriter = null;

        _enabled = false;
    }

    // --- Serialization helpers ---

    /// <summary>
    /// Converts common types and anonymous objects to a JSON value string.
    /// Covers: string, int, long, float, double, bool, null, and any object
    /// whose public properties can be recursively serialized.
    /// </summary>
    private static string SerializeSimple(object obj)
    {
        if (obj is string s) return $"\"{EscapeJson(s)}\"";
        if (obj is int i) return i.ToString();
        if (obj is long l) return l.ToString();
        if (obj is float f) return f.ToString("G");
        if (obj is double d) return d.ToString("G");
        if (obj is bool b) return b ? "true" : "false";

        // For anonymous objects and other complex types, reflect on public properties.
        // Anonymous type properties are always public and readable, so this is safe.
        PropertyInfo[] props = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        if (props.Length == 0) return $"\"{EscapeJson(obj.ToString() ?? "")}\"";

        var pairs = new List<string>(props.Length);
        foreach (var prop in props)
        {
            object? val = prop.GetValue(obj);
            string valStr = val == null ? "null" : SerializeSimple(val);
            pairs.Add($"\"{EscapeJson(prop.Name)}\":{valStr}");
        }
        return "{" + string.Join(",", pairs) + "}";
    }

    private static string EscapeJson(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}
