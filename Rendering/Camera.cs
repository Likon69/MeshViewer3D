using System;
using OpenTK.Mathematics;

namespace MeshViewer3D.Rendering
{
    /// <summary>
    /// Blender-style orbit camera with pivot-centric controls.
    /// Kept API-compatible with legacy MainForm camera calls.
    /// </summary>
    public class Camera
    {
        // Primary state
        public Vector3 Target { get; set; } = Vector3.Zero;
        public float Distance { get; set; } = 500f;
        public float Yaw { get; set; } = -MathHelper.PiOver2;
        public float Pitch { get; set; } = 0.4f;

        // Projection settings
        public float FieldOfView { get; set; } = MathHelper.DegreesToRadians(45f);
        public float NearPlane { get; set; } = 0.5f;
        public float FarPlane { get; set; } = 50000f;

        // Backward compatibility with existing UI options
        public bool FreeCameraMode { get; set; } = false;
        public float FreeMoveSpeed { get; set; } = 420f;
        public float PrecisionMultiplier { get; set; } = 0.25f;

        private const float MaxPitch = MathHelper.PiOver2 - 0.01f;
        private const float MinDist = 1f;
        private const float StepFactor = 0.12f;

        public Vector3 Forward
        {
            get
            {
                var f = new Vector3(
                    MathF.Cos(Pitch) * MathF.Cos(Yaw),
                    MathF.Sin(Pitch),
                    MathF.Cos(Pitch) * MathF.Sin(Yaw));
                return f.LengthSquared > 1e-8f ? Vector3.Normalize(f) : Vector3.UnitZ;
            }
        }

        public Vector3 Right
        {
            get
            {
                var r = Vector3.Cross(Forward, Vector3.UnitY);
                return r.LengthSquared > 1e-8f ? Vector3.Normalize(r) : Vector3.UnitX;
            }
        }

        public Vector3 Up
        {
            get
            {
                var u = Vector3.Cross(Right, Forward);
                return u.LengthSquared > 1e-8f ? Vector3.Normalize(u) : Vector3.UnitY;
            }
        }

        public Vector3 Eye => Target - Forward * Distance;
        public Vector3 LookAt => Target;
        public Vector3 EyePosition => Eye;

        public Matrix4 GetViewMatrix() => Matrix4.LookAt(Eye, Target, Vector3.UnitY);

        public Matrix4 GetProjectionMatrix(float aspectRatio)
            => Matrix4.CreatePerspectiveFieldOfView(FieldOfView, aspectRatio, NearPlane, FarPlane);

        public void Orbit(float deltaYaw, float deltaPitch)
        {
            Yaw += deltaYaw;
            Pitch = Math.Clamp(Pitch - deltaPitch, -MaxPitch, MaxPitch);
        }

        public void Pan(float deltaScreenX, float deltaScreenY)
        {
            float speed = Distance * 0.0015f;
            Target -= Right * (deltaScreenX * speed);
            Target += Up * (deltaScreenY * speed);
        }

        public void ZoomTowardPoint(Vector3 hitPoint, float steps)
        {
            float normalizedSteps = MathF.Abs(steps) > 10f ? steps / 120f : steps;
            if (MathF.Abs(normalizedSteps) < 1e-6f)
                return;

            float factor = normalizedSteps > 0f
                ? MathF.Pow(1f - StepFactor, normalizedSteps)
                : MathF.Pow(1f + StepFactor, -normalizedSteps);

            float prevDist = Math.Max(Distance, MinDist);
            float newDist = Math.Clamp(prevDist * factor, MinDist, FarPlane * 0.9f);
            float t = 1f - (newDist / prevDist);

            Target += (hitPoint - Target) * (t * 0.5f);
            Distance = newDist;
        }

        // Backward-compatible zoom entrypoint (mouse wheel delta or synthetic steps)
        public void Zoom(float steps)
        {
            float normalizedSteps = MathF.Abs(steps) > 10f ? steps / 120f : steps;
            if (MathF.Abs(normalizedSteps) < 1e-6f)
                return;

            if (FreeCameraMode)
            {
                float direction = normalizedSteps > 0f ? 1f : -1f;
                float dollyStep = MathF.Max(0.5f, Distance * 0.08f) * MathF.Abs(normalizedSteps);
                Target += Forward * (direction * dollyStep);
                return;
            }

            float factor = normalizedSteps > 0f
                ? MathF.Pow(1f - StepFactor, normalizedSteps)
                : MathF.Pow(1f + StepFactor, -normalizedSteps);
            Distance = Math.Clamp(Distance * factor, MinDist, FarPlane * 0.9f);
        }

        public void FrameBounds(Vector3 bMin, Vector3 bMax, float marginFactor = 1.3f)
        {
            Target = (bMin + bMax) * 0.5f;
            float radius = (bMax - bMin).Length * 0.5f;
            Distance = Math.Max((radius / MathF.Sin(FieldOfView * 0.5f)) * marginFactor, MinDist);
        }

        public void SetFrontView() => (Yaw, Pitch) = (-MathHelper.PiOver2, 0f);
        public void SetBackView() => (Yaw, Pitch) = (MathHelper.PiOver2, 0f);
        public void SetRightView() => (Yaw, Pitch) = (0f, 0f);
        public void SetLeftView() => (Yaw, Pitch) = (MathHelper.Pi, 0f);
        public void SetTopView() => (Yaw, Pitch) = (-MathHelper.PiOver2, -MaxPitch);
        public void SetBottomView() => (Yaw, Pitch) = (-MathHelper.PiOver2, MaxPitch);

        // Legacy helpers used in the existing codebase
        public Vector3 GetEyePosition() => Eye;
        public Vector3 GetForwardVector() => Forward;
        public Vector3 GetRightVector() => Right;
        public Vector3 GetUpVector() => Up;

        public void FocusOn(Vector3 target, float? distance = null)
        {
            Target = target;
            if (distance.HasValue)
                Distance = Math.Clamp(distance.Value, MinDist, FarPlane * 0.9f);
        }

        public void TranslateLocal(float forward, float right, float up)
        {
            Target += Forward * forward;
            Target += Right * right;
            Target += Vector3.UnitY * up;
        }

        public void Reset()
        {
            Target = Vector3.Zero;
            Distance = 500f;
            Yaw = -MathHelper.PiOver2;
            Pitch = 0.4f;
        }
    }
}
