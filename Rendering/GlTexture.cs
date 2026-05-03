// ============================================================================
//  GlTexture.cs  —  OpenGL texture helper for BLP image upload
//
//  Takes decoded BLP pixel data (RGBA byte[]) and creates a GL_TEXTURE_2D
//  with mipmaps. Designed as a lightweight wrapper — no texture manager,
//  no atlas, just one GL texture per BLP file.
//
//  Usage:
//    var blp = BlpFile.Load(bytes);
//    using var tex = GlTexture.FromBlp(blp);
//    tex.Bind(TextureUnit.Texture0);
// ============================================================================

using System;
using OpenTK.Graphics.OpenGL4;
using MeshViewer3D.Core.Formats.Blp;

namespace MeshViewer3D.Rendering
{
    /// <summary>
    /// Manages a single OpenGL texture resource created from BLP image data.
    /// Implements IDisposable for proper GPU resource cleanup.
    /// </summary>
    public sealed class GlTexture : IDisposable
    {
        /// <summary>OpenGL texture handle (0 = not created).</summary>
        public int Handle { get; private set; }

        /// <summary>Texture width in pixels.</summary>
        public int Width { get; private set; }

        /// <summary>Texture height in pixels.</summary>
        public int Height { get; private set; }

        private bool _disposed;

        // ── Factory ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates an OpenGL texture from a decoded BLP file.
        /// Generates mipmaps automatically via glGenerateMipmap.
        /// </summary>
        public static GlTexture FromBlp(BlpFile blp)
        {
            if (blp is null) throw new ArgumentNullException(nameof(blp));
            if (!blp.IsValid || blp.PixelData.Length == 0)
                throw new ArgumentException("BLP file is not valid or has no pixel data.", nameof(blp));

            int handle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, handle);

            // Upload pixel data: RGBA, UNSIGNED_BYTE
            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgba8,
                blp.Width,
                blp.Height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                blp.PixelData);

            // Generate mipmaps
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

            // Set sampling parameters (terrain-friendly defaults)
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            // Repeat wrapping (terrain tiles)
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            // Anisotropic filtering (if supported)
            // GL 3.3 supports EXT_texture_filter_anisotropic
            float maxAniso;
            GL.GetFloat((GetPName)0x84FF, out maxAniso); // MAX_TEXTURE_MAX_ANISOTROPY
            if (maxAniso > 1f)
            {
                GL.TexParameter(TextureTarget.Texture2D,
                    (TextureParameterName)0x84FE, // TEXTURE_MAX_ANISOTROPY
                    Math.Min(maxAniso, 4f));
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);

            return new GlTexture
            {
                Handle = handle,
                Width  = blp.Width,
                Height = blp.Height,
            };
        }

        /// <summary>
        /// Creates a 1×1 solid-color texture (useful as fallback when BLP loading fails).
        /// </summary>
        public static GlTexture SolidColor(byte r, byte g, byte b, byte a = 255)
        {
            int handle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, handle);

            var pixels = new byte[] { r, g, b, a };
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            GL.BindTexture(TextureTarget.Texture2D, 0);

            return new GlTexture
            {
                Handle = handle,
                Width  = 1,
                Height = 1,
            };
        }

        // ── Binding ───────────────────────────────────────────────────────────

        /// <summary>Binds this texture to the specified texture unit.</summary>
        public void Bind(TextureUnit unit = TextureUnit.Texture0)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GlTexture));
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2D, Handle);
        }

        /// <summary>Unbinds the current texture from the specified unit.</summary>
        public static void Unbind(TextureUnit unit = TextureUnit.Texture0)
        {
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            if (Handle != 0)
            {
                GL.DeleteTexture(Handle);
                Handle = 0;
            }
            _disposed = true;
        }
    }
}
