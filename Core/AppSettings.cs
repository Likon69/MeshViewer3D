using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace MeshViewer3D.Core
{
    public static class AppSettings
    {
        private static readonly string SettingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static string? WowDataPath { get; set; }

        public static void Load()
        {
            if (!File.Exists(SettingsPath)) return;

            try
            {
                string json = File.ReadAllText(SettingsPath);
                var obj = JObject.Parse(json);
                WowDataPath = obj.Value<string>("wowDataPath");
            }
            catch
            {
                // Corrupt settings file — ignore, will be overwritten on next save
            }
        }

        public static void Save()
        {
            var obj = new JObject
            {
                ["wowDataPath"] = WowDataPath
            };
            File.WriteAllText(SettingsPath, obj.ToString(Newtonsoft.Json.Formatting.Indented));
        }
    }
}
