using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using OpenTK.Mathematics;
using MeshViewer3D.Core;

namespace MeshViewer3D.Data
{
    /// <summary>
    /// Sérialisation/désérialisation des OffMesh connections au format XML compatible Honorbuddy
    /// Format basé sur le format MeshConnections de HB/Tripper
    /// </summary>
    public static class OffMeshSerializer
    {
        /// <summary>
        /// Sauvegarde les OffMesh connections au format XML
        /// </summary>
        public static void SaveToXml(List<OffMeshConnection> connections, string filePath)
        {
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("OffMeshConnections",
                    new XAttribute("Version", "1.0")
                )
            );

            var root = doc.Root;
            if (root == null) return;

            int id = 0;
            foreach (var conn in connections)
            {
                // Convertir en coordonnées WoW
                var startWow = CoordinateSystem.DetourToWow(conn.Start);
                var endWow = CoordinateSystem.DetourToWow(conn.End);

                var element = new XElement("Connection",
                    new XAttribute("Id", id++),
                    new XAttribute("StartX", startWow.X.ToString("F3")),
                    new XAttribute("StartY", startWow.Y.ToString("F3")),
                    new XAttribute("StartZ", startWow.Z.ToString("F3")),
                    new XAttribute("EndX", endWow.X.ToString("F3")),
                    new XAttribute("EndY", endWow.Y.ToString("F3")),
                    new XAttribute("EndZ", endWow.Z.ToString("F3")),
                    new XAttribute("Radius", conn.Radius.ToString("F2")),
                    new XAttribute("Bidirectional", conn.IsBidirectional.ToString())
                );

                if (!string.IsNullOrEmpty(conn.Name))
                {
                    element.Add(new XAttribute("Name", conn.Name));
                }

                // Ajouter le type si ce n'est pas Jump (par défaut)
                if (conn.ConnectionType != OffMeshConnectionType.Jump)
                {
                    element.Add(new XAttribute("Type", conn.ConnectionType.ToString()));
                }

                root.Add(element);
            }

            doc.Save(filePath);
        }

        /// <summary>
        /// Charge les OffMesh connections depuis un fichier XML
        /// </summary>
        public static List<OffMeshConnection> LoadFromXml(string filePath)
        {
            var connections = new List<OffMeshConnection>();

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var doc = XDocument.Load(filePath);
            var root = doc.Root;
            if (root == null) return connections;

            foreach (var element in root.Elements("Connection"))
            {
                try
                {
                    float startX = float.Parse(element.Attribute("StartX")?.Value ?? "0");
                    float startY = float.Parse(element.Attribute("StartY")?.Value ?? "0");
                    float startZ = float.Parse(element.Attribute("StartZ")?.Value ?? "0");
                    float endX = float.Parse(element.Attribute("EndX")?.Value ?? "0");
                    float endY = float.Parse(element.Attribute("EndY")?.Value ?? "0");
                    float endZ = float.Parse(element.Attribute("EndZ")?.Value ?? "0");
                    float radius = float.Parse(element.Attribute("Radius")?.Value ?? "1.0");
                    bool bidirectional = bool.Parse(element.Attribute("Bidirectional")?.Value ?? "true");
                    string name = element.Attribute("Name")?.Value ?? "";
                    
                    var typeStr = element.Attribute("Type")?.Value ?? "Jump";
                    OffMeshConnectionType connType = Enum.TryParse<OffMeshConnectionType>(typeStr, out var t) 
                        ? t : OffMeshConnectionType.Jump;

                    // Convertir de WoW vers Detour
                    var startDetour = CoordinateSystem.WowToDetour(new Vector3(startX, startY, startZ));
                    var endDetour = CoordinateSystem.WowToDetour(new Vector3(endX, endY, endZ));

                    var conn = new OffMeshConnection
                    {
                        Start = startDetour,
                        End = endDetour,
                        Radius = radius,
                        Flags = bidirectional ? (byte)1 : (byte)0,
                        Name = name,
                        ConnectionType = connType
                    };

                    connections.Add(conn);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing OffMesh connection: {ex.Message}");
                }
            }

            return connections;
        }

        /// <summary>
        /// Exporte au format CSV pour analyse
        /// </summary>
        public static void ExportToCsv(List<OffMeshConnection> connections, string filePath)
        {
            using var writer = new StreamWriter(filePath);
            writer.WriteLine("Name,StartX,StartY,StartZ,EndX,EndY,EndZ,Radius,Bidirectional,Type,Distance");

            foreach (var conn in connections)
            {
                var startWow = CoordinateSystem.DetourToWow(conn.Start);
                var endWow = CoordinateSystem.DetourToWow(conn.End);
                float distance = conn.GetDistance();

                writer.WriteLine($"\"{conn.Name}\",{startWow.X:F3},{startWow.Y:F3},{startWow.Z:F3}," +
                    $"{endWow.X:F3},{endWow.Y:F3},{endWow.Z:F3}," +
                    $"{conn.Radius:F2},{conn.IsBidirectional},{conn.ConnectionType},{distance:F2}");
            }
        }

        /// <summary>
        /// Sauvegarde au format binaire compact (pour performance)
        /// </summary>
        public static void SaveToBinary(List<OffMeshConnection> connections, string filePath)
        {
            using var stream = File.Create(filePath);
            using var writer = new BinaryWriter(stream);

            // Header
            writer.Write("OMSH"); // Magic
            writer.Write(1); // Version
            writer.Write(connections.Count);

            foreach (var conn in connections)
            {
                // Start position (Detour coords)
                writer.Write(conn.Start.X);
                writer.Write(conn.Start.Y);
                writer.Write(conn.Start.Z);
                
                // End position (Detour coords)
                writer.Write(conn.End.X);
                writer.Write(conn.End.Y);
                writer.Write(conn.End.Z);
                
                // Properties
                writer.Write(conn.Radius);
                writer.Write(conn.Flags);
                writer.Write((byte)conn.ConnectionType);
                
                // Name (with length prefix)
                writer.Write(conn.Name ?? "");
            }
        }

        /// <summary>
        /// Charge depuis le format binaire
        /// </summary>
        public static List<OffMeshConnection> LoadFromBinary(string filePath)
        {
            var connections = new List<OffMeshConnection>();

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);

            // Verify magic
            var magic = new string(reader.ReadChars(4));
            if (magic != "OMSH")
                throw new InvalidDataException("Invalid OffMesh file format");

            var version = reader.ReadInt32();
            if (version != 1)
                throw new InvalidDataException($"Unsupported version: {version}");

            int count = reader.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                var conn = new OffMeshConnection
                {
                    Start = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    End = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    Radius = reader.ReadSingle(),
                    Flags = reader.ReadByte(),
                    ConnectionType = (OffMeshConnectionType)reader.ReadByte(),
                    Name = reader.ReadString()
                };

                connections.Add(conn);
            }

            return connections;
        }
    }
}
