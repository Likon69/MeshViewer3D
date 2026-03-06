using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace MeshViewer3D.Core
{
    public class MapEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Continent { get; set; } = "";
        public string? Directory { get; set; }
    }

    public static class MapDatabase
    {
        private static Dictionary<int, MapEntry>? _maps;

        private static void EnsureLoaded()
        {
            if (_maps != null) return;
            _maps = new Dictionary<int, MapEntry>();

            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Maps.json");
            if (!File.Exists(jsonPath)) return;

            string json = File.ReadAllText(jsonPath);
            var root = JObject.Parse(json);
            var mapsArray = root["maps"] as JArray;
            if (mapsArray == null) return;

            foreach (var item in mapsArray)
            {
                var entry = new MapEntry
                {
                    Id = item.Value<int>("id"),
                    Name = item.Value<string>("name") ?? "",
                    Continent = item.Value<string>("continent") ?? "",
                    Directory = item.Value<string>("directory")
                };
                _maps[entry.Id] = entry;
            }
        }

        public static string? GetDirectory(int mapId)
        {
            EnsureLoaded();
            return _maps!.TryGetValue(mapId, out var entry) ? entry.Directory : null;
        }

        public static string? GetName(int mapId)
        {
            EnsureLoaded();
            return _maps!.TryGetValue(mapId, out var entry) ? entry.Name : null;
        }

        public static MapEntry? Get(int mapId)
        {
            EnsureLoaded();
            return _maps!.TryGetValue(mapId, out var entry) ? entry : null;
        }
    }
}
