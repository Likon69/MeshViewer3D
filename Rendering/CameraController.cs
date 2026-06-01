using System;
using System.Drawing;
using System.Windows.Forms;
using OpenTK.Mathematics;

namespace MeshViewer3D.Rendering
{
    /// <summary>
    /// Camera input controller with Blender-style mappings.
    /// Left click stays dedicated to selection/editing.
    /// </summary>
    public class CameraController
    {
        private readonly Camera _cam;
        private readonly Func<int, int, Vector3?> _raycast;
        private readonly Func<(Vector3 bMin, Vector3 bMax)?> _getSceneBounds;
        private readonly Control? _viewport;

        private bool _middleDragging;
        private bool _rightDragging;
        private bool _shiftAtDragStart;
        private bool _ctrlAtDragStart;
        private Point _lastMouse;

        // Free camera: pointer-lock state
        private bool _freeLookActive;

        public float OrbitSensitivity = 0.005f;   // radians per pixel
        public float FreeLookSensitivity = 0.0015f;

        public CameraController(
            Camera camera,
            Func<int, int, Vector3?> raycastFunc,
            Func<(Vector3 bMin, Vector3 bMax)?> getSceneBounds,
            Control? viewport = null)
        {
            _cam = camera;
            _raycast = raycastFunc;
            _getSceneBounds = getSceneBounds;
            _viewport = viewport;
        }

        /// <summary>
        /// Active le pointer-lock pour le mode free cam (souris capturee, curseur caché).
        /// </summary>
        public void ActivateFreeLook()
        {
            if (_freeLookActive || _viewport == null) return;

            _freeLookActive = true;
            _viewport.Cursor = Cursors.NoMove2D;
            CenterCursor();
        }

        /// <summary>
        /// Désactive le pointer-lock du mode free cam.
        /// </summary>
        public void DeactivateFreeLook()
        {
            if (!_freeLookActive) return;

            _freeLookActive = false;
            if (_viewport != null)
                _viewport.Cursor = Cursors.Default;
        }

        public bool OnMouseDown(MouseEventArgs e, Keys modifiers)
        {
            bool shift = modifiers.HasFlag(Keys.Shift);
            bool ctrl = modifiers.HasFlag(Keys.Control);

            if (e.Button == MouseButtons.Middle)
            {
                _middleDragging = true;
                _shiftAtDragStart = shift;
                _ctrlAtDragStart = ctrl;
                _lastMouse = e.Location;
                return true;
            }

            if (e.Button == MouseButtons.Right)
            {
                _rightDragging = true;
                _shiftAtDragStart = false;
                _ctrlAtDragStart = false;
                _lastMouse = e.Location;
                return true;
            }

            return false;
        }

        public bool OnMouseMove(MouseEventArgs e, Keys modifiers)
        {
            // Free camera: pointer-lock — le mouvement de la souris tourne la vue directement
            if (_freeLookActive && _cam.FreeCameraMode)
            {
                var center = new Point(_viewport!.Width / 2, _viewport.Height / 2);
                int dx = e.X - center.X;
                int dy = e.Y - center.Y;

                if (dx != 0 || dy != 0)
                {
                    _cam.FreeLook(dx * FreeLookSensitivity, dy * FreeLookSensitivity);
                    CenterCursor();
                }

                return true;
            }

            if (!_middleDragging && !_rightDragging)
                return false;

            float dragDx = e.X - _lastMouse.X;
            float dragDy = e.Y - _lastMouse.Y;
            _lastMouse = e.Location;

            if (_rightDragging)
            {
                if (_cam.FreeCameraMode)
                    _cam.FreeLook(dragDx * OrbitSensitivity, dragDy * OrbitSensitivity);
                else
                    _cam.Pan(dragDx, dragDy);
                return true;
            }

            bool panning = _shiftAtDragStart || modifiers.HasFlag(Keys.Shift);
            bool zooming = _ctrlAtDragStart || modifiers.HasFlag(Keys.Control);

            if (zooming)
            {
                _cam.Zoom(-dragDy * 0.03f);
            }
            else if (panning)
            {
                _cam.Pan(dragDx, dragDy);
            }
            else
            {
                _cam.Orbit(dragDx * OrbitSensitivity, dragDy * OrbitSensitivity);
            }

            return true;
        }

        public bool OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle && _middleDragging)
            {
                _middleDragging = false;
                return true;
            }

            if (e.Button == MouseButtons.Right && _rightDragging)
            {
                _rightDragging = false;
                return true;
            }

            return false;
        }

        public void OnMouseWheel(MouseEventArgs e)
        {
            float steps = e.Delta / 120f;
            Vector3 hitPoint = _raycast(e.X, e.Y) ?? _cam.Target;
            _cam.ZoomTowardPoint(hitPoint, steps);
        }

        public bool OnKeyDown(KeyEventArgs e)
        {
            bool ctrl = e.Control;

            switch (e.KeyCode)
            {
                case Keys.NumPad1:
                    if (ctrl) _cam.SetBackView();
                    else _cam.SetFrontView();
                    return true;

                case Keys.NumPad3:
                    if (ctrl) _cam.SetLeftView();
                    else _cam.SetRightView();
                    return true;

                case Keys.NumPad7:
                    if (ctrl) _cam.SetBottomView();
                    else _cam.SetTopView();
                    return true;

                case Keys.Decimal:
                    FrameScene();
                    return true;

                case Keys.Home:
                    FrameScene();
                    return true;

                case Keys.R:
                    if (!ctrl && !e.Shift)
                    {
                        FrameScene();
                        return true;
                    }
                    break;
            }

            return false;
        }

        public void FrameScene()
        {
            var bounds = _getSceneBounds();
            if (bounds.HasValue)
                _cam.FrameBounds(bounds.Value.bMin, bounds.Value.bMax);
        }

        public bool IsDragging => _middleDragging || _rightDragging;

        private void CenterCursor()
        {
            if (_viewport == null) return;
            var center = _viewport.PointToScreen(new Point(_viewport.Width / 2, _viewport.Height / 2));
            Cursor.Position = center;
        }
    }
}
