using System;
using OpenTK.Mathematics;

namespace MeshViewer3D.Rendering
{
    /// <summary>
    /// Caméra orbite professionnelle style Honorbuddy
    /// Contrôles: Molette=zoom, Middle/Left drag=orbite, Right drag=pan
    /// </summary>
    public class Camera
    {
        // État de la caméra
        public Vector3 Target { get; set; } = Vector3.Zero;
        public float Distance { get; set; } = 500f;
        public float Yaw { get; set; } = 45f;           // Rotation horizontale (degrés)
        public float Pitch { get; set; } = 45f;         // Rotation verticale (degrés)

        // Limites
        public float MinDistance { get; set; } = 10f;
        public float MaxDistance { get; set; } = 5000f;
        public float MinPitch { get; set; } = -89f;
        public float MaxPitch { get; set; } = 89f;

        // Vitesses
        public float OrbitSensitivity { get; set; } = 0.3f;
        public float PanSensitivity { get; set; } = 0.5f;
        public float ZoomSensitivity { get; set; } = 0.1f;

        /// <summary>
        /// Applique rotation orbite (mouse drag)
        /// </summary>
        public void Orbit(float deltaX, float deltaY)
        {
            Yaw += deltaX * OrbitSensitivity;
            Pitch -= deltaY * OrbitSensitivity;

            // Normaliser Yaw
            while (Yaw > 360f) Yaw -= 360f;
            while (Yaw < 0f) Yaw += 360f;

            // Clamp Pitch
            Pitch = Math.Clamp(Pitch, MinPitch, MaxPitch);
        }

        /// <summary>
        /// Applique pan (déplacement du target)
        /// </summary>
        public void Pan(float deltaX, float deltaY)
        {
            var right = GetRightVector();
            var up = GetUpVector();

            float panSpeed = Distance * PanSensitivity * 0.001f;
            Target += right * (-deltaX * panSpeed);
            Target += up * (deltaY * panSpeed);
        }

        /// <summary>
        /// Applique zoom (molette souris)
        /// </summary>
        public void Zoom(float delta)
        {
            float factor = delta > 0 ? (1f - ZoomSensitivity) : (1f + ZoomSensitivity);
            Distance *= factor;
            Distance = Math.Clamp(Distance, MinDistance, MaxDistance);
        }

        /// <summary>
        /// Calcule la matrice View pour OpenGL
        /// </summary>
        public Matrix4 GetViewMatrix()
        {
            var eye = GetEyePosition();
            return Matrix4.LookAt(eye, Target, Vector3.UnitY);
        }

        /// <summary>
        /// Calcule la position de l'œil de la caméra
        /// </summary>
        public Vector3 GetEyePosition()
        {
            float yawRad = MathHelper.DegreesToRadians(Yaw);
            float pitchRad = MathHelper.DegreesToRadians(Pitch);

            // Direction depuis target vers eye
            var forward = new Vector3(
                MathF.Cos(pitchRad) * MathF.Sin(yawRad),
                MathF.Sin(pitchRad),
                MathF.Cos(pitchRad) * MathF.Cos(yawRad)
            );

            return Target - forward * Distance;
        }

        /// <summary>
        /// Calcule le vecteur forward (direction de vue)
        /// </summary>
        public Vector3 GetForwardVector()
        {
            float yawRad = MathHelper.DegreesToRadians(Yaw);
            float pitchRad = MathHelper.DegreesToRadians(Pitch);

            return new Vector3(
                MathF.Cos(pitchRad) * MathF.Sin(yawRad),
                MathF.Sin(pitchRad),
                MathF.Cos(pitchRad) * MathF.Cos(yawRad)
            );
        }

        /// <summary>
        /// Calcule le vecteur right (droite de la caméra)
        /// </summary>
        public Vector3 GetRightVector()
        {
            float yawRad = MathHelper.DegreesToRadians(Yaw);
            return new Vector3(
                MathF.Cos(yawRad),
                0f,
                -MathF.Sin(yawRad)
            );
        }

        /// <summary>
        /// Calcule le vecteur up (haut de la caméra)
        /// </summary>
        public Vector3 GetUpVector()
        {
            var forward = GetForwardVector();
            var right = GetRightVector();
            var cross = Vector3.Cross(right, forward);
            return Vector3.Normalize(cross);
        }

        /// <summary>
        /// Recentre la caméra sur une position
        /// </summary>
        public void FocusOn(Vector3 target, float? distance = null)
        {
            Target = target;
            if (distance.HasValue)
                Distance = Math.Clamp(distance.Value, MinDistance, MaxDistance);
        }

        /// <summary>
        /// Reset la caméra à une vue par défaut
        /// </summary>
        public void Reset()
        {
            Target = Vector3.Zero;
            Distance = 500f;
            Yaw = 45f;
            Pitch = 45f;
        }
    }
}
