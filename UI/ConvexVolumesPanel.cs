using System;
using System.Drawing;
using System.Windows.Forms;
using MeshViewer3D.Core;
using MeshViewer3D.Data;

namespace MeshViewer3D.UI
{
    /// <summary>
    /// Panel d'édition des Convex Volumes
    /// Comme dans Tripper.Renderer de Honorbuddy
    /// </summary>
    public class ConvexVolumesPanel : UserControl
    {
        private EditableElements _elements;
        
        // UI Controls
        private ListBox _listBox;
        private Button _btnAdd;
        private Button _btnRemove;
        private Button _btnClear;
        private Button _btnClickToPlace;
        private GroupBox _propertiesGroup;
        private ComboBox _cmbAreaType;
        private NumericUpDown _nudMinHeight;
        private NumericUpDown _nudMaxHeight;
        private Label _lblVertexCount;
        private Button _btnApply;
        
        // État
        private bool _isClickMode = false;

        // Événements
        public event EventHandler? VolumesChanged;
        public event EventHandler<bool>? ClickModeToggled;

        public ConvexVolumesPanel(EditableElements elements)
        {
            _elements = elements;
            SetupUI();
        }

        private void SetupUI()
        {
            this.BackColor = Color.FromArgb(37, 37, 38);
            this.Dock = DockStyle.Fill;

            int y = 10;

            // Instruction
            var lblInstruction = new Label
            {
                Text = "Convex Volumes\n\n" +
                       "Define custom area types\n" +
                       "for specific regions.\n\n" +
                       "V: Toggle placement mode\n" +
                       "Click: Add vertex\n" +
                       "Enter: Close polygon",
                Location = new Point(10, y),
                Size = new Size(210, 100),
                ForeColor = Color.LightGray
            };
            this.Controls.Add(lblInstruction);
            y += 105;

            // ListBox
            _listBox = new ListBox
            {
                Location = new Point(10, y),
                Size = new Size(210, 80),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _listBox.SelectedIndexChanged += ListBox_SelectedIndexChanged;
            this.Controls.Add(_listBox);
            y += 85;

            // Boutons
            var btnPanel = new FlowLayoutPanel
            {
                Location = new Point(10, y),
                Size = new Size(210, 30),
                FlowDirection = FlowDirection.LeftToRight
            };

            _btnAdd = CreateButton("Add", 50);
            _btnAdd.Click += BtnAdd_Click;
            btnPanel.Controls.Add(_btnAdd);

            _btnRemove = CreateButton("Remove", 60);
            _btnRemove.Click += BtnRemove_Click;
            btnPanel.Controls.Add(_btnRemove);

            _btnClear = CreateButton("Clear", 50);
            _btnClear.Click += BtnClear_Click;
            btnPanel.Controls.Add(_btnClear);

            this.Controls.Add(btnPanel);
            y += 35;

            // Bouton Click to Place
            _btnClickToPlace = new Button
            {
                Text = "Click to Draw Volume",
                Location = new Point(10, y),
                Size = new Size(210, 28),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false // Coming soon
            };
            _btnClickToPlace.Click += BtnClickToPlace_Click;
            this.Controls.Add(_btnClickToPlace);
            y += 35;

            // Properties Group
            _propertiesGroup = new GroupBox
            {
                Text = "Properties",
                Location = new Point(5, y),
                Size = new Size(220, 150),
                ForeColor = Color.White
            };

            int py = 20;

            // Area Type
            AddLabel(_propertiesGroup, "Area Type:", 10, py);
            _cmbAreaType = new ComboBox
            {
                Location = new Point(80, py - 3),
                Size = new Size(125, 21),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White
            };
            _cmbAreaType.Items.AddRange(new object[] 
            { 
                "Ground (0)", 
                "Water (1)", 
                "Magma/Slime (2)",
                "Steep Ground (3)",
                "Blocked (63)"
            });
            _cmbAreaType.SelectedIndex = 0;
            _propertiesGroup.Controls.Add(_cmbAreaType);
            py += 28;

            // Min Height
            AddLabel(_propertiesGroup, "Min Height:", 10, py);
            _nudMinHeight = AddNumeric(_propertiesGroup, 90, py, -1000, 1000, 0, 1);
            py += 25;

            // Max Height
            AddLabel(_propertiesGroup, "Max Height:", 10, py);
            _nudMaxHeight = AddNumeric(_propertiesGroup, 90, py, -1000, 1000, 50, 1);
            py += 25;

            // Vertex Count
            _lblVertexCount = new Label
            {
                Text = "Vertices: 0",
                Location = new Point(10, py),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            _propertiesGroup.Controls.Add(_lblVertexCount);
            py += 25;

            // Apply Button
            _btnApply = new Button
            {
                Text = "Apply Changes",
                Location = new Point(10, py),
                Size = new Size(195, 25),
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnApply.Click += BtnApply_Click;
            _propertiesGroup.Controls.Add(_btnApply);

            this.Controls.Add(_propertiesGroup);
        }

        private Button CreateButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Size = new Size(width, 25),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
        }

        private void AddLabel(Control parent, string text, int x, int y)
        {
            var lbl = new Label
            {
                Text = text,
                Location = new Point(x, y + 3),
                AutoSize = true,
                ForeColor = Color.White
            };
            parent.Controls.Add(lbl);
        }

        private NumericUpDown AddNumeric(Control parent, int x, int y, decimal min, decimal max, decimal value = 0, int decimals = 1)
        {
            var nud = new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(70, 20),
                Minimum = min,
                Maximum = max,
                Value = Math.Clamp(value, min, max),
                DecimalPlaces = decimals,
                Increment = 1m,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White
            };
            parent.Controls.Add(nud);
            return nud;
        }

        private void ListBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_listBox.SelectedIndex >= 0 && _listBox.SelectedIndex < _elements.ConvexVolumes.Count)
            {
                var vol = _elements.ConvexVolumes[_listBox.SelectedIndex];
                
                _cmbAreaType.SelectedIndex = vol.AreaType switch
                {
                    Data.AreaType.Ground => 0,
                    Data.AreaType.Water => 1,
                    Data.AreaType.MagmaSlime => 2,
                    Data.AreaType.GroundSteep => 3,
                    Data.AreaType.Unwalkable => 4,
                    _ => 0
                };
                
                _nudMinHeight.Value = (decimal)vol.MinHeight;
                _nudMaxHeight.Value = (decimal)vol.MaxHeight;
                _lblVertexCount.Text = $"Vertices: {vol.Vertices.Count}";
                
                _elements.SelectedType = EditableElementType.ConvexVolume;
                _elements.SelectedVolumeIndex = _listBox.SelectedIndex;
                VolumesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            // Créer un volume par défaut (carré)
            var vol = new ConvexVolume
            {
                AreaType = Data.AreaType.Ground,
                MinHeight = 0,
                MaxHeight = 50
            };
            vol.Vertices.Add(new OpenTK.Mathematics.Vector3(0, 0, 0));
            vol.Vertices.Add(new OpenTK.Mathematics.Vector3(10, 0, 0));
            vol.Vertices.Add(new OpenTK.Mathematics.Vector3(10, 0, 10));
            vol.Vertices.Add(new OpenTK.Mathematics.Vector3(0, 0, 10));
            
            _elements.ConvexVolumes.Add(vol);
            RefreshList();
            _listBox.SelectedIndex = _elements.ConvexVolumes.Count - 1;
            VolumesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BtnRemove_Click(object? sender, EventArgs e)
        {
            if (_listBox.SelectedIndex >= 0)
            {
                _elements.ConvexVolumes.RemoveAt(_listBox.SelectedIndex);
                RefreshList();
                VolumesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnClear_Click(object? sender, EventArgs e)
        {
            if (MessageBox.Show("Clear all convex volumes?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                _elements.ConvexVolumes.Clear();
                RefreshList();
                VolumesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnClickToPlace_Click(object? sender, EventArgs e)
        {
            _isClickMode = !_isClickMode;
            _btnClickToPlace.BackColor = _isClickMode 
                ? Color.FromArgb(0, 122, 204) 
                : Color.FromArgb(60, 60, 60);
            ClickModeToggled?.Invoke(this, _isClickMode);
        }

        private void BtnApply_Click(object? sender, EventArgs e)
        {
            if (_listBox.SelectedIndex >= 0 && _listBox.SelectedIndex < _elements.ConvexVolumes.Count)
            {
                var vol = _elements.ConvexVolumes[_listBox.SelectedIndex];
                
                vol.AreaType = _cmbAreaType.SelectedIndex switch
                {
                    0 => Data.AreaType.Ground,
                    1 => Data.AreaType.Water,
                    2 => Data.AreaType.MagmaSlime,
                    3 => Data.AreaType.GroundSteep,
                    4 => Data.AreaType.Unwalkable,
                    _ => Data.AreaType.Ground
                };
                
                vol.MinHeight = (float)_nudMinHeight.Value;
                vol.MaxHeight = (float)_nudMaxHeight.Value;
                
                _elements.ConvexVolumes[_listBox.SelectedIndex] = vol;
                RefreshList();
                VolumesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void RefreshList()
        {
            _listBox.Items.Clear();
            for (int i = 0; i < _elements.ConvexVolumes.Count; i++)
            {
                var vol = _elements.ConvexVolumes[i];
                string areaName = vol.AreaType.ToString();
                _listBox.Items.Add($"Volume {i + 1} ({areaName}, {vol.Vertices.Count}v)");
            }
        }

        public void UpdateElements(EditableElements elements)
        {
            _elements = elements;
            RefreshList();
        }
    }
}
