using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using MSTask = Microsoft.Build.Utilities.Task;

namespace Pamidur.SteamModReference.Tasks;

/// <summary>
/// Entry point invoked by the .targets file. Given the project's `@(SteamModReference)`
/// items, ensures steamcmd is present, downloads every distinct workshop id (batched),
/// resolves each item's dll(s), applies dedup, and returns `ResolvedAssemblies` for the
/// targets file to turn into `@(Reference)` items.
///
/// STATUS: end-to-end flow implemented and wired to run synchronously over the async
/// helpers (MSBuild tasks are sync by contract; `.GetAwaiter().GetResult()` is used -
/// fine here since this isn't UI-thread code). NOT YET DONE: incremental skip via
/// ModManifest (currently downloads every distinct mod id on every build - see TODO),
/// and the MSBuild Inputs/Outputs on the Target itself in the .targets file.
/// </summary>
public sealed class ResolveModReferencesTask : MSTask
{
    /// <summary>The @(SteamModReference) items from the project.</summary>
    [Required]
    public ITaskItem[] Mods { get; set; } = [];

    /// <summary>Default Steam AppId (e.g. RimWorld = 294100), from $(SteamModAppId).</summary>
    [Required]
    public string AppId { get; set; } = "";

    /// <summary>obj/steam - everything lives under here.</summary>
    [Required]
    public string IntermediateDir { get; set; } = "";

    /// <summary>
    /// Semicolon-separated ordered fallback patterns from $(SteamModDefaultAssemblies),
    /// used for any SteamModReference item that doesn't set Assemblies="...".
    /// e.g. "1.6/Assemblies/*.*;Assemblies/*.*"
    /// </summary>
    public string DefaultAssembliesPatterns { get; set; } = "";

    /// <summary>The project's pre-existing @(Reference) items, for dedup seeding.</summary>
    public ITaskItem[] ExistingReferences { get; set; } = [];

    /// <summary>Output: resolved dll paths as Reference-shaped items (HintPath set).</summary>
    [Output]
    public ITaskItem[] ResolvedAssemblies { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            return ExecuteAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: true);
            return false;
        }
    }

    private async System.Threading.Tasks.Task<bool> ExecuteAsync()
    {
        if (Mods.Length == 0)
        {
            ResolvedAssemblies = [];
            return true;
        }

        var steamRoot = Path.Combine(IntermediateDir, "steamcmd");
        var workshopRoot = Path.Combine(IntermediateDir, "steam"); // force_install_dir target

        var bootstrapper = new SteamCmdBootstrapper(Log);
        var steamCmdExe = await bootstrapper.EnsureAsync(steamRoot);

        // --- Batch every distinct (appId, workshopId) into one steamcmd run. ---
        // TODO: consult ModManifest here and skip ids whose content folder is already
        // present and unchanged, instead of unconditionally re-downloading every id
        // on every build. For now: always attempt download (steamcmd itself is close
        // to a no-op if content is already current, so this is correct but slow).
        var distinctModIds = Mods.Select(m => m.ItemSpec).Distinct().ToList();
        var runner = new SteamCmdRunner(steamCmdExe, workshopRoot, Log);

        var ok = await runner.DownloadWorkshopItemsAsync(
            distinctModIds.Select(id => (AppId, id)));

        if (!ok)
        {
            Log.LogWarning("One or more steamcmd downloads reported failure; continuing with whatever content is on disk.");
        }

        // --- Resolve + dedup, in item declaration order. ---
        var resolver = new AssemblyResolver(Log);
        var defaultPatterns = (DefaultAssembliesPatterns ?? "")
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();

        var alreadyKnownNames = new HashSet<string>(
            ExistingReferences.Select(r => Path.GetFileNameWithoutExtension(r.ItemSpec)),
            StringComparer.OrdinalIgnoreCase);

        var results = new List<ITaskItem>();

        foreach (var modItem in Mods)
        {
            var workshopId = modItem.ItemSpec;
            var assembliesPattern = modItem.GetMetadata("Assemblies");
            var allowDuplicates = string.Equals(
                modItem.GetMetadata("AllowDuplicates"), "true", StringComparison.OrdinalIgnoreCase);

            var contentDir = runner.GetWorkshopContentDir(AppId, workshopId);

            var dlls = resolver.ResolveDllPaths(contentDir, assembliesPattern, defaultPatterns);
            var kept = resolver.ApplyDedup(dlls, allowDuplicates, alreadyKnownNames, workshopId);

            foreach (var dll in kept)
            {
                var refItem = new Microsoft.Build.Utilities.TaskItem(Path.GetFileNameWithoutExtension(dll));
                refItem.SetMetadata("HintPath", dll);
                refItem.SetMetadata("Private", "false");
                refItem.SetMetadata("SteamModWorkshopId", workshopId);
                results.Add(refItem);
            }
        }

        ResolvedAssemblies = [.. results];

        // TODO: write/update manifest.json here (ModManifest.Save) once the
        // skip-logic above actually reads it back.

        return !Log.HasLoggedErrors;
    }
}
