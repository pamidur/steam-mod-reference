using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Formats.Tar;
using Microsoft.Build.Framework;
using TaskLoggingHelper = Microsoft.Build.Utilities.TaskLoggingHelper;

namespace Pamidur.SteamModReference.Tasks;

/// <summary>
/// Ensures a working steamcmd exists under {intermediateDir}/steamcmd
/// downloading and extracting it on first use.
/// </summary>
public sealed class SteamCmdBootstrapper(TaskLoggingHelper log)
{
    private const string WindowsUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
    private const string LinuxUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";
    private const string MacUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz";

    public static string ExecutableName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "steamcmd.exe" : "steamcmd.sh";

    public async Task<string> EnsureAsync(string steamRootDir)
    {
        Directory.CreateDirectory(steamRootDir);
        var exePath = Path.Combine(steamRootDir, ExecutableName);

        if (File.Exists(exePath))
        {
            log.LogMessage(MessageImportance.Low, $"steamcmd already present at {exePath}");
            return exePath;
        }

        var url = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsUrl
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? MacUrl
                : LinuxUrl;

        log.LogMessage(MessageImportance.High, $"Downloading steamcmd from {url}");

        var archivePath = Path.Combine(steamRootDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "steamcmd.zip" : "steamcmd.tar.gz");

        using (var http = new HttpClient())
        using (var stream = await http.GetStreamAsync(url))
        using (var file = File.Create(archivePath))
        {
            await stream.CopyToAsync(file);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ZipFile.ExtractToDirectory(archivePath, steamRootDir);
        }
        else
        {
            await ExtractTarGzAsync(archivePath, steamRootDir);
            EnsureExecutable(exePath);
        }

        File.Delete(archivePath);

        // todo:: check if this needed on windows as well

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await RunWarmupAsync(exePath, steamRootDir);
        }

        return exePath;
    }

    private static async Task ExtractTarGzAsync(string archivePath, string destDir)
    {
        await using var fileStream = File.OpenRead(archivePath);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzipStream, destDir, overwriteFiles: true);
    }

    private static void EnsureExecutable(string path)
    {
        if (!File.Exists(path) || OperatingSystem.IsWindows())
            return;

        var mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path,
            mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    private async Task RunWarmupAsync(string exePath, string steamRootDir)
    {
        log.LogMessage(MessageImportance.Low, "Running one-time steamcmd warm-up (self-update) pass...");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            ArgumentList = { "+quit" },
            WorkingDirectory = steamRootDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
                return;

            await process.StandardOutput.ReadToEndAsync();
            await process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            // Exit code intentionally ignored - this pass exists purely to let steamcmd finish self-updating
        }
        catch (Exception ex)
        {
            log.LogMessage(MessageImportance.Low,
                $"steamcmd warm-up pass threw ({ex.Message}) - continuing anyway, real download run will surface any genuine problem.");
        }
    }
}
