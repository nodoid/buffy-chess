using Chess.Desktop.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;

namespace Chess.Desktop.Input
{
    /// <summary>
    /// Slices Android / iOS touch events into the same <see cref="InputSnapshot"/>
    /// the mouse produces (the chapter 2 contract), so nothing in the game loop
    /// ever branches on platform. One finger acts as the pointer, a one-finger
    /// drag pans the camera, a two-finger pinch zooms via the wheel-notch path.
    /// Touch is merged on top of mouse data, so hybrid devices (touchscreen
    /// laptops, Surface-style tablets) work with both at once.
    /// </summary>
    public sealed class TouchInputMapper
    {
        bool _wasDown;             // finger edge detection across frames
        bool _gesturesEnabled;     // True once the platform touch service exists

        /// <summary>Gesture enabling deliberately happens lazily on the first
        /// CollectInto call, not in the constructor: on iOS MonoGame's touch
        /// platform service is not wired up until after Game.Initialize, and
        /// TouchPanel.EnabledGestures throws a NullReferenceException if it is
        /// touched earlier (the bug that taught the simulator how to fail).</summary>
        void EnsureGestures()
        {
            if (_gesturesEnabled) return;
            TouchPanel.EnabledGestures =
                GestureType.FreeDrag | GestureType.Pinch | GestureType.DoubleTap;
            _gesturesEnabled = true;
        }

        /// <summary>Merges one frame of device touch state into <paramref name="snap"/>.</summary>
        public void CollectInto(ref InputSnapshot snap)
        {
            EnsureGestures();
            TouchCollection touches = TouchPanel.GetState();
            bool down = false;
            Vector2 point = snap.Point;

            if (touches.Count > 0)
            {
                TouchLocation t = touches[0];
                point = t.Position;
                down = t.State != TouchLocationState.Released &&
                       t.State != TouchLocationState.Invalid;
            }

            while (TouchPanel.IsGestureAvailable)
            {
                GestureSample g = TouchPanel.ReadGesture();
                switch (g.GestureType)
                {
                    case GestureType.FreeDrag:              // one-finger pan
                        snap.MiddleDown = true;             // RouteCamera's pan hook
                        point = g.Position;
                        down = true;
                        break;

                    case GestureType.Pinch:                 // two-finger zoom -> wheel notch
                        float pre = Vector2.Distance(g.Position, g.Position2);
                        float post = Vector2.Distance(
                            g.Position + g.Delta, g.Position2 + g.Delta2);
                        int notch = (int)((post - pre) / 60f);   // 60 px per wheel-notch
                        if (notch != 0) snap.ScrollDelta += notch;
                        break;

                    case GestureType.DoubleTap:             // reserved: context menu
                        break;
                }
            }

            if (down || snap.ScrollDelta != 0)
            {
                snap.Point = point;
                snap.PrimaryDown |= down;
                snap.PrimaryPressed |= down && !_wasDown;   // press edge, mouse coexists
            }
            _wasDown = down;
        }
    }
}
