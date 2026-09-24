using System.IO;

namespace UnderWarden.Logic.Persistence;

public interface IPersistencePathProvider
{
    string GetMainSaveFilePath();
    string GetDailySeedsFilePath();
    string GetSettingsFilePath();
    string GetBackupDirectory();

    /// <summary>Path of the single mid-run save (M1.4). Defaults to the main-save directory so existing
    /// implementers get it for free; the device provider may override. Distinct file from the cross-run
    /// save — the mid-run save holds nothing from it.</summary>
    string GetMidRunSaveFilePath() =>
        Path.Combine(Path.GetDirectoryName(GetMainSaveFilePath()) ?? ".", "yarl_midrun.json");
}
