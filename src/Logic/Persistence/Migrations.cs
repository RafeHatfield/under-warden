using System.Text.Json.Nodes;

namespace UnderWarden.Logic.Persistence;

/// <summary>
/// Forward migration table and migration runner.
///
/// When to add a migration: only when a namespace version MUST be bumped (field removal,
/// rename, type change). Default-value escape hatch handles most field additions without
/// any migration — see spec §3 for the rule table.
///
/// Migration signature: Func&lt;JsonNode?, JsonNode&gt;
///   Input:  the old "data" node (null if the data key was missing).
///   Output: the new "data" node at the next schema version.
///
/// Example (hypothetical v1→v2 rename of "total_runs" → "run_count"):
///   ["run_counter"][1] = (data) => {
///       var obj = data as JsonObject ?? new JsonObject();
///       obj["run_count"] = obj["total_runs"]?.GetValue&lt;int&gt;() ?? 0;
///       obj.Remove("total_runs");
///       return obj;
///   };
/// </summary>
public static class Migrations
{
    // [namespaceKey][fromVersion] → migrate data from fromVersion to fromVersion+1
    public static readonly Dictionary<string, Dictionary<int, Func<JsonNode?, JsonNode>>> Forward = new()
    {
        ["run_counter"]       = new(),
        ["past_sashas"]       = new(),
        ["factions"]          = new(),
        ["borrek"]            = new(),
        ["vesh"]              = new(),
        ["hael"]              = new(),
        ["marya_fragments"]   = new(),
        ["hael_hints"]        = new(),
        ["freed_past_selves"] = new(),
        ["unshriven_geas"]    = new(),
        ["hollowmark_meta"]   = new(),
        ["achievements"]      = new(),
        ["encounters"]        = new(),
        ["hollowmark_span"]   = new(),
        ["under_warden"]      = new(),
    };

    // Latest schema version the current binary knows about for each namespace.
    public static readonly IReadOnlyDictionary<string, int> LatestVersion =
        new Dictionary<string, int>
        {
            ["run_counter"]       = 1,
            ["past_sashas"]       = 1,
            ["factions"]          = 1,
            ["borrek"]            = 1,
            ["vesh"]              = 1,
            ["hael"]              = 1,
            ["marya_fragments"]   = 1,
            ["hael_hints"]        = 1,
            ["freed_past_selves"] = 1,
            ["unshriven_geas"]    = 1,
            ["hollowmark_meta"]   = 1,
            ["achievements"]      = 1,
            ["encounters"]        = 1,
            ["hollowmark_span"]   = 1,
            ["under_warden"]      = 1,
        };

    /// <summary>
    /// Apply forward migrations and future-namespace fallbacks to a raw persistence JSON string.
    /// Returns the original string unchanged if no namespaces need migration (fast path).
    ///
    /// Called pre-deserialization in LoadFromDisk so that the typed DTO always receives
    /// data at the current binary version. See spec §3.
    ///
    /// latestVersionOverride: inject a custom version table in tests; null uses LatestVersion.
    /// forwardOverride: inject a custom migration function table in tests; null uses Forward.
    /// </summary>
    public static string ApplyMigrations(
        string json,
        IReadOnlyDictionary<string, int>? latestVersionOverride = null,
        Action<string>? logger = null,
        IReadOnlyDictionary<string, Dictionary<int, Func<JsonNode?, JsonNode>>>? forwardOverride = null)
    {
        var versions = latestVersionOverride ?? LatestVersion;

        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch { return json; } // malformed JSON — let the caller's deserializer surface the error

        if (root is not JsonObject rootObj) return json;
        if (rootObj["namespaces"] is not JsonObject nsObj) return json;

        bool modified = false;

        foreach (var (nsKey, latestVersion) in versions)
        {
            if (nsObj[nsKey] is not JsonObject nsEnvelope)
                continue; // absent namespace — System.Text.Json will default it on deserialize

            int fileVersion = nsEnvelope["schema_version"]?.GetValue<int>() ?? 1;
            if (fileVersion == latestVersion)
                continue; // fast path: already current

            modified = true;

            if (fileVersion > latestVersion)
            {
                // Newer save, older binary (spec §3 OQ-1 resolution B): refuse this namespace,
                // fall back to defaults. Other namespaces are unaffected.
                logger?.Invoke(
                    $"[Persistence] Namespace '{nsKey}' is v{fileVersion} (binary: v{latestVersion}). " +
                    $"Falling back to defaults for this namespace.");

                nsObj[nsKey] = new JsonObject
                {
                    ["schema_version"] = latestVersion,
                    ["data"] = new JsonObject(),
                };
            }
            else
            {
                // fileVersion < latestVersion: apply forward migration chain.
                var dataNode = nsEnvelope["data"];
                var migratedData = ApplyChain(nsKey, dataNode, fileVersion, latestVersion, logger, forwardOverride);
                nsEnvelope["schema_version"] = latestVersion;
                nsEnvelope["data"] = migratedData;
            }
        }

        return modified ? rootObj.ToJsonString() : json;
    }

    private static JsonNode ApplyChain(
        string nsKey, JsonNode? data, int fromVersion, int toVersion, Action<string>? logger,
        IReadOnlyDictionary<string, Dictionary<int, Func<JsonNode?, JsonNode>>>? forwardOverride = null)
    {
        var table = forwardOverride ?? Forward;
        if (!table.TryGetValue(nsKey, out var migrations))
            return data ?? new JsonObject();

        var current = data;
        for (int v = fromVersion; v < toVersion; v++)
        {
            if (!migrations.TryGetValue(v, out var migrate))
            {
                logger?.Invoke(
                    $"[Persistence] No migration registered for '{nsKey}' v{v}→v{v + 1}. " +
                    $"Proceeding with data at v{v} (some fields may default).");
                break;
            }
            // Clone before passing so the function can safely mutate.
            var input = current != null ? JsonNode.Parse(current.ToJsonString()) : null;
            current = migrate(input);
        }

        return current ?? new JsonObject();
    }
}
