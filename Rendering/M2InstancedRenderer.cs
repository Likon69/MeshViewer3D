// ============================================================================
//  M2InstancedRenderer.cs  —  Instanced M2 doodad renderer
//
//  Mirrors WmoInstancedRenderer for M2 doodads: geometry shared once per M2 file,
//  per-placement model matrix uploaded once, single instanced draw call.
//  Per-placement world-space triangles exposed via PlacementTriangles for MeshBaker.
// ============================================================================

using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using MeshViewer3D.Core.Formats.M2;
using MeshViewer3D.Core.Formats.Adt;

namespace MeshViewer3D.Rendering
{
    public sealed class M2InstancedRenderer : IDisposable
    {
        private const float GRID_SIZE  = 533.33333f;
        private const float MAP_OFFSET = 32.0f * GRID_SIZE;

        // M2 solid colour: red for trees/spikes/doodads (matches M2Renderer)
        private const float CR = 0.85f;
        private const float CG = 0.15f;
        private const float CB = 0.15f;

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

        /// <summary>One entry per placement; each is the world-space triangle list for that placement.</summary>
        public List<OpenTK.Mathematics.Vector3[]> PlacementTriangles { get; } = new();

        /// <summary>
        /// Loads shared local-space geometry from <paramref name="model"/>, then builds
        /// per-instance model matrices and per-placement world-space triangles.
        /// </summary>
        public void LoadGeometry(M2File model, IReadOnlyList<MDDF> mddfs)
        {
            ReleaseGpuBuffers();

            // ── 1. Build shared local-space geometry ───────────────────────────
            var vertexData = new float[model.Vertices.Length / 3 * 6];
            int vi = 0;
            for (int i = 0; i < model.Vertices.Length; i += 3)
            {
                vertexData[vi++] = model.Vertices[i];
                vertexData[vi++] = model.Vertices[i + 1];
                vertexData[vi++] = model.Vertices[i + 2];
                vertexData[vi++] = CR;
                vertexData[vi++] = CG;
                vertexData[vi++] = CB;
            }

            var indices = new uint[model.Indices.Length];
            for (int i = 0; i < model.Indices.Length; i++)
                indices[i] = (uint)model.Indices[i];

            // ── 2. Per-instance: model matrix + world-space triangles + AABB ───
            PlacementTriangles.Clear();
            var modelMatrices = new List<Matrix4>(mddfs.Count);

            var boundsMin = new Vector3(float.PositiveInfinity);
            var boundsMax = new Vector3(float.NegativeInfinity);

            foreach (var mddf in mddfs)
            {
                var rot = G3D_fromEulerAnglesXYZ(
                    MathF.PI * mddf.rotZ / -180.0f,
                    MathF.PI * mddf.rotX / -180.0f,
                    MathF.PI * mddf.rotY / -180.0f);

                float scale = mddf.scale == 0 ? 1.0f : mddf.scale / 1024.0f;
                float gPosX = mddf.posZ - MAP_OFFSET;
                float gPosY = mddf.posX - MAP_OFFSET;
                float gPosZ = mddf.posY;

                var M = BuildLegacyModelMatrix(rot, scale, gPosX, gPosY, gPosZ);
                modelMatrices.Add(M);

                var worldTris = TransformLocalToWorld(vertexData, indices, M);
                PlacementTriangles.Add(worldTris);

                for (int i = 0; i < worldTris.Length; i++)
                {
                    boundsMin = Vector3.ComponentMin(boundsMin, worldTris[i]);
                    boundsMax = Vector3.ComponentMax(boundsMax, worldTris[i]);
                }
            }

            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
            _instanceCount = mddfs.Count;

            if (vertexData.Length == 0 || indices.Length == 0 || _instanceCount == 0)
            {
                _indexCount = 0;
                return;
            }

            // ── 3. Upload shared vertex/index buffers ───────────────────────────
            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);

            _vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Length * sizeof(float),
                          vertexData, BufferUsageHint.StaticDraw);

            const int vstride = 6 * sizeof(float);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, vstride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, vstride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            _ebo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint),
                          indices, BufferUsageHint.StaticDraw);

            // Attributes 2..5 carry the ROWS of the OpenTK matrix — same layout as
            // GL.UniformMatrix4(transpose:false) uploads for the non-instanced path.
            _instanceVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);

            var instanceData = new float[_instanceCount * 16];
            for (int i = 0; i < _instanceCount; i++)
            {
                var M = modelMatrices[i];
                int idx = i * 16;
                instanceData[idx + 0]  = M.M11; instanceData[idx + 1]  = M.M12; instanceData[idx + 2]  = M.M13; instanceData[idx + 3]  = M.M14;
                instanceData[idx + 4]  = M.M21; instanceData[idx + 5]  = M.M22; instanceData[idx + 6]  = M.M23; instanceData[idx + 7]  = M.M24;
                instanceData[idx + 8]  = M.M31; instanceData[idx + 9]  = M.M32; instanceData[idx + 10] = M.M33; instanceData[idx + 11] = M.M34;
                instanceData[idx + 12] = M.M41; instanceData[idx + 13] = M.M42; instanceData[idx + 14] = M.M43; instanceData[idx + 15] = M.M44;
            }
            GL.BufferData(BufferTarget.ArrayBuffer, instanceData.Length * sizeof(float),
                          instanceData, BufferUsageHint.StaticDraw);

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

            _indexCount = indices.Length;
        }

        /// <summary>
        /// Single instanced draw call. The caller binds the instanced shader and sets its
        /// uniforms once for the whole batch.
        /// </summary>
        public void Draw()
        {
            if (_disposed || _indexCount == 0 || _instanceCount == 0) return;

            GL.BindVertexArray(_vao);
            GL.DrawElementsInstanced(PrimitiveType.Triangles, _indexCount,
                                    DrawElementsType.UnsignedInt, IntPtr.Zero,
                                    _instanceCount);
        }

        public void Dispose()
        {
            if (_disposed) return;
            ReleaseGpuBuffers();
            _disposed = true;
        }

        private void ReleaseGpuBuffers()
        {
            if (_vao != 0)         { GL.DeleteVertexArray(_vao); _vao = 0; }
            if (_vbo != 0)         { GL.DeleteBuffer(_vbo);      _vbo = 0; }
            if (_ebo != 0)         { GL.DeleteBuffer(_ebo);      _ebo = 0; }
            if (_instanceVbo != 0) { GL.DeleteBuffer(_instanceVbo); _instanceVbo = 0; }
            _indexCount = 0;
            _instanceCount = 0;
        }

        // ── Math helpers (same as WmoInstancedRenderer) ──────────────────────

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

        private static Matrix4 BuildLegacyModelMatrix(float[,] rot, float scale, float gPosX, float gPosY, float gPosZ)
        {
            return new Matrix4(
                new Vector4(-rot[0, 1] * scale,  rot[0, 2] * scale, -rot[0, 0] * scale, 0),
                new Vector4(-rot[1, 1] * scale,  rot[1, 2] * scale, -rot[1, 0] * scale, 0),
                new Vector4(-rot[2, 1] * scale,  rot[2, 2] * scale, -rot[2, 0] * scale, 0),
                new Vector4(-gPosY,             gPosZ,             -gPosX,             1)
            );
        }

        private static Vector3[] TransformLocalToWorld(float[] localVerts, uint[] indices, Matrix4 M)
        {
            var world = new Vector3[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                int vBase = (int)indices[i] * 6;
                var local = new Vector4(localVerts[vBase], localVerts[vBase + 1], localVerts[vBase + 2], 1.0f);
                var world4 = local * M;
                world[i] = new Vector3(world4.X, world4.Y, world4.Z);
            }
            return world;
        }
    }
}