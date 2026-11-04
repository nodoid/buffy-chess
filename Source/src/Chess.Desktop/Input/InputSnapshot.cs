using System;
using Chess.Core;
using Chess.Desktop.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Chess.Desktop.Input
{
    /// <summary>
    /// A device-agnostic frame of cursor input. The game loop only ever sees an
    /// InputSnapshot, never a MouseState, TouchState or a platform gesture: each
    /// platform's bootstrap slices its own events into this one shape (chapter 2).
    /// NUnit tests construct snapshots by hand - no window, no device required.
    /// </summary>
    public struct InputSnapshot
    {
        public Vector2 Point;            // in platform surface coordinates
        public bool PrimaryPressed;      // left button / single touch
        public bool PrimaryDown;         // held this frame
        public bool SecondaryDown;       // right button / two-finger tap
        public bool MiddleDown;          // middle button / two-finger drag
        public int ScrollDelta;          // wheel notch / pinch delta, platform units

        public static InputSnapshot FromMouse(MouseState m, MouseState prev)
        {
            return new InputSnapshot
            {
                Point = new Vector2(m.Position.X, m.Position.Y),
                PrimaryPressed = m.LeftButton == ButtonState.Released &&
                                 prev.LeftButton == ButtonState.Pressed,
                PrimaryDown = m.LeftButton == ButtonState.Pressed,
                SecondaryDown = m.RightButton == ButtonState.Pressed,
                MiddleDown = m.MiddleButton == ButtonState.Pressed,
                ScrollDelta = m.ScrollWheelValue - prev.ScrollWheelValue,
            };
        }

        // ------------------------------------------------------------ routing

        /// <summary>Zoom when the wheel moved; pan when two-finger drag; pick via
        /// ChessGame.HandleClick otherwise. Returns true when consumed. Kept in
        /// one place so desktop mice, Android touches and iOS trackpads all take
        /// this exact path.</summary>
        public bool RouteCamera(Camera2D camera, int viewportW, int viewportH)
        {
            if (ScrollDelta != 0)
            {
                camera.ZoomBy(ScrollDelta > 0 ? 1.12f : 1 / 1.12f,
                              Point, viewportW, viewportH);
                return true;
            }
            if (MiddleDown)
                camera.Pan(Point);        // StartPan run on the transition (see ChessGame)
            return false;
        }

        public int SquareUnder(Camera2D camera) => camera.SquareAt(Point);
    }
}
