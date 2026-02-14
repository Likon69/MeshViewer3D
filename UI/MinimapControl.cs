using System;
using System.Drawing;
using System.Windows.Forms;

namespace MeshViewer3D.UI
{
    /// <summary>
    /// Minimap 2D style Honorbuddy
    /// Affiche tiles chargées + position courante
    /// </summary>
    public class MinimapControl : Control
    {
        private const int TILE_GRID_SIZE = 64;
        private bool[,] _loadedTiles = new bool[TILE_GRID_SIZE, TILE_GRID_SIZE];
        private int _currentTileX = -1;
        private int _currentTileY = -1;

        public MinimapControl()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(0, 50, 100);  // Fond bleu foncé
            Size = new Size(150, 150);
        }

        /// <summary>
        /// Marque une tile comme chargée
        /// </summary>
        public void SetTileLoaded(int tileX, int tileY, bool loaded)
        {
            if (tileX >= 0 && tileX < TILE_GRID_SIZE && tileY >= 0 && tileY < TILE_GRID_SIZE)
            {
                _loadedTiles[tileX, tileY] = loaded;
                Invalidate();
            }
        }

        /// <summary>
        /// Définit la tile courante (position caméra)
        /// </summary>
        public void SetCurrentTile(int tileX, int tileY)
        {
            if (_currentTileX != tileX || _currentTileY != tileY)
            {
                _currentTileX = tileX;
                _currentTileY = tileY;
                Invalidate();
            }
        }

        /// <summary>
        /// Efface toutes les tiles
        /// </summary>
        public void Clear()
        {
            _loadedTiles = new bool[TILE_GRID_SIZE, TILE_GRID_SIZE];
            _currentTileX = -1;
            _currentTileY = -1;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

            float scale = Math.Min((float)Width / TILE_GRID_SIZE, (float)Height / TILE_GRID_SIZE);
            int gridWidth = (int)(TILE_GRID_SIZE * scale);
            int gridHeight = (int)(TILE_GRID_SIZE * scale);
            int offsetX = (Width - gridWidth) / 2;
            int offsetY = (Height - gridHeight) / 2;

            // Dessiner tiles chargées
            for (int x = 0; x < TILE_GRID_SIZE; x++)
            {
                for (int y = 0; y < TILE_GRID_SIZE; y++)
                {
                    if (_loadedTiles[x, y])
                    {
                        g.FillRectangle(
                            Brushes.DarkGreen,
                            offsetX + x * scale,
                            offsetY + y * scale,
                            scale + 1,
                            scale + 1
                        );
                    }
                }
            }

            // Dessiner tile courante
            if (_currentTileX >= 0 && _currentTileY >= 0)
            {
                using var brush = new SolidBrush(Color.FromArgb(200, 255, 100, 0)); // Orange
                g.FillRectangle(
                    brush,
                    offsetX + _currentTileX * scale - 1,
                    offsetY + _currentTileY * scale - 1,
                    scale + 2,
                    scale + 2
                );
            }

            // Cadre
            g.DrawRectangle(Pens.Gray, offsetX, offsetY, gridWidth, gridHeight);

            // Coordonnées
            if (_currentTileX >= 0 && _currentTileY >= 0)
            {
                string coords = $"<{_currentTileX}, {_currentTileY}>";
                using var font = new Font("Consolas", 8);
                var size = g.MeasureString(coords, font);
                g.DrawString(coords, font, Brushes.White, 
                    (Width - size.Width) / 2, Height - size.Height - 2);
            }
        }
    }
}
