using System;
using System.Drawing;
using System.Windows.Forms;
using MeshViewer3D.Rendering;

namespace MeshViewer3D.UI
{
    /// <summary>
    /// Panel Settings comme dans Tripper.Renderer de Honorbuddy
    /// Contrôle les options de rendu: wireframe, lighting, couleurs, etc.
    /// </summary>
    public class SettingsPanel : UserControl
    {
        // UI Controls
        private CheckBox _chkWireframe;
        private CheckBox _chkLighting;
        private CheckBox _chkOffMesh;
        private CheckBox _chkBlackspots;
        private CheckBox _chkVolumes;
        private CheckBox _chkPolygonIds;
        private CheckBox _chkBvTree;
        private ComboBox _cmbColorMode;
        private TrackBar _trkWireAlpha;
        private TrackBar _trkFog;
        private Label _lblWireAlpha;
        private Label _lblFog;

        // Événements
        public event EventHandler<bool>? WireframeChanged;
        public event EventHandler<bool>? LightingChanged;
        public event EventHandler<bool>? OffMeshChanged;
        public event EventHandler<bool>? BlackspotsChanged;
        public event EventHandler<bool>? VolumesChanged;
        public event EventHandler<ColorMode>? ColorModeChanged;
        public event EventHandler<int>? WireframeAlphaChanged;
        public event EventHandler<int>? FogDensityChanged;

        public SettingsPanel()
        {
            SetupUI();
        }

        private void SetupUI()
        {
            this.BackColor = Color.FromArgb(37, 37, 38);
            this.Dock = DockStyle.Fill;

            int y = 10;

            // Titre
            var lblTitle = new Label
            {
                Text = "Render Settings",
                Location = new Point(10, y),
                Size = new Size(200, 20),
                ForeColor = Color.White,
                Font = new Font(this.Font, FontStyle.Bold)
            };
            this.Controls.Add(lblTitle);
            y += 30;

            // === VISIBILITY ===
            var lblVisibility = new Label
            {
                Text = "─── Visibility ───",
                Location = new Point(10, y),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            this.Controls.Add(lblVisibility);
            y += 20;

            _chkWireframe = CreateCheckBox("Wireframe Overlay", y, true);
            _chkWireframe.CheckedChanged += (s, e) => WireframeChanged?.Invoke(this, _chkWireframe.Checked);
            this.Controls.Add(_chkWireframe);
            y += 22;

            _chkLighting = CreateCheckBox("Dynamic Lighting", y, true);
            _chkLighting.CheckedChanged += (s, e) => LightingChanged?.Invoke(this, _chkLighting.Checked);
            this.Controls.Add(_chkLighting);
            y += 22;

            _chkOffMesh = CreateCheckBox("OffMesh Connections", y, true);
            _chkOffMesh.CheckedChanged += (s, e) => OffMeshChanged?.Invoke(this, _chkOffMesh.Checked);
            this.Controls.Add(_chkOffMesh);
            y += 22;

            _chkBlackspots = CreateCheckBox("Blackspots", y, true);
            _chkBlackspots.CheckedChanged += (s, e) => BlackspotsChanged?.Invoke(this, _chkBlackspots.Checked);
            this.Controls.Add(_chkBlackspots);
            y += 22;

            _chkVolumes = CreateCheckBox("Convex Volumes", y, true);
            _chkVolumes.CheckedChanged += (s, e) => VolumesChanged?.Invoke(this, _chkVolumes.Checked);
            this.Controls.Add(_chkVolumes);
            y += 22;

            _chkPolygonIds = CreateCheckBox("Polygon IDs (debug)", y, false);
            _chkPolygonIds.Enabled = false; // Coming soon
            this.Controls.Add(_chkPolygonIds);
            y += 22;

            _chkBvTree = CreateCheckBox("BV Tree (debug)", y, false);
            _chkBvTree.Enabled = false; // Coming soon
            this.Controls.Add(_chkBvTree);
            y += 30;

            // === COLOR MODE ===
            var lblColorMode = new Label
            {
                Text = "─── Color Mode ───",
                Location = new Point(10, y),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            this.Controls.Add(lblColorMode);
            y += 20;

            var lblColor = new Label
            {
                Text = "Mode:",
                Location = new Point(10, y + 3),
                AutoSize = true,
                ForeColor = Color.White
            };
            this.Controls.Add(lblColor);

            _cmbColorMode = new ComboBox
            {
                Location = new Point(60, y),
                Size = new Size(150, 21),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White
            };
            _cmbColorMode.Items.AddRange(new object[] { "By Area Type", "By Height", "By Polygon", "Flat" });
            _cmbColorMode.SelectedIndex = 0;
            _cmbColorMode.SelectedIndexChanged += CmbColorMode_SelectedIndexChanged;
            this.Controls.Add(_cmbColorMode);
            y += 35;

            // === SLIDERS ===
            var lblSliders = new Label
            {
                Text = "─── Adjustments ───",
                Location = new Point(10, y),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            this.Controls.Add(lblSliders);
            y += 20;

            // Wireframe Alpha
            var lblWire = new Label
            {
                Text = "Wire Alpha:",
                Location = new Point(10, y),
                AutoSize = true,
                ForeColor = Color.White
            };
            this.Controls.Add(lblWire);

            _lblWireAlpha = new Label
            {
                Text = "60",
                Location = new Point(180, y),
                AutoSize = true,
                ForeColor = Color.LightGray
            };
            this.Controls.Add(_lblWireAlpha);
            y += 18;

            _trkWireAlpha = new TrackBar
            {
                Location = new Point(10, y),
                Size = new Size(200, 30),
                Minimum = 0,
                Maximum = 255,
                Value = 60,
                TickFrequency = 25,
                BackColor = Color.FromArgb(37, 37, 38)
            };
            _trkWireAlpha.ValueChanged += (s, e) =>
            {
                _lblWireAlpha.Text = _trkWireAlpha.Value.ToString();
                WireframeAlphaChanged?.Invoke(this, _trkWireAlpha.Value);
            };
            this.Controls.Add(_trkWireAlpha);
            y += 40;

            // Fog Density
            var lblFogLabel = new Label
            {
                Text = "Fog Density:",
                Location = new Point(10, y),
                AutoSize = true,
                ForeColor = Color.White
            };
            this.Controls.Add(lblFogLabel);

            _lblFog = new Label
            {
                Text = "0",
                Location = new Point(180, y),
                AutoSize = true,
                ForeColor = Color.LightGray
            };
            this.Controls.Add(_lblFog);
            y += 18;

            _trkFog = new TrackBar
            {
                Location = new Point(10, y),
                Size = new Size(200, 30),
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                TickFrequency = 10,
                BackColor = Color.FromArgb(37, 37, 38)
            };
            _trkFog.ValueChanged += (s, e) =>
            {
                _lblFog.Text = _trkFog.Value.ToString();
                FogDensityChanged?.Invoke(this, _trkFog.Value);
            };
            this.Controls.Add(_trkFog);
            y += 50;

            // === INFO ===
            var lblInfo = new Label
            {
                Text = "Keyboard Shortcuts:\n" +
                       "W - Toggle Wireframe\n" +
                       "L - Toggle Lighting\n" +
                       "R - Reset Camera\n" +
                       "B - Blackspot Mode\n" +
                       "J - Jump Link Mode\n" +
                       "V - Volume Mode\n" +
                       "Q - Navigation Mode",
                Location = new Point(10, y),
                Size = new Size(210, 130),
                ForeColor = Color.Gray
            };
            this.Controls.Add(lblInfo);
        }

        private CheckBox CreateCheckBox(string text, int y, bool isChecked)
        {
            return new CheckBox
            {
                Text = text,
                Location = new Point(20, y),
                Size = new Size(180, 20),
                ForeColor = Color.White,
                Checked = isChecked
            };
        }

        private void CmbColorMode_SelectedIndexChanged(object? sender, EventArgs e)
        {
            ColorMode mode = _cmbColorMode.SelectedIndex switch
            {
                0 => ColorMode.ByAreaType,
                1 => ColorMode.ByHeight,
                2 => ColorMode.ByPolygon,
                3 => ColorMode.Flat,
                _ => ColorMode.ByAreaType
            };
            ColorModeChanged?.Invoke(this, mode);
        }

        // Propriétés publiques pour synchronisation
        public bool ShowWireframe
        {
            get => _chkWireframe.Checked;
            set => _chkWireframe.Checked = value;
        }

        public bool ShowLighting
        {
            get => _chkLighting.Checked;
            set => _chkLighting.Checked = value;
        }

        public bool ShowOffMesh
        {
            get => _chkOffMesh.Checked;
            set => _chkOffMesh.Checked = value;
        }

        public bool ShowBlackspots
        {
            get => _chkBlackspots.Checked;
            set => _chkBlackspots.Checked = value;
        }

        public bool ShowVolumes
        {
            get => _chkVolumes.Checked;
            set => _chkVolumes.Checked = value;
        }
    }
}
