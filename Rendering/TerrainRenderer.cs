// ============================================================================
//  TerrainRenderer.cs  —  ADT terrain heightmap renderer (UV-textured)
//
//  Converts MCNK heightmap data into OpenGL triangle meshes grouped by
//  texture.  Each group shares the same BLP base-layer texture and is drawn
//  with a single DrawElements call.
//
//  Vertex format: [x, y, z, u, v]  — 5 floats per vertex.
//  Shader used:   terrain.vert / terrain.frag
//
//  Coordinate pipeline:
//    MCNK xpos/zpos are already in the same horizontal tile space as Detour.
//    MCNK ypos is the base terrain height (MaNGOS map-extractor uses it for V9/V8).
//    OpenGL position = (xpos - localX, ypos + MCVT height, zpos - localZ).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using MeshViewer3D.Core.Formats.Adt;
using MeshViewer3D.Core.Formats.Blp;

namespace MeshViewer3D.Rendering
{
    public sealed class TerrainRenderer : IDisposable
    {
        // ── Constants ─────────────────────────────────────────────────────────
        private const float GRID_SIZE  = 533.33333f;
        private const float CHUNK_SIZE = GRID_SIZE / 16.0f;
        private const float UNIT_SIZE  = CHUNK_SIZE / 8.0f;

        // ── Per-texture draw group ────────────────────────────────────────────
        private sealed class DrawGroup : IDisposable
        {
            public int Vao, Vbo, Ebo;
            public int IndexCount;
            public GlTexture? Texture;  // null → sandy fallback in shader
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                if (Vao != 0) GL.DeleteVertexArray(Vao);
                if (Vbo != 0) GL.DeleteBuffer(Vbo);
                if (Ebo != 0) GL.DeleteBuffer(Ebo);
                Texture?.Dispose();
                _disposed = true;
            }
        }

        // ── State ─────────────────────────────────────────────────────────────
        private readonly List<DrawGroup> _groups = new();
        private bool _disposed;

        public string Name       { get; set; } = "";
        public int    TotalTris  { get; private set; }
        public int    TotalVerts { get; private set; }

        // ── Factory ───────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the terrain mesh from ADT chunks.
        /// </summary>
        /// <param name="chunks">256 TerrainChunk entries from AdtFile.</param>
        /// <param name="textureNames">MTEX filename list from AdtFile.</param>
        /// <param name="fileLoader">
        ///   Callback that returns raw bytes for a given MPQ path, or null if not found.
        ///   Pass null to render without textures (sandy fallback colour).
        /// </param>
        public void LoadTerrain(
            TerrainChunk?[]        chunks,
            string[]               textureNames,
            Func<string, byte[]?>? fileLoader,
            Action<string>?        logWarning = null)
        {
            DisposeGroups();

            if (chunks is null) return;

            // ── Group vertex/index data by first-layer texture ID ──────────
            // Key: textureId (-1 = no texture)
            var groupData = new Dictionary<int, (List<float> verts, List<uint> inds, uint baseVert)>
            {
                [-1] = (new List<float>(), new List<uint>(), 0u)
            };

            for (int cy = 0; cy < 16; cy++)
            {
                for (int cx = 0; cx < 16; cx++)
                {
                    var chunk = chunks[cy * 16 + cx];
                    if (chunk == null) continue;

                    int texId = -1;
                    if (chunk.TextureLayers != null && chunk.TextureLayers.Length > 0)
                        texId = (int)chunk.TextureLayers[0].textureId;

                    if (!groupData.ContainsKey(texId))
                        groupData[texId] = (new List<float>(), new List<uint>(), 0u);

                    var entry = groupData[texId];
                    AddChunkVertices(chunk, entry.verts, entry.inds, ref entry.baseVert);
                    groupData[texId] = entry;
                }
            }

            // ── Pre-load all BLP textures ──────────────────────────────────
            var texCache = new Dictionary<int, GlTexture?>();

            foreach (int texId in groupData.Keys)
            {
                if (texId < 0) { texCache[texId] = null; continue; }
                if (texId >= textureNames.Length) { texCache[texId] = null; continue; }

                string blpPath = textureNames[texId];
                GlTexture? glTex = null;

                if (fileLoader != null)
                {
                    try
                    {
                        byte[]? blpData = fileLoader(blpPath);
                        if (blpData != null)
                        {
                            var blp = BlpFile.Load(blpData);
                            if (blp.IsValid)
                                glTex = GlTexture.FromBlp(blp);
                            else
                                logWarning?.Invoke($"BLP invalid: {blpPath} ({blp.ErrorMessage})");
                        }
                        else
                        {
                            logWarning?.Invoke($"BLP not found in MPQ: {blpPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logWarning?.Invoke($"BLP error {blpPath}: {ex.Message}");
                    }
                }

                texCache[texId] = glTex;
            }

            int texLoaded = texCache.Values.Count(t => t != null);
            if (texLoaded == 0 && fileLoader != null && textureNames.Length > 0)
                logWarning?.Invoke($"  0/{textureNames.Length} textures loaded for {Name}");

            // ── Upload each group to GPU ───────────────────────────────────
            int totalVerts = 0;
            int totalTris  = 0;

            foreach (var (texId, (verts, inds, _)) in groupData)
            {
                if (verts.Count == 0 || inds.Count == 0) continue;

                var group = new DrawGroup
                {
                    Texture = texCache.TryGetValue(texId, out var t) ? t : null
                };

                group.Vao = GL.GenVertexArray();
                GL.BindVertexArray(group.Vao);

                group.Vbo = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, group.Vbo);
                GL.BufferData(BufferTarget.ArrayBuffer,
                    verts.Count * sizeof(float),
                    verts.ToArray(),
                    BufferUsageHint.StaticDraw);

                const int stride = 5 * sizeof(float);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
                GL.EnableVertexAttribArray(1);

                group.Ebo = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, group.Ebo);
                GL.BufferData(BufferTarget.ElementArrayBuffer,
                    inds.Count * sizeof(uint),
                    inds.ToArray(),
                    BufferUsageHint.StaticDraw);

                GL.BindVertexArray(0);

                group.IndexCount = inds.Count;
                totalVerts += verts.Count / 5;
                totalTris  += inds.Count / 3;

                _groups.Add(group);
            }

            TotalVerts = totalVerts;
            TotalTris  = totalTris;
        }

        // ── Heightmap → UV mesh ───────────────────────────────────────────────

        private static void AddChunkVertices(
            TerrainChunk chunk,
            List<float>  vertexData,
            List<uint>   indices,
            ref uint     baseVertex)
        {
            var renderVerts = new (Vector3 pos, Vector2 uv)[145];
            int hIdx = 0;

            for (int row = 0; row < 17; row++)
            {
                bool isOuter  = (row % 2 == 0);
                int  colCount = isOuter ? 9 : 8;

                float rowOffset = (row * 0.5f) * UNIT_SIZE;
                float v_uv      = row / 16.0f;

                for (int col = 0; col < colCount; col++)
                {
                    float height    = chunk.BaseHeight + chunk.Heights[hIdx];
                    float colOffset = isOuter ? col * UNIT_SIZE : (col + 0.5f) * UNIT_SIZE;
                    float u_uv      = isOuter ? col / 8.0f : (col + 0.5f) / 8.0f;

                    // MCNK positions are the high corner of the chunk in Detour/OpenGL
                    // tile space.  IndexX/columns move along X, IndexY/rows move along Z.
                    float terrainX = chunk.PositionX - colOffset;
                    float terrainZ = chunk.PositionY - rowOffset;

                    renderVerts[hIdx] = (
                        new Vector3(terrainX, height, terrainZ),
                        new Vector2(u_uv, v_uv)
                    );
                    hIdx++;
                }
            }

            foreach (var (pos, uv) in renderVerts)
            {
                vertexData.Add(pos.X);
                vertexData.Add(pos.Y);
                vertexData.Add(pos.Z);
                vertexData.Add(uv.X);
                vertexData.Add(uv.Y);
            }

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    uint tl = (uint)(r * 17 + c);
                    uint tr = (uint)(r * 17 + c + 1);
                    uint bl = (uint)((r + 1) * 17 + c);
                    uint br = (uint)((r + 1) * 17 + c + 1);
                    uint m  = (uint)(r * 17 + 9 + c);
                    uint bv = baseVertex;

                    // Vertex coordinates decrease on X/Z as col/row increase, so the
                    // original MCVT fan order must be reversed to face upward in OpenGL.
                    // Global backface culling is enabled by NavMeshRenderer; wrong winding
                    // makes flat terrain disappear and leaves only steep mountain faces.
                    indices.Add(bv + tl); indices.Add(bv + m);  indices.Add(bv + tr);
                    indices.Add(bv + tl); indices.Add(bv + bl); indices.Add(bv + m);
                    indices.Add(bv + tr); indices.Add(bv + m);  indices.Add(bv + br);
                    indices.Add(bv + bl); indices.Add(bv + br); indices.Add(bv + m);
                }
            }

            baseVertex += 145;
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        public void Render(Matrix4 view, Matrix4 projection, ShaderProgram terrainShader)
        {
            if (_disposed || _groups.Count == 0) return;

            terrainShader.Use();
            terrainShader.SetMatrix4("uModel",         Matrix4.Identity);
            terrainShader.SetMatrix4("uView",          view);
            terrainShader.SetMatrix4("uProjection",    projection);
            terrainShader.SetBool  ("uEnableLighting", true);
            terrainShader.SetFloat ("uAlpha",          0.90f);

            foreach (var group in _groups)
            {
                if (group.IndexCount == 0) continue;

                bool hasTexture = group.Texture != null;
                terrainShader.SetBool("uHasTexture", hasTexture);

                if (hasTexture)
                {
                    GL.ActiveTexture(TextureUnit.Texture0);
                    group.Texture!.Bind();
                    terrainShader.SetInt("uTexture", 0);
                }

                GL.BindVertexArray(group.Vao);
                GL.DrawElements(PrimitiveType.Triangles, group.IndexCount,
                                DrawElementsType.UnsignedInt, 0);
            }

            GL.BindVertexArray(0);
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        private void DisposeGroups()
        {
            foreach (var g in _groups) g.Dispose();
            _groups.Clear();
            TotalVerts = 0;
            TotalTris  = 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            DisposeGroups();
            _disposed = true;
        }
    }
}
