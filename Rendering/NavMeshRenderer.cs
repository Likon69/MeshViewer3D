using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using MeshViewer3D.Core;
using MeshViewer3D.Core.Formats.Adt;
using MeshViewer3D.Core.Formats.M2;
using MeshViewer3D.Core.Formats.Wmo;
using MeshViewer3D.Core.Mpq;

namespace MeshViewer3D.Rendering
{
    /// <summary>
    /// Moteur de rendu OpenGL principal
    /// Qualité Honorbuddy - Gère navmesh, wireframe, offmesh, debug
    /// </summary>
    public class NavMeshRenderer : IDisposable
    {
        // Shaders
        private ShaderProgram? _meshShader;
        private ShaderProgram? _lineShader;
        private ShaderProgram? _coloredLineShader;

        // Buffers navmesh
        private int _meshVao, _meshVbo, _meshEbo;
        private int _meshVertexCount;

        // Buffers wireframe
        private int _wireVao, _wireVbo, _wireEbo;
        private int _wireVertexCount;

        // Buffers OffMesh
        private int _offmeshVao, _offmeshVbo;
        private int _offmeshVertexCount;

        // Buffers édition (blackspots, volumes, etc.)
        private int _blackspotVao, _blackspotVbo, _blackspotEbo;
        private int _blackspotVertexCount;
        
        private int _volumeVao, _volumeVbo, _volumeEbo;
        private int _volumeVertexCount;

        // Buffers pour les Custom OffMesh Connections (Jump Links)
        private int _customOffmeshVao, _customOffmeshVbo;
        private int _customOffmeshVertexCount;

        // État
        private NavMeshData? _currentMesh;
        private EditableElements? _editableElements;
        private ColorMode _colorMode = ColorMode.ByAreaType;
        private bool _showWireframe = true;
        private bool _showOffMesh = true;
        private bool _enableLighting = true;
        private bool _showBlackspots = true;
        private bool _showVolumes = true;
        
        // Animation flash
        private int _flashBlackspotIndex = -1;
        private DateTime _flashStartTime;
        private const float FlashDurationSeconds = 0.8f;

        // Buffer prévisualisation polygone en cours (Convex Volume placement)
        private int _previewVao, _previewVbo;
        private int _previewLineCount;

        // WMO World Object renderers (one per MODF placement)
        private readonly List<WmoRenderer> _wmoRenderers = new();
        private bool _showWmoObjects = true;
        private HashSet<string> _wmoBlacklist = new(StringComparer.OrdinalIgnoreCase);

        // M2 doodad renderers (one per MDDF placement)
        private readonly List<M2Renderer> _m2Renderers = new();
        private bool _showM2Objects = true;

        // Raytrace marker
        private int _raytraceVao, _raytraceVbo;
        private int _raytraceVertexCount;
        private Vector3? _raytraceMarkerPos;

        // Test Navigation path
        private int _testNavVao, _testNavVbo;
        private int _testNavVertexCount;

        private bool _disposed;

        /// <summary>
        /// Initialise le renderer (appelé après contexte OpenGL créé)
        /// </summary>
        public void Initialize(string resourcePath)
        {
            // Charger shaders
            string meshVert = System.IO.Path.Combine(resourcePath, "mesh.vert");
            string meshFrag = System.IO.Path.Combine(resourcePath, "mesh.frag");
            string lineVert = System.IO.Path.Combine(resourcePath, "line.vert");
            string lineFrag = System.IO.Path.Combine(resourcePath, "line.frag");

            _meshShader = new ShaderProgram(meshVert, meshFrag);
            _lineShader = new ShaderProgram(lineVert, lineFrag);

            string coloredLineVert = System.IO.Path.Combine(resourcePath, "colored_line.vert");
            string coloredLineFrag = System.IO.Path.Combine(resourcePath, "colored_line.frag");
            _coloredLineShader = new ShaderProgram(coloredLineVert, coloredLineFrag);

            // Configuration OpenGL
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.FrontFace(FrontFaceDirection.Ccw);

            // Couleur de fond (bleu-gris HB)
            var bg = ColorScheme.Background;
            GL.ClearColor(bg.R / 255f, bg.G / 255f, bg.B / 255f, 1.0f);
        }

        /// <summary>
        /// Charge une tile navmesh
        /// </summary>
        public void LoadMesh(NavMeshData mesh)
        {
            _currentMesh = mesh;

            // Générer données de rendu
            var (verts, indices, areas) = mesh.GenerateRenderData();

            // For ByComponent mode, compute connected components and per-polygon colors
            System.Drawing.Color[]? componentColors = null;
            int[]? polyOfVertex = null;
            if (_colorMode == ColorMode.ByComponent)
            {
                var components = Core.NavMeshAnalyzer.FindConnectedComponents(mesh, out int componentCount);
                componentColors = Core.NavMeshAnalyzer.GenerateComponentColors(mesh, components, componentCount);

                // Build mapping: vertexIndex → polyIndex (one entry per vertex)
                var vtxToPoly = new List<int>();
                for (int pi = 0; pi < mesh.Polys.Length; pi++)
                {
                    int vc = mesh.Polys[pi].VertCount;
                    if (vc < 3) continue;
                    for (int v = 0; v < vc; v++)
                        vtxToPoly.Add(pi);
                }
                polyOfVertex = vtxToPoly.ToArray();
            }

            // Créer vertex data avec couleurs
            var vertexData = new List<float>();
            float minHeight = mesh.Header.BMin.Y;
            float maxHeight = mesh.Header.BMax.Y;

            for (int i = 0; i < verts.Count; i++)
            {
                // Position (3 floats)
                vertexData.Add(verts[i].X);
                vertexData.Add(verts[i].Y);
                vertexData.Add(verts[i].Z);

                // Couleur (3 floats) — areas is per-vertex from GenerateRenderData
                byte area = i < areas.Count ? areas[i] : (byte)1;

                System.Drawing.Color color;
                if (_colorMode == ColorMode.ByComponent && componentColors != null && polyOfVertex != null)
                {
                    int polyIdx = i < polyOfVertex.Length ? polyOfVertex[i] : 0;
                    color = polyIdx < componentColors.Length ? componentColors[polyIdx] : System.Drawing.Color.Gray;
                }
                else if (_colorMode == ColorMode.ByHeight)
                {
                    bool walkable = area > 0 && area < 63;
                    color = walkable 
                        ? ColorScheme.GetHeightColor(verts[i].Y, minHeight, maxHeight)
                        : ColorScheme.GetHeightColorObstacle(verts[i].Y, minHeight, maxHeight);
                }
                else
                {
                    color = ColorScheme.GetAreaColor(area);
                }

                vertexData.Add(color.R / 255f);
                vertexData.Add(color.G / 255f);
                vertexData.Add(color.B / 255f);
            }

            // Créer VAO/VBO/EBO pour mesh
            _meshVao = GL.GenVertexArray();
            GL.BindVertexArray(_meshVao);

            _meshVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _meshVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Count * sizeof(float), 
                          vertexData.ToArray(), BufferUsageHint.StaticDraw);

            _meshEbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _meshEbo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint),
                          indices.ToArray(), BufferUsageHint.StaticDraw);

            // Vertex attributes
            int stride = 6 * sizeof(float);
            // Position
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            // Color
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            _meshVertexCount = indices.Count;

            // Créer wireframe
            CreateWireframe(mesh);

            // Créer OffMesh
            CreateOffMeshLines(mesh);

            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Charge les éléments éditables (blackspots, volumes, etc.)
        /// </summary>
        public void LoadEditableElements(EditableElements elements)
        {
            _editableElements = elements;
            UpdateBlackspotBuffers();
            UpdateVolumeBuffers();
            UpdateCustomOffMeshBuffers();
        }
        
        /// <summary>
        /// Déclenche une animation flash sur un blackspot (feedback visuel création)
        /// </summary>
        public void TriggerBlackspotFlash(int blackspotIndex)
        {
            _flashBlackspotIndex = blackspotIndex;
            _flashStartTime = DateTime.Now;
        }

        /// <summary>
        /// Met à jour les buffers OpenGL pour les blackspots
        /// </summary>
        private void UpdateBlackspotBuffers()
        {
            if (_editableElements == null || _editableElements.Blackspots.Count == 0)
            {
                _blackspotVertexCount = 0;
                return;
            }

            var vertexData = new List<float>();
            var indices = new List<uint>();

            // Générer la géométrie des cylindres
            const int segments = 24; // Nombre de segments pour le cercle

            for (int blackspotIndex = 0; blackspotIndex < _editableElements.Blackspots.Count; blackspotIndex++)
            {
                var blackspot = _editableElements.Blackspots[blackspotIndex];
                uint baseIndex = (uint)(vertexData.Count / 6);
                
                // Couleur selon sélection
                bool isSelected = _editableElements.SelectedType == EditableElementType.Blackspot
                                  && _editableElements.SelectedBlackspotIndex == blackspotIndex;
                
                // Animation flash (pulsation verte pour feedback création)
                bool isFlashing = _flashBlackspotIndex == blackspotIndex;
                float flashProgress = 0f;
                if (isFlashing)
                {
                    float elapsed = (float)(DateTime.Now - _flashStartTime).TotalSeconds;
                    if (elapsed < FlashDurationSeconds)
                    {
                        // Pulsation: 0 -> 1 -> 0
                        flashProgress = MathF.Sin(elapsed / FlashDurationSeconds * MathF.PI);
                    }
                    else
                    {
                        _flashBlackspotIndex = -1; // Fin animation
                    }
                }
                
                float r, g, b;
                if (isFlashing)
                {
                    // Vert brillant qui pulse
                    r = 0.2f + flashProgress * 0.8f;
                    g = 1.0f;
                    b = 0.2f + flashProgress * 0.8f;
                }
                else if (isSelected)
                {
                    // Jaune/orange si sélectionné
                    r = 1.0f;
                    g = 0.8f;
                    b = 0.2f;
                }
                else
                {
                    // Rouge normal
                    r = 1.0f;
                    g = 0.0f;
                    b = 0.0f;
                }
                
                // Générer les vertices du cylindre
                for (int i = 0; i < segments; i++)
                {
                    float angle = (float)i / segments * MathF.PI * 2;
                    float cosA = MathF.Cos(angle);
                    float sinA = MathF.Sin(angle);
                    
                    // Vertex bas
                    float x = blackspot.Location.X + cosA * blackspot.Radius;
                    float y = blackspot.Location.Y;
                    float z = blackspot.Location.Z + sinA * blackspot.Radius;
                    
                    vertexData.AddRange(new[] { x, y, z });
                    vertexData.AddRange(new[] { r, g, b });
                    
                    // Vertex haut
                    vertexData.AddRange(new[] { x, y + blackspot.Height, z });
                    vertexData.AddRange(new[] { r, g, b });
                }
                
                // Générer les indices (quads du cylindre)
                for (int i = 0; i < segments; i++)
                {
                    int next = (i + 1) % segments;
                    uint v0 = baseIndex + (uint)(i * 2);
                    uint v1 = baseIndex + (uint)(i * 2 + 1);
                    uint v2 = baseIndex + (uint)(next * 2);
                    uint v3 = baseIndex + (uint)(next * 2 + 1);
                    
                    // Triangle 1 (bas-gauche, haut-gauche, bas-droite)
                    indices.Add(v0);
                    indices.Add(v1);
                    indices.Add(v2);
                    
                    // Triangle 2 (bas-droite, haut-gauche, haut-droite)
                    indices.Add(v2);
                    indices.Add(v1);
                    indices.Add(v3);
                }
                
                // Ajouter les capuchons (haut et bas)
                uint centerBottom = (uint)(vertexData.Count / 6);
                vertexData.AddRange(new[] { blackspot.Location.X, blackspot.Location.Y, blackspot.Location.Z });
                vertexData.AddRange(new[] { r, g, b });
                
                uint centerTop = (uint)(vertexData.Count / 6);
                vertexData.AddRange(new[] { blackspot.Location.X, blackspot.Location.Y + blackspot.Height, blackspot.Location.Z });
                vertexData.AddRange(new[] { r, g, b });
                
                // Triangles du capuchon bas
                for (int i = 0; i < segments; i++)
                {
                    int next = (i + 1) % segments;
                    indices.Add(centerBottom);
                    indices.Add(baseIndex + (uint)(next * 2));
                    indices.Add(baseIndex + (uint)(i * 2));
                }
                
                // Triangles du capuchon haut
                for (int i = 0; i < segments; i++)
                {
                    int next = (i + 1) % segments;
                    indices.Add(centerTop);
                    indices.Add(baseIndex + (uint)(i * 2 + 1));
                    indices.Add(baseIndex + (uint)(next * 2 + 1));
                }
            }

            // Créer/mettre à jour les buffers OpenGL
            if (_blackspotVao == 0)
            {
                _blackspotVao = GL.GenVertexArray();
                _blackspotVbo = GL.GenBuffer();
                _blackspotEbo = GL.GenBuffer();
            }

            GL.BindVertexArray(_blackspotVao);

            GL.BindBuffer(BufferTarget.ArrayBuffer, _blackspotVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Count * sizeof(float),
                          vertexData.ToArray(), BufferUsageHint.DynamicDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _blackspotEbo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint),
                          indices.ToArray(), BufferUsageHint.DynamicDraw);

            int stride = 6 * sizeof(float);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            _blackspotVertexCount = indices.Count;

            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Met à jour les buffers OpenGL pour les volumes convexes
        /// </summary>
        private void UpdateVolumeBuffers()
        {
            if (_editableElements == null || _editableElements.ConvexVolumes.Count == 0)
            {
                _volumeVertexCount = 0;
                return;
            }

            var vertexData = new List<float>();
            var indices = new List<uint>();

            foreach (var volume in _editableElements.ConvexVolumes)
            {
                if (!volume.IsValid())
                    continue;

                uint baseIndex = (uint)(vertexData.Count / 6);
                var areaInfo = Data.AreaTypeInfo.Get((byte)volume.AreaType);
                var color = areaInfo.Color;
                float r = color.R / 255f;
                float g = color.G / 255f;
                float b = color.B / 255f;

                // Ajouter les vertices du polygone (base)
                foreach (var v in volume.Vertices)
                {
                    vertexData.AddRange(new[] { v.X, volume.MinHeight, v.Z });
                    vertexData.AddRange(new[] { r, g, b });
                }

                // Ajouter les vertices du haut
                foreach (var v in volume.Vertices)
                {
                    vertexData.AddRange(new[] { v.X, volume.MaxHeight, v.Z });
                    vertexData.AddRange(new[] { r, g, b });
                }

                int vertCount = volume.Vertices.Count;

                // Triangles des murs (quads)
                for (int i = 0; i < vertCount; i++)
                {
                    int next = (i + 1) % vertCount;
                    uint v0 = baseIndex + (uint)i;              // Bas gauche
                    uint v1 = baseIndex + (uint)i + (uint)vertCount;      // Haut gauche
                    uint v2 = baseIndex + (uint)next;           // Bas droite
                    uint v3 = baseIndex + (uint)next + (uint)vertCount;   // Haut droite

                    indices.Add(v0);
                    indices.Add(v1);
                    indices.Add(v2);

                    indices.Add(v2);
                    indices.Add(v1);
                    indices.Add(v3);
                }

                // Triangles du plancher (fan triangulation)
                for (int i = 1; i < vertCount - 1; i++)
                {
                    indices.Add(baseIndex);
                    indices.Add(baseIndex + (uint)i);
                    indices.Add(baseIndex + (uint)i + 1);
                }

                // Triangles du plafond (fan triangulation inversée)
                for (int i = 1; i < vertCount - 1; i++)
                {
                    indices.Add(baseIndex + (uint)vertCount);
                    indices.Add(baseIndex + (uint)vertCount + (uint)i + 1);
                    indices.Add(baseIndex + (uint)vertCount + (uint)i);
                }
            }

            // Créer/mettre à jour les buffers OpenGL
            if (_volumeVao == 0)
            {
                _volumeVao = GL.GenVertexArray();
                _volumeVbo = GL.GenBuffer();
                _volumeEbo = GL.GenBuffer();
            }

            GL.BindVertexArray(_volumeVao);

            GL.BindBuffer(BufferTarget.ArrayBuffer, _volumeVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Count * sizeof(float),
                          vertexData.ToArray(), BufferUsageHint.DynamicDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _volumeEbo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint),
                          indices.ToArray(), BufferUsageHint.DynamicDraw);

            int stride = 6 * sizeof(float);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            _volumeVertexCount = indices.Count;

            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Met à jour les buffers OpenGL pour les Custom OffMesh Connections (Jump Links)
        /// Comme dans Tripper.Renderer de Honorbuddy
        /// </summary>
        private void UpdateCustomOffMeshBuffers()
        {
            if (_editableElements == null || _editableElements.CustomOffMeshConnections.Count == 0)
            {
                _customOffmeshVertexCount = 0;
                return;
            }

            var lineData = new List<float>();

            foreach (var conn in _editableElements.CustomOffMeshConnections)
            {
                // Couleur selon direction (comme HB)
                // Cyan = bidirectionnel, Orange = unidirectionnel
                float r, g, b;
                if (conn.IsBidirectional)
                {
                    r = 0f; g = 1f; b = 1f; // Cyan
                }
                else
                {
                    r = 1f; g = 0.6f; b = 0f; // Orange
                }

                // Start point (position + couleur)
                lineData.Add(conn.Start.X);
                lineData.Add(conn.Start.Y);
                lineData.Add(conn.Start.Z);
                lineData.Add(r);
                lineData.Add(g);
                lineData.Add(b);

                // End point (position + couleur)
                lineData.Add(conn.End.X);
                lineData.Add(conn.End.Y);
                lineData.Add(conn.End.Z);
                lineData.Add(r);
                lineData.Add(g);
                lineData.Add(b);
            }

            if (lineData.Count == 0) return;

            // Créer/mettre à jour les buffers OpenGL
            if (_customOffmeshVao == 0)
            {
                _customOffmeshVao = GL.GenVertexArray();
                _customOffmeshVbo = GL.GenBuffer();
            }

            GL.BindVertexArray(_customOffmeshVao);

            GL.BindBuffer(BufferTarget.ArrayBuffer, _customOffmeshVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, lineData.Count * sizeof(float),
                          lineData.ToArray(), BufferUsageHint.DynamicDraw);

            // Vertex format: position (3 floats) + color (3 floats)
            int stride = 6 * sizeof(float);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            _customOffmeshVertexCount = lineData.Count / 6; // 6 floats par vertex

            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Charge la géométrie de prévisualisation pour le polygone Convex Volume en cours de dessin.
        /// Passer null ou liste vide pour effacer.
        /// </summary>
        public void LoadPreviewPolygon(IReadOnlyList<Vector3>? vertices)
        {
            if (vertices == null || vertices.Count == 0)
            {
                _previewLineCount = 0;
                return;
            }

            // Green for polygon outline, yellow for vertex markers
            const float lr = 0.2f, lg = 1.0f, lb = 0.2f;
            const float mr = 1.0f, mg = 1.0f, mb = 0.0f;
            const float crossSize = 0.5f;
            var lineData = new List<float>();

            // Line loop: N segments connecting consecutive vertices, plus closing segment
            for (int i = 0; i < vertices.Count; i++)
            {
                var va = vertices[i];
                var vb = vertices[(i + 1) % vertices.Count];
                lineData.AddRange(new[] { va.X, va.Y, va.Z, lr, lg, lb });
                lineData.AddRange(new[] { vb.X, vb.Y, vb.Z, lr, lg, lb });
            }

            // Yellow cross markers at each vertex
            foreach (var v in vertices)
            {
                lineData.AddRange(new[] { v.X - crossSize, v.Y, v.Z, mr, mg, mb });
                lineData.AddRange(new[] { v.X + crossSize, v.Y, v.Z, mr, mg, mb });
                lineData.AddRange(new[] { v.X, v.Y, v.Z - crossSize, mr, mg, mb });
                lineData.AddRange(new[] { v.X, v.Y, v.Z + crossSize, mr, mg, mb });
            }

            if (_previewVao == 0)
            {
                _previewVao = GL.GenVertexArray();
                _previewVbo = GL.GenBuffer();
            }

            GL.BindVertexArray(_previewVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _previewVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, lineData.Count * sizeof(float),
                          lineData.ToArray(), BufferUsageHint.DynamicDraw);

            int stride = 6 * sizeof(float);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            _previewLineCount = lineData.Count / 6;
            GL.BindVertexArray(0);
        }

        private void CreateWireframe(NavMeshData mesh)
        {
            var (edgeVerts, edgeIndices) = mesh.GenerateWireframeData();

            var wireData = new List<float>();
            foreach (var v in edgeVerts)
            {
                wireData.Add(v.X);
                wireData.Add(v.Y);
                wireData.Add(v.Z);
            }

            _wireVao = GL.GenVertexArray();
            GL.BindVertexArray(_wireVao);

            _wireVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _wireVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, wireData.Count * sizeof(float),
                          wireData.ToArray(), BufferUsageHint.StaticDraw);

            _wireEbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _wireEbo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, edgeIndices.Count * sizeof(uint),
                          edgeIndices.ToArray(), BufferUsageHint.StaticDraw);

            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            _wireVertexCount = edgeIndices.Count;
        }

        private void CreateOffMeshLines(NavMeshData mesh)
        {
            var lineData = new List<float>();

            foreach (var con in mesh.OffMeshConnections)
            {
                // Start point
                lineData.Add(con.Start.X);
                lineData.Add(con.Start.Y);
                lineData.Add(con.Start.Z);

                // End point
                lineData.Add(con.End.X);
                lineData.Add(con.End.Y);
                lineData.Add(con.End.Z);
            }

            if (lineData.Count == 0) return;

            _offmeshVao = GL.GenVertexArray();
            GL.BindVertexArray(_offmeshVao);

            _offmeshVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _offmeshVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, lineData.Count * sizeof(float),
                          lineData.ToArray(), BufferUsageHint.StaticDraw);

            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            _offmeshVertexCount = lineData.Count / 3;
        }

        /// <summary>
        /// Rendu de la frame
        /// </summary>
        public void Render(Camera camera, int viewportWidth, int viewportHeight)
        {
            if (_currentMesh == null || _meshShader == null || _lineShader == null || _coloredLineShader == null)
                return;

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Matrices
            var model = Matrix4.Identity;
            var view = camera.GetViewMatrix();
            var projection = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(60f),
                (float)viewportWidth / viewportHeight,
                1f, 10000f
            );

            // Render navmesh
            _meshShader.Use();
            _meshShader.SetMatrix4("uModel", model);
            _meshShader.SetMatrix4("uView", view);
            _meshShader.SetMatrix4("uProjection", projection);
            _meshShader.SetBool("uEnableLighting", _enableLighting);
            _meshShader.SetBool("uEnableFog", false);
            _meshShader.SetFloat("uAlpha", 1.0f);
            _meshShader.SetVector3("uCameraPos", camera.GetEyePosition());

            GL.BindVertexArray(_meshVao);
            GL.DrawElements(PrimitiveType.Triangles, _meshVertexCount, DrawElementsType.UnsignedInt, 0);

            // Render wireframe
            if (_showWireframe && _wireVertexCount > 0)
            {
                _lineShader.Use();
                _lineShader.SetMatrix4("uModel", model);
                _lineShader.SetMatrix4("uView", view);
                _lineShader.SetMatrix4("uProjection", projection);
                
                var wireColor = ColorScheme.ColorToVector4(ColorScheme.Wireframe);
                _lineShader.SetVector4("uColor", wireColor);

                GL.BindVertexArray(_wireVao);
                GL.DrawElements(PrimitiveType.Lines, _wireVertexCount, DrawElementsType.UnsignedInt, 0);
            }

            // Render OffMesh from tile
            if (_showOffMesh && _offmeshVertexCount > 0)
            {
                _lineShader.Use();
                _lineShader.SetMatrix4("uModel", model);
                _lineShader.SetMatrix4("uView", view);
                _lineShader.SetMatrix4("uProjection", projection);

                var offmeshColor = ColorScheme.ColorToVector4(ColorScheme.OffMeshBidirectional);
                _lineShader.SetVector4("uColor", offmeshColor);

                GL.LineWidth(2.0f);
                GL.BindVertexArray(_offmeshVao);
                GL.DrawArrays(PrimitiveType.Lines, 0, _offmeshVertexCount);
                GL.LineWidth(1.0f);
            }

            // Render Custom OffMesh Connections (Jump Links créés par l'utilisateur)
            if (_showOffMesh && _customOffmeshVertexCount > 0)
            {
                _coloredLineShader.Use();
                _coloredLineShader.SetMatrix4("uModel", model);
                _coloredLineShader.SetMatrix4("uView", view);
                _coloredLineShader.SetMatrix4("uProjection", projection);

                GL.LineWidth(3.0f); // Plus épais pour distinction
                GL.BindVertexArray(_customOffmeshVao);
                GL.DrawArrays(PrimitiveType.Lines, 0, _customOffmeshVertexCount);
                GL.LineWidth(1.0f);
            }

            // Render Blackspots (cylindres rouges semi-transparents)
            if (_showBlackspots && _blackspotVertexCount > 0)
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.DepthMask(false); // Ne pas écrire dans le depth buffer pour la transparence
                
                _meshShader.Use();
                _meshShader.SetMatrix4("uModel", model);
                _meshShader.SetMatrix4("uView", view);
                _meshShader.SetMatrix4("uProjection", projection);
                _meshShader.SetBool("uEnableLighting", false);
                _meshShader.SetFloat("uAlpha", 0.4f); // Semi-transparent
                
                GL.BindVertexArray(_blackspotVao);
                GL.DrawElements(PrimitiveType.Triangles, _blackspotVertexCount, DrawElementsType.UnsignedInt, 0);
                
                GL.DepthMask(true);
            }

            // Render Convex Volumes (polygones extrudés)
            if (_showVolumes && _volumeVertexCount > 0)
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.DepthMask(false);
                
                _meshShader.Use();
                _meshShader.SetMatrix4("uModel", model);
                _meshShader.SetMatrix4("uView", view);
                _meshShader.SetMatrix4("uProjection", projection);
                _meshShader.SetBool("uEnableLighting", false);
                _meshShader.SetFloat("uAlpha", 0.5f);
                
                GL.BindVertexArray(_volumeVao);
                GL.DrawElements(PrimitiveType.Triangles, _volumeVertexCount, DrawElementsType.UnsignedInt, 0);
                
                GL.DepthMask(true);
            }

            // Render preview polygon (Convex Volume placement en cours)
            if (_previewLineCount > 0)
            {
                _coloredLineShader.Use();
                _coloredLineShader.SetMatrix4("uModel", model);
                _coloredLineShader.SetMatrix4("uView", view);
                _coloredLineShader.SetMatrix4("uProjection", projection);

                GL.LineWidth(2.0f);
                GL.BindVertexArray(_previewVao);
                GL.DrawArrays(PrimitiveType.Lines, 0, _previewLineCount);
                GL.LineWidth(1.0f);
            }

            // Render WMO world objects (semi-transparent, drawn last for correct blending)
            if (_showWmoObjects && _wmoRenderers.Count > 0 && _meshShader != null)
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.DepthMask(false);

                foreach (var wmoRenderer in _wmoRenderers)
                {
                    if (_wmoBlacklist.Count > 0 && _wmoBlacklist.Contains(wmoRenderer.Name))
                        continue;
                    wmoRenderer.Render(view, projection, _meshShader);
                }

                GL.DepthMask(true);
            }

            // Render M2 doodad objects (semi-transparent, same blending as WMO)
            if (_showM2Objects && _m2Renderers.Count > 0 && _meshShader != null)
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.DepthMask(false);

                foreach (var m2Renderer in _m2Renderers)
                    m2Renderer.Render(view, projection, _meshShader);

                GL.DepthMask(true);
            }

            // Render test navigation path
            if (_testNavVertexCount > 0 && _coloredLineShader != null)
            {
                GL.Disable(EnableCap.DepthTest);
                _coloredLineShader.Use();
                _coloredLineShader.SetMatrix4("uModel", model);
                _coloredLineShader.SetMatrix4("uView", view);
                _coloredLineShader.SetMatrix4("uProjection", projection);

                GL.LineWidth(4.0f);
                GL.BindVertexArray(_testNavVao);
                GL.DrawArrays(PrimitiveType.Lines, 0, _testNavVertexCount);
                GL.LineWidth(1.0f);
                GL.Enable(EnableCap.DepthTest);
            }

            // Render raytrace marker (3D cross at cursor)
            if (_raytraceVertexCount > 0 && _coloredLineShader != null)
            {
                GL.Disable(EnableCap.DepthTest); // Draw on top of everything
                _coloredLineShader.Use();
                _coloredLineShader.SetMatrix4("uModel", model);
                _coloredLineShader.SetMatrix4("uView", view);
                _coloredLineShader.SetMatrix4("uProjection", projection);

                GL.LineWidth(3.0f);
                GL.BindVertexArray(_raytraceVao);
                GL.DrawArrays(PrimitiveType.Lines, 0, _raytraceVertexCount);
                GL.LineWidth(1.0f);
                GL.Enable(EnableCap.DepthTest);
            }

            GL.BindVertexArray(0);
        }

        // Propriétés publiques
        public ColorMode ColorMode
        {
            get => _colorMode;
            set
            {
                if (_colorMode != value)
                {
                    _colorMode = value;
                    if (_currentMesh != null)
                        LoadMesh(_currentMesh); // Reload avec nouvelles couleurs
                }
            }
        }

        public bool ShowWireframe
        {
            get => _showWireframe;
            set => _showWireframe = value;
        }

        public bool ShowOffMesh
        {
            get => _showOffMesh;
            set => _showOffMesh = value;
        }

        public bool EnableLighting
        {
            get => _enableLighting;
            set => _enableLighting = value;
        }

        public bool ShowBlackspots
        {
            get => _showBlackspots;
            set => _showBlackspots = value;
        }

        public bool ShowWmoObjects
        {
            get => _showWmoObjects;
            set => _showWmoObjects = value;
        }

        /// <summary>Sets the WMO blacklist. Blacklisted WMO names are not rendered.</summary>
        public void SetWmoBlacklist(HashSet<string> blacklisted)
        {
            _wmoBlacklist = blacklisted ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"[NavMeshRenderer] WMO blacklist updated: {_wmoBlacklist.Count} entries");
        }

        public bool ShowM2Objects
        {
            get => _showM2Objects;
            set => _showM2Objects = value;
        }

        public bool ShowVolumes
        {
            get => _showVolumes;
            set => _showVolumes = value;
        }

        /// <summary>
        /// Sets the position of the raytrace marker (3D cross at cursor hit point).
        /// Pass null to hide the marker.
        /// </summary>
        /// <summary>
        /// Sets the test navigation path to render. Pass null to clear.
        /// </summary>
        public void SetTestNavPath(Vector3? startPos, Vector3? endPos, List<Vector3>? waypoints)
        {
            // Delete old buffers
            if (_testNavVao != 0) GL.DeleteVertexArray(_testNavVao);
            if (_testNavVbo != 0) GL.DeleteBuffer(_testNavVbo);
            _testNavVao = 0;
            _testNavVbo = 0;
            _testNavVertexCount = 0;

            if (waypoints == null || waypoints.Count < 2) return;

            var vertexData = new List<float>();

            // Path line segments (yellow-orange)
            for (int i = 0; i < waypoints.Count - 1; i++)
            {
                var a = waypoints[i];
                var b = waypoints[i + 1];
                // Slight Y offset to draw above navmesh
                vertexData.AddRange(new[] { a.X, a.Y + 0.3f, a.Z, 1.0f, 0.8f, 0.0f }); // yellow-orange
                vertexData.AddRange(new[] { b.X, b.Y + 0.3f, b.Z, 1.0f, 0.8f, 0.0f });
            }

            // Start marker (green cross)
            if (startPos.HasValue)
                AddCrossVertices(vertexData, startPos.Value, 4.0f, 0.0f, 1.0f, 0.0f);

            // End marker (red cross)
            if (endPos.HasValue)
                AddCrossVertices(vertexData, endPos.Value, 4.0f, 1.0f, 0.0f, 0.0f);

            if (vertexData.Count == 0) return;

            var data = vertexData.ToArray();

            _testNavVao = GL.GenVertexArray();
            GL.BindVertexArray(_testNavVao);

            _testNavVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _testNavVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float),
                          data, BufferUsageHint.DynamicDraw);

            int stride = 6 * sizeof(float);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            _testNavVertexCount = data.Length / 6;
            GL.BindVertexArray(0);
        }

        private static void AddCrossVertices(List<float> data, Vector3 pos, float size, float r, float g, float b)
        {
            float y = pos.Y + 0.5f;
            data.AddRange(new[] { pos.X - size, y, pos.Z, r, g, b });
            data.AddRange(new[] { pos.X + size, y, pos.Z, r, g, b });
            data.AddRange(new[] { pos.X, y, pos.Z - size, r, g, b });
            data.AddRange(new[] { pos.X, y, pos.Z + size, r, g, b });
            // Vertical
            data.AddRange(new[] { pos.X, pos.Y - size, pos.Z, r, g, b });
            data.AddRange(new[] { pos.X, pos.Y + size, pos.Z, r, g, b });
        }

        public void SetRaytraceMarker(Vector3? position)
        {
            _raytraceMarkerPos = position;
            UpdateRaytraceMarkerBuffers();
        }

        /// <summary>
        /// Creates a 3D cross (3 axis lines) at the raytrace hit position.
        /// </summary>
        private void UpdateRaytraceMarkerBuffers()
        {
            // Delete old buffers
            if (_raytraceVao != 0) GL.DeleteVertexArray(_raytraceVao);
            if (_raytraceVbo != 0) GL.DeleteBuffer(_raytraceVbo);
            _raytraceVao = 0;
            _raytraceVbo = 0;
            _raytraceVertexCount = 0;

            if (_raytraceMarkerPos == null) return;

            var pos = _raytraceMarkerPos.Value;
            const float size = 3.0f; // Cross arm length

            // 3 axis lines (6 vertices) + vertical line to show height clearly
            // Each vertex: position (3 floats) + color (3 floats)
            var vertexData = new float[]
            {
                // X axis (red)
                pos.X - size, pos.Y, pos.Z,    1.0f, 0.2f, 0.2f,
                pos.X + size, pos.Y, pos.Z,    1.0f, 0.2f, 0.2f,
                // Y axis (green) - vertical
                pos.X, pos.Y - size, pos.Z,    0.2f, 1.0f, 0.2f,
                pos.X, pos.Y + size, pos.Z,    0.2f, 1.0f, 0.2f,
                // Z axis (blue)
                pos.X, pos.Y, pos.Z - size,    0.4f, 0.4f, 1.0f,
                pos.X, pos.Y, pos.Z + size,    0.4f, 0.4f, 1.0f,
                // Yellow outer cross for visibility
                pos.X - size * 2, pos.Y + 0.5f, pos.Z,    1.0f, 1.0f, 0.0f,
                pos.X + size * 2, pos.Y + 0.5f, pos.Z,    1.0f, 1.0f, 0.0f,
                pos.X, pos.Y + 0.5f, pos.Z - size * 2,    1.0f, 1.0f, 0.0f,
                pos.X, pos.Y + 0.5f, pos.Z + size * 2,    1.0f, 1.0f, 0.0f,
            };

            _raytraceVao = GL.GenVertexArray();
            GL.BindVertexArray(_raytraceVao);

            _raytraceVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _raytraceVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Length * sizeof(float),
                          vertexData, BufferUsageHint.DynamicDraw);

            int stride = 6 * sizeof(float);
            // Position
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            // Color
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            _raytraceVertexCount = vertexData.Length / 6; // 10 vertices = 5 line segments
            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Loads WMO objects from an ADT tile using the WoW data provider.
        /// Replaces any previously loaded WMO renderers.
        /// </summary>
        public void LoadWorldObjects(WowDataProvider dataProvider, AdtFile adt)
        {
            // Clear old renderers
            foreach (var r in _wmoRenderers) r.Dispose();
            _wmoRenderers.Clear();

            foreach (var modf in adt.WmoInstances)
            {
                if (modf.mwidEntry >= (uint)adt.WmoNames.Length)
                {
                    Console.WriteLine($"  WMO skip: mwidEntry {modf.mwidEntry} >= WmoNames.Length {adt.WmoNames.Length}");
                    continue;
                }

                string rootPath = adt.WmoNames[modf.mwidEntry];
                if (string.IsNullOrEmpty(rootPath))
                {
                    Console.WriteLine($"  WMO skip: empty root path for mwidEntry {modf.mwidEntry}");
                    continue;
                }

                Console.WriteLine($"  WMO: Loading '{rootPath}' pos=({modf.posX:F1},{modf.posY:F1},{modf.posZ:F1})");

                byte[]? rootBytes = dataProvider.GetFileBytes(rootPath);
                if (rootBytes == null)
                {
                    Console.WriteLine($"  WMO FAIL: root file not found in MPQ: {rootPath}");
                    continue;
                }

                WmoFile wmoFile;
                try { wmoFile = new WmoFile(rootBytes); }
                catch (Exception ex)
                {
                    Console.WriteLine($"  WMO FAIL: root parse error: {ex.Message}");
                    continue;
                }

                Console.WriteLine($"  WMO: root parsed OK, GroupCount={wmoFile.GroupCount}");

                var groups = new List<WmoGroup>();
                for (int g = 0; g < wmoFile.GroupCount; g++)
                {
                    string groupPath = WmoFile.GetGroupFilePath(rootPath, g);
                    byte[]? groupBytes = dataProvider.GetFileBytes(groupPath);
                    if (groupBytes == null)
                    {
                        Console.WriteLine($"  WMO FAIL: group {g} not found: {groupPath}");
                        continue;
                    }
                    try
                    {
                        var grp = new WmoGroup(groupBytes);
                        if (grp.IsValid)
                        {
                            groups.Add(grp);
                            Console.WriteLine($"  WMO: group {g} OK — {grp.Geometry.VertexCount} verts, {grp.Geometry.CollisionTriangleCount} col tris, {grp.Geometry.RenderTriangleCount} ren tris");
                        }
                        else
                        {
                            Console.WriteLine($"  WMO: group {g} invalid (no geometry)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  WMO FAIL: group {g} parse error: {ex.Message}");
                    }
                }

                if (groups.Count == 0)
                {
                    Console.WriteLine($"  WMO skip: 0 valid groups loaded");
                    continue;
                }

                var renderer = new WmoRenderer();
                renderer.Name = Path.GetFileName(rootPath) ?? rootPath;
                renderer.LoadGeometry(groups, modf);
                _wmoRenderers.Add(renderer);
                Console.WriteLine($"  WMO OK: '{rootPath}' — {groups.Count} groups loaded, renderer added");
            }
            Console.WriteLine($"  WMO TOTAL: {_wmoRenderers.Count} renderers created");

            // ── M2 doodad loading ──────────────────────────────────────────
            foreach (var r in _m2Renderers) r.Dispose();
            _m2Renderers.Clear();

            foreach (var mddf in adt.M2Instances)
            {
                if (mddf.mmidEntry >= (uint)adt.M2Names.Length)
                {
                    Console.WriteLine($"  M2 skip: mmidEntry {mddf.mmidEntry} >= M2Names.Length {adt.M2Names.Length}");
                    continue;
                }

                string m2Path = adt.M2Names[mddf.mmidEntry];
                if (string.IsNullOrEmpty(m2Path))
                {
                    Console.WriteLine($"  M2 skip: empty path for mmidEntry {mddf.mmidEntry}");
                    continue;
                }

                // Normalize extension: .mdx/.mdl → .m2 (like MaNGOS ExtractSingleModel)
                if (m2Path.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase) ||
                    m2Path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
                {
                    m2Path = m2Path[..^2] + "2";
                }

                Console.WriteLine($"  M2: Loading '{m2Path}' pos=({mddf.posX:F1},{mddf.posY:F1},{mddf.posZ:F1}) scale={mddf.scale}");

                byte[]? m2Bytes = dataProvider.GetFileBytes(m2Path);
                if (m2Bytes == null)
                {
                    Console.WriteLine($"  M2 FAIL: file not found in MPQ: {m2Path}");
                    continue;
                }

                M2File m2File;
                try { m2File = M2File.Load(m2Bytes); }
                catch (Exception ex)
                {
                    Console.WriteLine($"  M2 FAIL: parse error: {ex.Message}");
                    continue;
                }

                if (!m2File.IsValid)
                {
                    Console.WriteLine($"  M2 skip: no valid bounding geometry in '{m2Path}'");
                    continue;
                }

                Console.WriteLine($"  M2: parsed OK — {m2File.VertexCount} bounding verts, {m2File.TriangleCount} bounding tris");

                var renderer = new M2Renderer();
                renderer.LoadGeometry(m2File, mddf);
                _m2Renderers.Add(renderer);
                Console.WriteLine($"  M2 OK: '{m2Path}' — renderer added");
            }
            Console.WriteLine($"  M2 TOTAL: {_m2Renderers.Count} renderers created");
        }

        public void Dispose()
        {
            if (_disposed) return;

            // Delete WMO renderers
            foreach (var r in _wmoRenderers) r.Dispose();
            _wmoRenderers.Clear();

            // Delete M2 renderers
            foreach (var r in _m2Renderers) r.Dispose();
            _m2Renderers.Clear();

            // Delete buffers
            if (_meshVao != 0) GL.DeleteVertexArray(_meshVao);
            if (_meshVbo != 0) GL.DeleteBuffer(_meshVbo);
            if (_meshEbo != 0) GL.DeleteBuffer(_meshEbo);
            if (_wireVao != 0) GL.DeleteVertexArray(_wireVao);
            if (_wireVbo != 0) GL.DeleteBuffer(_wireVbo);
            if (_wireEbo != 0) GL.DeleteBuffer(_wireEbo);
            if (_offmeshVao != 0) GL.DeleteVertexArray(_offmeshVao);
            if (_offmeshVbo != 0) GL.DeleteBuffer(_offmeshVbo);
            if (_blackspotVao != 0) GL.DeleteVertexArray(_blackspotVao);
            if (_blackspotVbo != 0) GL.DeleteBuffer(_blackspotVbo);
            if (_blackspotEbo != 0) GL.DeleteBuffer(_blackspotEbo);
            if (_volumeVao != 0) GL.DeleteVertexArray(_volumeVao);
            if (_volumeVbo != 0) GL.DeleteBuffer(_volumeVbo);
            if (_volumeEbo != 0) GL.DeleteBuffer(_volumeEbo);
            if (_customOffmeshVao != 0) GL.DeleteVertexArray(_customOffmeshVao);
            if (_customOffmeshVbo != 0) GL.DeleteBuffer(_customOffmeshVbo);
            if (_previewVao != 0) GL.DeleteVertexArray(_previewVao);
            if (_previewVbo != 0) GL.DeleteBuffer(_previewVbo);

            // Delete raytrace marker buffers
            if (_raytraceVao != 0) GL.DeleteVertexArray(_raytraceVao);
            if (_raytraceVbo != 0) GL.DeleteBuffer(_raytraceVbo);

            // Delete test navigation path buffers
            if (_testNavVao != 0) GL.DeleteVertexArray(_testNavVao);
            if (_testNavVbo != 0) GL.DeleteBuffer(_testNavVbo);

            _meshShader?.Dispose();
            _lineShader?.Dispose();
            _coloredLineShader?.Dispose();

            _disposed = true;
        }
    }
}
