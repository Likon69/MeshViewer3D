using System;
using System.Drawing;
using System.Windows.Forms;
using MeshViewer3D.Core;
using MeshViewer3D.Data;
using OpenTK.Mathematics;

namespace MeshViewer3D.UI
{
    /// <summary>
    /// Panel d'édition des Jump Links / OffMesh Connections
    /// Comme dans Tripper.Renderer de Honorbuddy
    /// </summary>
    public class JumpLinksPanel : UserControl
    {
        private EditableElements _elements;
        
        // UI Controls
        private ListBox _listBox;
        private Button _btnAdd;
        private Button _btnRemove;
        private Button _btnClear;
        private Button _btnClickToPlace;
        private GroupBox _propertiesGroup;
        private NumericUpDown _nudStartX, _nudStartY, _nudStartZ;
        private NumericUpDown _nudEndX, _nudEndY, _nudEndZ;
        private NumericUpDown _nudRadius;
        private CheckBox _chkBidirectional;
        private ComboBox _cmbType;
        private Button _btnApply;
        
        // État
        private bool _isClickMode = false;
        private bool _isPlacingStart = true; // true = placer Start, false = placer End
        private Vector3? _pendingStart = null;

        // Événements
        public event EventHandler? JumpLinksChanged;
        public event EventHandler<bool>? ClickModeToggled;
        public event EventHandler<(Vector3 start, Vector3 end, bool bidirectional)>? JumpLinkCreated;

        public JumpLinksPanel(EditableElements elements)
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
                Text = "Jump Links / OffMesh Connections\n\n" +
                       "Ctrl+Click: Place Start point\n" +
                       "Ctrl+Click: Place End point\n" +
                       "Tab: Toggle bidirectional",
                Location = new Point(10, y),
                Size = new Size(210, 80),
                ForeColor = Color.LightGray
            };
            this.Controls.Add(lblInstruction);
            y += 85;

            // ListBox
            _listBox = new ListBox
            {
                Location = new Point(10, y),
                Size = new Size(210, 100),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _listBox.SelectedIndexChanged += ListBox_SelectedIndexChanged;
            this.Controls.Add(_listBox);
            y += 105;

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
                Text = "Click to Place Start",
                Location = new Point(10, y),
                Size = new Size(210, 28),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnClickToPlace.Click += BtnClickToPlace_Click;
            this.Controls.Add(_btnClickToPlace);
            y += 35;

            // Properties Group
            _propertiesGroup = new GroupBox
            {
                Text = "Properties",
                Location = new Point(5, y),
                Size = new Size(220, 220),
                ForeColor = Color.White
            };

            int py = 20;

            // Start Point
            AddLabel(_propertiesGroup, "Start:", 10, py);
            _nudStartX = AddNumeric(_propertiesGroup, 50, py, -20000, 20000);
            _nudStartY = AddNumeric(_propertiesGroup, 105, py, -20000, 20000);
            _nudStartZ = AddNumeric(_propertiesGroup, 160, py, -20000, 20000);
            py += 25;

            // End Point
            AddLabel(_propertiesGroup, "End:", 10, py);
            _nudEndX = AddNumeric(_propertiesGroup, 50, py, -20000, 20000);
            _nudEndY = AddNumeric(_propertiesGroup, 105, py, -20000, 20000);
            _nudEndZ = AddNumeric(_propertiesGroup, 160, py, -20000, 20000);
            py += 25;

            // Radius
            AddLabel(_propertiesGroup, "Radius:", 10, py);
            _nudRadius = AddNumeric(_propertiesGroup, 70, py, 0.1m, 50m, 1m, 1);
            py += 25;

            // Bidirectional
            _chkBidirectional = new CheckBox
            {
                Text = "Bidirectional (↔)",
                Location = new Point(10, py),
                Size = new Size(150, 20),
                ForeColor = Color.White,
                Checked = true
            };
            _propertiesGroup.Controls.Add(_chkBidirectional);
            py += 25;

            // Type
            AddLabel(_propertiesGroup, "Type:", 10, py);
            _cmbType = new ComboBox
            {
                Location = new Point(70, py - 3),
                Size = new Size(130, 21),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White
            };
            _cmbType.Items.AddRange(new object[] { "Normal", "Elevator", "Portal", "Boat", "Jump" });
            _cmbType.SelectedIndex = 0;
            _propertiesGroup.Controls.Add(_cmbType);
            py += 30;

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
                Size = new Size(50, 20),
                Minimum = min,
                Maximum = max,
                Value = Math.Clamp(value, min, max),
                DecimalPlaces = decimals,
                Increment = 0.1m,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White
            };
            parent.Controls.Add(nud);
            return nud;
        }

        private void ListBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_listBox.SelectedIndex >= 0 && _listBox.SelectedIndex < _elements.CustomOffMeshConnections.Count)
            {
                var conn = _elements.CustomOffMeshConnections[_listBox.SelectedIndex];
                
                _nudStartX.Value = (decimal)conn.Start.X;
                _nudStartY.Value = (decimal)conn.Start.Y;
                _nudStartZ.Value = (decimal)conn.Start.Z;
                
                _nudEndX.Value = (decimal)conn.End.X;
                _nudEndY.Value = (decimal)conn.End.Y;
                _nudEndZ.Value = (decimal)conn.End.Z;
                
                _nudRadius.Value = (decimal)conn.Radius;
                _chkBidirectional.Checked = conn.IsBidirectional;
                
                _elements.SelectedType = EditableElementType.OffMeshConnection;
                _elements.SelectedOffMeshIndex = _listBox.SelectedIndex;
                JumpLinksChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            // Ajouter une connexion par défaut
            var conn = new OffMeshConnection
            {
                Start = new Vector3(0, 0, 0),
                End = new Vector3(10, 0, 0),
                Radius = 1.0f,
                Flags = 1 // Bidirectional
            };
            _elements.CustomOffMeshConnections.Add(conn);
            RefreshList();
            _listBox.SelectedIndex = _elements.CustomOffMeshConnections.Count - 1;
            JumpLinksChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BtnRemove_Click(object? sender, EventArgs e)
        {
            if (_listBox.SelectedIndex >= 0)
            {
                _elements.CustomOffMeshConnections.RemoveAt(_listBox.SelectedIndex);
                RefreshList();
                JumpLinksChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnClear_Click(object? sender, EventArgs e)
        {
            if (MessageBox.Show("Clear all jump links?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                _elements.CustomOffMeshConnections.Clear();
                RefreshList();
                JumpLinksChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnClickToPlace_Click(object? sender, EventArgs e)
        {
            _isClickMode = !_isClickMode;
            if (_isClickMode)
            {
                _isPlacingStart = true;
                _pendingStart = null;
                _btnClickToPlace.Text = "Click to Place Start";
                _btnClickToPlace.BackColor = Color.FromArgb(0, 122, 204);
            }
            else
            {
                _btnClickToPlace.Text = "Click to Place Start";
                _btnClickToPlace.BackColor = Color.FromArgb(60, 60, 60);
            }
            ClickModeToggled?.Invoke(this, _isClickMode);
        }

        private void BtnApply_Click(object? sender, EventArgs e)
        {
            if (_listBox.SelectedIndex >= 0 && _listBox.SelectedIndex < _elements.CustomOffMeshConnections.Count)
            {
                var conn = _elements.CustomOffMeshConnections[_listBox.SelectedIndex];
                
                conn.Start = new Vector3((float)_nudStartX.Value, (float)_nudStartY.Value, (float)_nudStartZ.Value);
                conn.End = new Vector3((float)_nudEndX.Value, (float)_nudEndY.Value, (float)_nudEndZ.Value);
                conn.Radius = (float)_nudRadius.Value;
                conn.Flags = _chkBidirectional.Checked ? (byte)1 : (byte)0;
                
                _elements.CustomOffMeshConnections[_listBox.SelectedIndex] = conn;
                RefreshList();
                JumpLinksChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Appelé quand l'utilisateur clique sur le mesh en mode placement
        /// </summary>
        public void OnWorldClick(Vector3 worldPos)
        {
            if (!_isClickMode) return;

            if (_isPlacingStart)
            {
                _pendingStart = worldPos;
                _isPlacingStart = false;
                _btnClickToPlace.Text = "Click to Place End";
            }
            else if (_pendingStart.HasValue)
            {
                // Créer la connexion
                var conn = new OffMeshConnection
                {
                    Start = _pendingStart.Value,
                    End = worldPos,
                    Radius = 1.0f,
                    Flags = _chkBidirectional.Checked ? (byte)1 : (byte)0
                };
                _elements.CustomOffMeshConnections.Add(conn);
                RefreshList();
                
                // Notifier
                JumpLinkCreated?.Invoke(this, (_pendingStart.Value, worldPos, _chkBidirectional.Checked));
                JumpLinksChanged?.Invoke(this, EventArgs.Empty);
                
                // Reset pour prochain
                _isPlacingStart = true;
                _pendingStart = null;
                _btnClickToPlace.Text = "Click to Place Start";
            }
        }

        /// <summary>
        /// Point de départ en cours (pour rendu preview)
        /// </summary>
        public Vector3? PendingStartPoint => _pendingStart;
        
        /// <summary>
        /// Mode placement actif?
        /// </summary>
        public bool IsClickMode => _isClickMode;

        public void RefreshList()
        {
            _listBox.Items.Clear();
            for (int i = 0; i < _elements.CustomOffMeshConnections.Count; i++)
            {
                var conn = _elements.CustomOffMeshConnections[i];
                string dir = conn.IsBidirectional ? "↔" : "→";
                float dist = conn.GetDistance();
                _listBox.Items.Add($"JumpLink {i + 1} {dir} ({dist:F1}yd)");
            }
        }

        public void UpdateElements(EditableElements elements)
        {
            _elements = elements;
            RefreshList();
        }
    }
}
