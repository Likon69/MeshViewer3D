// ============================================================================
//  M2Renderer.cs  —  OpenGL renderer for a single M2 (doodad) instance
//
//  One M2Renderer = one MDDF placement = one M2 model's bounding geometry.
//  EXACTLY mirrors WmoRenderer.cs coordinate pipeline:
//    1) G3D rotation: fromEulerAnglesXYZ(-rz*pi/180, -rx*pi/180, -ry*pi/180)
//    2) Scale (ushort / 1024), fixCoords position - 32*GRID_SIZE
//    3) v * rotation * scale + position, mirror X/Y, copyVertices(y,z,x) → Recast
// ============================================================================

using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using MeshViewer3D.Core.Formats.Adt;
using MeshViewer3D.Core.Formats.M2;

namespace MeshViewer3D.Rendering
{
    /// <summary>
    /// Renders one M2 doodad placement (bounding geometry).
    /// Receives <see cref="ShaderProgram"/> from <see cref="NavMeshRenderer"/>; does not own it.
    /// </summary>
    public sealed class M2Renderer : IDisposable
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

        // M2 solid colour: red for trees/spikes/doodads (HB style)
        private const float CR = 0.85f;
        private const float CG = 0.15f;
        private const float CB = 0.15f;

        private bool _disposed;

        // ── Factory ───────────────────────────────────────────────────────────

        /// <summary>
        /// Loads bounding geometry from an M2 model into GPU buffers.
        /// EXACT same coordinate pipeline as WmoRenderer.LoadGeometry().
        /// </summary>
        public void LoadGeometry(M2File model, MDDF mddf)
        {
            if (!model.IsValid || model.Vertices.Length == 0 || model.Indices.Length == 0)
            {
                _indexCount = 0;
                return;
            }

            // ── Build G3D rotation matrix (row-vector convention) ──────────
            // Same as WmoRenderer: fromEulerAnglesXYZ(-rz*pi/180, -rx*pi/180, -ry*pi/180)
            float[,] rot = G3D_fromEulerAnglesXYZ(
                MathF.PI * mddf.rotZ / -180.0f,
                MathF.PI * mddf.rotX / -180.0f,
                MathF.PI * mddf.rotY / -180.0f);

            float scale = mddf.scale == 0 ? 1.0f : mddf.scale / 1024.0f;

            // fixCoords(pos) = (posZ, posX, posY), then subtract 32*GRID
            float gPosX = mddf.posZ - MAP_OFFSET;
            float gPosY = mddf.posX - MAP_OFFSET;
            float gPosZ = mddf.posY; // height, no offset

            var vertexData = new float[model.VertexCount * 6]; // x,y,z,r,g,b
            int vi = 0;

            for (int i = 0; i < model.Vertices.Length; i += 3)
            {
                float lx = model.Vertices[i];
                float ly = model.Vertices[i + 1];
                float lz = model.Vertices[i + 2];

                // G3D row-vector * rotation * scale + position
                float rx = (lx * rot[0,0] + ly * rot[1,0] + lz * rot[2,0]) * scale + gPosX;
                float ry = (lx * rot[0,1] + ly * rot[1,1] + lz * rot[2,1]) * scale + gPosY;
                float rz = (lx * rot[0,2] + ly * rot[1,2] + lz * rot[2,2]) * scale + gPosZ;

                // Mirror: v.x *= -1; v.y *= -1
                rx = -rx;
                ry = -ry;

                // copyVertices: dest = (v.y, v.z, v.x) → Recast (X, Y=height, Z)
                float recastX = ry;
                float recastY = rz;  // height
                float recastZ = rx;

                vertexData[vi++] = recastX;
                vertexData[vi++] = recastY;
                vertexData[vi++] = recastZ;
                vertexData[vi++] = CR;
                vertexData[vi++] = CG;
                vertexData[vi++] = CB;
            }

            // Convert indices to uint for EBO
            var indices = new uint[model.Indices.Length];
            for (int i = 0; i < model.Indices.Length; i++)
                indices[i] = (uint)model.Indices[i];

            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);

            _vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer,
                vertexData.Length * sizeof(float),
                vertexData,
                BufferUsageHint.StaticDraw);

            const int stride = 6 * sizeof(float);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            _ebo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer,
                indices.Length * sizeof(uint),
                indices,
                BufferUsageHint.StaticDraw);

            GL.BindVertexArray(0);
            _indexCount = indices.Length;
        }

        /// <summary>
        /// Reproduces G3D::Matrix3::fromEulerAnglesXYZ(ax, ay, az).
        /// Returns a 3×3 matrix in row-major order for row-vector multiplication.
        /// EXACT copy from WmoRenderer.
        /// </summary>
        private static float[,] G3D_fromEulerAnglesXYZ(float ax, float ay, float az)
        {
            float cx = MathF.Cos(ax), sx = MathF.Sin(ax);
            float cy = MathF.Cos(ay), sy = MathF.Sin(ay);
            float cz = MathF.Cos(az), sz = MathF.Sin(az);

            return new float[,]
            {
                {  cy*cz,               cy*sz,              -sy    },
                {  sx*sy*cz - cx*sz,    sx*sy*sz + cx*cz,    sx*cy },
                {  cx*sy*cz + sx*sz,    cx*sy*sz - sx*cz,    cx*cy }
            };
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        /// <summary>
        /// Renders the M2 bounding geometry.
        /// Same pattern as WmoRenderer.Render().
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
