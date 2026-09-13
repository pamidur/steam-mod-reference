using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TaskLoggingHelper = Microsoft.Build.Utilities.TaskLoggingHelper;

namespace Pamidur.SteamModReference.Tasks;

/// <summary>
/// Wraps steamcmd invocation. 
/// Batches every distinct workshop id for a given AppId into a single steamcmd process run via a generated script file (`+runscript`)
/// since spawning one steamcmd process per mod is very slow (each login/handshake has real latency).
/// </summary>
public sealed class SteamCmdRunner(string steamCmdExePath, string installDir, TaskLoggingHelper log)
{
    /// <summary>
    /// Downloads every (appId, workshopId) pair in one batched steamcmd run.
    /// </summary>
    public async Task<bool> DownloadWorkshopItemsAsync(IEnumerable<(string AppId, string WorkshopId)> items)
    {
        // todo:: see if we can use inline script instead of a file
        var scriptPath = Path.Combine(installDir, "batch.txt");
        var sb = new StringBuilder();
        sb.AppendLine("login anonymous");
        sb.AppendLine($"force_install_dir \"{installDir}\"");

        var any = false;
        foreach (var (appId, workshopId) in items)
        {
            sb.AppendLine($"workshop_download_item {appId} {workshopId}");
            any = true;
        }
        sb.AppendLine("quit");

        if (!any)
            return true; // nothing to do

        Directory.CreateDirectory(installDir);
        File.WriteAllText(scriptPath, sb.ToString());

        log.LogMessage(Microsoft.Build.Framework.MessageImportance.High,
            $"Running steamcmd batch download ({scriptPath})");

        var psi = new ProcessStartInfo
        {
            FileName = steamCmdExePath,
            Arguments = $"+runscript \"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            log.LogError("Failed to start steamcmd process.");
            return false;
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        foreach (var line in stdout.Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
                log.LogMessage(Microsoft.Build.Framework.MessageImportance.Low, line.TrimEnd());
        }

        if (process.ExitCode != 0)
        {
            log.LogWarning($"steamcmd exited with code {process.ExitCode}. stderr: {stderr}");
            return false;
        }

        return true;
    }

    public string GetWorkshopContentDir(string appId, string workshopId) =>
        Path.Combine(installDir, "steamapps", "workshop", "content", appId, workshopId);
}
