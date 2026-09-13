using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Utilities;

namespace Pamidur.SteamModReference.Tasks;

/// <summary>
/// Resolves the `Assemblies` pattern (or the `SteamModDefaultAssemblies` fallback list) 
/// against a mod's downloaded content folder into a concrete set of .dll paths,
/// and applies name-based dedup against already-referenced assemblies.
/// </summary>
public sealed class AssemblyResolver(TaskLoggingHelper log)
{

    /// <summary>
    /// Resolves one mod's dll set.
    /// </summary>
    /// <param name="modContentDir">e.g. obj/steam/steamapps/workshop/content/{appid}/{modid}</param>
    /// <param name="assembliesPattern">The item's `Assemblies` metadata, or null/empty to fall back to <paramref name="defaultPatterns"/></param>
    public IReadOnlyList<string> ResolveDllPaths(
        string modContentDir,
        string? assembliesPattern,
        IReadOnlyList<string> defaultPatterns)
    {
        if (!Directory.Exists(modContentDir))
        {
            log.LogWarning($"Mod content directory not found: {modContentDir}");
            return [];
        }

        var patternsToTry = !string.IsNullOrWhiteSpace(assembliesPattern)
            ? [assembliesPattern!]
            : defaultPatterns;

        foreach (var pattern in patternsToTry)
        {
            var matches = MatchPattern(modContentDir, pattern);
            if (matches.Count > 0)
            {
                if (patternsToTry.Count > 1)
                    log.LogMessage(Microsoft.Build.Framework.MessageImportance.Low,
                        $"Resolved via pattern '{pattern}' ({matches.Count} dll(s)) in {modContentDir}");
                return matches;
            }
        }

        log.LogWarning(
            $"No assemblies matched in {modContentDir} for pattern(s): {string.Join(", ", patternsToTry)}");
        return [];
    }

    /// <summary>
    /// Supports:
    ///   - a bare directory ("1.6/Assemblies")            -> all *.dll directly inside it
    ///   - a glob ending in a filename pattern ("Assemblies/*.dll", "Assemblies/*MyStuff.dll")
    ///   - "**" as a recursive-directories wildcard segment
    /// </summary>
    private static List<string> MatchPattern(string root, string pattern)
    {
        // todo: proper regex
        pattern = pattern.Replace('\\', '/').TrimStart('/');

        // Bare directory, no glob chars at all -> take every dll directly inside.
        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            var dir = Path.Combine(root, pattern.Replace('/', Path.DirectorySeparatorChar));
            return Directory.Exists(dir)
                ? [.. Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly)]
                : [];
        }

        var segments = pattern.Split('/');
        var dirSegments = segments.Take(segments.Length - 1).ToArray();
        var filePattern = segments.Last();

        var baseDir = Path.Combine([root, .. dirSegments.Where(s => s != "**")]);
        var recursive = dirSegments.Contains("**");

        if (!Directory.Exists(baseDir))
            return [];

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        // Directory.GetFiles supports simple `*`/`?` glob patterns natively.
        return [.. Directory.GetFiles(baseDir, filePattern, searchOption).Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// Dedups the references with known names having precedence
    /// </summary>
    public IReadOnlyList<string> ApplyDedup(
        IReadOnlyList<string> dllPaths,
        bool allowDuplicates,
        HashSet<string> alreadyKnownNames,
        string modIdForLogging)
    {
        var kept = new List<string>();
        foreach (var dll in dllPaths)
        {
            var name = Path.GetFileNameWithoutExtension(dll);

            if (!allowDuplicates && alreadyKnownNames.Contains(name))
            {
                log.LogMessage(Microsoft.Build.Framework.MessageImportance.Normal,
                    $"Skipping '{name}' from mod {modIdForLogging} - already referenced (set AllowDuplicates=\"true\" to force).");
                continue;
            }

            kept.Add(dll);
            alreadyKnownNames.Add(name);
        }

        return kept;
    }
}
