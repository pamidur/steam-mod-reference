using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Pamidur.SteamModReference.Tasks
{
    /// <summary>
    /// Tracks what has already been downloaded/resolved 
    /// so repeated builds don't reinvoke SteamCMD for mods that haven't changed.
    /// </summary>
    public sealed class ModManifest
    {
        public Dictionary<string, ModEntry> Mods { get; set; } = [];

        public static ModManifest Load(string path)
        {
            if (!File.Exists(path))
                return new ModManifest();

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<ModManifest>(json) ?? new ModManifest();
            }
            catch
            {
                // Corrupt/incompatible manifest -> treat as empty rather than fail the build.
                return new ModManifest();
            }
        }

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
    }

    public sealed class ModEntry
    {
        public string WorkshopId { get; set; } = "";
        public string AppId { get; set; } = "";
        public DateTimeOffset DownloadedAtUtc { get; set; }

        /// <summary>
        /// Last-write-time (UTC ticks) of the mod's content folder at the time we last
        /// successfully processed it. Used as a cheap "has steam updated this mod" check
        /// without hashing the whole folder.
        /// </summary>
        public long ContentFolderStampTicks { get; set; }
    }
}
