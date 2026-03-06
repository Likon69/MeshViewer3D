// ============================================================================
//  WmoRenderer.cs  —  OpenGL renderer for a single WMO instance
//
//  One WmoRenderer = one MODF placement = all groups of that WMO merged.
//  Groups are merged into a SINGLE VAO/VBO/EBO to minimise draw calls.
//
//  Design:
//    - Vertex format: [x,y,z,r,g,b] (6 floats) — reuses mesh.vert exactly
//    - CollisionIndices are primary; falls back to RenderIndices
//    - MODF placement + coordinate conversion baked at load time
//    - Shaders are NOT owned by this class (NavMeshRenderer passes them)
//    - uModel = Identity at render time (transform already baked into vertices)
//
//  Coordinate pipeline (mirrors MaNGOS Movemap-Generator exactly):
//    1) WMO local vertices from MOVT are in raw WoW-file space (X,Y,Z)
//    2) G3D rotation: fromEulerAnglesXYZ(-rz*pi/180, -rx*pi/180, -ry*pi/180)
//       Applied as row-vector * matrix (G3D convention)
//    3) Scale, then add fixCoords'd position offset by -32*GRID_SIZE
//    4) Mirror X,Y (negate), then copyVertices swap (y,z,x) → Recast
// ============================================================================

using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using MeshViewer3D.Core.Formats.Adt;
using MeshViewer3D.Core.Formats.Wmo;

namespace MeshViewer3D.Rendering
{
    /// <summary>
    /// Renders one WMO placement (all its groups merged into a single draw call).
    /// Receives <see cref="ShaderProgram"/> from <see cref="NavMeshRenderer"/>; does not own it.
    /// </summary>
    public sealed class WmoRenderer : IDisposable
    {
        private const float GRID_SIZE  = 533.33333f;
        private const float MAP_OFFSET = 32.0f * GRID_SIZE; // 17066.666

        // ── GPU resources ─────────────────────────────────────────────────────
        private int _vao;
        private int _vbo;
        private int _ebo;
        private int _indexCount;

        // ── Visual state ──────────────────────────────────────────────────────
        public bool ShowWireframe { get; set; }

        /// <summary>Display name (filename only) of this WMO, used for blacklist matching.</summary>
        public string Name { get; set; } = "";

        // WMO solid colour: yellow for buildings (HB style)
        private const float CR = 0.90f;
        private const float CG = 0.82f;
        private const float CB = 0.25f;

        private bool _disposed;

        // ── Factory ───────────────────────────────────────────────────────────

        /// <summary>
        /// Loads geometry from all WMO groups into GPU buffers.
        /// Reproduces MaNGOS Movemap-Generator coordinate pipeline exactly:
        ///   vmap-extractor: fixCoords(pos), raw rot written to file
        ///   TerrainBuilder: fromEulerAnglesXYZ(-rz,-rx,-ry), position-=32*GRID,
        ///                   transform(v*rot*scale+pos, mirror x/y), copyVertices(y,z,x)
        /// </summary>
        public void LoadGeometry(IEnumerable<WmoGroup> groups, MODF modf)
        {
            // ── Build G3D rotation matrix (row-vector convention) ──────────
            // MaNGOS TerrainBuilder.cpp line 723:
            //   fromEulerAnglesXYZ(pi*iRot.z/-180, pi*iRot.x/-180, pi*iRot.y/-180)
            // iRot = raw MODF rotation (NOT fixCoords'd)
            float[,] rot = G3D_fromEulerAnglesXYZ(
                MathF.PI * modf.rotZ / -180.0f,
                MathF.PI * modf.rotX / -180.0f,
                MathF.PI * modf.rotY / -180.0f);

            float scale = modf.scale == 0 ? 1.0f : modf.scale / 1024.0f;

            // fixCoords(pos) = (posZ, posX, posY), then subtract 32*GRID
            // (vmap-extractor wmo.cpp line 621: pos = fixCoords(pos))
            // (TerrainBuilder.cpp line 725: position.x -= 32*GRID_SIZE; position.y -= 32*GRID_SIZE)
            float gPosX = modf.posZ - MAP_OFFSET;
            float gPosY = modf.posX - MAP_OFFSET;
            float gPosZ = modf.posY; // height, no offset

            var vertexData = new List<float>();
            var indices    = new List<uint>();
            uint baseVertex = 0;

            foreach (var group in groups)
            {
                var geo = group.Geometry;
                if (geo.VertexCount == 0) continue;

                for (int i = 0; i < geo.Vertices.Length; i += 3)
                {
                    float lx = geo.Vertices[i];
                    float ly = geo.Vertices[i + 1];
                    float lz = geo.Vertices[i + 2];

                    // G3D row-vector * rotation * scale + position
                    // TerrainBuilder::transform line 844: v = (*it) * rotation * scale + position
                    float rx = (lx * rot[0,0] + ly * rot[1,0] + lz * rot[2,0]) * scale + gPosX;
                    float ry = (lx * rot[0,1] + ly * rot[1,1] + lz * rot[2,1]) * scale + gPosY;
                    float rz = (lx * rot[0,2] + ly * rot[1,2] + lz * rot[2,2]) * scale + gPosZ;

                    // Mirror: v.x *= -1; v.y *= -1  (TerrainBuilder::transform line 845-846)
                    rx = -rx;
                    ry = -ry;
                    // rz unchanged

                    // copyVertices: dest = (v.y, v.z, v.x)  → Recast (X, Y=height, Z)
                    float recastX = ry;
                    float recastY = rz;  // height
                    float recastZ = rx;

                    vertexData.Add(recastX);
                    vertexData.Add(recastY);
                    vertexData.Add(recastZ);
                    vertexData.Add(CR);
                    vertexData.Add(CG);
                    vertexData.Add(CB);
                }

                int[] src = geo.CollisionIndices.Length > 0
                    ? geo.CollisionIndices
                    : geo.RenderIndices;

                foreach (int idx in src)
                    indices.Add(baseVertex + (uint)idx);

                baseVertex += (uint)geo.VertexCount;
            }

            if (vertexData.Count == 0 || indices.Count == 0)
            {
                _indexCount = 0;
                return;
            }

            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);

            _vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer,
                vertexData.Count * sizeof(float),
                vertexData.ToArray(),
                BufferUsageHint.StaticDraw);

            const int stride = 6 * sizeof(float);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            _ebo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer,
                indices.Count * sizeof(uint),
                indices.ToArray(),
                BufferUsageHint.StaticDraw);

            GL.BindVertexArray(0);
            _indexCount = indices.Count;
        }

        /// <summary>
        /// Reproduces G3D::Matrix3::fromEulerAnglesXYZ(ax, ay, az).
        /// Returns a 3×3 matrix in row-major order for row-vector multiplication.
        /// </summary>
        private static float[,] G3D_fromEulerAnglesXYZ(float ax, float ay, float az)
        {
            float cx = MathF.Cos(ax), sx = MathF.Sin(ax);
            float cy = MathF.Cos(ay), sy = MathF.Sin(ay);
            float cz = MathF.Cos(az), sz = MathF.Sin(az);

            // G3D source: Matrix3::fromEulerAnglesXYZ
            // M = Rx(ax) * Ry(ay) * Rz(az)
            return new float[,]
            {
                {  cy*cz,               cy*sz,              -sy    },
                {  sx*sy*cz - cx*sz,    sx*sy*sz + cx*cz,    sx*cy },
                {  cx*sy*cz + sx*sz,    cx*sy*sz - sx*cz,    cx*cy }
            };
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        /// <summary>
        /// Renders the WMO geometry.
        /// Caller must set view/projection uniforms on <paramref name="meshShader"/> before
        /// calling Render, OR pass them explicitly — this method sets them.
        /// </summary>
        public void Render(Matrix4 view, Matrix4 projection, ShaderProgram meshShader)
        {
            if (_disposed || _indexCount == 0) return;

            var identity = Matrix4.Identity;

            meshShader.Use();
            meshShader.SetMatrix4("uModel",      identity);
            meshShader.SetMatrix4("uView",       view);
            meshShader.SetMatrix4("uProjection", projection);
            meshShader.SetBool  ("uEnableLighting", true);
            meshShader.SetBool  ("uEnableFog",      false);
            meshShader.SetFloat ("uAlpha",          0.65f);

            if (ShowWireframe)
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                GL.BindVertexArray(_vao);
                GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            }
            else
            {
                GL.BindVertexArray(_vao);
                GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
            }

            GL.BindVertexArray(0);
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            if (_vao != 0) { GL.DeleteVertexArray(_vao); _vao = 0; }
            if (_vbo != 0) { GL.DeleteBuffer(_vbo);      _vbo = 0; }
            if (_ebo != 0) { GL.DeleteBuffer(_ebo);      _ebo = 0; }
            _disposed = true;
        }
    }
}
