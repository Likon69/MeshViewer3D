using System;
using OpenTK.Mathematics;

namespace MeshViewer3D.Core
{
    /// <summary>
    /// Système de conversion de coordonnées entre WoW, Detour et OpenGL
    /// CRITIQUE: Les coordonnées sont stockées en format Detour dans les .mmtile
    /// Basé sur l'analyse du code Honorbuddy Tripper.Tools
    /// </summary>
    public static class CoordinateSystem
    {
        // Constantes WoW
        public const float TILE_SIZE = 533.3333f;       // Taille d'une tile en yards
        public const float MAP_HALF_SIZE = 32.0f * TILE_SIZE;  // 17066.66 yards
        public const int TILE_GRID_SIZE = 64;           // 64x64 tiles par map

        /// <summary>
        /// Convertit coordonnées WoW vers Detour (pour stockage/pathfinding)
        /// WoW: X=Nord, Y=Ouest, Z=Haut
        /// Detour: X=-WoW.Y, Y=WoW.Z, Z=-WoW.X
        /// </summary>
        public static Vector3 WowToDetour(Vector3 wow)
        {
            return new Vector3(
                -wow.Y,  // Detour X = -WoW Y
                wow.Z,   // Detour Y = WoW Z (hauteur)
                -wow.X   // Detour Z = -WoW X
            );
        }

        /// <summary>
        /// Convertit coordonnées Detour vers WoW (pour affichage)
        /// Detour: X, Y=hauteur, Z
        /// WoW: X=-Detour.Z, Y=-Detour.X, Z=Detour.Y
        /// </summary>
        public static Vector3 DetourToWow(Vector3 detour)
        {
            return new Vector3(
                -detour.Z,  // WoW X = -Detour Z
                -detour.X,  // WoW Y = -Detour X
                detour.Y    // WoW Z = Detour Y (hauteur)
            );
        }

        /// <summary>
        /// Convertit coordonnées WoW vers OpenGL pour rendu
        /// Note: On utilise directement les coords Detour car Y=up correspond
        /// </summary>
        public static Vector3 WowToOpenGL(Vector3 wow)
        {
            // OpenGL utilise Y-up, ce qui correspond à Detour Y = hauteur
            return WowToDetour(wow);
        }

        /// <summary>
        /// Convertit coordonnées Detour vers OpenGL (identité car même référentiel)
        /// </summary>
        public static Vector3 DetourToOpenGL(Vector3 detour)
        {
            return detour;  // Pas de conversion nécessaire
        }

        /// <summary>
        /// Calcule les coordonnées tile (0-63) depuis coordonnées WoW
        /// </summary>
        public static (int tileX, int tileY) WorldToTile(float wowX, float wowY)
        {
            int tileX = (int)((MAP_HALF_SIZE - wowX) / TILE_SIZE);
            int tileY = (int)((MAP_HALF_SIZE - wowY) / TILE_SIZE);
            
            // Clamp dans la grille 64x64
            tileX = Math.Clamp(tileX, 0, TILE_GRID_SIZE - 1);
            tileY = Math.Clamp(tileY, 0, TILE_GRID_SIZE - 1);
            
            return (tileX, tileY);
        }

        /// <summary>
        /// Calcule les coordonnées tile depuis position 3D WoW
        /// </summary>
        public static (int tileX, int tileY) WorldToTile(Vector3 wowPos)
        {
            return WorldToTile(wowPos.X, wowPos.Y);
        }

        /// <summary>
        /// Calcule les coordonnées monde WoW du centre d'une tile
        /// </summary>
        public static (float wowX, float wowY) TileToWorld(int tileX, int tileY)
        {
            float wowX = MAP_HALF_SIZE - (tileX + 0.5f) * TILE_SIZE;
            float wowY = MAP_HALF_SIZE - (tileY + 0.5f) * TILE_SIZE;
            
            return (wowX, wowY);
        }

        /// <summary>
        /// Calcule les coordonnées monde WoW du coin supérieur gauche d'une tile
        /// </summary>
        public static (float wowX, float wowY) TileToWorldCorner(int tileX, int tileY)
        {
            float wowX = MAP_HALF_SIZE - tileX * TILE_SIZE;
            float wowY = MAP_HALF_SIZE - tileY * TILE_SIZE;
            
            return (wowX, wowY);
        }

        /// <summary>
        /// Extrait mapId, tileX, tileY depuis nom de fichier .mmtile
        /// Format: MMMYYXX.mmtile (ex: 0013933.mmtile = map 1, tileY=39, tileX=33)
        /// Correspond à MapBuilder.cpp: sprintf("mmaps/%03u%02i%02i.mmtile", mapID, tileY, tileX)
        /// </summary>
        public static (int mapId, int tileX, int tileY) ParseTileFileName(string fileName)
        {
            // Enlever extension
            string nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
            
            // Vérifier longueur (7 caractères: MMM YY XX)
            if (nameWithoutExt.Length != 7)
                throw new ArgumentException($"Invalid tile filename format: {fileName}");
            
            int mapId = int.Parse(nameWithoutExt.Substring(0, 3));
            int tileY = int.Parse(nameWithoutExt.Substring(3, 2));  // Position 3-4 = tileY
            int tileX = int.Parse(nameWithoutExt.Substring(5, 2));  // Position 5-6 = tileX
            
            return (mapId, tileX, tileY);
        }

        /// <summary>
        /// Génère un nom de fichier .mmtile depuis mapId et coords tile
        /// Format: MMMYYXX.mmtile (Y first, then X — matches MapBuilder.cpp convention)
        /// </summary>
        public static string GenerateTileFileName(int mapId, int tileX, int tileY)
        {
            return $"{mapId:D3}{tileY:D2}{tileX:D2}.mmtile";
        }
    }
}
