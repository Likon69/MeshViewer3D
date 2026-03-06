using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using MpqLib;

namespace MeshViewer3D.Core.Mpq
{
    /// <summary>
    /// Manages the full WoW 3.3.5a MPQ archive stack.
    /// Discovers and opens all archives from a Data/ directory in the correct
    /// priority order (patch-3 overrides patch-2 overrides patch, etc).
    ///
    /// Usage:
    ///   using var provider = WowDataProvider.Open(@"C:\WoW\Data");
    ///   byte[]? adt = provider.GetFileBytes(@"World\Maps\Azeroth\Azeroth_32_48.adt");
    /// </summary>
    public sealed class WowDataProvider : IDisposable
    {
        private readonly MpqFileSystem _fs = new();
        private bool _disposed;

        public string DataPath { get; private set; } = string.Empty;
        public int ArchivesLoaded => _fs.ArchiveCount;

        // WoW 3.3.5a load order — ascending priority (last = highest)
        private static readonly string[] BaseOrder =
        [
            "common.MPQ", "common-2.MPQ", "expansion.MPQ", "lichking.MPQ",
            "patch.MPQ", "patch-2.MPQ", "patch-3.MPQ",
        ];

        private static readonly string[] LocaleOrder =
        [
            "base-{0}.MPQ", "locale-{0}.MPQ", "expansion-locale-{0}.MPQ",
            "lichking-locale-{0}.MPQ", "patch-{0}.MPQ", "patch-{0}-2.MPQ",
            "patch-{0}-3.MPQ",
        ];

        private WowDataProvider() { }

        public static WowDataProvider Open(string dataPath, string? locale = null)
        {
            if (!Directory.Exists(dataPath))
                throw new DirectoryNotFoundException($"WoW Data directory not found: {dataPath}");

            var provider = new WowDataProvider { DataPath = dataPath };

            foreach (string name in BaseOrder)
                provider.TryAdd(Path.Combine(dataPath, name));

            string? localeDir = ResolveLocaleDir(dataPath, locale);
            if (localeDir != null)
            {
                string detectedLocale = Path.GetFileName(localeDir);
                foreach (string template in LocaleOrder)
                    provider.TryAdd(Path.Combine(localeDir, string.Format(template, detectedLocale)));
            }

            if (provider._fs.ArchiveCount == 0)
                throw new InvalidOperationException(
                    $"No MPQ archives found in '{dataPath}'. Ensure this is a valid WoW 3.3.5a Data directory.");

            Debug.WriteLine($"[MPQ] Opened {provider._fs.ArchiveCount} archives from: {dataPath}");
            return provider;
        }

        public byte[]? GetFileBytes(string internalPath)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WowDataProvider));
            return _fs.GetFileBytes(internalPath);
        }

        public bool FileExists(string internalPath)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WowDataProvider));
            return _fs.FileExists(internalPath);
        }

        private void TryAdd(string fullPath)
        {
            if (!File.Exists(fullPath)) return;
            try
            {
                _fs.AddArchive(new MpqArchive(fullPath));
                Debug.WriteLine($"[MPQ] Loaded: {Path.GetFileName(fullPath)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MPQ] Skip {Path.GetFileName(fullPath)}: {ex.Message}");
            }
        }

        private static string? ResolveLocaleDir(string dataPath, string? preferred)
        {
            if (preferred != null)
            {
                string dir = Path.Combine(dataPath, preferred);
                return Directory.Exists(dir) ? dir : null;
            }
            foreach (string dir in Directory.GetDirectories(dataPath))
            {
                string folderName = Path.GetFileName(dir);
                if (Regex.IsMatch(folderName, @"^[a-z]{2}[A-Z]{2}$"))
                    return dir;
            }
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _fs.Dispose();
        }
    }
}
