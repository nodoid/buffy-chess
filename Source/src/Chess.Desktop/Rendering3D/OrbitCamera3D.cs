using System;
using Microsoft.Xna.Framework;

namespace Chess.Desktop.Rendering3D
{
    /// <summary>
    /// The chapter 6 teaching camera: orbits a target point on the board plane.
    /// Every input source - mouse, trackpad, fingers - feeds the same three
    /// primitives: <see cref="OrbitBy"/>, <see cref="PanBy"/>, <see cref="ZoomBy"/>.
    /// Pure matrix maths, so NUnit can drive it with no window (chapter 7).
    /// </summary>
    public sealed class OrbitCamera3D
    {
        public Vector3 Target = new Vector3(3.5f, 0f, 3.5f);   // board centre in world units
        public float Yaw = -1.30f;      // radians about +Y; white's rank-1 side
                                        // sits on the small-z edge, so a yaw near
                                        // -pi/2 puts White nearest the viewer (0
                                        // would look from +X)
        public float Pitch = 0.95f;     // 0 = side-on, 1.45 = near top-down
        public float Distance = 10.5f;  // eye metres from the target

        public const float MinPitch = 0.18f, MaxPitch = 1.45f;
        public const float MinDistance = 5f, MaxDistance = 40f;

        /// <summary>Vertical field of view. Wider on portrait phones so the board fits.</summary>
        public float FieldOfView = MathHelper.PiOver4;

        public Matrix View => Matrix.CreateLookAt(Eye, Target, Vector3.Up);

        public Matrix Projection { get; private set; }

        public OrbitCamera3D(float aspectRatio)
        {
            SetAspect(aspectRatio);
        }

        public void SetAspect(float aspectRatio)
        {
            Projection = Matrix.CreatePerspectiveFieldOfView(FieldOfView, aspectRatio, 0.1f, 200f);
        }

        /// <summary>The eye sits on a sphere around the target: yaw around the
        /// vertical axis, pitch out of the board plane, radius = Distance.</summary>
        public Vector3 Eye
        {
            get
            {
                float cp = MathF.Cos(Pitch), sp = MathF.Sin(Pitch);
                float cy = MathF.Cos(Yaw),  sy = MathF.Sin(Yaw);
                return Target + Distance * new Vector3(cy * cp, sp, sy * cp);
            }
        }

        /// <summary>Chooses the starting distance so the whole board fits in the
        /// viewport at the camera's fixed vertical FOV. Desktop loads: a wide
        /// screen fits the 8 m board at ~12 m; a portrait phone needs ~27 m,
        /// which is why the shell calls this once whenever the backbuffer size
        /// changes (and lets the player zoom from there).</summary>
        public void Fit(float aspectRatio, float halfExtent = 4.7f)
        {
            float half = MathF.Tan(FieldOfView / 2f)
                       * MathF.Min(1f, aspectRatio);
            Distance = MathHelper.Clamp(halfExtent / half + 2f,
                                        MinDistance, MaxDistance);
        }

        /// <summary>Orbit: yaw wraps freely, pitch is clamped so the eye never
        /// goes under the horizon and never stacks directly overhead (both make
        /// Unified Up impossible for look-at).</summary>
        public void OrbitBy(float deltaYaw, float deltaPitch)
        {
            Yaw += deltaYaw;
            Pitch = MathHelper.Clamp(Pitch + deltaPitch, MinPitch, MaxPitch);
        }

        /// <summary>Pan the target across the board plane. Screen-relative:
        /// dragging left always drags the *player's* left, whichever side of
        /// the board the yaw has put you on. With right = forward x up
        /// (forward = eye-to-target on the plane), right = (sin, 0, -cos) and
        /// forward = (-cos, 0, -sin) - both derived, never hard-coded.</summary>
        public void PanBy(float deltaRight, float deltaForward)
        {
            float cy = MathF.Cos(Yaw), sy = MathF.Sin(Yaw);
            Target.X += sy * deltaRight - cy * deltaForward;
            Target.Z += -cy * deltaRight - sy * deltaForward;
            Target.X = MathHelper.Clamp(Target.X, -1.5f, 8.5f);
            Target.Z = MathHelper.Clamp(Target.Z, -1.5f, 8.5f);
        }

        /// <summary>Distance zoom; factor &gt; 1 zooms in, mirroring the 2D wheel.</summary>
        public void ZoomBy(float factor)
        {
            Distance = MathHelper.Clamp(Distance / factor, MinDistance, MaxDistance);
        }

        /// <summary>The world-space ray through a screen point (chapter 7's pick).
        /// Returns (origin, direction); direction is NOT normalised.
        /// Deliberately *not* Viewport.Unproject - that API needs a live
        /// GraphicsDevice, and chapter 7's tests run headless with pure maths.</summary>
        public (Vector3 Origin, Vector3 Dir) RayThrough(float sx, float sy, int viewportW, int viewportH)
        {
            var near = Unproject(new Vector3(sx, sy, 0f), viewportW, viewportH);
            var far = Unproject(new Vector3(sx, sy, 1f), viewportW, viewportH);
            return (near, far - near);
        }

        /// <summary>Screen (pixels, zDepth 0 = near plane .. 1 = far plane) ->
        /// world point, via the inverse view-projection matrix and a manual
        /// homogeneous divide.</summary>
        public Vector3 Unproject(Vector3 screen, int viewportW, int viewportH)
        {
            var ndc = new Vector4(
                screen.X / viewportW * 2f - 1f,     // X: pixel -> -1..1
                1f - screen.Y / viewportH * 2f,     // Y: flip (screen Y grows down)
                screen.Z,                            // Z: NDC 0..1 on every MonoGame backend
                1f);
            Matrix invVp = Matrix.Invert(View * Projection);
            var t = Vector4.Transform(ndc, invVp);
            return new Vector3(t.X / t.W, t.Y / t.W, t.Z / t.W);
        }
    }
}
