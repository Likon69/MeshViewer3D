// ============================================================================
//  WmoInstancedRenderer.cs  —  Instanced WMO renderer
//
//  Used only when a single WMO model (rootPath) appears multiple times in the
//  loaded scene. Geometry is stored ONCE in local space; each placement gets
//  a per-instance model matrix uploaded once and consumed by mesh_instanced.vert.
//
//  Per-placement world-space triangles are precomputed at load time and exposed
//  via PlacementTriangles so MeshBaker's GetObjectGeometries() still sees one
//  entry per placement (count preserved).
//
//  Created additively — does not modify WmoRenderer or mesh.vert.
// ============================================================================

using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using MeshViewer3D.Core.Formats.Wmo;
using MeshViewer3D.Core.Formats.Adt;

namespace MeshViewer3D.Rendering
{
    public sealed class WmoInstancedRenderer : IDisposable
    {
        private const float GRID_SIZE  = 533.33333f;
        private const float MAP_OFFSET = 32.0f * GRID_SIZE;

        // WMO solid colour: yellow for buildings (matches WmoRenderer)
        private const float CR = 0.90f;
        private const float CG = 0.82f;
        private const float CB = 0.25f;

        // ── GPU resources ─────────────────────────────────────────────────────
        private int _vao;
        private int _vbo;
        private int _ebo;
        private int _instanceVbo;
        private int _indexCount;
        private int _instanceCount;
        private bool _disposed;

        public string Name { get; set; } = "";

        /// <summary>World-space AABB minimum across all instances (Detour coords).</summary>
        public OpenTK.Mathematics.Vector3 BoundsMin { get; private set; }

        /// <summary>World-space AABB maximum across all instances (Detour coords).</summary>
        public OpenTK.Mathematics.Vector3 BoundsMax { get; private set; }

        /// <summary>
        /// Per-placement world-space triangles (Detour coords). One entry per placement.
        /// MeshBaker reads these through GetObjectGeometries() — one ObjectGeometry per placement.
        /// </summary>
        public List<OpenTK.Mathematics.Vector3[]> PlacementTriangles { get; } = new();

        /// <summary>
        /// Loads shared local-space geometry once, then builds per-instance model matrices
        /// and per-placement world-space triangles from <paramref name="modfs"/>.
        /// Only supports the legacy (non-direct) placement path — duplicates the legacy
        /// vertex math in matrix form rather than per-vertex.
        /// </summary>
        public void LoadGeometry(IEnumerable<WmoGroup> groups, IReadOnlyList<MODF> modfs)
        {
            Dispose();

            // ── 1. Build shared local-space geometry (vertex positions + colors) ──
            var vertexData = new List<float>();
            var indices = new List<uint>();
            uint baseVertex = 0;

            foreach (var group in groups)
            {
                var geo = group.Geometry;
                if (geo.VertexCount == 0) continue;

                for (int i = 0; i < geo.Vertices.Length; i += 3)
                {
                    vertexData.Add(geo.Vertices[i]);
                    vertexData.Add(geo.Vertices[i + 1]);
                    vertexData.Add(geo.Vertices[i + 2]);
                    vertexData.Add(CR);
                    vertexData.Add(CG);
                    vertexData.Add(CB);
                }

                var src = geo.CollisionIndices.Length > 0
                    ? geo.CollisionIndices
                    : geo.RenderIndices;

                foreach (int idx in src)
                    indices.Add(baseVertex + (uint)idx);

                baseVertex += (uint)geo.VertexCount;
            }

            // ── 2. Per-instance: model matrix + world-space triangles + AABB ───
            PlacementTriangles.Clear();
            var modelMatrices = new List<Matrix4>(modfs.Count);

            var boundsMin = new Vector3(float.PositiveInfinity);
            var boundsMax = new Vector3(float.NegativeInfinity);

            foreach (var modf in modfs)
            {
                var rot = G3D_fromEulerAnglesXYZ(
                    MathF.PI * modf.rotZ / -180.0f,
                    MathF.PI * modf.rotX / -180.0f,
                    MathF.PI * modf.rotY / -180.0f);

                float scale = modf.scale == 0 ? 1.0f : modf.scale / 1024.0f;
                float gPosX = modf.posZ - MAP_OFFSET;
                float gPosY = modf.posX - MAP_OFFSET;
                float gPosZ = modf.posY;

                var M = BuildLegacyModelMatrix(rot, scale, gPosX, gPosY, gPosZ);
                modelMatrices.Add(M);

                // Pre-compute world-space triangles by transforming local vertices through M.
                var worldTris = TransformLocalToWorld(vertexData, indices, M);
                PlacementTriangles.Add(worldTris);

                for (int i = 0; i < worldTris.Length; i++)
                {
                    var v = worldTris[i];
                    if (v.X < boundsMin.X) boundsMin.X = v.X; else if (v.X > boundsMax.X) boundsMax.X = v.X;
                    if (v.Y < boundsMin.Y) boundsMin.Y = v.Y; else if (v.Y > boundsMax.Y) boundsMax.Y = v.Y;
                    if (v.Z < boundsMin.Z) boundsMin.Z = v.Z; else if (v.Z > boundsMax.Z) boundsMax.Z = v.Z;
                }
            }

            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
            _instanceCount = modfs.Count;

            if (vertexData.Count == 0 || indices.Count == 0 || _instanceCount == 0)
            {
                _indexCount = 0;
                return;
            }

            // ── 3. Upload shared vertex/index buffers ───────────────────────────
            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);

            _vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Count * sizeof(float),
                          vertexData.ToArray(), BufferUsageHint.StaticDraw);

            const int vstride = 6 * sizeof(float);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, vstride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, vstride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            _ebo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint),
                          indices.ToArray(), BufferUsageHint.StaticDraw);

            // ── 4. Upload per-instance model matrices (4 vec4 columns) ─────────
            _instanceVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);

            var instanceData = new float[_instanceCount * 16];
            for (int i = 0; i < _instanceCount; i++)
            {
                var M = modelMatrices[i];
                int idx = i * 16;
                // Each aModelRow* attribute is one column of uModel. OpenTK Matrix4 is
                // column-major, so column j = (M.M(j+1)1, M.M(j+1)2, M.M(j+1)3, M.M(j+1)4).
                instanceData[idx + 0]  = M.M11; instanceData[idx + 1]  = M.M21; instanceData[idx + 2]  = M.M31; instanceData[idx + 3]  = M.M41;
                instanceData[idx + 4]  = M.M12; instanceData[idx + 5]  = M.M22; instanceData[idx + 6]  = M.M32; instanceData[idx + 7]  = M.M42;
                instanceData[idx + 8]  = M.M13; instanceData[idx + 9]  = M.M23; instanceData[idx + 10] = M.M33; instanceData[idx + 11] = M.M43;
                instanceData[idx + 12] = M.M14; instanceData[idx + 13] = M.M24; instanceData[idx + 14] = M.M34; instanceData[idx + 15] = M.M44;
            }
            GL.BufferData(BufferTarget.ArrayBuffer, instanceData.Length * sizeof(float),
                          instanceData, BufferUsageHint.StaticDraw);

            // Per-instance attributes (locations 2..5) — divisor 1 = advance once per instance
            const int mstride = 16 * sizeof(float);
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, mstride, 0);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribDivisor(2, 1);

            GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, mstride, 4 * sizeof(float));
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribDivisor(3, 1);

            GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, mstride, 8 * sizeof(float));
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribDivisor(4, 1);

            GL.VertexAttribPointer(5, 4, VertexAttribPointerType.Float, false, mstride, 12 * sizeof(float));
            GL.EnableVertexAttribArray(5);
            GL.VertexAttribDivisor(5, 1);

            GL.BindVertexArray(0);

            _indexCount = indices.Count;
        }

        /// <summary>
        /// Renders all instances of this WMO in a single draw call via glDrawElementsInstanced.
        /// Caller is responsible for binding the instanced shader and setting uView / uProjection.
        /// </summary>
        public void Render(Matrix4 view, Matrix4 projection, ShaderProgram instancedShader)
        {
            if (_disposed || _indexCount == 0 || _instanceCount == 0) return;

            instancedShader.Use();
            instancedShader.SetMatrix4("uView", view);
            instancedShader.SetMatrix4("uProjection", projection);
            instancedShader.SetBool  ("uEnableLighting", true);
            instancedShader.SetBool  ("uEnableFog",      false);
            instancedShader.SetFloat ("uAlpha",          0.65f);

            GL.BindVertexArray(_vao);
            GL.DrawElementsInstanced(PrimitiveType.Triangles, _indexCount,
                                    DrawElementsType.UnsignedInt, IntPtr.Zero,
                                    _instanceCount);
            GL.BindVertexArray(0);
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_vao != 0)         { GL.DeleteVertexArray(_vao); _vao = 0; }
            if (_vbo != 0)         { GL.DeleteBuffer(_vbo);      _vbo = 0; }
            if (_ebo != 0)         { GL.DeleteBuffer(_ebo);      _ebo = 0; }
            if (_instanceVbo != 0) { GL.DeleteBuffer(_instanceVbo); _instanceVbo = 0; }
            _disposed = true;
        }

        // ── Math helpers (duplicated from WmoRenderer; that class stays untouched) ──

        private static float[,] G3D_fromEulerAnglesXYZ(float ax, float ay, float az)
        {
            float cx = MathF.Cos(ax), sx = MathF.Sin(ax);
            float cy = MathF.Cos(ay), sy = MathF.Sin(ay);
            float cz = MathF.Cos(az), sz = MathF.Sin(az);

            return new float[,]
            {
                { cy * cz, -cy * sz, sy, 0 },
                { cx * sz + sx * sy * cz, cx * cz - sx * sy * sz, -sx * cy, 0 },
                { sx * sz - cx * sy * cz, sx * cz + cx * sy * sz, cx * cy, 0 },
                { 0, 0, 0, 1 }
            };
        }

        /// <summary>
        /// Builds the 4x4 model matrix that produces the same world-space vertex as the
        /// legacy per-vertex WMO transform in WmoRenderer:
        ///   rx = (lx*rot[0,0] + ly*rot[1,0] + lz*rot[2,0])*scale + gPosX, then -rx
        ///   ry = (lx*rot[0,1] + ly*rot[1,1] + lz*rot[2,1])*scale + gPosY, then -ry
        ///   rz = (lx*rot[0,2] + ly*rot[1,2] + lz*rot[2,2])*scale + gPosZ
        ///   recastX = ry, recastY = rz, recastZ = rx
        /// </summary>
        private static Matrix4 BuildLegacyModelMatrix(float[,] rot, float scale, float gPosX, float gPosY, float gPosZ)
        {
            return new Matrix4(
                new Vector4(-rot[0, 1] * scale,  rot[0, 2] * scale, -rot[0, 0] * scale, 0),
                new Vector4(-rot[1, 1] * scale,  rot[1, 2] * scale, -rot[1, 0] * scale, 0),
                new Vector4(-rot[2, 1] * scale,  rot[2, 2] * scale, -rot[2, 0] * scale, 0),
                new Vector4(-gPosY,             gPosZ,             -gPosX,             1)
            );
        }

        /// <summary>
        /// Apply the model matrix to each indexed local vertex. Returns flat list of
        /// triangle vertices in world space, in the same order as WmoRenderer's BuildRecastTriangles.
        /// </summary>
        private static Vector3[] TransformLocalToWorld(List<float> localVerts, List<uint> indices, Matrix4 M)
        {
            var world = new Vector3[indices.Count];
            for (int i = 0; i < indices.Count; i++)
            {
                int vBase = (int)indices[i] * 6; // 6 floats per vertex
                var local = new Vector4(localVerts[vBase], localVerts[vBase + 1], localVerts[vBase + 2], 1.0f);
                var world4 = local * M;
                world[i] = new Vector3(world4.X, world4.Y, world4.Z);
            }
            return world;
        }
    }
}