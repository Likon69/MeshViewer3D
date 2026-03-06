using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenTK.WinForms;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using MeshViewer3D.Core;
using MeshViewer3D.Core.Mpq;
using MeshViewer3D.Core.Formats.Adt;
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
        private GameObjectPanel? _gameObjectPanel;
        private WmoBlacklistPanel? _wmoBlacklistPanel;
        private PerModelPanel? _perModelPanel;

        // État
        private NavMeshData? _currentMesh;
        private EditableElements _editableElements = new();
        private DateTime _lastFrameTime = DateTime.Now;
        private float _fps;
        private bool _blackspotClickMode = false;
        private bool _jumpLinkClickMode = false;
        private bool _volumeClickMode = false;

        // Mouse state
        private Point _lastMousePos;
        private bool _isDragging;
        private MouseButtons _dragButton;
        
        // Drag de blackspot
        private bool _isDraggingBlackspot = false;
        private int _draggedBlackspotIndex = -1;
        private Vector3 _dragStartPosition; // For undo/redo move command

        // Undo/Redo manager
        private readonly UndoRedoManager _undoRedo = new();

        // WoW data path for WMO loading
        private string? _wowDataPath;

        // Raytrace mode
        private bool _raytraceMode = false;
        private Vector3? _raytraceHitPoint = null;   // Detour coords of hit point
        private int _raytraceHitPolyIndex = -1;       // Index into _currentMesh.Polys

        // Test Navigation mode (A→B pathfinding)
        private bool _testNavMode = false;
        private Vector3? _testNavStartPoint = null;
        private int _testNavStartPolyIndex = -1;
        private Vector3? _testNavEndPoint = null;
        private int _testNavEndPolyIndex = -1;
        private List<Vector3>? _testNavPath = null;

        public MainForm()
        {
            InitializeComponent();
            SetupUI();
            SetupKeyboardShortcuts();
            LoadSavedSettings();
        }

        private void LoadSavedSettings()
        {
            AppSettings.Load();
            if (AppSettings.WowDataPath != null && Directory.Exists(AppSettings.WowDataPath))
            {
                var mpqFiles = Directory.GetFiles(AppSettings.WowDataPath, "*.MPQ");
                if (mpqFiles.Length > 0)
                {
                    _wowDataPath = AppSettings.WowDataPath;
                    _console?.LogSuccess($"WoW Data folder restored: {_wowDataPath} ({mpqFiles.Length} MPQ archives)");
                }
            }
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
                if (_volumeClickMode)
                {
                    _volumesPanel?.CancelInProgress();
                    _renderer?.LoadPreviewPolygon(null);
                }
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
                    _undoRedo.Clear();
                    _console?.LogSuccess("All blackspots cleared");
                }
                e.Handled = true;
            }
            // Ctrl+Z - Undo
            else if (e.Control && e.KeyCode == Keys.Z && !e.Shift)
            {
                var cmd = _undoRedo.Undo();
                if (cmd != null)
                {
                    _blackspotPanel?.UpdateElements(_editableElements);
                    _jumpLinksPanel?.UpdateElements(_editableElements);
                    _volumesPanel?.UpdateElements(_editableElements);
                    OnEditableElementsChanged();
                    _console?.Log($"Undo: {cmd.Description}");
                }
                else
                {
                    _console?.LogWarning("Nothing to undo");
                }
                e.Handled = true;
            }
            // Ctrl+Y or Ctrl+Shift+Z - Redo
            else if ((e.Control && e.KeyCode == Keys.Y) || (e.Control && e.Shift && e.KeyCode == Keys.Z))
            {
                var cmd = _undoRedo.Redo();
                if (cmd != null)
                {
                    _blackspotPanel?.UpdateElements(_editableElements);
                    _jumpLinksPanel?.UpdateElements(_editableElements);
                    _volumesPanel?.UpdateElements(_editableElements);
                    OnEditableElementsChanged();
                    _console?.Log($"Redo: {cmd.Description}");
                }
                else
                {
                    _console?.LogWarning("Nothing to redo");
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
                bool newMode = !_jumpLinkClickMode;
                _jumpLinkClickMode = newMode;
                if (newMode) _blackspotClickMode = false;
                if (_glControl != null)
                    _glControl.Cursor = newMode ? System.Windows.Forms.Cursors.Cross : System.Windows.Forms.Cursors.Default;
                _jumpLinksPanel?.SetClickMode(newMode);
                _console?.Log($"Jump Link click mode: {(newMode ? "ON - Click to place Start then End" : "OFF")}");
                e.Handled = true;
            }
            // V - Toggle mode Convex Volume (comme dans HB)
            else if (e.KeyCode == Keys.V && !e.Control && !e.Alt)
            {
                bool newMode = !_volumeClickMode;
                _volumeClickMode = newMode;
                if (newMode) { _blackspotClickMode = false; _jumpLinkClickMode = false; _jumpLinksPanel?.SetClickMode(false); }
                if (!newMode) _renderer?.LoadPreviewPolygon(null);
                if (_glControl != null)
                    _glControl.Cursor = newMode ? System.Windows.Forms.Cursors.Cross : System.Windows.Forms.Cursors.Default;
                _volumesPanel?.SetClickMode(newMode);
                _console?.Log($"Volume click mode: {(newMode ? "ON - Click terrain to add vertices, Enter to finalize" : "OFF")}");
                e.Handled = true;
            }
            // Q - Mode Navigation (désactive tous les modes d'édition) (comme dans HB)
            else if (e.KeyCode == Keys.Q && !e.Control && !e.Alt)
            {
                _blackspotClickMode = false;
                _jumpLinkClickMode = false;
                _volumeClickMode = false;
                _jumpLinksPanel?.SetClickMode(false);
                _volumesPanel?.SetClickMode(false);
                _renderer?.LoadPreviewPolygon(null);
                if (_glControl != null)
                    _glControl.Cursor = System.Windows.Forms.Cursors.Default;
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
            // Enter - Finaliser le volume en cours
            else if (e.KeyCode == Keys.Return && !e.Control && !e.Alt && _volumeClickMode)
            {
                bool ok = _volumesPanel?.FinalizeVolume() ?? false;
                if (ok)
                {
                    // Wrap the volume the panel just added in an undo command
                    var justAdded = _editableElements.ConvexVolumes[^1];
                    _editableElements.ConvexVolumes.RemoveAt(_editableElements.ConvexVolumes.Count - 1);
                    _undoRedo.Execute(new Core.Commands.AddVolumeCommand(
                        _editableElements, justAdded, () => { OnEditableElementsChanged(); _volumesPanel?.UpdateElements(_editableElements); }));

                    _volumeClickMode = false;
                    _volumesPanel?.SetClickMode(false);
                    _renderer?.LoadPreviewPolygon(null);
                    if (_glControl != null) _glControl.Cursor = System.Windows.Forms.Cursors.Default;
                    _console?.LogSuccess("Convex Volume finalized");
                }
                else
                {
                    _console?.LogWarning("Need at least 3 vertices to finalize volume");
                }
                e.Handled = true;
            }
            // Home - Reset camera (alias comme dans HB)
            else if (e.KeyCode == Keys.Home)
            {
                OnResetCamera(null, EventArgs.Empty);
                e.Handled = true;
            }
            // A - Analyze NavMesh (connected components, degenerate polys)
            else if (e.KeyCode == Keys.A && !e.Control && !e.Alt)
            {
                OnAnalyzeNavMesh(null, EventArgs.Empty);
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
                int idx = _editableElements.SelectedBlackspotIndex;
                _undoRedo.Execute(new Core.Commands.RemoveBlackspotCommand(
                    _editableElements, idx, () => OnEditableElementsChanged()));
                _editableElements.ClearSelection();
                _blackspotPanel?.UpdateElements(_editableElements);
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
            mapMenu.DropDownItems.Add("Set WoW Data Folder...", null, OnSetWowDataFolder);
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
            colorMenu.DropDownItems.Add("By Component (Analysis)", null, (s, e) => SetColorMode(ColorMode.ByComponent));
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
            meshMenu.DropDownItems.Add(new ToolStripSeparator());
            meshMenu.DropDownItems.Add("Load Volumes...", null, OnLoadVolumes);
            meshMenu.DropDownItems.Add("Save Volumes...", null, OnSaveVolumes);
            menuStrip.Items.Add(meshMenu);

            // Menu Tools
            var toolsMenu = new ToolStripMenuItem("Tools");
            toolsMenu.DropDownItems.Add("Clear Console", null, (s, e) => _console?.ClearConsole());
            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            toolsMenu.DropDownItems.Add("Go To Coordinates... (G)", null, OnGoToCoordinates);
            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            toolsMenu.DropDownItems.Add("Analyze NavMesh (A)", null, OnAnalyzeNavMesh);
            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            toolsMenu.DropDownItems.Add("Export Path (JSON)...", null, OnExportPathJson);
            toolsMenu.DropDownItems.Add("Export Path (CSV)...", null, OnExportPathCsv);
            toolsMenu.DropDownItems.Add("Export Path (HB Hotspot XML)...", null, OnExportPathHbXml);
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
                // Intercept: the panel already added the connection — pop it and re-add via undo command
                if (_editableElements.CustomOffMeshConnections.Count > 0)
                {
                    var justAdded = _editableElements.CustomOffMeshConnections[^1];
                    _editableElements.CustomOffMeshConnections.RemoveAt(_editableElements.CustomOffMeshConnections.Count - 1);
                    _undoRedo.Execute(new Core.Commands.AddOffMeshCommand(
                        _editableElements, justAdded, () => { OnEditableElementsChanged(); _jumpLinksPanel?.UpdateElements(_editableElements); }));
                }
                _console?.LogSuccess($"Created Jump Link: {data.start:F1} -> {data.end:F1} ({(data.bidirectional ? "↔" : "→")})" );
            };
            var jumpLinksTab = new TabPage("Jump Links") { BackColor = Color.FromArgb(37, 37, 38) };
            jumpLinksTab.Controls.Add(_jumpLinksPanel);
            _editorTabs.TabPages.Add(jumpLinksTab);

            // Onglet Convex Volumes (comme dans HB)
            _volumesPanel = new ConvexVolumesPanel(_editableElements, _undoRedo);
            _volumesPanel.VolumesChanged += (s, e) => OnEditableElementsChanged();
            _volumesPanel.ClickModeToggled += OnVolumeClickModeToggled;
            var volumesTab = new TabPage("Volumes") { BackColor = Color.FromArgb(37, 37, 38) };
            volumesTab.Controls.Add(_volumesPanel);
            _editorTabs.TabPages.Add(volumesTab);

            // Onglet Objects (WMO/M2 viewer)
            _gameObjectPanel = new GameObjectPanel();
            _gameObjectPanel.WmoVisibilityChanged += v => { if (_renderer != null) _renderer.ShowWmoObjects = v; };
            _gameObjectPanel.M2VisibilityChanged += v => { if (_renderer != null) _renderer.ShowM2Objects = v; };
            var objectsTab = new TabPage("Objects") { BackColor = Color.FromArgb(37, 37, 38) };
            objectsTab.Controls.Add(_gameObjectPanel);
            _editorTabs.TabPages.Add(objectsTab);

            // Onglet WMO Blacklist (comme dans HB)
            _wmoBlacklistPanel = new WmoBlacklistPanel();
            _wmoBlacklistPanel.BlacklistChanged += blacklist =>
            {
                if (_renderer != null) _renderer.SetWmoBlacklist(blacklist);
                _console?.Log($"WMO blacklist: {blacklist.Count} hidden");
            };
            var wmoTab = new TabPage("WMO Blacklist") { BackColor = Color.FromArgb(37, 37, 38) };
            wmoTab.Controls.Add(_wmoBlacklistPanel);
            _editorTabs.TabPages.Add(wmoTab);

            // Onglet Per-Model Volumes (comme dans HB)
            _perModelPanel = new PerModelPanel();
            var perModelTab = new TabPage("Per-Model") { BackColor = Color.FromArgb(37, 37, 38) };
            perModelTab.Controls.Add(_perModelPanel);
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

            // Test Navigation mode: click to set start/end
            if (_testNavMode && e.Button == MouseButtons.Left && _currentMesh != null)
            {
                PlaceTestNavPoint(e.X, e.Y);
                return;
            }
            
            // Mode placement de Jump Link (Ctrl+Click ou mode actif)
            if (_jumpLinkClickMode && e.Button == MouseButtons.Left && _currentMesh != null)
            {
                PlaceJumpLinkPointAtCursor(e.X, e.Y);
                return;
            }
            
            // Mode placement de vertex Convex Volume
            if (_volumeClickMode && e.Button == MouseButtons.Left && _currentMesh != null)
            {
                PlaceConvexVolumeVertex(e.X, e.Y);
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
                    _dragStartPosition = _editableElements.Blackspots[_draggedBlackspotIndex].Location;
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
            // Raytrace: update hit point on every mouse move
            if (_raytraceMode && _currentMesh != null && _glControl != null && !_isDraggingBlackspot)
            {
                UpdateRaytraceHit(e.X, e.Y);
            }

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
            if (_isDraggingBlackspot && _draggedBlackspotIndex >= 0 
                && _draggedBlackspotIndex < _editableElements.Blackspots.Count)
            {
                var newPos = _editableElements.Blackspots[_draggedBlackspotIndex].Location;
                if ((newPos - _dragStartPosition).Length > 0.01f)
                {
                    // Revert to start position first so the command can apply the move
                    var bs = _editableElements.Blackspots[_draggedBlackspotIndex];
                    bs.Location = _dragStartPosition;
                    _editableElements.Blackspots[_draggedBlackspotIndex] = bs;

                    _undoRedo.Execute(new Core.Commands.MoveBlackspotCommand(
                        _editableElements, _draggedBlackspotIndex,
                        _dragStartPosition, newPos, () => OnEditableElementsChanged()));
                    _blackspotPanel?.UpdateElements(_editableElements);
                }
            }
            _isDragging = false;
            _isDraggingBlackspot = false;
            _draggedBlackspotIndex = -1;
        }

        private void GlControl_MouseWheel(object? sender, MouseEventArgs e)
        {
            // Si blackspot sélectionné + Shift : ajuster rayon (undoable)
            if (ModifierKeys.HasFlag(Keys.Shift) && _editableElements.SelectedType == Core.EditableElementType.Blackspot
                && _editableElements.SelectedBlackspotIndex >= 0 && _editableElements.SelectedBlackspotIndex < _editableElements.Blackspots.Count)
            {
                var bs = _editableElements.Blackspots[_editableElements.SelectedBlackspotIndex];
                float newRadius = Math.Max(1f, Math.Min(500f, bs.Radius + (e.Delta > 0 ? 1f : -1f)));
                _undoRedo.Execute(new Core.Commands.ResizeBlackspotCommand(
                    _editableElements, _editableElements.SelectedBlackspotIndex,
                    bs.Radius, newRadius, bs.Height, bs.Height,
                    () => { _blackspotPanel?.UpdateElements(_editableElements); OnEditableElementsChanged(); }));
                return;
            }
            
            // Si blackspot sélectionné + Ctrl : ajuster hauteur (undoable)
            if (ModifierKeys.HasFlag(Keys.Control) && _editableElements.SelectedType == Core.EditableElementType.Blackspot
                && _editableElements.SelectedBlackspotIndex >= 0 && _editableElements.SelectedBlackspotIndex < _editableElements.Blackspots.Count)
            {
                var bs = _editableElements.Blackspots[_editableElements.SelectedBlackspotIndex];
                float newHeight = Math.Max(1f, Math.Min(200f, bs.Height + (e.Delta > 0 ? 1f : -1f)));
                _undoRedo.Execute(new Core.Commands.ResizeBlackspotCommand(
                    _editableElements, _editableElements.SelectedBlackspotIndex,
                    bs.Radius, bs.Radius, bs.Height, newHeight,
                    () => { _blackspotPanel?.UpdateElements(_editableElements); OnEditableElementsChanged(); }));
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

                    TryLoadWorldObjects();
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
            using var fbd = new FolderBrowserDialog
            {
                Description = "Select folder containing .mmtile files",
                UseDescriptionForTitle = true
            };
            if (fbd.ShowDialog() != DialogResult.OK) return;

            var files = Directory.GetFiles(fbd.SelectedPath, "*.mmtile");
            if (files.Length == 0)
            {
                _console?.LogWarning("No .mmtile files found in selected folder");
                return;
            }

            _console?.Log($"Loading {files.Length} tiles...");
            _minimap?.Clear();

            var allMeshes = new List<NavMeshData>();
            int loaded = 0, failed = 0;

            foreach (var file in files)
            {
                try
                {
                    var mesh = MmtileLoader.LoadTile(file);
                    allMeshes.Add(mesh);
                    _minimap?.SetTileLoaded(mesh.TileX, mesh.TileY, true);
                    loaded++;
                }
                catch (Exception ex)
                {
                    _console?.LogWarning($"Skipped {Path.GetFileName(file)}: {ex.Message}");
                    failed++;
                }
            }

            if (allMeshes.Count == 0)
            {
                _console?.LogError("No tiles loaded successfully");
                return;
            }

            // Hard check: aborting before merge is safer than rendering corrupt geometry
            int totalVerts = 0;
            foreach (var m in allMeshes) totalVerts += m.Vertices.Length;
            if (totalVerts > 60000)
            {
                _console?.LogError($"Cannot load {loaded} tiles: {totalVerts} total vertices exceed ushort index limit (60000). Load fewer tiles.");
                _minimap?.Clear();
                return;
            }

            _currentMesh = NavMeshData.Merge(allMeshes);
            _renderer?.LoadMesh(_currentMesh);
            _camera.FocusOn(_currentMesh.GetCenterDetour(), 800f);
            _minimap?.SetCurrentTile(allMeshes[0].TileX, allMeshes[0].TileY);

            _console?.LogSuccess($"Loaded {loaded} tiles" +
                (failed > 0 ? $" ({failed} failed)" : string.Empty) +
                $" — {_currentMesh.Polys.Length} polys, {_currentMesh.Vertices.Length} verts");

            TryLoadWorldObjects();
        }

        private void OnSetWowDataFolder(object? sender, EventArgs e)
        {
            using var fbd = new FolderBrowserDialog
            {
                Description = "Select WoW 3.3.5a Data folder (containing MPQ archives)",
                UseDescriptionForTitle = true
            };
            if (fbd.ShowDialog() != DialogResult.OK) return;

            // Validate: folder must contain at least one MPQ
            var mpqFiles = Directory.GetFiles(fbd.SelectedPath, "*.MPQ");
            if (mpqFiles.Length == 0)
            {
                _console?.LogError("No .MPQ files found in selected folder");
                MessageBox.Show("The selected folder does not contain any .MPQ archives.\nSelect the WoW 3.3.5a Data\\ folder.",
                    "Invalid Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _wowDataPath = fbd.SelectedPath;
            AppSettings.WowDataPath = _wowDataPath;
            AppSettings.Save();
            _console?.LogSuccess($"WoW Data folder set: {_wowDataPath} ({mpqFiles.Length} MPQ archives)");

            // If tiles are already loaded, try loading WMO objects now
            TryLoadWorldObjects();
        }

        /// <summary>
        /// Attempts to load WMO objects from MPQ archives for the currently loaded tile.
        /// Requires both _wowDataPath and _currentMesh to be set.
        /// </summary>
        private void TryLoadWorldObjects()
        {
            if (_wowDataPath == null || _currentMesh == null || _renderer == null) return;

            string? mapDir = GetMapDirectory(_currentMesh.MapId);
            if (mapDir == null)
            {
                _console?.LogWarning($"Unknown map ID {_currentMesh.MapId} — cannot load WMO objects");
                return;
            }

            string adtPath = $@"World\Maps\{mapDir}\{mapDir}_{_currentMesh.TileX}_{_currentMesh.TileY}.adt";
            _console?.Log($"Loading ADT: {adtPath}...");

            try
            {
                using var provider = WowDataProvider.Open(_wowDataPath);
                byte[]? adtBytes = provider.GetFileBytes(adtPath);
                if (adtBytes == null)
                {
                    _console?.LogWarning($"ADT not found in MPQ: {adtPath}");
                    return;
                }

                var adt = AdtFile.Load(adtBytes);
                _gameObjectPanel?.LoadObjects(adt);
                _wmoBlacklistPanel?.LoadWmoNames(adt.WmoNames);
                _perModelPanel?.LoadModelNames(adt.WmoNames, adt.M2Names);
                _renderer.LoadWorldObjects(provider, adt);

                int wmoCount = adt.WmoInstances.Length;
                _console?.LogSuccess($"Loaded {wmoCount} WMO placements from ADT");
            }
            catch (Exception ex)
            {
                _console?.LogError($"Failed to load WMO objects: {ex.Message}");
            }
        }

        /// <summary>
        /// Maps a WoW map ID to its internal directory name used in MPQ file paths.
        /// Uses Maps.json database for dynamic lookup (supports all 3.3.5a maps).
        /// </summary>
        private static string? GetMapDirectory(int mapId) => MapDatabase.GetDirectory(mapId);

        private void OnCloseTile(object? sender, EventArgs e)
        {
            _currentMesh = null;
            ClearTestNavState();
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

        private void OnVolumeClickModeToggled(object? sender, bool enabled)
        {
            _volumeClickMode = enabled;
            if (enabled) { _blackspotClickMode = false; _jumpLinkClickMode = false; }
            if (_glControl != null)
                _glControl.Cursor = enabled ? System.Windows.Forms.Cursors.Cross : System.Windows.Forms.Cursors.Default;
            if (!enabled) _renderer?.LoadPreviewPolygon(null);
            _console?.Log($"Volume click mode: {(enabled ? "ON - Click terrain to add vertices, Enter to finalize" : "OFF")}");
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
        
        /// <summary>
        /// Updates the raytrace hit point for the current cursor position.
        /// Called on every mouse move when raytrace mode is active.
        /// </summary>
        private void UpdateRaytraceHit(int screenX, int screenY)
        {
            if (_glControl == null || _currentMesh == null || _renderer == null) return;

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

                float closestDistance = float.MaxValue;
                Vector3 closestHit = Vector3.Zero;
                int closestPolyIndex = -1;

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
                                closestPolyIndex = i;
                            }
                        }
                    }
                }

                if (closestPolyIndex >= 0)
                {
                    _raytraceHitPoint = closestHit;
                    _raytraceHitPolyIndex = closestPolyIndex;
                    _renderer.SetRaytraceMarker(closestHit);
                }
                else
                {
                    _raytraceHitPoint = null;
                    _raytraceHitPolyIndex = -1;
                    _renderer.SetRaytraceMarker(null);
                }
            }
            catch
            {
                // Silently ignore raytrace errors during mouse move
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
                
                // Tester l'intersection avec tous les triangles du navmesh
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
                    }
                }
                
                if (foundHit)
                {
                    var newBlackspot = new Data.Blackspot(
                        closestHit,
                        10.0f,
                        10.0f,
                        $"Blackspot {_editableElements.Blackspots.Count + 1}"
                    );
                    
                    _undoRedo.Execute(new Core.Commands.AddBlackspotCommand(
                        _editableElements, newBlackspot, () => OnEditableElementsChanged()));
                    int newIndex = _editableElements.Blackspots.Count - 1;
                    _blackspotPanel?.UpdateElements(_editableElements);
                    _renderer?.TriggerBlackspotFlash(newIndex);
                    
                    var wowPos = CoordinateSystem.DetourToWow(closestHit);
                    _console?.LogSuccess($"Blackspot placed at [{wowPos.X:F1}, {wowPos.Y:F1}, {wowPos.Z:F1}]");
                }
                else
                {
                    _console?.LogWarning("No navmesh intersection found - click directly on the navmesh surface");
                }
            }
            catch (Exception ex)
            {
                _console?.LogError($"Error placing blackspot: {ex.Message}");
            }
        }

        private void PlaceConvexVolumeVertex(int screenX, int screenY)
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
                    _volumesPanel.OnWorldClick(closestHit);
                    _renderer?.LoadPreviewPolygon(_volumesPanel.InProgressVertices);

                    var wowPos = CoordinateSystem.DetourToWow(closestHit);
                    int count = _volumesPanel.InProgressVertices.Count;
                    _console?.Log($"Volume vertex {count} placed at [{wowPos.X:F1}, {wowPos.Y:F1}, {wowPos.Z:F1}]");
                }
                else
                {
                    _console?.LogWarning("No navmesh intersection found");
                }
            }
            catch (Exception ex)
            {
                _console?.LogError($"Error placing volume vertex: {ex.Message}");
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
                string modeText = _blackspotClickMode ? "\n[CLICK MODE - Place Blackspot]"
                    : _jumpLinkClickMode ? $"\n[CLICK MODE - Place Jump Link {(_jumpLinksPanel?.PendingStartPoint == null ? "Start" : "End")}]"
                    : _volumeClickMode ? $"\n[VOLUME MODE - {_volumesPanel?.InProgressVertices.Count ?? 0} vertices (Enter to finalize)]"
                    : "";

                if (_raytraceMode && _raytraceHitPoint.HasValue && _raytraceHitPolyIndex >= 0)
                {
                    var wowHit = CoordinateSystem.DetourToWow(_raytraceHitPoint.Value);
                    byte area = _currentMesh.Polys[_raytraceHitPolyIndex].Area;
                    string areaName = Data.AreaTypeInfo.GetName(area);
                    modeText += $"\n[RAYTRACE] Hit: {{{wowHit.X:F1}, {wowHit.Y:F1}, {wowHit.Z:F1}}} | Poly #{_raytraceHitPolyIndex} | {areaName} ({area})";
                }
                else if (_raytraceMode)
                {
                    modeText += "\n[RAYTRACE] No hit";
                }

                if (_testNavMode)
                {
                    if (_testNavStartPoint.HasValue && _testNavEndPoint == null)
                    {
                        var ws = CoordinateSystem.DetourToWow(_testNavStartPoint.Value);
                        modeText += $"\n[TEST NAV] Start: {{{ws.X:F1}, {ws.Y:F1}, {ws.Z:F1}}} — Click end point";
                    }
                    else if (_testNavPath != null)
                    {
                        modeText += $"\n[TEST NAV] Path: {_testNavPath.Count} waypoints";
                    }
                    else if (_testNavStartPoint == null)
                    {
                        modeText += "\n[TEST NAV] Click start point";
                    }
                }

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

        private void OnLoadVolumes(object? sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                Title = "Load Convex Volumes",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var loaded = Data.ConvexVolumeSerializer.LoadFromXml(dialog.FileName);
                    _editableElements.ConvexVolumes.AddRange(loaded);
                    _volumesPanel?.RefreshList();
                    OnEditableElementsChanged();
                    _console?.LogSuccess($"Loaded {loaded.Count} convex volumes from {Path.GetFileName(dialog.FileName)}");
                }
                catch (Exception ex)
                {
                    _console?.LogError($"Failed to load volumes: {ex.Message}");
                    MessageBox.Show($"Error loading volumes:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnSaveVolumes(object? sender, EventArgs e)
        {
            if (_editableElements.ConvexVolumes.Count == 0)
            {
                MessageBox.Show("No convex volumes to save.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                Title = "Save Convex Volumes",
                DefaultExt = "xml",
                FileName = "ConvexVolumes.xml",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    Data.ConvexVolumeSerializer.SaveToXml(_editableElements.ConvexVolumes, dialog.FileName);
                    _console?.LogSuccess($"Saved {_editableElements.ConvexVolumes.Count} convex volumes to {Path.GetFileName(dialog.FileName)}");
                }
                catch (Exception ex)
                {
                    _console?.LogError($"Failed to save volumes: {ex.Message}");
                    MessageBox.Show($"Error saving volumes:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ========== Toolbar handlers (HB conformity) ==========
        
        private void OnRaytraceToggled(object? sender, EventArgs e)
        {
            if (sender is ToolStripButton btn)
            {
                _raytraceMode = btn.Checked;
                if (!_raytraceMode)
                {
                    _raytraceHitPoint = null;
                    _raytraceHitPolyIndex = -1;
                    _renderer?.SetRaytraceMarker(null);
                }
                _console?.Log($"Raytrace mode: {(_raytraceMode ? "ON" : "OFF")}");
            }
        }

        private void OnTestNavigationToggled(object? sender, EventArgs e)
        {
            if (sender is ToolStripButton btn)
            {
                _testNavMode = btn.Checked;
                if (!_testNavMode)
                {
                    ClearTestNavState();
                }
                else
                {
                    ClearTestNavState();
                }
                if (_glControl != null)
                    _glControl.Cursor = _testNavMode ? Cursors.Cross : Cursors.Default;
                _console?.Log($"Test Navigation mode: {(_testNavMode ? "ON — Click start point" : "OFF")}");
            }
        }

        private void ClearTestNavState()
        {
            _testNavStartPoint = null;
            _testNavStartPolyIndex = -1;
            _testNavEndPoint = null;
            _testNavEndPolyIndex = -1;
            _testNavPath = null;
            _renderer?.SetTestNavPath(null, null, null);
        }

        private void PlaceTestNavPoint(int screenX, int screenY)
        {
            if (_glControl == null || _currentMesh == null || _renderer == null) return;

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

                float closestDistance = float.MaxValue;
                Vector3 closestHit = Vector3.Zero;
                int closestPolyIndex = -1;

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
                                closestPolyIndex = i;
                            }
                        }
                    }
                }

                if (closestPolyIndex < 0)
                {
                    _console?.LogWarning("No navmesh intersection — click on the navmesh surface");
                    return;
                }

                var wowHit = CoordinateSystem.DetourToWow(closestHit);

                if (_testNavStartPoint == null)
                {
                    // First click: set start
                    _testNavStartPoint = closestHit;
                    _testNavStartPolyIndex = closestPolyIndex;
                    _testNavEndPoint = null;
                    _testNavEndPolyIndex = -1;
                    _testNavPath = null;

                    // Show start marker only
                    _renderer.SetTestNavPath(closestHit, null, new List<Vector3> { closestHit, closestHit });

                    byte area = _currentMesh.Polys[closestPolyIndex].Area;
                    _console?.LogSuccess($"Start: [{wowHit.X:F1}, {wowHit.Y:F1}, {wowHit.Z:F1}] Poly #{closestPolyIndex} ({Data.AreaTypeInfo.GetName(area)}) — Click end point");
                }
                else
                {
                    // Second click: set end and compute path
                    _testNavEndPoint = closestHit;
                    _testNavEndPolyIndex = closestPolyIndex;

                    byte area = _currentMesh.Polys[closestPolyIndex].Area;
                    _console?.Log($"End: [{wowHit.X:F1}, {wowHit.Y:F1}, {wowHit.Z:F1}] Poly #{closestPolyIndex} ({Data.AreaTypeInfo.GetName(area)})");

                    // Run A* pathfinding
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    _testNavPath = NavMeshPathfinder.FindPath(
                        _currentMesh, _testNavStartPoint.Value, _testNavStartPolyIndex,
                        closestHit, closestPolyIndex);
                    sw.Stop();

                    if (_testNavPath != null)
                    {
                        // Compute path distance
                        float totalDist = 0;
                        for (int i = 0; i < _testNavPath.Count - 1; i++)
                            totalDist += (_testNavPath[i] - _testNavPath[i + 1]).Length;

                        _renderer.SetTestNavPath(_testNavStartPoint, closestHit, _testNavPath);
                        _console?.LogSuccess($"Path found: {_testNavPath.Count} waypoints, {totalDist:F1} yards, {sw.ElapsedMilliseconds} ms");
                    }
                    else
                    {
                        _renderer.SetTestNavPath(_testNavStartPoint, closestHit, null);
                        _console?.LogError($"No path found between start and end ({sw.ElapsedMilliseconds} ms)");
                    }

                    // Reset for next attempt (keep displaying result)
                    _testNavStartPoint = null;
                    _testNavStartPolyIndex = -1;
                }
            }
            catch (Exception ex)
            {
                _console?.LogError($"Test Navigation error: {ex.Message}");
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

        private void OnAnalyzeNavMesh(object? sender, EventArgs e)
        {
            if (_currentMesh == null)
            {
                _console?.LogWarning("No mesh loaded — load a tile first");
                return;
            }

            var report = Core.NavMeshAnalyzer.GetAnalysisReport(_currentMesh);
            _console?.Log(report);

            // Switch to component color mode for visual feedback
            SetColorMode(Rendering.ColorMode.ByComponent);
            _console?.LogSuccess("Color mode switched to 'By Component' — each connected region has a distinct color");
        }

        private void OnExportPathJson(object? sender, EventArgs e)
        {
            if (_testNavPath == null || _testNavPath.Count < 2)
            {
                _console?.LogWarning("No path to export — use Test Navigation first");
                return;
            }
            if (_testNavPath.Any(p => float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z) ||
                                      float.IsInfinity(p.X) || float.IsInfinity(p.Y) || float.IsInfinity(p.Z)))
            {
                _console?.LogError("Path contains NaN/Infinity values — cannot export");
                return;
            }

            using var dlg = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                DefaultExt = "json",
                FileName = "path_export.json"
            };

            if (dlg.ShowDialog() != DialogResult.OK) return;

            try
            {
                var pathData = new
                {
                    waypoints = _testNavPath.Select(p => new { x = Math.Round(p.X, 4), y = Math.Round(p.Y, 4), z = Math.Round(p.Z, 4) }).ToArray(),
                    waypointCount = _testNavPath.Count,
                    totalDistance = ComputePathDistance(_testNavPath),
                    coordinateSystem = "Detour"
                };

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(pathData, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(dlg.FileName, json);
                _console?.LogSuccess($"Path exported to JSON: {dlg.FileName} ({_testNavPath.Count} waypoints)");
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                _console?.LogError($"Export failed: {ex.Message}");
                MessageBox.Show($"Failed to export:\n{ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnExportPathCsv(object? sender, EventArgs e)
        {
            if (_testNavPath == null || _testNavPath.Count < 2)
            {
                _console?.LogWarning("No path to export — use Test Navigation first");
                return;
            }
            if (_testNavPath.Any(p => float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z) ||
                                      float.IsInfinity(p.X) || float.IsInfinity(p.Y) || float.IsInfinity(p.Z)))
            {
                _console?.LogError("Path contains NaN/Infinity values — cannot export");
                return;
            }

            using var dlg = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                DefaultExt = "csv",
                FileName = "path_export.csv"
            };

            if (dlg.ShowDialog() != DialogResult.OK) return;

            try
            {
                var lines = new List<string> { "Index,X,Y,Z" };
                for (int i = 0; i < _testNavPath.Count; i++)
                {
                    var p = _testNavPath[i];
                    lines.Add($"{i},{p.X:F4},{p.Y:F4},{p.Z:F4}");
                }

                System.IO.File.WriteAllLines(dlg.FileName, lines);
                _console?.LogSuccess($"Path exported to CSV: {dlg.FileName} ({_testNavPath.Count} waypoints)");
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                _console?.LogError($"Export failed: {ex.Message}");
                MessageBox.Show($"Failed to export:\n{ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnExportPathHbXml(object? sender, EventArgs e)
        {
            if (_testNavPath == null || _testNavPath.Count < 2)
            {
                _console?.LogWarning("No path to export — use Test Navigation first");
                return;
            }
            if (_testNavPath.Any(p => float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z) ||
                                      float.IsInfinity(p.X) || float.IsInfinity(p.Y) || float.IsInfinity(p.Z)))
            {
                _console?.LogError("Path contains NaN/Infinity values — cannot export");
                return;
            }

            using var dlg = new SaveFileDialog
            {
                Filter = "XML files (*.xml)|*.xml",
                DefaultExt = "xml",
                FileName = "path_hotspots.xml"
            };

            if (dlg.ShowDialog() != DialogResult.OK) return;

            try
            {
                // Convert Detour → WoW coordinates for HB format
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                sb.AppendLine("<HBProfile>");
                sb.AppendLine("  <Hotspots>");
                foreach (var detourPos in _testNavPath)
                {
                    var wow = Core.CoordinateSystem.DetourToWow(detourPos);
                    sb.AppendLine($"    <Hotspot X=\"{wow.X:F4}\" Y=\"{wow.Y:F4}\" Z=\"{wow.Z:F4}\" />");
                }
                sb.AppendLine("  </Hotspots>");
                sb.AppendLine("</HBProfile>");

                System.IO.File.WriteAllText(dlg.FileName, sb.ToString());
                _console?.LogSuccess($"Path exported to HB XML: {dlg.FileName} ({_testNavPath.Count} hotspots, WoW coords)");
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                _console?.LogError($"Export failed: {ex.Message}");
                MessageBox.Show($"Failed to export:\n{ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static float ComputePathDistance(List<OpenTK.Mathematics.Vector3> path)
        {
            float dist = 0f;
            for (int i = 0; i < path.Count - 1; i++)
                dist += (path[i] - path[i + 1]).Length;
            return dist;
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
