// ============================================================================
//  PerModelPanel.cs  —  Per-Model volume/settings override panel
//
//  Lets users define custom navigation overrides for specific WMO/M2 models.
//  Similar to Honorbuddy's per-model override system.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace MeshViewer3D.UI
{
    /// <summary>
    /// Per-model override settings: ignore collision, custom area type, scale override.
    /// </summary>
    public class ModelOverride
    {
        public string ModelName { get; set; } = "";
        public bool IgnoreCollision { get; set; }
        public string AreaType { get; set; } = "None";
        public float ScaleOverride { get; set; }

        public override string ToString()
        {
            var parts = new List<string>();
            if (IgnoreCollision) parts.Add("ignore=true");
            if (AreaType != "None") parts.Add($"area={AreaType}");
            if (ScaleOverride > 0) parts.Add($"scale={ScaleOverride:F2}");
            return $"{ModelName}: {(parts.Count > 0 ? string.Join(", ", parts) : "(default)")}";
        }
    }

    /// <summary>
    /// Panel for defining per-model navigation overrides.
    /// </summary>
    public class PerModelPanel : UserControl
    {
        // ── Controls ──────────────────────────────────────────────────────────
        private ComboBox _cmbModel = null!;
        private CheckBox _chkIgnoreCollision = null!;
        private ComboBox _cmbAreaType = null!;
        private NumericUpDown _numScale = null!;
        private ListBox _listOverrides = null!;
        private Button _btnAdd = null!;
        private Button _btnRemove = null!;
        private Button _btnExport = null!;
        private Button _btnImport = null!;
        private Label _lblCount = null!;

        // ── State ─────────────────────────────────────────────────────────────
        private readonly List<ModelOverride> _overrides = new();
        private static readonly string[] AreaTypes = { "None", "Water", "Road", "Grass", "Magma", "Slime", "Blocked" };

        // ── Constructor ───────────────────────────────────────────────────────

        public PerModelPanel()
        {
            this.BackColor = Color.FromArgb(37, 37, 38);
            this.Dock = DockStyle.Fill;
            SetupUI();
        }

        // ── UI setup ──────────────────────────────────────────────────────────

        private void SetupUI()
        {
            int y = 10;

            // Header
            var lblHeader = new Label
            {
                Text = "Per-Model Overrides",
                Location = new Point(10, y),
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblHeader);
            y += 24;

            // Model selector
            var lblModel = new Label
            {
                Text = "Model:",
                Location = new Point(10, y),
                Size = new Size(50, 20),
                ForeColor = Color.White
            };
            Controls.Add(lblModel);

            _cmbModel = new ComboBox
            {
                Location = new Point(65, y),
                Size = new Size(160, 22),
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat
            };
            Controls.Add(_cmbModel);
            y += 28;

            // Separator
            var sep1 = new Label
            {
                Location = new Point(10, y),
                Size = new Size(215, 1),
                BackColor = Color.FromArgb(65, 65, 65)
            };
            Controls.Add(sep1);
            y += 8;

            // Ignore Collision checkbox
            _chkIgnoreCollision = new CheckBox
            {
                Text = "Ignore Collision",
                Location = new Point(10, y),
                Size = new Size(215, 20),
                ForeColor = Color.LightGray
            };
            Controls.Add(_chkIgnoreCollision);
            y += 24;

            // Custom Area Type
            var lblArea = new Label
            {
                Text = "Area Type:",
                Location = new Point(10, y),
                Size = new Size(70, 20),
                ForeColor = Color.White
            };
            Controls.Add(lblArea);

            _cmbAreaType = new ComboBox
            {
                Location = new Point(85, y),
                Size = new Size(140, 22),
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat
            };
            _cmbAreaType.Items.AddRange(AreaTypes);
            _cmbAreaType.SelectedIndex = 0;
            Controls.Add(_cmbAreaType);
            y += 28;

            // Scale Override
            var lblScale = new Label
            {
                Text = "Scale:",
                Location = new Point(10, y),
                Size = new Size(70, 20),
                ForeColor = Color.White
            };
            Controls.Add(lblScale);

            _numScale = new NumericUpDown
            {
                Location = new Point(85, y),
                Size = new Size(140, 20),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                Minimum = 0,
                Maximum = 100,
                DecimalPlaces = 2,
                Increment = 0.1m,
                Value = 0,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_numScale);
            y += 26;

            // Scale hint
            var lblScaleHint = new Label
            {
                Text = "(0 = use default scale)",
                Location = new Point(85, y),
                AutoSize = true,
                ForeColor = Color.FromArgb(140, 140, 140),
                Font = new Font(Font.FontFamily, 7.5f)
            };
            Controls.Add(lblScaleHint);
            y += 18;

            // Add / Remove buttons
            _btnAdd = CreateButton("Add Override", 10, y);
            _btnAdd.Size = new Size(105, 25);
            _btnAdd.Click += OnAddOverride;
            Controls.Add(_btnAdd);

            _btnRemove = CreateButton("Remove", 120, y);
            _btnRemove.Size = new Size(105, 25);
            _btnRemove.Click += OnRemoveOverride;
            Controls.Add(_btnRemove);
            y += 32;

            // Separator
            var sep2 = new Label
            {
                Location = new Point(10, y),
                Size = new Size(215, 1),
                BackColor = Color.FromArgb(65, 65, 65)
            };
            Controls.Add(sep2);
            y += 8;

            // Overrides list header
            var lblOverrides = new Label
            {
                Text = "Configured Overrides:",
                Location = new Point(10, y),
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblOverrides);

            _lblCount = new Label
            {
                Text = "(0)",
                Location = new Point(170, y),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            Controls.Add(_lblCount);
            y += 22;

            // Overrides ListBox
            _listOverrides = new ListBox
            {
                Location = new Point(10, y),
                Size = new Size(215, 110),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                SelectionMode = SelectionMode.One,
                IntegralHeight = false
            };
            _listOverrides.SelectedIndexChanged += OnOverrideSelected;
            Controls.Add(_listOverrides);
            y += 118;

            // Export / Import
            _btnExport = CreateButton("Export", 10, y);
            _btnExport.Size = new Size(105, 25);
            _btnExport.Click += OnExport;
            Controls.Add(_btnExport);

            _btnImport = CreateButton("Import", 120, y);
            _btnImport.Size = new Size(105, 25);
            _btnImport.Click += OnImport;
            Controls.Add(_btnImport);
        }

        private Button CreateButton(string text, int x, int y)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(75, 25),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            return btn;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Populates the model dropdown from WMO and M2 name arrays.
        /// </summary>
        public void LoadModelNames(string[] wmoNames, string[] m2Names)
        {
            _cmbModel.Items.Clear();

            var allNames = (wmoNames ?? Array.Empty<string>())
                .Concat(m2Names ?? Array.Empty<string>())
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => Path.GetFileName(n))
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            foreach (var name in allNames)
                _cmbModel.Items.Add(name);

            if (_cmbModel.Items.Count > 0)
                _cmbModel.SelectedIndex = 0;

            Console.WriteLine($"[PerModelPanel] Loaded {allNames.Count} model names");
        }

        /// <summary>Clears everything.</summary>
        public void Clear()
        {
            _cmbModel.Items.Clear();
            _overrides.Clear();
            RefreshOverrideList();
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnAddOverride(object? sender, EventArgs e)
        {
            if (_cmbModel.SelectedItem == null)
            {
                MessageBox.Show("Select a model first.", "Add Override", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string modelName = _cmbModel.SelectedItem.ToString()!;

            // Check duplicate
            var existing = _overrides.FindIndex(o => o.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                // Update existing
                _overrides[existing].IgnoreCollision = _chkIgnoreCollision.Checked;
                _overrides[existing].AreaType = _cmbAreaType.SelectedItem?.ToString() ?? "None";
                _overrides[existing].ScaleOverride = (float)_numScale.Value;
                Console.WriteLine($"[PerModelPanel] Updated override for '{modelName}'");
            }
            else
            {
                _overrides.Add(new ModelOverride
                {
                    ModelName = modelName,
                    IgnoreCollision = _chkIgnoreCollision.Checked,
                    AreaType = _cmbAreaType.SelectedItem?.ToString() ?? "None",
                    ScaleOverride = (float)_numScale.Value
                });
                Console.WriteLine($"[PerModelPanel] Added override for '{modelName}'");
            }

            RefreshOverrideList();
        }

        private void OnRemoveOverride(object? sender, EventArgs e)
        {
            if (_listOverrides.SelectedIndex < 0) return;

            int idx = _listOverrides.SelectedIndex;
            string name = _overrides[idx].ModelName;
            _overrides.RemoveAt(idx);
            RefreshOverrideList();
            Console.WriteLine($"[PerModelPanel] Removed override for '{name}'");
        }

        private void OnOverrideSelected(object? sender, EventArgs e)
        {
            if (_listOverrides.SelectedIndex < 0) return;

            var ov = _overrides[_listOverrides.SelectedIndex];
            // Select the model in combobox if it exists
            for (int i = 0; i < _cmbModel.Items.Count; i++)
            {
                if (_cmbModel.Items[i]?.ToString()?.Equals(ov.ModelName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    _cmbModel.SelectedIndex = i;
                    break;
                }
            }
            _chkIgnoreCollision.Checked = ov.IgnoreCollision;
            for (int i = 0; i < _cmbAreaType.Items.Count; i++)
            {
                if (_cmbAreaType.Items[i]?.ToString() == ov.AreaType)
                {
                    _cmbAreaType.SelectedIndex = i;
                    break;
                }
            }
            _numScale.Value = (decimal)ov.ScaleOverride;
        }

        private void OnExport(object? sender, EventArgs e)
        {
            if (_overrides.Count == 0)
            {
                MessageBox.Show("No overrides configured.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dlg = new SaveFileDialog
            {
                Filter = "JSON files|*.json|All files|*.*",
                Title = "Export Per-Model Overrides",
                FileName = "model_overrides.json"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                var json = JsonSerializer.Serialize(_overrides, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dlg.FileName, json);
                Console.WriteLine($"[PerModelPanel] Exported {_overrides.Count} overrides to {dlg.FileName}");
            }
        }

        private void OnImport(object? sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "JSON files|*.json|All files|*.*",
                Title = "Import Per-Model Overrides"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            try
            {
                var json = File.ReadAllText(dlg.FileName);
                var imported = JsonSerializer.Deserialize<List<ModelOverride>>(json);
                if (imported == null || imported.Count == 0)
                {
                    MessageBox.Show("No overrides found in file.", "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _overrides.Clear();
                _overrides.AddRange(imported);
                RefreshOverrideList();
                Console.WriteLine($"[PerModelPanel] Imported {_overrides.Count} overrides from {dlg.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import: {ex.Message}", "Import Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshOverrideList()
        {
            _listOverrides.BeginUpdate();
            _listOverrides.Items.Clear();
            foreach (var ov in _overrides)
                _listOverrides.Items.Add(ov.ToString());
            _listOverrides.EndUpdate();
            _lblCount.Text = $"({_overrides.Count})";
        }
    }
}
