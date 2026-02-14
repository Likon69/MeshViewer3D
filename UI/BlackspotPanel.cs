using System;
using System.Drawing;
using System.Windows.Forms;
using MeshViewer3D.Core;
using MeshViewer3D.Data;
using OpenTK.Mathematics;

namespace MeshViewer3D.UI
{
    /// <summary>
    /// Panel d'édition des blackspots
    /// Permet de créer, modifier, et supprimer des blackspots
    /// </summary>
    public class BlackspotPanel : Panel
    {
        private EditableElements _elements;
        private ListBox? _blackspotList;
        private Button? _btnAdd;
        private Button? _btnRemove;
        private Button? _btnClear;
        
        // Propriétés du blackspot sélectionné
        private NumericUpDown? _numX;
        private NumericUpDown? _numY;
        private NumericUpDown? _numZ;
        private NumericUpDown? _numRadius;
        private NumericUpDown? _numHeight;
        private TextBox? _txtName;

        public event EventHandler? BlackspotsChanged;
        public event EventHandler<bool>? ClickModeToggled;
        
        private bool _clickToPlaceMode = false;

        public BlackspotPanel(EditableElements elements)
        {
            _elements = elements;
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.Dock = DockStyle.Fill;
            this.BackColor = Color.FromArgb(37, 37, 38);
            this.Padding = new Padding(5);

            int yPos = 5;

            // Liste des blackspots
            var lblList = new Label
            {
                Text = "Blackspots:",
                Location = new Point(5, yPos),
                Size = new Size(150, 20),
                ForeColor = Color.White
            };
            this.Controls.Add(lblList);
            yPos += 25;

            _blackspotList = new ListBox
            {
                Location = new Point(5, yPos),
                Size = new Size(240, 150),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White
            };
            _blackspotList.SelectedIndexChanged += BlackspotList_SelectedIndexChanged;
            this.Controls.Add(_blackspotList);
            yPos += 155;

            // Boutons
            _btnAdd = CreateButton("Add", 5, yPos, OnAddBlackspot);
            _btnRemove = CreateButton("Remove", 85, yPos, OnRemoveBlackspot);
            _btnClear = CreateButton("Clear", 165, yPos, OnClearBlackspots);
            this.Controls.AddRange(new Control[] { _btnAdd, _btnRemove, _btnClear });
            yPos += 30;
            
            // Bouton Click to Place
            var btnClickPlace = CreateButton("Click to Place", 5, yPos, OnToggleClickMode);
            btnClickPlace.Size = new Size(240, 25);
            btnClickPlace.BackColor = Color.FromArgb(45, 45, 48);
            this.Controls.Add(btnClickPlace);
            yPos += 30;

            // Séparateur
            var separator = new Label
            {
                Location = new Point(5, yPos),
                Size = new Size(240, 2),
                BackColor = Color.FromArgb(60, 60, 60)
            };
            this.Controls.Add(separator);
            yPos += 10;

            // Propriétés
            var lblProps = new Label
            {
                Text = "Properties:",
                Location = new Point(5, yPos),
                Size = new Size(150, 20),
                ForeColor = Color.White
            };
            this.Controls.Add(lblProps);
            yPos += 25;

            // Nom
            yPos = AddTextProperty("Name:", ref _txtName, yPos);

            // Position
            yPos = AddNumericProperty("X:", ref _numX, yPos, -20000, 20000, 0);
            yPos = AddNumericProperty("Y:", ref _numY, yPos, -20000, 20000, 0);
            yPos = AddNumericProperty("Z:", ref _numZ, yPos, -20000, 20000, 0);

            // Dimensions
            yPos = AddNumericProperty("Radius:", ref _numRadius, yPos, 1, 500, 10);
            yPos = AddNumericProperty("Height:", ref _numHeight, yPos, 1, 200, 10);

            // Bouton Apply
            var btnApply = CreateButton("Apply Changes", 5, yPos, OnApplyChanges);
            btnApply.Size = new Size(240, 25);
            this.Controls.Add(btnApply);

            RefreshList();
        }

        private Button CreateButton(string text, int x, int y, EventHandler handler)
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
            btn.Click += handler;
            return btn;
        }

        private int AddTextProperty(string label, ref TextBox textBox, int yPos)
        {
            var lbl = new Label
            {
                Text = label,
                Location = new Point(5, yPos),
                Size = new Size(70, 20),
                ForeColor = Color.White
            };
            this.Controls.Add(lbl);

            textBox = new TextBox
            {
                Location = new Point(80, yPos),
                Size = new Size(165, 20),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White
            };
            this.Controls.Add(textBox);

            return yPos + 25;
        }

        private int AddNumericProperty(string label, ref NumericUpDown numericBox, int yPos, decimal min, decimal max, decimal defaultValue)
        {
            var lbl = new Label
            {
                Text = label,
                Location = new Point(5, yPos),
                Size = new Size(70, 20),
                ForeColor = Color.White
            };
            this.Controls.Add(lbl);

            numericBox = new NumericUpDown
            {
                Location = new Point(80, yPos),
                Size = new Size(165, 20),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                Minimum = min,
                Maximum = max,
                DecimalPlaces = 1,
                Value = defaultValue,
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(numericBox);

            return yPos + 25;
        }

        private void RefreshList()
        {
            if (_blackspotList == null) return;
            
            _blackspotList.Items.Clear();
            for (int i = 0; i < _elements.Blackspots.Count; i++)
            {
                var bs = _elements.Blackspots[i];
                string displayName = string.IsNullOrEmpty(bs.Name) ? $"Blackspot #{i + 1}" : bs.Name;
                _blackspotList.Items.Add($"{displayName} (R={bs.Radius:F0})");
            }
        }

        private void BlackspotList_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_blackspotList == null || _blackspotList.SelectedIndex < 0 || _blackspotList.SelectedIndex >= _elements.Blackspots.Count)
                return;
            if (_txtName == null || _numX == null || _numY == null || _numZ == null || _numRadius == null || _numHeight == null)
                return;

            var bs = _elements.Blackspots[_blackspotList.SelectedIndex];
            var wowPos = bs.ToWoWCoords();

            _txtName.Text = bs.Name;
            _numX.Value = (decimal)wowPos.X;
            _numY.Value = (decimal)wowPos.Y;
            _numZ.Value = (decimal)wowPos.Z;
            _numRadius.Value = (decimal)bs.Radius;
            _numHeight.Value = (decimal)bs.Height;
            
            // Synchroniser la sélection globale
            _elements.SelectedBlackspotIndex = _blackspotList.SelectedIndex;
            _elements.SelectedType = Core.EditableElementType.Blackspot;
            BlackspotsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnAddBlackspot(object? sender, EventArgs e)
        {
            if (_blackspotList == null) return;
            
            // Ajouter un blackspot au centre (0,0,0) par défaut
            var newBlackspot = new Blackspot(
                Vector3.Zero,
                10.0f,
                10.0f,
                $"Blackspot {_elements.Blackspots.Count + 1}"
            );

            _elements.Blackspots.Add(newBlackspot);
            RefreshList();
            _blackspotList.SelectedIndex = _elements.Blackspots.Count - 1;
            
            BlackspotsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnRemoveBlackspot(object? sender, EventArgs e)
        {
            if (_blackspotList == null || _blackspotList.SelectedIndex < 0 || _blackspotList.SelectedIndex >= _elements.Blackspots.Count)
                return;

            _elements.Blackspots.RemoveAt(_blackspotList.SelectedIndex);
            RefreshList();
            
            BlackspotsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnClearBlackspots(object? sender, EventArgs e)
        {
            if (MessageBox.Show("Clear all blackspots?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;

            _elements.Blackspots.Clear();
            RefreshList();
            
            BlackspotsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnApplyChanges(object? sender, EventArgs e)
        {
            if (_blackspotList == null || _blackspotList.SelectedIndex < 0 || _blackspotList.SelectedIndex >= _elements.Blackspots.Count)
                return;
            if (_txtName == null || _numX == null || _numY == null || _numZ == null || _numRadius == null || _numHeight == null)
                return;

            var bs = _elements.Blackspots[_blackspotList.SelectedIndex];

            // Mettre à jour depuis les contrôles
            var wowPos = new Vector3((float)_numX.Value, (float)_numY.Value, (float)_numZ.Value);
            bs.Location = MeshViewer3D.Core.CoordinateSystem.WowToDetour(wowPos);
            bs.Radius = (float)_numRadius.Value;
            bs.Height = (float)_numHeight.Value;
            bs.Name = _txtName.Text;

            _elements.Blackspots[_blackspotList.SelectedIndex] = bs;
            RefreshList();
            
            BlackspotsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnToggleClickMode(object? sender, EventArgs e)
        {
            _clickToPlaceMode = !_clickToPlaceMode;
            
            if (sender is Button btn)
            {
                if (_clickToPlaceMode)
                {
                    btn.BackColor = Color.FromArgb(0, 120, 215); // Bleu actif
                    btn.Text = "Click Mode: ON";
                }
                else
                {
                    btn.BackColor = Color.FromArgb(45, 45, 48);
                    btn.Text = "Click to Place";
                }
            }
            
            ClickModeToggled?.Invoke(this, _clickToPlaceMode);
        }
        
        public void UpdateElements(EditableElements elements)
        {
            _elements = elements;
            RefreshList();
            
            // Synchroniser la sélection de la liste avec l'état global
            if (_blackspotList != null && elements.SelectedType == Core.EditableElementType.Blackspot
                && elements.SelectedBlackspotIndex >= 0 && elements.SelectedBlackspotIndex < elements.Blackspots.Count)
            {
                _blackspotList.SelectedIndex = elements.SelectedBlackspotIndex;
            }
        }
    }
}
