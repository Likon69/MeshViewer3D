using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using OpenTK.Mathematics;
using MeshViewer3D.Core;

namespace MeshViewer3D.Data
{
    /// <summary>
    /// Sérialisation/désérialisation des ConvexVolumes au format XML child-element.
    ///
    /// Approche retenue: child Vertex elements (approche B)
    /// ────────────────────────────────────────────────────
    /// Chaque sommet est un élément enfant &lt;Vertex X="..." Y="..." Z="..." /&gt;
    /// Les coordonnées (X/Y/Z WoW) sont nommées explicitement.
    ///
    /// Pourquoi PAS l'attribut inline "Vertices=x1,y1,z1;x2,y2,z2;...":
    ///   • Un attribut XML est un scalaire — pas un conteneur de séquence structurée.
    ///     Une liste variable de points 3D est exactement ce que les éléments enfants existent pour représenter.
    ///   • Le format inline ne peut pas être validé par XSD, ni interrogé par XPath.
    ///   • Avec 8+ sommets, la chaîne dépasse 600 chars sur une ligne — illisible.
    ///   • Les deux formats ont une complexité de parsing équivalente; il n'y a pas de gain réel à être flat.
    ///
    /// Format XML:
    /// &lt;?xml version="1.0" encoding="utf-8"?&gt;
    /// &lt;ConvexVolumes Version="1.0"&gt;
    ///   &lt;Volume Id="0" Name="Water Zone" AreaType="Water" MinHeight="0.000" MaxHeight="50.000"&gt;
    ///     &lt;Vertex X="1234.560" Y="5678.900" Z="100.000" /&gt;
    ///     &lt;Vertex X="1244.560" Y="5678.900" Z="100.000" /&gt;
    ///   &lt;/Volume&gt;
    /// &lt;/ConvexVolumes&gt;
    ///
    /// Coordonnées:
    ///   • Fichier  : WoW  (X=Nord, Y=Ouest, Z=Haut)
    ///   • Interne  : Detour (X=-WoW.Y, Y=WoW.Z, Z=-WoW.X)
    ///   • MinHeight / MaxHeight : Detour Y (= WoW Z, hauteur absolue)
    /// </summary>
    public static class ConvexVolumeSerializer
    {
        private const string FILE_VERSION = "1.0";

        // ─────────────────────────────────────── SAVE ───────────────────────────

        /// <summary>
        /// Sauvegarde une liste de ConvexVolumes au format XML child-element.
        /// Les coordonnées internes (Detour) sont converties en WoW avant écriture.
        /// Seuls les volumes valides (≥ 3 sommets) sont écrits.
        /// </summary>
        public static void SaveToXml(List<ConvexVolume> volumes, string filePath)
        {
            if (volumes == null)  throw new ArgumentNullException(nameof(volumes));
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));

            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("ConvexVolumes",
                    new XAttribute("Version", FILE_VERSION)
                )
            );

            var root = doc.Root!;

            int id = 0;
            foreach (var vol in volumes)
            {
                if (!vol.IsValid())
                    continue;

                var elem = new XElement("Volume",
                    new XAttribute("Id",        id++),
                    new XAttribute("Name",       vol.Name ?? ""),
                    new XAttribute("AreaType",   vol.AreaType.ToString()),
                    new XAttribute("MinHeight",  vol.MinHeight.ToString("F3", CultureInfo.InvariantCulture)),
                    new XAttribute("MaxHeight",  vol.MaxHeight.ToString("F3", CultureInfo.InvariantCulture))
                );

                foreach (var v in vol.Vertices)
                {
                    var wow = CoordinateSystem.DetourToWow(v);
                    elem.Add(new XElement("Vertex",
                        new XAttribute("X", wow.X.ToString("F3", CultureInfo.InvariantCulture)),
                        new XAttribute("Y", wow.Y.ToString("F3", CultureInfo.InvariantCulture)),
                        new XAttribute("Z", wow.Z.ToString("F3", CultureInfo.InvariantCulture))
                    ));
                }

                root.Add(elem);
            }

            doc.Save(filePath);
        }

        // ─────────────────────────────────────── LOAD ───────────────────────────

        /// <summary>
        /// Charge une liste de ConvexVolumes depuis un fichier XML.
        /// Les coordonnées WoW lues sont converties en Detour.
        /// Un volume avec moins de 3 sommets est rejeté avec un message de diagnostic.
        /// </summary>
        public static List<ConvexVolume> LoadFromXml(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"ConvexVolumes file not found: {filePath}");

            var volumes = new List<ConvexVolume>();
            var doc  = XDocument.Load(filePath);
            var root = doc.Root;
            if (root == null) return volumes;

            foreach (var elem in root.Elements("Volume"))
            {
                try
                {
                    var vol = new ConvexVolume
                    {
                        Name      = (string?)elem.Attribute("Name") ?? "",
                        AreaType  = ParseAreaType((string?)elem.Attribute("AreaType") ?? ""),
                        MinHeight = ParseFloatAttr(elem, "MinHeight", 0f),
                        MaxHeight = ParseFloatAttr(elem, "MaxHeight", 100f),
                    };

                    foreach (var vElem in elem.Elements("Vertex"))
                    {
                        float x = ParseFloatAttr(vElem, "X", 0f);
                        float y = ParseFloatAttr(vElem, "Y", 0f);
                        float z = ParseFloatAttr(vElem, "Z", 0f);
                        vol.Vertices.Add(CoordinateSystem.WowToDetour(new Vector3(x, y, z)));
                    }

                    if (vol.IsValid())
                    {
                        volumes.Add(vol);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[ConvexVolumeSerializer] Skipped volume '{vol.Name}': " +
                            $"only {vol.Vertices.Count} vertex/vertices (need ≥ 3).");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ConvexVolumeSerializer] Error parsing <Volume> element: {ex.Message}");
                }
            }

            return volumes;
        }

        // ─────────────────────────────────────── HELPERS ────────────────────────

        private static float ParseFloatAttr(XElement elem, string attr, float def)
        {
            var raw = (string?)elem.Attribute(attr);
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;
        }

        private static AreaType ParseAreaType(string value)
        {
            if (Enum.TryParse<AreaType>(value, ignoreCase: true, out var named))
                return named;

            if (byte.TryParse(value, out byte numeric))
                return (AreaType)numeric;

            return AreaType.Ground;
        }
    }
}
