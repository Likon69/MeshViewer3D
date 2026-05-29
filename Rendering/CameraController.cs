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

        private bool _middleDragging;
        private bool _rightDragging;
        private bool _shiftAtDragStart;
        private bool _ctrlAtDragStart;
        private Point _lastMouse;

        public float OrbitSensitivity = 0.005f; // radians per pixel

        public CameraController(
            Camera camera,
            Func<int, int, Vector3?> raycastFunc,
            Func<(Vector3 bMin, Vector3 bMax)?> getSceneBounds)
        {
            _cam = camera;
            _raycast = raycastFunc;
            _getSceneBounds = getSceneBounds;
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
            if (!_middleDragging && !_rightDragging)
                return false;

            float dx = e.X - _lastMouse.X;
            float dy = e.Y - _lastMouse.Y;
            _lastMouse = e.Location;

            if (_rightDragging)
            {
                if (_cam.FreeCameraMode)
                    // Free cam: right drag = look around (FPS-style, eye stays fixed)
                    _cam.FreeLook(dx * OrbitSensitivity, dy * OrbitSensitivity);
                else
                    _cam.Pan(dx, dy);
                return true;
            }

            bool panning = _shiftAtDragStart || modifiers.HasFlag(Keys.Shift);
            bool zooming = _ctrlAtDragStart || modifiers.HasFlag(Keys.Control);

            if (zooming)
            {
                _cam.Zoom(-dy * 0.03f);
            }
            else if (panning)
            {
                _cam.Pan(dx, dy);
            }
            else
            {
                _cam.Orbit(dx * OrbitSensitivity, dy * OrbitSensitivity);
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
    }
}
