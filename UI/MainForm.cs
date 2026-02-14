using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OpenTK.WinForms;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using MeshViewer3D.Core;
using MeshViewer3D.Rendering;

namespace MeshViewer3D.UI
{
    /// <summary>
    /// Fenêtre principale MeshViewer3D
    /// Qualité Honorbuddy - Interface professionnelle complète
    /// </summary>
    public partial class MainForm : Form
    {
        // Composants OpenGL
        private GLControl? _glControl;
        private Camera _camera = new Camera();
        private NavMeshRenderer? _renderer;

        // Composants UI
        private MinimapControl? _minimap;
        private ConsoleControl? _console;
        private Label? _overlayLabel;
        private System.Windows.Forms.Timer? _renderTimer;
        private TabControl? _editorTabs;
        private BlackspotPanel? _blackspotPanel;
        private JumpLinksPanel? _jumpLinksPanel;
        private SettingsPanel? _settingsPanel;
        private ConvexVolumesPanel? _volumesPanel;

        // État
        private NavMeshData? _currentMesh;
        private EditableElements _editableElements = new();
        private DateTime _lastFrameTime = DateTime.Now;
        private float _fps;
        private bool _blackspotClickMode = false;
        private bool _jumpLinkClickMode = false;

        // Mouse state
        private Point _lastMousePos;
        private bool _isDragging;
        private MouseButtons _dragButton;
        
        // Drag de blackspot
        private bool _isDraggingBlackspot = false;
        private int _draggedBlackspotIndex = -1;

        public MainForm()
        {
            InitializeComponent();
            SetupUI();
            SetupKeyboardShortcuts();
        }

        private void InitializeComponent()
        {
            this.Text = "MeshViewer3D - WoW 3.3.5a Navigation Mesh Viewer";
            this.Size = new Size(1280, 720);
            this.BackColor = Color.FromArgb(45, 45, 48);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.KeyPreview = true; // Important pour recevoir les événements clavier
        }
        
        private void SetupKeyboardShortcuts()
        {
            this.KeyDown += MainForm_KeyDown;
        }
        
        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            // Del - Supprimer élément sélectionné
            if (e.KeyCode == Keys.Delete)
            {
                DeleteSelectedElement();
                e.Handled = true;
            }
            // Échap - Désélectionner
            else if (e.KeyCode == Keys.Escape)
            {
                _editableElements.ClearSelection();
                _blackspotPanel?.UpdateElements(_editableElements);
                OnEditableElementsChanged();
                _console?.Log("Selection cleared");
                e.Handled = true;
            }
            // B - Toggle mode blackspot placement
            else if (e.KeyCode == Keys.B && !e.Control && !e.Alt)
            {
                _blackspotClickMode = !_blackspotClickMode;
                if (_glControl != null)
                {
                    _glControl.Cursor = _blackspotClickMode ? System.Windows.Forms.Cursors.Cross : System.Windows.Forms.Cursors.Default;
                }
                _console?.Log($"Blackspot click mode: {(_blackspotClickMode ? "ON" : "OFF")}");
                e.Handled = true;
            }
            // Ctrl+S - Save blackspots
            else if (e.Control && e.KeyCode == Keys.S)
            {
                OnSaveBlackspots(null, EventArgs.Empty);
                e.Handled = true;
            }
            // Ctrl+O - Load blackspots
            else if (e.Control && e.KeyCode == Keys.O)
            {
                OnLoadBlackspots(null, EventArgs.Empty);
                e.Handled = true;
            }
            // Ctrl+N - Clear all
            else if (e.Control && e.KeyCode == Keys.N)
            {
                if (MessageBox.Show("Clear all blackspots?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    _editableElements.Blackspots.Clear();
                    _blackspotPanel?.UpdateElements(_editableElements);
                    OnEditableElementsChanged();
                    _console?.LogSuccess("All blackspots cleared");
                }
                e.Handled = true;
            }
            // R - Reset camera
            else if (e.KeyCode == Keys.R && !e.Control && !e.Alt)
            {
                OnResetCamera(null, EventArgs.Empty);
                e.Handled = true;
            }
            // W - Toggle wireframe
            else if (e.KeyCode == Keys.W && !e.Control && !e.Alt)
            {
                if (_renderer != null)
                {
                    _renderer.ShowWireframe = !_renderer.ShowWireframe;
                    _console?.Log($"Wireframe: {(_renderer.ShowWireframe ? "ON" : "OFF")}");
                }
                e.Handled = true;
            }
            // L - Toggle lighting
            else if (e.KeyCode == Keys.L && !e.Control && !e.Alt)
            {
                if (_renderer != null)
                {
                    _renderer.EnableLighting = !_renderer.EnableLighting;
                    _console?.Log($"Lighting: {(_renderer.EnableLighting ? "ON" : "OFF")}");
                }
                e.Handled = true;
            }
            // J - Toggle mode Jump Link (comme dans HB)
            else if (e.KeyCode == Keys.J && !e.Control && !e.Alt)
            {
                _console?.LogWarning("Jump Link mode - Coming soon!");
                // TODO: Implémenter JumpLinkTool
                e.Handled = true;
            }
            // V - Toggle mode Convex Volume (comme dans HB)
            else if (e.KeyCode == Keys.V && !e.Control && !e.Alt)
            {
                _console?.LogWarning("Convex Volume mode - Coming soon!");
                // TODO: Implémenter ConvexVolumeTool
                e.Handled = true;
            }
            // Q - Mode Navigation (désactive tous les modes d'édition) (comme dans HB)
            else if (e.KeyCode == Keys.Q && !e.Control && !e.Alt)
            {
                _blackspotClickMode = false;
                if (_glControl != null)
                {
                    _glControl.Cursor = System.Windows.Forms.Cursors.Default;
                }
                _console?.Log("Navigation mode (all edit modes disabled)");
                e.Handled = true;
            }
            // G - Go To Coordinates (comme dans HB)
            else if (e.KeyCode == Keys.G && !e.Control && !e.Alt)
            {
                OnGoToCoordinates(null, EventArgs.Empty);
                e.Handled = true;
            }
            // F - Focus on selection (comme dans HB)
            else if (e.KeyCode == Keys.F && !e.Control && !e.Alt)
            {
                FocusOnSelection();
                e.Handled = true;
            }
            // Home - Reset camera (alias comme dans HB)
            else if (e.KeyCode == Keys.Home)
            {
                OnResetCamera(null, EventArgs.Empty);
                e.Handled = true;
            }
        }
        
        private void DeleteSelectedElement()
        {
            if (_editableElements.SelectedType == Core.EditableElementType.Blackspot
                && _editableElements.SelectedBlackspotIndex >= 0
                && _editableElements.SelectedBlackspotIndex < _editableElements.Blackspots.Count)
            {
                var bs = _editableElements.Blackspots[_editableElements.SelectedBlackspotIndex];
                _editableElements.Blackspots.RemoveAt(_editableElements.SelectedBlackspotIndex);
                _editableElements.ClearSelection();
                _blackspotPanel?.UpdateElements(_editableElements);
                OnEditableElementsChanged();
                _console?.LogSuccess($"Deleted blackspot: {bs.Name}");
            }
        }

        private void SetupUI()
        {
            // Menu Strip
            var menuStrip = new MenuStrip
            {
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White
            };

            // Menu Map
            var mapMenu = new ToolStripMenuItem("Map");
            mapMenu.DropDownItems.Add("Load Tile...", null, OnLoadTile);
            mapMenu.DropDownItems.Add("Load Folder...", null, OnLoadFolder);
            mapMenu.DropDownItems.Add(new ToolStripSeparator());
            mapMenu.DropDownItems.Add("Close Tile", null, OnCloseTile);
            mapMenu.DropDownItems.Add(new ToolStripSeparator());
            mapMenu.DropDownItems.Add("Exit", null, (s, e) => Close());
            menuStrip.Items.Add(mapMenu);

            // Menu View
            var viewMenu = new ToolStripMenuItem("View");
            var wireframeItem = new ToolStripMenuItem("Wireframe", null, OnToggleWireframe) { Checked = true };
            var offmeshItem = new ToolStripMenuItem("OffMesh Connections", null, OnToggleOffMesh) { Checked = true };
            var lightingItem = new ToolStripMenuItem("Lighting", null, OnToggleLighting) { Checked = true };
            viewMenu.DropDownItems.Add(wireframeItem);
            viewMenu.DropDownItems.Add(offmeshItem);
            viewMenu.DropDownItems.Add(lightingItem);
            viewMenu.DropDownItems.Add(new ToolStripSeparator());
            
            var colorMenu = new ToolStripMenuItem("Color Mode");
            colorMenu.DropDownItems.Add("By Area Type", null, (s, e) => SetColorMode(ColorMode.ByAreaType));
            colorMenu.DropDownItems.Add("By Height", null, (s, e) => SetColorMode(ColorMode.ByHeight));
            viewMenu.DropDownItems.Add(colorMenu);
            
            viewMenu.DropDownItems.Add(new ToolStripSeparator());
            viewMenu.DropDownItems.Add("Reset Camera", null, OnResetCamera);
            menuStrip.Items.Add(viewMenu);

            // Menu Mesh (édition)
            var meshMenu = new ToolStripMenuItem("Mesh");
            meshMenu.DropDownItems.Add("Load Blackspots...", null, OnLoadBlackspots);
            meshMenu.DropDownItems.Add("Save Blackspots...", null, OnSaveBlackspots);
            meshMenu.DropDownItems.Add("Export Blackspots (CSV)...", null, OnExportBlackspotsCsv);
            meshMenu.DropDownItems.Add(new ToolStripSeparator());
            meshMenu.DropDownItems.Add("Load Jump Links...", null, OnLoadJumpLinks);
            meshMenu.DropDownItems.Add("Save Jump Links...", null, OnSaveJumpLinks);
            meshMenu.DropDownItems.Add("Export Jump Links (CSV)...", null, OnExportJumpLinksCsv);
            menuStrip.Items.Add(meshMenu);

            // Menu Tools
            var toolsMenu = new ToolStripMenuItem("Tools");
            toolsMenu.DropDownItems.Add("Clear Console", null, (s, e) => _console?.ClearConsole());
            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            toolsMenu.DropDownItems.Add("Go To Coordinates... (G)", null, OnGoToCoordinates);
            menuStrip.Items.Add(toolsMenu);

            // Séparateur avant boutons toolbar
            menuStrip.Items.Add(new ToolStripSeparator());

            // Bouton Raytrace (comme dans HB)
            var btnRaytrace = new ToolStripButton("Raytrace")
            {
                CheckOnClick = true,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                ToolTipText = "Toggle Raytrace Mode - Show point under cursor on navmesh"
            };
            btnRaytrace.CheckedChanged += OnRaytraceToggled;
            menuStrip.Items.Add(btnRaytrace);

            // Bouton Test Navigation (comme dans HB)
            var btnTestNav = new ToolStripButton("Test Navigation")
            {
                CheckOnClick = true,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                ToolTipText = "Test pathfinding A to B - Click two points to test path"
            };
            btnTestNav.CheckedChanged += OnTestNavigationToggled;
            menuStrip.Items.Add(btnTestNav);

            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);

            // Console (en bas)
            _console = new ConsoleControl();
            this.Controls.Add(_console);
            _console.Log("MeshViewer3D initialized. Quality: Honorbuddy/Apoc level.");

            // Panel droit (minimap + tabs d'édition)
            var rightPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 250,
                BackColor = Color.FromArgb(37, 37, 38)
            };
            this.Controls.Add(rightPanel);

            // Minimap
            _minimap = new MinimapControl
            {
                Location = new Point(10, 10),
                Size = new Size(230, 150)
            };
            rightPanel.Controls.Add(_minimap);

            // TabControl d'édition
            _editorTabs = new TabControl
            {
                Location = new Point(5, 170),
                Size = new Size(240, 400),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White
            };
            rightPanel.Controls.Add(_editorTabs);

            // Onglet Settings (comme dans HB)
            _settingsPanel = new SettingsPanel();
            _settingsPanel.WireframeChanged += (s, e) => { if (_renderer != null) _renderer.ShowWireframe = e; };
            _settingsPanel.LightingChanged += (s, e) => { if (_renderer != null) _renderer.EnableLighting = e; };
            _settingsPanel.OffMeshChanged += (s, e) => { if (_renderer != null) _renderer.ShowOffMesh = e; };
            _settingsPanel.BlackspotsChanged += (s, e) => { if (_renderer != null) _renderer.ShowBlackspots = e; };
            _settingsPanel.VolumesChanged += (s, e) => { if (_renderer != null) _renderer.ShowVolumes = e; };
            _settingsPanel.ColorModeChanged += (s, mode) => { if (_renderer != null) _renderer.ColorMode = mode; _console?.Log($"Color mode: {mode}"); };
            var settingsTab = new TabPage("Settings") { BackColor = Color.FromArgb(37, 37, 38) };
            settingsTab.Controls.Add(_settingsPanel);
            _editorTabs.TabPages.Add(settingsTab);

            // Onglet Blackspots
            _blackspotPanel = new BlackspotPanel(_editableElements);
            _blackspotPanel.BlackspotsChanged += (s, e) => OnEditableElementsChanged();
            _blackspotPanel.ClickModeToggled += OnBlackspotClickModeToggled;
            var blackspotTab = new TabPage("Blackspots") { BackColor = Color.FromArgb(37, 37, 38) };
            blackspotTab.Controls.Add(_blackspotPanel);
            _editorTabs.TabPages.Add(blackspotTab);

            // Onglet Jump Links (comme dans HB)
            _jumpLinksPanel = new JumpLinksPanel(_editableElements);
            _jumpLinksPanel.JumpLinksChanged += (s, e) => OnEditableElementsChanged();
            _jumpLinksPanel.ClickModeToggled += OnJumpLinkClickModeToggled;
            _jumpLinksPanel.JumpLinkCreated += (s, data) => {
                _console?.LogSuccess($"Created Jump Link: {data.start:F1} -> {data.end:F1} ({(data.bidirectional ? "↔" : "→")})" );
            };
            var jumpLinksTab = new TabPage("Jump Links") { BackColor = Color.FromArgb(37, 37, 38) };
            jumpLinksTab.Controls.Add(_jumpLinksPanel);
            _editorTabs.TabPages.Add(jumpLinksTab);

            // Onglet Convex Volumes (comme dans HB)
            _volumesPanel = new ConvexVolumesPanel(_editableElements);
            _volumesPanel.VolumesChanged += (s, e) => OnEditableElementsChanged();
            var volumesTab = new TabPage("Volumes") { BackColor = Color.FromArgb(37, 37, 38) };
            volumesTab.Controls.Add(_volumesPanel);
            _editorTabs.TabPages.Add(volumesTab);

            // Onglet WMO Blacklist (placeholder - comme dans HB)
            var wmoTab = new TabPage("WMO Blacklist") { BackColor = Color.FromArgb(37, 37, 38) };
            var lblWmo = new Label
            {
                Text = "WMO Blacklist\n\n" +
                       "(Coming soon)\n\n" +
                       "Ignore specific buildings\n" +
                       "during navmesh generation",
                Location = new Point(10, 10),
                Size = new Size(220, 120),
                ForeColor = Color.White
            };
            wmoTab.Controls.Add(lblWmo);
            _editorTabs.TabPages.Add(wmoTab);

            // Onglet Per-Model Volumes (placeholder - comme dans HB)
            var perModelTab = new TabPage("Per-Model") { BackColor = Color.FromArgb(37, 37, 38) };
            var lblPerModel = new Label
            {
                Text = "Per-Model Volumes\n\n" +
                       "(Coming soon)\n\n" +
                       "Define custom volumes\n" +
                       "for specific M2/WMO models",
                Location = new Point(10, 10),
                Size = new Size(220, 120),
                ForeColor = Color.White
            };
            perModelTab.Controls.Add(lblPerModel);
            _editorTabs.TabPages.Add(perModelTab);

            // OpenTK GLControl
            var settings = new OpenTK.WinForms.GLControlSettings
            {
                NumberOfSamples = 4  // MSAA 4x
            };
            _glControl = new GLControl(settings)
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black
            };
            _glControl.Load += GlControl_Load;
            _glControl.Paint += GlControl_Paint;
            _glControl.Resize += GlControl_Resize;
            _glControl.MouseDown += GlControl_MouseDown;
            _glControl.MouseMove += GlControl_MouseMove;
            _glControl.MouseUp += GlControl_MouseUp;
            _glControl.MouseWheel += GlControl_MouseWheel;
            this.Controls.Add(_glControl);
            _glControl.BringToFront();

            // Overlay info - Label flottant au-dessus du GLControl (pas dedans pour éviter clignotement)
            _overlayLabel = new Label
            {
                AutoSize = true,
                Location = new Point(15, 45),
                BackColor = Color.FromArgb(180, 0, 0, 0),
                ForeColor = Color.White,
                Font = new Font("Consolas", 10),
                Padding = new Padding(8),
                Text = ""
            };
            this.Controls.Add(_overlayLabel);
            _overlayLabel.BringToFront();

            // Timer de rendu (60 FPS)
            _renderTimer = new System.Windows.Forms.Timer
            {
                Interval = 16  // ~60 FPS
            };
            _renderTimer.Tick += (s, e) => _glControl?.Invalidate();
            _renderTimer.Start();

            // Focus camera default
            _camera.Target = Vector3.Zero;
            _camera.Distance = 500f;
            _camera.Yaw = 45f;
            _camera.Pitch = 45f;
        }

        private void GlControl_Load(object? sender, EventArgs e)
        {
            if (_glControl == null) return;

            _glControl.MakeCurrent();

            // Log OpenGL version après initialisation
            _console?.Log($"OpenGL version: {GL.GetString(StringName.Version)}");
            _console?.Log($"GLSL version: {GL.GetString(StringName.ShadingLanguageVersion)}");

            // Initialiser renderer
            string resourcePath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Resources");
            
            _renderer = new NavMeshRenderer();
            _renderer.Initialize(resourcePath);
            _renderer.LoadEditableElements(_editableElements);

            _console?.LogSuccess("OpenGL renderer initialized");
        }

        /// <summary>
        /// Appelé quand les éléments éditables changent (blackspots, volumes, etc.)
        /// </summary>
        private void OnEditableElementsChanged()
        {
            _renderer?.LoadEditableElements(_editableElements);
            _console?.Log($"Updated editable elements: {_editableElements.Blackspots.Count} blackspots, {_editableElements.ConvexVolumes.Count} volumes");
        }

        private void GlControl_Paint(object? sender, PaintEventArgs e)
        {
            if (_glControl == null || _renderer == null) return;

            _glControl.MakeCurrent();

            // Calculer FPS
            var now = DateTime.Now;
            float deltaTime = (float)(now - _lastFrameTime).TotalSeconds;
            _lastFrameTime = now;
            _fps = deltaTime > 0 ? 1f / deltaTime : 60f;

            // Render
            _renderer.Render(_camera, _glControl.Width, _glControl.Height);

            // Update overlay
            UpdateOverlay();

            _glControl.SwapBuffers();
        }

        private void GlControl_Resize(object? sender, EventArgs e)
        {
            if (_glControl == null) return;
            GL.Viewport(0, 0, _glControl.Width, _glControl.Height);
        }

        // Mouse handlers
        private void GlControl_MouseDown(object? sender, MouseEventArgs e)
        {
            if (_glControl == null) return;
            
            // Mode placement de Jump Link (Ctrl+Click ou mode actif)
            if (_jumpLinkClickMode && e.Button == MouseButtons.Left && _currentMesh != null)
            {
                PlaceJumpLinkPointAtCursor(e.X, e.Y);
                return;
            }
            
            // Mode placement de blackspot
            if (_blackspotClickMode && e.Button == MouseButtons.Left && _currentMesh != null)
            {
                PlaceBlackspotAtCursor(e.X, e.Y);
                return;
            }
            
            // Mode normal : sélection de blackspot
            if (e.Button == MouseButtons.Left && !_blackspotClickMode)
            {
                TrySelectBlackspot(e.X, e.Y);
                
                // Si blackspot sélectionné, activer drag
                if (_editableElements.SelectedType == Core.EditableElementType.Blackspot
                    && _editableElements.SelectedBlackspotIndex >= 0)
                {
                    _isDraggingBlackspot = true;
                    _draggedBlackspotIndex = _editableElements.SelectedBlackspotIndex;
                    _isDragging = false; // Prévenir drag caméra
                    return;
                }
            }
            
            _isDragging = true;
            _dragButton = e.Button;
            _lastMousePos = e.Location;
        }

        private void GlControl_MouseMove(object? sender, MouseEventArgs e)
        {
            // Drag de blackspot
            if (_isDraggingBlackspot && _draggedBlackspotIndex >= 0 && _draggedBlackspotIndex < _editableElements.Blackspots.Count)
            {
                DragBlackspot(e.X, e.Y);
                _lastMousePos = e.Location;
                return;
            }
            
            if (!_isDragging) return;

            float dx = e.X - _lastMousePos.X;
            float dy = e.Y - _lastMousePos.Y;
            _lastMousePos = e.Location;

            if (_dragButton == MouseButtons.Middle || _dragButton == MouseButtons.Left)
            {
                _camera.Orbit(dx, dy);
            }
            else if (_dragButton == MouseButtons.Right)
            {
                _camera.Pan(dx, dy);
            }
        }

        private void GlControl_MouseUp(object? sender, MouseEventArgs e)
        {
            _isDragging = false;
            _isDraggingBlackspot = false;
            _draggedBlackspotIndex = -1;
        }

        private void GlControl_MouseWheel(object? sender, MouseEventArgs e)
        {
            // Si blackspot sélectionné + Shift : ajuster rayon
            if (ModifierKeys.HasFlag(Keys.Shift) && _editableElements.SelectedType == Core.EditableElementType.Blackspot
                && _editableElements.SelectedBlackspotIndex >= 0 && _editableElements.SelectedBlackspotIndex < _editableElements.Blackspots.Count)
            {
                var bs = _editableElements.Blackspots[_editableElements.SelectedBlackspotIndex];
                float delta = e.Delta > 0 ? 1f : -1f;
                bs.Radius = Math.Max(1f, Math.Min(500f, bs.Radius + delta));
                _editableElements.Blackspots[_editableElements.SelectedBlackspotIndex] = bs;
                _blackspotPanel?.UpdateElements(_editableElements);
                OnEditableElementsChanged();
                return;
            }
            
            // Si blackspot sélectionné + Ctrl : ajuster hauteur
            if (ModifierKeys.HasFlag(Keys.Control) && _editableElements.SelectedType == Core.EditableElementType.Blackspot
                && _editableElements.SelectedBlackspotIndex >= 0 && _editableElements.SelectedBlackspotIndex < _editableElements.Blackspots.Count)
            {
                var bs = _editableElements.Blackspots[_editableElements.SelectedBlackspotIndex];
                float delta = e.Delta > 0 ? 1f : -1f;
                bs.Height = Math.Max(1f, Math.Min(200f, bs.Height + delta));
                _editableElements.Blackspots[_editableElements.SelectedBlackspotIndex] = bs;
                _blackspotPanel?.UpdateElements(_editableElements);
                OnEditableElementsChanged();
                return;
            }
            
            // Sinon : zoom caméra
            _camera.Zoom(e.Delta);
        }

        // Menu handlers
        private void OnLoadTile(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "NavMesh Tiles (*.mmtile)|*.mmtile",
                Title = "Load Navigation Mesh Tile"
            };

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    _console?.Log($"Loading tile: {System.IO.Path.GetFileName(ofd.FileName)}...");
                    
                    _currentMesh = MmtileLoader.LoadTile(ofd.FileName);
                    _renderer?.LoadMesh(_currentMesh);
                    
                    // Focus camera sur la tile
                    _camera.FocusOn(_currentMesh.GetCenterDetour(), 500f);
                    
                    // Update minimap
                    _minimap?.Clear();
                    _minimap?.SetTileLoaded(_currentMesh.TileX, _currentMesh.TileY, true);
                    _minimap?.SetCurrentTile(_currentMesh.TileX, _currentMesh.TileY);
                    
                    _console?.LogSuccess($"Loaded tile ({_currentMesh.TileX},{_currentMesh.TileY}): " +
                        $"{_currentMesh.Header.PolyCount} polys, {_currentMesh.Header.VertCount} verts");
                }
                catch (Exception ex)
                {
                    _console?.LogError($"Failed to load tile: {ex.Message}");
                    MessageBox.Show($"Error loading tile:\n{ex.Message}", "Error", 
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnLoadFolder(object? sender, EventArgs e)
        {
            _console?.LogWarning("Load Folder not yet implemented");
        }

        private void OnCloseTile(object? sender, EventArgs e)
        {
            _currentMesh = null;
            _minimap?.Clear();
            _console?.Log("Tile closed");
        }

        private void OnToggleWireframe(object? sender, EventArgs e)
        {
            if (_renderer == null || sender is not ToolStripMenuItem item) return;
            item.Checked = !item.Checked;
            _renderer.ShowWireframe = item.Checked;
            _console?.Log($"Wireframe: {(item.Checked ? "ON" : "OFF")}");
        }

        private void OnToggleOffMesh(object? sender, EventArgs e)
        {
            if (_renderer == null || sender is not ToolStripMenuItem item) return;
            item.Checked = !item.Checked;
            _renderer.ShowOffMesh = item.Checked;
            _console?.Log($"OffMesh: {(item.Checked ? "ON" : "OFF")}");
        }

        private void OnToggleLighting(object? sender, EventArgs e)
        {
            if (_renderer == null || sender is not ToolStripMenuItem item) return;
            item.Checked = !item.Checked;
            _renderer.EnableLighting = item.Checked;
            _console?.Log($"Lighting: {(item.Checked ? "ON" : "OFF")}");
        }

        private void SetColorMode(ColorMode mode)
        {
            if (_renderer == null) return;
            _renderer.ColorMode = mode;
            _console?.Log($"Color mode: {mode}");
        }

        private void OnResetCamera(object? sender, EventArgs e)
        {
            if (_currentMesh != null)
            {
                _camera.FocusOn(_currentMesh.GetCenterDetour(), 500f);
            }
            else
            {
                _camera.Reset();
            }
            _console?.Log("Camera reset");
        }

        private void OnBlackspotClickModeToggled(object? sender, bool enabled)
        {
            _blackspotClickMode = enabled;
            
            // Désactiver Jump Link mode si on active Blackspot mode
            if (enabled) _jumpLinkClickMode = false;
            
            if (_glControl != null)
            {
                _glControl.Cursor = enabled ? System.Windows.Forms.Cursors.Cross : System.Windows.Forms.Cursors.Default;
            }
            
            _console?.Log($"Blackspot click mode: {(enabled ? "ON - Click on terrain to place" : "OFF")}");
        }

        private void OnJumpLinkClickModeToggled(object? sender, bool enabled)
        {
            _jumpLinkClickMode = enabled;
            
            // Désactiver Blackspot mode si on active Jump Link mode
            if (enabled) _blackspotClickMode = false;
            
            if (_glControl != null)
            {
                _glControl.Cursor = enabled ? System.Windows.Forms.Cursors.Cross : System.Windows.Forms.Cursors.Default;
            }
            
            _console?.Log($"Jump Link click mode: {(enabled ? "ON - Click to place Start then End" : "OFF")}");
        }
        
        private void PlaceJumpLinkPointAtCursor(int screenX, int screenY)
        {
            if (_glControl == null || _currentMesh == null || _jumpLinksPanel == null) return;
            
            try
            {
                var view = _camera.GetViewMatrix();
                var projection = Matrix4.CreatePerspectiveFieldOfView(
                    MathHelper.DegreesToRadians(60f),
                    (float)_glControl.Width / _glControl.Height,
                    1f, 10000f
                );
                
                var ray = Rendering.RayCaster.ScreenToWorldRay(
                    screenX, screenY,
                    _glControl.Width, _glControl.Height,
                    view, projection
                );
                
                // Trouver l'intersection avec le navmesh
                float closestDistance = float.MaxValue;
                Vector3 closestHit = Vector3.Zero;
                bool foundHit = false;
                
                for (int i = 0; i < _currentMesh.Polys.Length; i++)
                {
                    var poly = _currentMesh.Polys[i];
                    if (poly.VertCount < 3) continue;
                    
                    for (int j = 1; j < poly.VertCount - 1; j++)
                    {
                        var v0 = _currentMesh.Vertices[poly.Verts[0]];
                        var v1 = _currentMesh.Vertices[poly.Verts[j]];
                        var v2 = _currentMesh.Vertices[poly.Verts[j + 1]];
                        
                        if (Rendering.RayCaster.RayTriangleIntersect(ray, v0, v1, v2, out float dist, out Vector3 hit))
                        {
                            if (dist < closestDistance)
                            {
                                closestDistance = dist;
                                closestHit = hit;
                                foundHit = true;
                            }
                        }
                        if (Rendering.RayCaster.RayTriangleIntersect(ray, v0, v2, v1, out float dist2, out Vector3 hit2))
                        {
                            if (dist2 < closestDistance)
                            {
                                closestDistance = dist2;
                                closestHit = hit2;
                                foundHit = true;
                            }
                        }
                    }
                }
                
                if (foundHit)
                {
                    // Passer la position au panel qui gère la logique Start/End
                    _jumpLinksPanel.OnWorldClick(closestHit);
                    
                    var wowPos = CoordinateSystem.DetourToWow(closestHit);
                    bool isStart = _jumpLinksPanel.PendingStartPoint == null;
                    _console?.Log($"Jump Link point placed at WoW [{wowPos.X:F1}, {wowPos.Y:F1}, {wowPos.Z:F1}] ({(isStart ? "Start" : "End")})");
                }
                else
                {
                    _console?.LogWarning("No navmesh intersection found");
                }
            }
            catch (Exception ex)
            {
                _console?.LogError($"Error placing Jump Link point: {ex.Message}");
            }
        }
        
        private void PlaceBlackspotAtCursor(int screenX, int screenY)
        {
            if (_glControl == null || _currentMesh == null) return;
            
            try
            {
                // Créer le rayon depuis la position de la souris
                var view = _camera.GetViewMatrix();
                var projection = Matrix4.CreatePerspectiveFieldOfView(
                    MathHelper.DegreesToRadians(60f),
                    (float)_glControl.Width / _glControl.Height,
                    1f, 10000f
                );
                
                var ray = Rendering.RayCaster.ScreenToWorldRay(
                    screenX, screenY,
                    _glControl.Width, _glControl.Height,
                    view, projection
                );
                
                // DEBUG: Log ray info and mesh bounds
                _console?.Log($"[DEBUG] Ray Origin: ({ray.Origin.X:F1}, {ray.Origin.Y:F1}, {ray.Origin.Z:F1})");
                _console?.Log($"[DEBUG] Ray Dir: ({ray.Direction.X:F3}, {ray.Direction.Y:F3}, {ray.Direction.Z:F3})");
                _console?.Log($"[DEBUG] Mesh BMin: ({_currentMesh.Header.BMin.X:F1}, {_currentMesh.Header.BMin.Y:F1}, {_currentMesh.Header.BMin.Z:F1})");
                _console?.Log($"[DEBUG] Mesh BMax: ({_currentMesh.Header.BMax.X:F1}, {_currentMesh.Header.BMax.Y:F1}, {_currentMesh.Header.BMax.Z:F1})");
                _console?.Log($"[DEBUG] Camera Eye: ({_camera.GetEyePosition().X:F1}, {_camera.GetEyePosition().Y:F1}, {_camera.GetEyePosition().Z:F1})");
                _console?.Log($"[DEBUG] Camera Target: ({_camera.Target.X:F1}, {_camera.Target.Y:F1}, {_camera.Target.Z:F1})");
                
                // Tester l'intersection avec tous les triangles du navmesh
                float closestDistance = float.MaxValue;
                Vector3 closestHit = Vector3.Zero;
                bool foundHit = false;
                int testedTriangles = 0;
                
                for (int i = 0; i < _currentMesh.Polys.Length; i++)
                {
                    var poly = _currentMesh.Polys[i];
                    if (poly.VertCount < 3) continue;
                    
                    // Fan triangulation
                    for (int j = 1; j < poly.VertCount - 1; j++)
                    {
                        var v0 = _currentMesh.Vertices[poly.Verts[0]];
                        var v1 = _currentMesh.Vertices[poly.Verts[j]];
                        var v2 = _currentMesh.Vertices[poly.Verts[j + 1]];
                        testedTriangles++;
                        
                        // Tester le triangle (double-sided est maintenant dans RayTriangleIntersect)
                        if (Rendering.RayCaster.RayTriangleIntersect(ray, v0, v1, v2, out float dist, out Vector3 hit))
                        {
                            if (dist < closestDistance)
                            {
                                closestDistance = dist;
                                closestHit = hit;
                                foundHit = true;
                            }
                        }
                    }
                }
                
                _console?.Log($"[DEBUG] Tested {testedTriangles} triangles");
                
                if (foundHit)
                {
                    // Créer un nouveau blackspot à la position cliquée
                    var newBlackspot = new Data.Blackspot(
                        closestHit,
                        10.0f,  // Rayon par défaut
                        10.0f,  // Hauteur par défaut
                        $"Blackspot {_editableElements.Blackspots.Count + 1}"
                    );
                    
                    int newIndex = _editableElements.Blackspots.Count;
                    _editableElements.Blackspots.Add(newBlackspot);
                    _blackspotPanel?.UpdateElements(_editableElements);
                    OnEditableElementsChanged();
                    
                    // Animation flash pour feedback visuel
                    _renderer?.TriggerBlackspotFlash(newIndex);
                    
                    var wowPos = CoordinateSystem.DetourToWow(closestHit);
                    _console?.LogSuccess($"Blackspot placed at [{wowPos.X:F1}, {wowPos.Y:F1}, {wowPos.Z:F1}]");
                }
                else
                {
                    // DEBUG: Sample first triangle
                    if (_currentMesh.Polys.Length > 0 && _currentMesh.Polys[0].VertCount >= 3)
                    {
                        var poly0 = _currentMesh.Polys[0];
                        var v0 = _currentMesh.Vertices[poly0.Verts[0]];
                        var v1 = _currentMesh.Vertices[poly0.Verts[1]];
                        var v2 = _currentMesh.Vertices[poly0.Verts[2]];
                        _console?.Log($"[DEBUG] First triangle: v0=({v0.X:F1}, {v0.Y:F1}, {v0.Z:F1})");
                        _console?.Log($"[DEBUG]                 v1=({v1.X:F1}, {v1.Y:F1}, {v1.Z:F1})");
                        _console?.Log($"[DEBUG]                 v2=({v2.X:F1}, {v2.Y:F1}, {v2.Z:F1})");
                    }
                    _console?.LogWarning("No navmesh intersection found");
                }
            }
            catch (Exception ex)
            {
                _console?.LogError($"Error placing blackspot: {ex.Message}");
            }
        }
        
        private void DragBlackspot(int screenX, int screenY)
        {
            if (_glControl == null || _currentMesh == null) return;
            
            try
            {
                var view = _camera.GetViewMatrix();
                var projection = Matrix4.CreatePerspectiveFieldOfView(
                    MathHelper.DegreesToRadians(60f),
                    (float)_glControl.Width / _glControl.Height,
                    1f, 10000f
                );
                
                var ray = Rendering.RayCaster.ScreenToWorldRay(
                    screenX, screenY,
                    _glControl.Width, _glControl.Height,
                    view, projection
                );
                
                // Tester intersection avec navmesh
                float closestDistance = float.MaxValue;
                Vector3 closestHit = Vector3.Zero;
                bool foundHit = false;
                
                for (int i = 0; i < _currentMesh.Polys.Length; i++)
                {
                    var poly = _currentMesh.Polys[i];
                    if (poly.VertCount < 3) continue;
                    
                    for (int j = 1; j < poly.VertCount - 1; j++)
                    {
                        var v0 = _currentMesh.Vertices[poly.Verts[0]];
                        var v1 = _currentMesh.Vertices[poly.Verts[j]];
                        var v2 = _currentMesh.Vertices[poly.Verts[j + 1]];
                        
                        // Tester les deux faces du triangle
                        if (Rendering.RayCaster.RayTriangleIntersect(ray, v0, v1, v2, out float dist, out Vector3 hit))
                        {
                            if (dist < closestDistance)
                            {
                                closestDistance = dist;
                                closestHit = hit;
                                foundHit = true;
                            }
                        }
                        if (Rendering.RayCaster.RayTriangleIntersect(ray, v0, v2, v1, out float dist2, out Vector3 hit2))
                        {
                            if (dist2 < closestDistance)
                            {
                                closestDistance = dist2;
                                closestHit = hit2;
                                foundHit = true;
                            }
                        }
                    }
                }
                
                if (foundHit)
                {
                    // Déplacer le blackspot
                    var bs = _editableElements.Blackspots[_draggedBlackspotIndex];
                    bs.Location = closestHit;
                    _editableElements.Blackspots[_draggedBlackspotIndex] = bs;
                    _blackspotPanel?.UpdateElements(_editableElements);
                    OnEditableElementsChanged();
                }
            }
            catch (Exception ex)
            {
                _console?.LogError($"Error dragging blackspot: {ex.Message}");
            }
        }
        
        private void TrySelectBlackspot(int screenX, int screenY)
        {
            if (_glControl == null || _editableElements.Blackspots.Count == 0) return;
            
            try
            {
                var view = _camera.GetViewMatrix();
                var projection = Matrix4.CreatePerspectiveFieldOfView(
                    MathHelper.DegreesToRadians(60f),
                    (float)_glControl.Width / _glControl.Height,
                    1f, 10000f
                );
                
                var ray = Rendering.RayCaster.ScreenToWorldRay(
                    screenX, screenY,
                    _glControl.Width, _glControl.Height,
                    view, projection
                );
                
                // Trouver le blackspot le plus proche
                float closestDistance = float.MaxValue;
                int closestIndex = -1;
                
                for (int i = 0; i < _editableElements.Blackspots.Count; i++)
                {
                    var bs = _editableElements.Blackspots[i];
                    
                    if (Rendering.RayCaster.RayCylinderIntersect(ray, bs.Location, bs.Radius, bs.Height, out float dist))
                    {
                        if (dist < closestDistance)
                        {
                            closestDistance = dist;
                            closestIndex = i;
                        }
                    }
                }
                
                if (closestIndex >= 0)
                {
                    _editableElements.SelectedBlackspotIndex = closestIndex;
                    _editableElements.SelectedType = Core.EditableElementType.Blackspot;
                    _blackspotPanel?.UpdateElements(_editableElements);
                    OnEditableElementsChanged();
                    
                    var bs = _editableElements.Blackspots[closestIndex];
                    var wowPos = CoordinateSystem.DetourToWow(bs.Location);
                    _console?.Log($"Selected: {bs.Name} [{wowPos.X:F1}, {wowPos.Y:F1}, {wowPos.Z:F1}]");
                }
                else
                {
                    // Désélectionner si clic dans le vide
                    _editableElements.ClearSelection();
                    _blackspotPanel?.UpdateElements(_editableElements);
                    OnEditableElementsChanged();
                }
            }
            catch (Exception ex)
            {
                _console?.LogError($"Error selecting blackspot: {ex.Message}");
            }
        }
        
        private void UpdateOverlay()
        {
            // Info overlay flottant
            if (_overlayLabel != null && _currentMesh != null)
            {
                var wowPos = CoordinateSystem.DetourToWow(_camera.Target);
                string modeText = _blackspotClickMode ? "\n[CLICK MODE - Place Blackspot]" : "";
                _overlayLabel.Text = $"Pos: {{{wowPos.X:F1}, {wowPos.Y:F1}, {wowPos.Z:F1}}}\n" +
                                     $"Tile: ({_currentMesh.TileX}, {_currentMesh.TileY})\n" +
                                     $"Polys: {_currentMesh.Polys.Length} | Verts: {_currentMesh.Vertices.Length}\n" +
                                     $"Blackspots: {_editableElements.Blackspots.Count} | Volumes: {_editableElements.ConvexVolumes.Count}\n" +
                                     $"FPS: {_fps:F0} ({1000f/_fps:F1} ms)" + modeText;
            }
        }
        
        private void OnLoadBlackspots(object? sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                Title = "Load Blackspots",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var loaded = Data.BlackspotSerializer.LoadFromXml(dialog.FileName);
                    _editableElements.Blackspots.Clear();
                    _editableElements.Blackspots.AddRange(loaded);
                    _blackspotPanel?.UpdateElements(_editableElements);
                    OnEditableElementsChanged();
                    _console?.LogSuccess($"Loaded {loaded.Count} blackspots from {Path.GetFileName(dialog.FileName)}");
                }
                catch (Exception ex)
                {
                    _console?.LogError($"Failed to load blackspots: {ex.Message}");
                    MessageBox.Show($"Error loading blackspots:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnSaveBlackspots(object? sender, EventArgs e)
        {
            if (_editableElements.Blackspots.Count == 0)
            {
                MessageBox.Show("No blackspots to save.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                Title = "Save Blackspots",
                DefaultExt = "xml",
                FileName = "Blackspots.xml",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    Data.BlackspotSerializer.SaveToXml(_editableElements.Blackspots, dialog.FileName);
                    _console?.LogSuccess($"Saved {_editableElements.Blackspots.Count} blackspots to {Path.GetFileName(dialog.FileName)}");
                }
                catch (Exception ex)
                {
                    _console?.LogError($"Failed to save blackspots: {ex.Message}");
                    MessageBox.Show($"Error saving blackspots:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnExportBlackspotsCsv(object? sender, EventArgs e)
        {
            if (_editableElements.Blackspots.Count == 0)
            {
                MessageBox.Show("No blackspots to export.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                Title = "Export Blackspots to CSV",
                DefaultExt = "csv",
                FileName = "Blackspots.csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    Data.BlackspotSerializer.ExportToCsv(_editableElements.Blackspots, dialog.FileName);
                    _console?.LogSuccess($"Exported {_editableElements.Blackspots.Count} blackspots to CSV");
                }
                catch (Exception ex)
                {
                    _console?.LogError($"Failed to export CSV: {ex.Message}");
                    MessageBox.Show($"Error exporting CSV:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnLoadJumpLinks(object? sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "XML Files (*.xml)|*.xml|Binary Files (*.offmesh)|*.offmesh|All Files (*.*)|*.*",
                Title = "Load Jump Links / OffMesh Connections",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    List<Data.OffMeshConnection> connections;
                    
                    if (dialog.FileName.EndsWith(".offmesh", StringComparison.OrdinalIgnoreCase))
                    {
                        connections = Data.OffMeshSerializer.LoadFromBinary(dialog.FileName);
                    }
                    else
                    {
                        connections = Data.OffMeshSerializer.LoadFromXml(dialog.FileName);
                    }
                    
                    _editableElements.CustomOffMeshConnections.AddRange(connections);
                    _jumpLinksPanel?.UpdateElements(_editableElements);
                    OnEditableElementsChanged();
                    
                    _console?.LogSuccess($"Loaded {connections.Count} Jump Links from {Path.GetFileName(dialog.FileName)}");
                }
                catch (Exception ex)
                {
                    _console?.LogError($"Failed to load Jump Links: {ex.Message}");
                    MessageBox.Show($"Error loading Jump Links:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnSaveJumpLinks(object? sender, EventArgs e)
        {
            if (_editableElements.CustomOffMeshConnections.Count == 0)
            {
                MessageBox.Show("No Jump Links to save.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Filter = "XML Files (*.xml)|*.xml|Binary Files (*.offmesh)|*.offmesh|All Files (*.*)|*.*",
                Title = "Save Jump Links",
                DefaultExt = "xml",
                FileName = "JumpLinks.xml",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    if (dialog.FileName.EndsWith(".offmesh", StringComparison.OrdinalIgnoreCase))
                    {
                        Data.OffMeshSerializer.SaveToBinary(_editableElements.CustomOffMeshConnections, dialog.FileName);
                    }
                    else
                    {
                        Data.OffMeshSerializer.SaveToXml(_editableElements.CustomOffMeshConnections, dialog.FileName);
                    }
                    
                    _console?.LogSuccess($"Saved {_editableElements.CustomOffMeshConnections.Count} Jump Links to {Path.GetFileName(dialog.FileName)}");
                }
                catch (Exception ex)
                {
                    _console?.LogError($"Failed to save Jump Links: {ex.Message}");
                    MessageBox.Show($"Error saving Jump Links:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnExportJumpLinksCsv(object? sender, EventArgs e)
        {
            if (_editableElements.CustomOffMeshConnections.Count == 0)
            {
                MessageBox.Show("No Jump Links to export.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                Title = "Export Jump Links to CSV",
                DefaultExt = "csv",
                FileName = "JumpLinks.csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    Data.OffMeshSerializer.ExportToCsv(_editableElements.CustomOffMeshConnections, dialog.FileName);
                    _console?.LogSuccess($"Exported {_editableElements.CustomOffMeshConnections.Count} Jump Links to CSV");
                }
                catch (Exception ex)
                {
                    _console?.LogError($"Failed to export CSV: {ex.Message}");
                    MessageBox.Show($"Error exporting CSV:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ========== Toolbar handlers (HB conformity) ==========
        
        private void OnRaytraceToggled(object? sender, EventArgs e)
        {
            if (sender is ToolStripButton btn)
            {
                // Toggle est automatique avec CheckOnClick=true
                _console?.Log($"Raytrace mode: {(btn.Checked ? "ON" : "OFF")}");
                // TODO: Activer le mode raytrace dans le renderer
            }
        }

        private void OnTestNavigationToggled(object? sender, EventArgs e)
        {
            if (sender is ToolStripButton btn)
            {
                _console?.Log($"Test Navigation mode: {(btn.Checked ? "ON" : "OFF")}");
                // TODO: Activer le mode test pathfinding
            }
        }

        private void OnGoToCoordinates(object? sender, EventArgs e)
        {
            using var inputForm = new Form
            {
                Text = "Go To Coordinates",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Width = 320,
                Height = 180,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var lblX = new Label { Text = "X:", Left = 20, Top = 20, Width = 30 };
            var txtX = new TextBox { Left = 60, Top = 18, Width = 80 };
            var lblY = new Label { Text = "Y:", Left = 150, Top = 20, Width = 30 };
            var txtY = new TextBox { Left = 180, Top = 18, Width = 80 };
            var lblZ = new Label { Text = "Z:", Left = 20, Top = 55, Width = 30 };
            var txtZ = new TextBox { Left = 60, Top = 53, Width = 80 };

            var btnGo = new Button { Text = "Go", Left = 80, Top = 95, Width = 70, DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "Cancel", Left = 160, Top = 95, Width = 70, DialogResult = DialogResult.Cancel };

            inputForm.Controls.AddRange(new Control[] { lblX, txtX, lblY, txtY, lblZ, txtZ, btnGo, btnCancel });
            inputForm.AcceptButton = btnGo;
            inputForm.CancelButton = btnCancel;

            // Pré-remplir avec position courante
            if (_currentMesh != null)
            {
                var center = _currentMesh.GetCenterDetour();
                txtX.Text = center.X.ToString("F2");
                txtY.Text = center.Y.ToString("F2");
                txtZ.Text = center.Z.ToString("F2");
            }

            if (inputForm.ShowDialog() == DialogResult.OK)
            {
                if (float.TryParse(txtX.Text, out float x) &&
                    float.TryParse(txtY.Text, out float y) &&
                    float.TryParse(txtZ.Text, out float z))
                {
                    _camera.FocusOn(new Vector3(x, y, z), _camera.Distance);
                    _console?.Log($"Camera moved to ({x:F2}, {y:F2}, {z:F2})");
                }
                else
                {
                    MessageBox.Show("Invalid coordinates. Please enter numeric values.", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void FocusOnSelection()
        {
            if (_editableElements.SelectedType == Core.EditableElementType.Blackspot &&
                _editableElements.SelectedBlackspotIndex >= 0 &&
                _editableElements.SelectedBlackspotIndex < _editableElements.Blackspots.Count)
            {
                var bs = _editableElements.Blackspots[_editableElements.SelectedBlackspotIndex];
                _camera.FocusOn(bs.Location, _camera.Distance);
                _console?.Log($"Focused on blackspot at ({bs.Location.X:F2}, {bs.Location.Y:F2}, {bs.Location.Z:F2})");
            }
            else if (_currentMesh != null)
            {
                _camera.FocusOn(_currentMesh.GetCenterDetour(), 500f);
                _console?.Log("Focused on mesh center (no selection)");
            }
            else
            {
                _console?.LogWarning("Nothing to focus on");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _renderTimer?.Stop();
                _renderTimer?.Dispose();
                _renderer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
