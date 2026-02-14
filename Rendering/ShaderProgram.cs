using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;

namespace MeshViewer3D.Rendering
{
    /// <summary>
    /// Gestionnaire de shaders OpenGL
    /// Compile et link vertex + fragment shaders
    /// </summary>
    public class ShaderProgram : IDisposable
    {
        public int ProgramId { get; private set; }
        private bool _disposed;

        public ShaderProgram(string vertexPath, string fragmentPath)
        {
            // Charger les sources avec encoding UTF8
            string vertexSource = File.ReadAllText(vertexPath, System.Text.Encoding.UTF8);
            string fragmentSource = File.ReadAllText(fragmentPath, System.Text.Encoding.UTF8);

            // Debug: afficher le nombre de lignes
            Console.WriteLine($"Vertex shader lines: {vertexSource.Split('\n').Length}");
            Console.WriteLine($"Fragment shader lines: {fragmentSource.Split('\n').Length}");

            // Compiler vertex shader
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexSource);
            GL.CompileShader(vertexShader);
            CheckCompileErrors(vertexShader, "VERTEX");

            // Compiler fragment shader
            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentSource);
            GL.CompileShader(fragmentShader);
            CheckCompileErrors(fragmentShader, "FRAGMENT");

            // Link program
            ProgramId = GL.CreateProgram();
            GL.AttachShader(ProgramId, vertexShader);
            GL.AttachShader(ProgramId, fragmentShader);
            GL.LinkProgram(ProgramId);
            CheckLinkErrors(ProgramId);

            // Nettoyer les shaders (plus besoin après link)
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
        }

        public void Use()
        {
            GL.UseProgram(ProgramId);
        }

        // Uniform setters
        public void SetInt(string name, int value)
        {
            GL.Uniform1(GL.GetUniformLocation(ProgramId, name), value);
        }

        public void SetFloat(string name, float value)
        {
            GL.Uniform1(GL.GetUniformLocation(ProgramId, name), value);
        }

        public void SetVector3(string name, OpenTK.Mathematics.Vector3 value)
        {
            GL.Uniform3(GL.GetUniformLocation(ProgramId, name), value.X, value.Y, value.Z);
        }

        public void SetVector4(string name, System.Numerics.Vector4 value)
        {
            GL.Uniform4(GL.GetUniformLocation(ProgramId, name), value.X, value.Y, value.Z, value.W);
        }

        public void SetMatrix4(string name, OpenTK.Mathematics.Matrix4 value)
        {
            GL.UniformMatrix4(GL.GetUniformLocation(ProgramId, name), false, ref value);
        }

        public void SetBool(string name, bool value)
        {
            GL.Uniform1(GL.GetUniformLocation(ProgramId, name), value ? 1 : 0);
        }

        private void CheckCompileErrors(int shader, string type)
        {
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
            if (success == 0)
            {
                string infoLog = GL.GetShaderInfoLog(shader);
                throw new Exception($"Shader compilation error ({type}):\n{infoLog}");
            }
        }

        private void CheckLinkErrors(int program)
        {
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int success);
            if (success == 0)
            {
                string infoLog = GL.GetProgramInfoLog(program);
                throw new Exception($"Shader linking error:\n{infoLog}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                GL.DeleteProgram(ProgramId);
                _disposed = true;
            }
        }
    }
}
