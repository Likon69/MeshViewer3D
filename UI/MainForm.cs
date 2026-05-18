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
using MeshViewer3D.Core.Formats.Wdt;
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
        private CameraController? _cameraController;
        private NavMeshRenderer? _renderer;

        // Composants UI
        private MinimapControl? _minimap;
        private ConsoleControl? _console;
        private Label? _overlayLabel;
        private Label? _testNavStartTag;
        private Label? _testNavEndTag;
        private System.Windows.Forms.Timer? _renderTimer;
        private TabControl? _editorTabs;
        private SplitContainer? _splitMain;
        private SplitContainer? _splitViewport;
        private BlackspotPanel? _blackspotPanel;
        private JumpLinksPanel? _jumpLinksPanel;
        private SettingsPanel? _settingsPanel;
        private ConvexVolumesPanel? _volumesPanel;
        private GameObjectPanel? _gameObjectPanel;
        private WmoBlacklistPanel? _wmoBlacklistPanel;
        private PerModelPanel? _perModelPanel;
        private ToolStripMenuItem? _freeCameraMenuItem;
        private Button? _collapseBtn;
        private bool _rightPanelCollapsed = false;
        private int _savedSplitDistance = -1;

        // État
        private NavMeshData? _currentMesh;
        private readonly List<(int TileX, int TileY)> _loadedTileCoords = new();
        private EditableElements _editableElements = new();
        private DateTime _lastFrameTime = DateTime.Now;
        private float _fps;
        private bool _blackspotClickMode = false;
        private bool _jumpLinkClickMode = false;
        private bool _volumeClickMode = false;
        private readonly HashSet<Keys> _pressedKeys = new();

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
        private bool _testNavHasAttemptedPath = false;
        private float _testNavPathDistanceYards = 0f;
        private float _testNavDirectDistanceYards = 0f;
        private long _testNavLastSolveMs = 0;

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
            this.KeyUp += MainForm_KeyUp;
            this.Deactivate += (_, _) => _pressedKeys.Clear();
        }

        private void MainForm_KeyUp(object? sender, KeyEventArgs e)
        {
            _pressedKeys.Remove(e.KeyCode);
        }
        
        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            _pressedKeys.Add(e.KeyCode);

            if (_cameraController != null && _cameraController.OnKeyDown(e))
            {
                _glControl?.Invalidate();
                e.Handled = true;
                return;
            }

            if (_camera.FreeCameraMode && !e.Control && !e.Alt)
            {
                if (e.KeyCode == Keys.W || e.KeyCode == Keys.A || e.KeyCode == Keys.S ||
                    e.KeyCode == Keys.D || e.KeyCode == Keys.Q || e.KeyCode == Keys.E)
                {
                    e.Handled = true;
                    return;
                }
            }

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
            // C - Toggle free camera mode
            else if (e.KeyCode == Keys.C && !e.Control && !e.Alt)
            {
                OnToggleFreeCamera(null, EventArgs.Empty);
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
            mapMenu.DropDownItems.Add("Load Tiles...", null, OnLoadTiles);
            mapMenu.DropDownItems.Add("Load Folder...", null, OnLoadFolder);
            mapMenu.DropDownItems.Add(new ToolStripSeparator());
            mapMenu.DropDownItems.Add("Load WDT (Tile Grid)...", null, OnLoadWdt);
            mapMenu.DropDownItems.Add("Load Terrain from ADT...", null, OnLoadTerrain);
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
            _freeCameraMenuItem = new ToolStripMenuItem("Free Camera Mode (C)", null, OnToggleFreeCamera)
            {
                Checked = _camera.FreeCameraMode,
                CheckOnClick = false
            };
            viewMenu.DropDownItems.Add(_freeCameraMenuItem);
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
            meshMenu.DropDownItems.Add(new ToolStripSeparator());
            meshMenu.DropDownItems.Add("Bake Blackspots into Tile", null, OnBakeBlackspots);
            meshMenu.DropDownItems.Add("Bake Jump Links into Tile", null, OnBakeOffMeshConnections);
            meshMenu.DropDownItems.Add("Save Modified Tile (.mmtile)...", null, OnSaveModifiedTile);
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

            // Panel droit (minimap + tabs d'édition)
            var rightPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(37, 37, 38)
            };

            // Inner panel holds minimap + tabs — added FIRST so Fill docks after Left button
            var rightInnerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(37, 37, 38)
            };
            rightPanel.Controls.Add(rightInnerPanel);

            // Collapse/expand toggle — narrow vertical strip docked to left edge of right panel
            // Added AFTER inner panel so WinForms docks it first (takes 20px), inner panel fills rest
            _collapseBtn = new Button
            {
                Dock = DockStyle.Left,
                Width = 20,
                Text = "◄",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                Font = new Font("Consolas", 8, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            _collapseBtn.FlatAppearance.BorderSize = 0;
            _collapseBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(80, 80, 80);
            _collapseBtn.Click += ToggleRightPanel;
            rightPanel.Controls.Add(_collapseBtn);

            // Minimap — created here, added to GL viewport panel below (floats over 3D view, always visible)
            _minimap = new MinimapControl
            {
                Size = new Size(150, 150),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            // TabControl d'édition — fills remaining space, tab strip on right edge
            _editorTabs = new TabControl
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                Alignment = TabAlignment.Right
            };
            rightInnerPanel.Controls.Add(_editorTabs);
            // Dock.Fill must be added BEFORE Dock.Top for correct z-order
            _editorTabs.BringToFront();

            // Onglet Settings (comme dans HB)
            _settingsPanel = new SettingsPanel();
            _settingsPanel.WireframeChanged   += (s, e) => { if (_renderer != null) _renderer.ShowWireframe    = e; };
            _settingsPanel.NavMeshFillChanged += (s, e) => { if (_renderer != null) _renderer.ShowNavMeshFill  = e; };
            _settingsPanel.LightingChanged    += (s, e) => { if (_renderer != null) _renderer.EnableLighting   = e; };
            _settingsPanel.OffMeshChanged     += (s, e) => { if (_renderer != null) _renderer.ShowOffMesh      = e; };
            _settingsPanel.BlackspotsChanged  += (s, e) => { if (_renderer != null) _renderer.ShowBlackspots   = e; };
            _settingsPanel.VolumesChanged     += (s, e) => { if (_renderer != null) _renderer.ShowVolumes      = e; };
            _settingsPanel.TerrainChanged     += (s, e) => { if (_renderer != null) _renderer.ShowTerrain      = e; };
            _settingsPanel.ColorModeChanged   += (s, mode) => { if (_renderer != null) _renderer.ColorMode = mode; _console?.Log($"Color mode: {mode}"); };
            _settingsPanel.WireframeAlphaChanged += (s, v) => { if (_renderer != null) _renderer.WireAlpha = v; };
            _settingsPanel.MeshAlphaChanged   += (s, v) => { if (_renderer != null) _renderer.MeshFillAlpha = v / 100f; };
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
            _gameObjectPanel.BakeRequested += OnBakeObjects;
            _gameObjectPanel.UnbakeRequested += OnUnbakeObjects;
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

            // ── Layout with resizable SplitContainers ─────────────────────────
            // splitViewport: top = GLControl, bottom = Console (horizontal splitter)
            _splitViewport = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                BackColor = Color.FromArgb(60, 60, 60),
                SplitterWidth = 5,
                FixedPanel = FixedPanel.Panel2
            };
            _splitViewport.Panel1.Controls.Add(_glControl);
            _splitViewport.Panel2.Controls.Add(_console);
            _console.Dock = DockStyle.Fill;
            _splitViewport.SplitterDistance = 500; // corrected on load

            // splitMain: left = viewport+console, right = settings panel (vertical splitter)
            _splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                BackColor = Color.FromArgb(60, 60, 60),
                SplitterWidth = 5,
                FixedPanel = FixedPanel.Panel2,
                Panel2MinSize = 20
            };
            _splitMain.Panel1.Controls.Add(_splitViewport);
            _splitMain.Panel2.Controls.Add(rightPanel);
            _splitMain.SplitterDistance = 800; // corrected on load

            this.Controls.Add(_splitMain);
            _splitMain.BringToFront();

            // Restore saved positions or use defaults
            this.Load += (_, _) =>
            {
                if (AppSettings.SplitMainDistance > 0 && AppSettings.SplitMainDistance < _splitMain.Width - 50)
                    _splitMain.SplitterDistance = AppSettings.SplitMainDistance;
                else
                    _splitMain.SplitterDistance = Math.Max(200, _splitMain.Width - 260);

                if (AppSettings.SplitViewportDistance > 0 && AppSettings.SplitViewportDistance < _splitViewport.Height - 30)
                    _splitViewport.SplitterDistance = AppSettings.SplitViewportDistance;
                else
                    _splitViewport.SplitterDistance = Math.Max(100, _splitViewport.Height - 120);

                // Position minimap at top-right of GL viewport (Anchor keeps it there on resize)
                _minimap.Location = new Point(_splitViewport.Panel1.Width - _minimap.Width - 5, 5);
            };

            // Save positions on close
            this.FormClosing += (_, _) =>
            {
                // Save the real (expanded) distance even if currently collapsed
                AppSettings.SplitMainDistance = _rightPanelCollapsed ? _savedSplitDistance : _splitMain.SplitterDistance;
                AppSettings.SplitViewportDistance = _splitViewport.SplitterDistance;
                AppSettings.Save();
            };

            // Overlay info - Label flottant au-dessus du GLControl (pas dedans pour éviter clignotement)
            _overlayLabel = new Label
            {
                AutoSize = true,
                Location = new Point(15, 5),
                BackColor = Color.FromArgb(180, 0, 0, 0),
                ForeColor = Color.White,
                Font = new Font("Consolas", 10),
                Padding = new Padding(8),
                Text = ""
            };
            _splitViewport.Panel1.Controls.Add(_overlayLabel);
            _overlayLabel.BringToFront();

            // Minimap floats over GL viewport — anchored top-right, always visible even when panel collapsed
            _splitViewport.Panel1.Controls.Add(_minimap);
            _minimap.BringToFront();

            _testNavStartTag = new Label
            {
                AutoSize = true,
                BackColor = Color.FromArgb(190, 25, 20, 20),
                ForeColor = Color.FromArgb(255, 220, 220),
                Font = new Font("Consolas", 9, FontStyle.Bold),
                Padding = new Padding(4, 1, 4, 1),
                Text = "Start",
                Visible = false
            };
            _splitViewport.Panel1.Controls.Add(_testNavStartTag);
            _testNavStartTag.BringToFront();

            _testNavEndTag = new Label
            {
                AutoSize = true,
                BackColor = Color.FromArgb(190, 20, 45, 20),
                ForeColor = Color.FromArgb(220, 255, 220),
                Font = new Font("Consolas", 9, FontStyle.Bold),
                Padding = new Padding(4, 1, 4, 1),
                Text = "End",
                Visible = false
            };
            _splitViewport.Panel1.Controls.Add(_testNavEndTag);
            _testNavEndTag.BringToFront();

            _console.Log("MeshViewer3D initialized. Quality: Honorbuddy/Apoc level.");

            // Timer de rendu (60 FPS)
            _renderTimer = new System.Windows.Forms.Timer
            {
                Interval = 16  // ~60 FPS
            };
            _renderTimer.Tick += (s, e) => _glControl?.Invalidate();
            _renderTimer.Start();

            // Focus camera default
            _camera.Reset();
            InitializeCameraController();
        }

        private void InitializeCameraController()
        {
            _cameraController = new CameraController(
                _camera,
                (screenX, screenY) => RaycastNavMeshPoint(screenX, screenY),
                () => GetCurrentSceneBounds());
        }

        private (Vector3 bMin, Vector3 bMax)? GetCurrentSceneBounds()
        {
            if (_currentMesh == null)
                return null;

            return (_currentMesh.Header.BMin, _currentMesh.Header.BMax);
        }

        private Vector3? RaycastNavMeshPoint(int screenX, int screenY)
        {
            if (_glControl == null || _currentMesh == null)
                return null;

            var view = _camera.GetViewMatrix();
            var projection = _camera.GetProjectionMatrix(
                (float)_glControl.Width / _glControl.Height);

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

            return foundHit ? closestHit : null;
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
            _renderer.SetLogCallback(msg => _console?.Log(msg));
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

            if (_camera.FreeCameraMode)
                UpdateFreeCameraMovement(deltaTime);

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

            if (_cameraController != null && _cameraController.OnMouseDown(e, ModifierKeys))
            {
                _isDragging = false;
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

                return;
            }

            _isDragging = false;
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

            if (_cameraController != null && _cameraController.OnMouseMove(e, ModifierKeys))
            {
                return;
            }

            if (!_isDragging) return;
        }

        private void GlControl_MouseUp(object? sender, MouseEventArgs e)
        {
            _cameraController?.OnMouseUp(e);

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
            if (_cameraController != null)
                _cameraController.OnMouseWheel(e);
            else
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
                    _loadedTileCoords.Clear();
                    _loadedTileCoords.Add((_currentMesh.TileX, _currentMesh.TileY));
                    _renderer?.LoadMesh(_currentMesh);
                    
                    // Frame scene from bounds (no fixed magic distance)
                    _cameraController?.FrameScene();
                    
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

            var files = Directory.GetFiles(fbd.SelectedPath, "*.mmtile")
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length == 0)
            {
                _console?.LogWarning("No .mmtile files found in selected folder");
                return;
            }

            LoadTileFiles(files, allowAutoSubset: true);
        }

        private void OnLoadTiles(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "NavMesh Tiles (*.mmtile)|*.mmtile",
                Title = "Select Navigation Mesh Tiles",
                Multiselect = true
            };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            var files = ofd.FileNames
                .Where(f => f.EndsWith(".mmtile", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
            {
                _console?.LogWarning("No .mmtile files selected");
                return;
            }

            LoadTileFiles(files, allowAutoSubset: true);
        }

        private void LoadTileFiles(string[] files, bool allowAutoSubset)
        {
            _console?.Log($"Loading {files.Length} tiles...");

            var allMeshes = new List<NavMeshData>();
            int loaded = 0, failed = 0;

            foreach (var file in files)
            {
                try
                {
                    var mesh = MmtileLoader.LoadTile(file);
                    allMeshes.Add(mesh);
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

            // If we exceed the ushort-based merge limit, either fail (manual selection) or pick a compact subset (folder load).
            int totalVerts = 0;
            foreach (var m in allMeshes) totalVerts += m.Vertices.Length;

            var meshesToMerge = allMeshes;
            if (totalVerts > NavMeshData.MaxMergeVertexCount)
            {
                if (!allowAutoSubset)
                {
                    _console?.LogError(
                        $"Cannot load {allMeshes.Count} selected tiles: {totalVerts} total vertices exceed merge limit " +
                        $"({NavMeshData.MaxMergeVertexCount}). Select fewer tiles.");
                    return;
                }

                float centerX = (float)allMeshes.Average(m => m.TileX);
                float centerY = (float)allMeshes.Average(m => m.TileY);

                meshesToMerge = allMeshes
                    .OrderBy(m =>
                    {
                        float dx = m.TileX - centerX;
                        float dy = m.TileY - centerY;
                        return dx * dx + dy * dy;
                    })
                    .ToList();

                int runningVerts = 0;
                var selected = new List<NavMeshData>();
                foreach (var mesh in meshesToMerge)
                {
                    if (runningVerts + mesh.Vertices.Length > NavMeshData.MaxMergeVertexCount)
                        continue;

                    selected.Add(mesh);
                    runningVerts += mesh.Vertices.Length;
                }

                if (selected.Count == 0)
                {
                    _console?.LogError($"Cannot load tiles: even a single tile exceeds merge limit ({NavMeshData.MaxMergeVertexCount}).");
                    _minimap?.Clear();
                    return;
                }

                meshesToMerge = selected;
                _console?.LogWarning(
                    $"Total vertices ({totalVerts}) exceed merge limit ({NavMeshData.MaxMergeVertexCount}). " +
                    $"Loaded nearest subset: {meshesToMerge.Count}/{allMeshes.Count} tiles ({runningVerts} verts).");
            }

            _minimap?.Clear();
            foreach (var mesh in allMeshes)
                _minimap?.SetTileLoaded(mesh.TileX, mesh.TileY, true);

            _loadedTileCoords.Clear();
            foreach (var mesh in allMeshes)
                _loadedTileCoords.Add((mesh.TileX, mesh.TileY));

            _currentMesh = NavMeshData.Merge(meshesToMerge);
            _renderer?.LoadMesh(_currentMesh);
            _renderer?.LoadTileSeams(meshesToMerge);
            _cameraController?.FrameScene();
            _minimap?.SetCurrentTile(_currentMesh.TileX, _currentMesh.TileY);

            _console?.LogSuccess($"Loaded {meshesToMerge.Count} tiles" +
                (failed > 0 ? $" ({failed} failed)" : string.Empty) +
                $" — {_currentMesh.Polys.Length} polys, {_currentMesh.Vertices.Length} verts");

            if (meshesToMerge.Count != allMeshes.Count)
            {
                _console?.LogWarning(
                    $"NavMesh render is limited to {meshesToMerge.Count}/{allMeshes.Count} tiles due to vertex index limits, " +
                    "but terrain and world objects will still load for all selected tiles.");
            }

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

            var tilesToLoad = _loadedTileCoords.Count > 0
                ? _loadedTileCoords.Distinct().ToList()
                : new List<(int TileX, int TileY)> { (_currentMesh.TileX, _currentMesh.TileY) };

            _console?.Log($"Loading ADT scene for {tilesToLoad.Count} tile(s)...");

            try
            {
                using var provider = WowDataProvider.Open(_wowDataPath);
                _console?.Log($"[MPQ] {provider.ArchivesLoaded} archives loaded:");
                foreach (var entry in provider.LoadLog)
                    _console?.Log($"  {entry}");
                _renderer.ClearWorldScene();

                bool panelSeeded = false;
                int adtLoaded = 0;
                int totalWmoPlacements = 0;

                foreach (var (tileX, tileY) in tilesToLoad)
                {
                    string adtPath = $@"World\Maps\{mapDir}\{mapDir}_{tileX}_{tileY}.adt";
                    byte[]? adtBytes = provider.GetFileBytes(adtPath);
                    if (adtBytes == null)
                    {
                        _console?.LogWarning($"ADT not found in MPQ: {adtPath}");
                        _console?.Log(provider.DiagnoseFile(adtPath));
                        continue;
                    }

                    var adt = AdtFile.Load(adtBytes);
                    adt.TileX = tileX;
                    adt.TileY = tileY;

                    if (!panelSeeded)
                    {
                        _gameObjectPanel?.LoadObjects(adt);
                        _wmoBlacklistPanel?.LoadWmoNames(adt.WmoNames);
                        _perModelPanel?.LoadModelNames(adt.WmoNames, adt.M2Names);
                        panelSeeded = true;
                    }

                    _renderer.LoadWorldObjects(provider, adt, clearExisting: false);
                    _renderer.LoadTerrain(adt, provider, mapDir, includeAdjacentTiles: false, clearExisting: false);

                    adtLoaded++;
                    totalWmoPlacements += adt.WmoInstances.Length;
                }

                if (adtLoaded == 0)
                {
                    _console?.LogWarning("No ADT files were loaded from MPQ for the selected tiles.");
                    return;
                }

                _console?.LogSuccess($"Loaded ADT scene for {adtLoaded}/{tilesToLoad.Count} tile(s), {totalWmoPlacements} WMO placements total");
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

        // ────────────────────────────────────────────────────────────────────
        //  WDT tile grid loading
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Loads a WDT file to populate the minimap with the tile existence grid.
        /// Shows which ADT tiles exist for the selected map.
        /// </summary>
        private void OnLoadWdt(object? sender, EventArgs e)
        {
            if (_wowDataPath == null)
            {
                _console?.LogWarning("Set the WoW Data folder first (Map > Set WoW Data Folder)");
                MessageBox.Show("Please set the WoW Data folder first.\nMap > Set WoW Data Folder",
                    "WoW Data Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Prompt for map ID
            string? input = ShowInputDialog("Load WDT Tile Grid", "Enter Map ID:\n(e.g. 0=Eastern Kingdoms, 1=Kalimdor, 571=Northrend)", "0");
            if (string.IsNullOrEmpty(input) || !int.TryParse(input, out int mapId))
                return;

            string? mapDir = GetMapDirectory(mapId);
            if (mapDir == null)
            {
                _console?.LogError($"Unknown map ID {mapId} — not found in Maps.json");
                return;
            }

            string wdtPath = $"World\\Maps\\{mapDir}\\{mapDir}.wdt";
            _console?.Log($"Loading WDT: {wdtPath}...");

            try
            {
                using var provider = WowDataProvider.Open(_wowDataPath);
                byte[]? wdtBytes = provider.GetFileBytes(wdtPath);
                if (wdtBytes == null)
                {
                    _console?.LogError($"WDT not found in MPQ: {wdtPath}");
                    _console?.Log(provider.DiagnoseFile(wdtPath));
                    return;
                }

                var wdt = WdtFile.Load(wdtBytes);
                int tileCount = wdt.TileCount;

                // Update minimap with tile grid
                _minimap?.Clear();
                foreach (var (tileX, tileY) in wdt.GetExistingTiles())
                    _minimap?.SetTileLoaded(tileX, tileY, true);

                string? mapName = MapDatabase.GetName(mapId);
                _console?.LogSuccess($"WDT loaded: {mapName ?? mapDir} — {tileCount} tiles exist out of 4096 (64×64 grid)");

                if (wdt.IsGlobalWmo)
                    _console?.LogWarning("  This map is a global WMO dungeon (single WMO file, no terrain)");
            }
            catch (Exception ex)
            {
                _console?.LogError($"Failed to load WDT: {ex.Message}");
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  Terrain heightmap loading
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Loads terrain heightmap from the ADT file for the currently loaded tile.
        /// Requires WoW data path and a loaded .mmtile.
        /// </summary>
        private void OnLoadTerrain(object? sender, EventArgs e)
        {
            if (_wowDataPath == null)
            {
                _console?.LogWarning("Set the WoW Data folder first (Map > Set WoW Data Folder)");
                return;
            }

            if (_currentMesh == null || _renderer == null)
            {
                _console?.LogWarning("Load a .mmtile first to identify the ADT file");
                return;
            }

            string? mapDir = GetMapDirectory(_currentMesh.MapId);
            if (mapDir == null)
            {
                _console?.LogError($"Unknown map ID {_currentMesh.MapId}");
                return;
            }

            var tilesToLoad = _loadedTileCoords.Count > 0
                ? _loadedTileCoords.Distinct().ToList()
                : new List<(int TileX, int TileY)> { (_currentMesh.TileX, _currentMesh.TileY) };

            _console?.Log($"Loading terrain from ADT for {tilesToLoad.Count} tile(s)...");

            try
            {
                using var provider = WowDataProvider.Open(_wowDataPath);

                int loadedTerrainTiles = 0;
                bool includeAdjacent = tilesToLoad.Count == 1;
                bool firstTerrainTile = true;

                foreach (var (tileX, tileY) in tilesToLoad)
                {
                    string adtPath = $"World\\Maps\\{mapDir}\\{mapDir}_{tileX}_{tileY}.adt";
                    byte[]? adtBytes = provider.GetFileBytes(adtPath);
                    if (adtBytes == null)
                    {
                        _console?.LogWarning($"ADT not found in MPQ: {adtPath}");
                        continue;
                    }

                    var adt = AdtFile.Load(adtBytes);
                    adt.TileX = tileX;
                    adt.TileY = tileY;

                    int chunkCount = 0;
                    for (int i = 0; i < adt.TerrainChunks.Length; i++)
                        if (adt.TerrainChunks[i] != null) chunkCount++;

                    _console?.Log($"  ADT ({tileX},{tileY}) has {chunkCount}/256 terrain chunks, {adt.TextureNames.Length} textures");

                    _renderer.LoadTerrain(adt, provider, mapDir,
                        includeAdjacentTiles: includeAdjacent,
                        clearExisting: firstTerrainTile);
                    firstTerrainTile = false;
                    loadedTerrainTiles++;
                }

                if (loadedTerrainTiles == 0)
                {
                    _console?.LogWarning("No terrain ADT files were loaded.");
                    return;
                }

                string? mapName = MapDatabase.GetName(_currentMesh.MapId);
                _console?.LogSuccess($"Terrain loaded for {mapName ?? mapDir}: {loadedTerrainTiles}/{tilesToLoad.Count} tile(s)");
            }
            catch (Exception ex)
            {
                _console?.LogError($"Failed to load terrain: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows a simple input dialog with a TextBox.
        /// Returns the entered text, or null if cancelled.
        /// </summary>
        private static string? ShowInputDialog(string title, string prompt, string defaultValue = "")
        {
            using var form = new Form
            {
                Text = title,
                Size = new Size(400, 200),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
            };

            var lbl = new Label { Text = prompt, Location = new Point(10, 10), AutoSize = true };
            var txt = new TextBox { Text = defaultValue, Location = new Point(10, 70), Width = 360 };
            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(200, 110) };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(290, 110) };

            form.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
            form.AcceptButton = btnOk;
            form.CancelButton = btnCancel;

            txt.SelectAll();
            txt.Focus();

            return form.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : null;
        }

        private void OnCloseTile(object? sender, EventArgs e)
        {
            _loadedTileCoords.Clear();
            _currentMesh = null;
            _renderer?.ClearLoadedData();
            ClearTestNavState();
            _minimap?.Clear();
            _console?.Log("Tile closed");
        }

        private void OnToggleWireframe(object? sender, EventArgs e)
        {
            if (_renderer == null || sender is not ToolStripMenuItem item) return;
            item.Checked = !item.Checked;
            _renderer.ShowWireframe = item.Checked;
            if (_settingsPanel != null) _settingsPanel.ShowWireframe = item.Checked;
            _console?.Log($"Wireframe: {(item.Checked ? "ON" : "OFF")}");
        }

        private void OnToggleOffMesh(object? sender, EventArgs e)
        {
            if (_renderer == null || sender is not ToolStripMenuItem item) return;
            item.Checked = !item.Checked;
            _renderer.ShowOffMesh = item.Checked;
            if (_settingsPanel != null) _settingsPanel.ShowOffMesh = item.Checked;
            _console?.Log($"OffMesh: {(item.Checked ? "ON" : "OFF")}");
        }

        private void OnToggleLighting(object? sender, EventArgs e)
        {
            if (_renderer == null || sender is not ToolStripMenuItem item) return;
            item.Checked = !item.Checked;
            _renderer.EnableLighting = item.Checked;
            if (_settingsPanel != null) _settingsPanel.ShowLighting = item.Checked;
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
                _cameraController?.FrameScene();
            }
            else
            {
                _camera.Reset();
            }
            _console?.Log("Camera reset");
        }

        private void OnToggleFreeCamera(object? sender, EventArgs e)
        {
            _camera.FreeCameraMode = !_camera.FreeCameraMode;
            if (_freeCameraMenuItem != null)
                _freeCameraMenuItem.Checked = _camera.FreeCameraMode;

            if (!_camera.FreeCameraMode)
                _pressedKeys.Clear();

            _console?.Log($"Free camera: {(_camera.FreeCameraMode ? "ON" : "OFF")}");
        }

        private void UpdateFreeCameraMovement(float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            float speed = _camera.FreeMoveSpeed;
            if (_pressedKeys.Contains(Keys.ShiftKey) || _pressedKeys.Contains(Keys.LShiftKey) || _pressedKeys.Contains(Keys.RShiftKey))
                speed *= _camera.PrecisionMultiplier;

            float forward = 0f;
            float right = 0f;
            float up = 0f;

            if (_pressedKeys.Contains(Keys.W)) forward += 1f;
            if (_pressedKeys.Contains(Keys.S)) forward -= 1f;
            if (_pressedKeys.Contains(Keys.D)) right -= 1f;
            if (_pressedKeys.Contains(Keys.A)) right += 1f;
            if (_pressedKeys.Contains(Keys.E)) up += 1f;
            if (_pressedKeys.Contains(Keys.Q)) up -= 1f;

            if (forward == 0f && right == 0f && up == 0f)
                return;

            var move = new Vector3(right, up, forward);
            if (move.LengthSquared > 1f)
                move = Vector3.Normalize(move);

            float frameStep = speed * deltaTime;
            _camera.TranslateLocal(move.Z * frameStep, move.X * frameStep, move.Y * frameStep);
        }

        private void ToggleRightPanel(object? sender, EventArgs e)
        {
            if (_splitMain == null || _collapseBtn == null) return;

            if (!_rightPanelCollapsed)
            {
                // Collapse: save current distance then shrink Panel2 to just the button strip
                _savedSplitDistance = _splitMain.SplitterDistance;
                _splitMain.SplitterDistance = _splitMain.Width - 20 - _splitMain.SplitterWidth;
                _collapseBtn.Text = "►";
                _rightPanelCollapsed = true;
            }
            else
            {
                // Expand: restore saved distance
                int dist = _savedSplitDistance > 0
                    ? _savedSplitDistance
                    : Math.Max(200, _splitMain.Width - 260);
                _splitMain.SplitterDistance = dist;
                _collapseBtn.Text = "◄";
                _rightPanelCollapsed = false;
            }
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
                var projection = _camera.GetProjectionMatrix(
                    (float)_glControl.Width / _glControl.Height);
                
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
                var projection = _camera.GetProjectionMatrix(
                    (float)_glControl.Width / _glControl.Height);

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
                var projection = _camera.GetProjectionMatrix(
                    (float)_glControl.Width / _glControl.Height);
                
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
                var wowTarget = CoordinateSystem.DetourToWow(_camera.Target);
                var wowEye = CoordinateSystem.DetourToWow(_camera.GetEyePosition());

                // Minimap — point rouge suit la caméra en temps réel
                var (camTileX, camTileY) = CoordinateSystem.WorldToTile(wowEye);
                _minimap?.SetCurrentTile(camTileX, camTileY);

                string modeText = _blackspotClickMode ? "\n[CLICK MODE - Place Blackspot]"
                    : _jumpLinkClickMode ? $"\n[CLICK MODE - Place Jump Link {(_jumpLinksPanel?.PendingStartPoint == null ? "Start" : "End")}]"
                    : _volumeClickMode ? $"\n[VOLUME MODE - {_volumesPanel?.InProgressVertices.Count ?? 0} vertices (Enter to finalize)]"
                    : "";

                if (_raytraceMode && _raytraceHitPoint.HasValue && _raytraceHitPolyIndex >= 0)
                {
                    var wowHit = CoordinateSystem.DetourToWow(_raytraceHitPoint.Value);
                    var (tileX, tileY) = CoordinateSystem.WorldToTile(wowHit);
                    byte area = _currentMesh.Polys[_raytraceHitPolyIndex].Area;
                    string areaName = Data.AreaTypeInfo.GetName(area);
                    modeText += $"\n[RAYTRACE] {{{wowHit.X:G7}, {wowHit.Y:G7}, {wowHit.Z:G7}}} | Tile: {{{tileX}, {tileY}}} | Poly #{_raytraceHitPolyIndex} | {areaName} ({area})";
                }
                else if (_raytraceMode)
                {
                    modeText += "\n[RAYTRACE] No hit";
                }

                if (_testNavMode)
                {
                    modeText += "\n[TEST NAV] Shift+Click = Start | Click = End";

                    if (_testNavStartPoint.HasValue && _testNavEndPoint.HasValue && _testNavHasAttemptedPath)
                    {
                        float pathMeters = _testNavPathDistanceYards * 0.9144f;
                        float directMeters = _testNavDirectDistanceYards * 0.9144f;

                        if (_testNavPath != null)
                        {
                            float detourFactor = _testNavDirectDistanceYards > 0.001f
                                ? _testNavPathDistanceYards / _testNavDirectDistanceYards
                                : 0f;
                            modeText += $"\n[TEST NAV] Path: {_testNavPathDistanceYards:F1} yd / {pathMeters:F1} m | {_testNavPath.Count} pts | straight {_testNavDirectDistanceYards:F1} yd / {directMeters:F1} m | x{detourFactor:F2} | {_testNavLastSolveMs} ms";
                        }
                        else
                        {
                            modeText += $"\n[TEST NAV] No path | straight {_testNavDirectDistanceYards:F1} yd / {directMeters:F1} m | {_testNavLastSolveMs} ms";
                        }
                    }
                    else if (_testNavStartPoint.HasValue && _testNavEndPoint == null)
                    {
                        var ws = CoordinateSystem.DetourToWow(_testNavStartPoint.Value);
                        modeText += $"\n[TEST NAV] Start: {{{ws.X:F1}, {ws.Y:F1}, {ws.Z:F1}}} — Click end point";
                    }
                    else if (!_testNavStartPoint.HasValue && _testNavEndPoint.HasValue)
                    {
                        var we = CoordinateSystem.DetourToWow(_testNavEndPoint.Value);
                        modeText += $"\n[TEST NAV] End: {{{we.X:F1}, {we.Y:F1}, {we.Z:F1}}} — Shift+Click start point";
                    }
                    else if (_testNavStartPoint == null)
                    {
                        modeText += "\n[TEST NAV] Shift+Click start point";
                    }
                }

                string? mapName = MapDatabase.GetName(_currentMesh.MapId);
                string mapLabel = mapName != null ? $"{mapName} (ID {_currentMesh.MapId})" : $"Map {_currentMesh.MapId}";

                _overlayLabel.Text = $"{mapLabel}\n" +
                                     $"Target: {{{wowTarget.X:F1}, {wowTarget.Y:F1}, {wowTarget.Z:F1}}}\n" +
                                     $"Eye: {{{wowEye.X:F1}, {wowEye.Y:F1}, {wowEye.Z:F1}}}\n" +
                                     $"Tile: ({camTileX}, {camTileY})\n" +
                                     $"Polys: {_currentMesh.Polys.Length} | Verts: {_currentMesh.Vertices.Length}\n" +
                                     $"Blackspots: {_editableElements.Blackspots.Count} | Volumes: {_editableElements.ConvexVolumes.Count}\n" +
                                     $"FPS: {_fps:F0} ({1000f/_fps:F1} ms)\n" +
                                     $"Camera: {(_camera.FreeCameraMode ? "Free" : "Orbit")}" + modeText;

                UpdateTestNavScreenTags();
            }
        }

        private void UpdateTestNavScreenTags()
        {
            if (_glControl == null)
                return;

            UpdateScreenTag(_testNavStartTag, _testNavMode ? _testNavStartPoint : null, 4, -18);
            UpdateScreenTag(_testNavEndTag, _testNavMode ? _testNavEndPoint : null, 4, -18);
        }

        private void UpdateScreenTag(Label? tag, Vector3? worldPos, int offsetX, int offsetY)
        {
            if (tag == null || _glControl == null)
                return;

            if (!worldPos.HasValue || !TryProjectWorldToScreen(worldPos.Value, out var screenPos))
            {
                tag.Visible = false;
                return;
            }

            int x = Math.Clamp(screenPos.X + offsetX, 0, Math.Max(0, _glControl.Width - tag.Width));
            int y = Math.Clamp(screenPos.Y + offsetY, 0, Math.Max(0, _glControl.Height - tag.Height));
            tag.Location = new Point(x, y);
            tag.Visible = true;
        }

        private bool TryProjectWorldToScreen(Vector3 worldPos, out Point screenPos)
        {
            screenPos = Point.Empty;
            if (_glControl == null || _glControl.Width <= 0 || _glControl.Height <= 0)
                return false;

            var view = _camera.GetViewMatrix();
            var projection = _camera.GetProjectionMatrix((float)_glControl.Width / _glControl.Height);

            var world = new Vector4(worldPos.X, worldPos.Y + 3.0f, worldPos.Z, 1.0f);
            var clip = MultiplyRowVectorByMatrix(world, view);
            clip = MultiplyRowVectorByMatrix(clip, projection);

            if (clip.W <= 0.0001f)
                return false;

            var ndc = new Vector3(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);
            if (ndc.X < -1.1f || ndc.X > 1.1f || ndc.Y < -1.1f || ndc.Y > 1.1f)
                return false;

            int sx = (int)MathF.Round((ndc.X * 0.5f + 0.5f) * _glControl.Width);
            int sy = (int)MathF.Round((1.0f - (ndc.Y * 0.5f + 0.5f)) * _glControl.Height);
            screenPos = new Point(sx, sy);
            return true;
        }

        private static Vector4 MultiplyRowVectorByMatrix(Vector4 v, Matrix4 m)
        {
            return new Vector4(
                v.X * m.M11 + v.Y * m.M21 + v.Z * m.M31 + v.W * m.M41,
                v.X * m.M12 + v.Y * m.M22 + v.Z * m.M32 + v.W * m.M42,
                v.X * m.M13 + v.Y * m.M23 + v.Z * m.M33 + v.W * m.M43,
                v.X * m.M14 + v.Y * m.M24 + v.Z * m.M34 + v.W * m.M44);
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

        private void OnSaveModifiedTile(object? sender, EventArgs e)
        {
            if (_currentMesh == null)
            {
                MessageBox.Show("No tile loaded.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Default filename matches extractor convention: MMMYYxx.mmtile
            string defaultName = Path.GetFileName(_currentMesh.FilePath);
            if (string.IsNullOrEmpty(defaultName))
                defaultName = $"{_currentMesh.MapId:D3}{_currentMesh.TileY:D2}{_currentMesh.TileX:D2}.mmtile";

            using var dialog = new SaveFileDialog
            {
                Filter = "MMTile Files (*.mmtile)|*.mmtile|All Files (*.*)|*.*",
                Title = "Save Modified Tile",
                DefaultExt = "mmtile",
                FileName = defaultName,
                InitialDirectory = Path.GetDirectoryName(_currentMesh.FilePath)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    Core.MmtileWriter.Save(_currentMesh, dialog.FileName);

                    // Verify round-trip: compare file size with expected
                    var fi = new FileInfo(dialog.FileName);
                    _console?.LogSuccess($"Saved modified tile to {Path.GetFileName(dialog.FileName)} ({fi.Length:N0} bytes)");
                }
                catch (Exception ex)
                {
                    _console?.LogError($"Failed to save tile: {ex.Message}");
                    MessageBox.Show($"Error saving tile:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnBakeBlackspots(object? sender, EventArgs e)
        {
            if (_currentMesh == null || _renderer == null)
            {
                _console?.LogWarning("No mesh loaded — cannot bake blackspots.");
                return;
            }
            if (_editableElements.Blackspots.Count == 0)
            {
                _console?.LogWarning("No blackspots placed — nothing to bake. Use B + click to place blackspots first.");
                return;
            }

            var result = MeshBaker.BakeBlackspots(_currentMesh, _editableElements.Blackspots);
            _renderer.LoadMesh(_currentMesh);
            _renderer.LoadEditableElements(_editableElements);
            _console?.LogSuccess($"Bake blackspots: {result.PolysMarked}/{result.PolysTotal} polys marked unwalkable ({_editableElements.Blackspots.Count} blackspots)");
        }

        private void OnBakeOffMeshConnections(object? sender, EventArgs e)
        {
            if (_currentMesh == null || _renderer == null)
            {
                _console?.LogWarning("No mesh loaded — cannot bake jump links.");
                return;
            }
            if (_editableElements.CustomOffMeshConnections.Count == 0)
            {
                _console?.LogWarning("No jump links placed — nothing to bake. Use Ctrl+Click in the Jump Links tab.");
                return;
            }

            int n = MeshBaker.BakeOffMeshConnections(_currentMesh, _editableElements.CustomOffMeshConnections);
            // Clear custom list — they are now part of the tile data, re-baking would duplicate them
            _editableElements.CustomOffMeshConnections.Clear();
            _jumpLinksPanel?.UpdateElements(_editableElements);
            _renderer.LoadMesh(_currentMesh);
            _renderer.LoadEditableElements(_editableElements);
            _console?.LogSuccess($"Baked {n} jump link(s) into tile — {_currentMesh.Header.OffMeshConCount} total offmesh connections. Use 'Save Modified Tile' to write the .mmtile.");
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
                ClearTestNavState();
                if (_glControl != null)
                    _glControl.Cursor = _testNavMode ? Cursors.Cross : Cursors.Default;
                _console?.Log($"Test Navigation mode: {(_testNavMode ? "ON — Shift+Click = Start, Click = End" : "OFF")}");
            }
        }

        private void ClearTestNavState()
        {
            _testNavStartPoint = null;
            _testNavStartPolyIndex = -1;
            _testNavEndPoint = null;
            _testNavEndPolyIndex = -1;
            _testNavPath = null;
            _testNavHasAttemptedPath = false;
            _testNavPathDistanceYards = 0f;
            _testNavDirectDistanceYards = 0f;
            _testNavLastSolveMs = 0;
            _renderer?.SetTestNavPath(null, null, null);
        }

        private void UpdateTestNavVisualization()
        {
            _renderer?.SetTestNavPath(_testNavStartPoint, _testNavEndPoint, _testNavPath);
        }

        private void RecalculateTestNavigationPath()
        {
            _testNavPath = null;
            _testNavHasAttemptedPath = false;
            _testNavPathDistanceYards = 0f;
            _testNavDirectDistanceYards = 0f;
            _testNavLastSolveMs = 0;

            if (_currentMesh == null || !_testNavStartPoint.HasValue || !_testNavEndPoint.HasValue)
            {
                UpdateTestNavVisualization();
                return;
            }

            _testNavDirectDistanceYards = (_testNavEndPoint.Value - _testNavStartPoint.Value).Length;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            _testNavPath = NavMeshPathfinder.FindPath(
                _currentMesh,
                _testNavStartPoint.Value,
                _testNavStartPolyIndex,
                _testNavEndPoint.Value,
                _testNavEndPolyIndex);
            sw.Stop();

            _testNavHasAttemptedPath = true;
            _testNavLastSolveMs = sw.ElapsedMilliseconds;

            if (_testNavPath != null)
                _testNavPathDistanceYards = ComputePathDistance(_testNavPath);

            UpdateTestNavVisualization();
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

                bool setStart = ModifierKeys.HasFlag(Keys.Shift);

                if (setStart)
                {
                    _testNavStartPoint = closestHit;
                    _testNavStartPolyIndex = closestPolyIndex;

                    byte area = _currentMesh.Polys[closestPolyIndex].Area;
                    _console?.LogSuccess($"Start: [{wowHit.X:G7}, {wowHit.Y:G7}, {wowHit.Z:G7}] Poly #{closestPolyIndex} ({Data.AreaTypeInfo.GetName(area)})");

                    RecalculateTestNavigationPath();

                    if (!_testNavEndPoint.HasValue)
                        _console?.Log("Test Nav: click to place or move End");
                }
                else
                {
                    _testNavEndPoint = closestHit;
                    _testNavEndPolyIndex = closestPolyIndex;

                    byte area = _currentMesh.Polys[closestPolyIndex].Area;
                    _console?.Log($"End: [{wowHit.X:G7}, {wowHit.Y:G7}, {wowHit.Z:G7}] Poly #{closestPolyIndex} ({Data.AreaTypeInfo.GetName(area)})");

                    RecalculateTestNavigationPath();

                    if (_testNavStartPoint.HasValue)
                    {
                        if (_testNavPath != null)
                        {
                            float pathMeters = _testNavPathDistanceYards * 0.9144f;
                            float directMeters = _testNavDirectDistanceYards * 0.9144f;
                            float detourFactor = _testNavDirectDistanceYards > 0.001f
                                ? _testNavPathDistanceYards / _testNavDirectDistanceYards
                                : 0f;
                            _console?.LogSuccess(
                                $"Path found: {_testNavPath.Count} waypoints | {_testNavPathDistanceYards:F1} yd / {pathMeters:F1} m | straight {_testNavDirectDistanceYards:F1} yd / {directMeters:F1} m | x{detourFactor:F2} | {_testNavLastSolveMs} ms");
                        }
                        else
                        {
                            float directMeters = _testNavDirectDistanceYards * 0.9144f;
                            _console?.LogError(
                                $"No path found | straight {_testNavDirectDistanceYards:F1} yd / {directMeters:F1} m | {_testNavLastSolveMs} ms");
                        }
                    }
                    else
                    {
                        UpdateTestNavVisualization();
                        _console?.Log("Test Nav: Shift+Click to place Start");
                    }
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
                _cameraController?.FrameScene();
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

        // ── Bake / Unbake handlers ────────────────────────────────────────

        private void OnBakeObjects()
        {
            if (_currentMesh == null || _renderer == null)
            {
                _console?.LogWarning("No mesh loaded — cannot bake.");
                return;
            }

            var geometries = _renderer.GetObjectGeometries();
            if (geometries.Count == 0)
            {
                _console?.LogWarning("No WMO/M2 objects loaded — nothing to bake.");
                return;
            }

            var result = MeshBaker.Bake(_currentMesh, geometries);
            _renderer.LoadMesh(_currentMesh);                      // refresh GPU buffers
            _renderer.LoadEditableElements(_editableElements);     // keep overlays
            _console?.LogSuccess($"Bake complete: {result.PolysMarked}/{result.PolysTotal} polys marked unwalkable ({result.TrianglesTested} tris tested)");
        }

        private void OnUnbakeObjects()
        {
            if (_currentMesh == null || _renderer == null)
            {
                _console?.LogWarning("No mesh loaded — cannot unbake.");
                return;
            }

            int restored = MeshBaker.Unbake(_currentMesh);
            if (restored == 0)
            {
                _console?.Log("Nothing to unbake — no previous bake snapshot found.");
                return;
            }

            _renderer.LoadMesh(_currentMesh);
            _renderer.LoadEditableElements(_editableElements);
            _console?.LogSuccess($"Unbake complete: {restored} polys restored to original area types.");
        }
    }
}
