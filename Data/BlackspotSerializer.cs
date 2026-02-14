using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using OpenTK.Mathematics;
using MeshViewer3D.Core;

namespace MeshViewer3D.Data
{
    /// <summary>
    /// Sérialisation/désérialisation des blackspots au format XML Honorbuddy
    /// Format: <Blackspot X="..." Y="..." Z="..." Radius="..." Height="..." Name="..." />
    /// </summary>
    public static class BlackspotSerializer
    {
        /// <summary>
        /// Sauvegarde les blackspots au format XML Honorbuddy
        /// </summary>
        public static void SaveToXml(List<Blackspot> blackspots, string filePath)
        {
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("Blackspots")
            );

            var root = doc.Root;
            if (root == null) return;

            foreach (var bs in blackspots)
            {
                var wowPos = bs.ToWoWCoords();
                
                var element = new XElement("Blackspot",
                    new XAttribute("X", wowPos.X.ToString("F2")),
                    new XAttribute("Y", wowPos.Y.ToString("F2")),
                    new XAttribute("Z", wowPos.Z.ToString("F2")),
                    new XAttribute("Radius", bs.Radius.ToString("F2")),
                    new XAttribute("Height", bs.Height.ToString("F2"))
                );

                if (!string.IsNullOrEmpty(bs.Name))
                {
                    element.Add(new XAttribute("Name", bs.Name));
                }

                root.Add(element);
            }

            doc.Save(filePath);
        }

        /// <summary>
        /// Charge les blackspots depuis un fichier XML Honorbuddy
        /// </summary>
        public static List<Blackspot> LoadFromXml(string filePath)
        {
            var blackspots = new List<Blackspot>();

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var doc = XDocument.Load(filePath);
            var root = doc.Root;
            if (root == null) return blackspots;

            foreach (var element in root.Elements("Blackspot"))
            {
                try
                {
                    float x = float.Parse(element.Attribute("X")?.Value ?? "0");
                    float y = float.Parse(element.Attribute("Y")?.Value ?? "0");
                    float z = float.Parse(element.Attribute("Z")?.Value ?? "0");
                    float radius = float.Parse(element.Attribute("Radius")?.Value ?? "10");
                    float height = float.Parse(element.Attribute("Height")?.Value ?? "10");
                    string name = element.Attribute("Name")?.Value ?? "";

                    var blackspot = Blackspot.FromWoW(x, y, z, radius, height, name);
                    blackspots.Add(blackspot);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing blackspot: {ex.Message}");
                }
            }

            return blackspots;
        }

        /// <summary>
        /// Exporte au format CSV pour analyse
        /// </summary>
        public static void ExportToCsv(List<Blackspot> blackspots, string filePath)
        {
            using var writer = new StreamWriter(filePath);
            writer.WriteLine("Name,X,Y,Z,Radius,Height");

            foreach (var bs in blackspots)
            {
                var wowPos = bs.ToWoWCoords();
                writer.WriteLine($"\"{bs.Name}\",{wowPos.X:F2},{wowPos.Y:F2},{wowPos.Z:F2},{bs.Radius:F2},{bs.Height:F2}");
            }
        }
    }
}
