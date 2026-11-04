using System;
using Microsoft.Xna.Framework;

namespace Chess.Desktop.Rendering
{
    /// <summary>
    /// The book's teaching camera: a 2D transform with wheel-zoom around the
    /// cursor and middle-drag panning. Rendering only ever multiplies the sprite
    /// batch's transform matrix, so every platform (GestureSample on mobile,
    /// MouseState here) feeds the exact same Camera2D.
    /// </summary>
    public sealed class Camera2D
    {
        public float Zoom = 1f;
        public Vector2 ScreenOrigin;            // board-space point drawn at (0,0)
        private Vector2 _anchorScreen;          // screen point held during a pan
        private Vector2 _anchorBoard;

        public float MinZoom = 0.5f;
        public float MaxZoom = 2.5f;

        public Matrix View => Matrix.CreateTranslation(new Vector3(-ScreenOrigin, 0))
                            * Matrix.CreateScale(Zoom, Zoom, 1);

        /// <summary>Offset (in board pixels) of the *board's top-left corner*.</summary>
        public Vector2 BoardOffset = new Vector2(40, 36);

        public void ZoomBy(float factor, Vector2 screenPoint, int viewportW, int viewportH)
        {
            float old = Zoom;
            Zoom = MathHelper.Clamp(Zoom * factor, MinZoom, MaxZoom);
            if (System.Math.Abs(Zoom - old) < 1e-6f) return;

            // keep the point under the cursor fixed while zooming, the way every
            // map application behaves
            var world = ScreenToWorld(screenPoint);
            ScreenOrigin += world * (1f - old / Zoom);
            ClampToBoard(viewportW, viewportH);
        }

        public void ClampToBoard(int viewportW, int viewportH)
        {
            float viewW = viewportW / Zoom, viewH = viewportH / Zoom;
            const float BoardSpan = 8f * 96f;
            float minX = BoardOffset.X - 24, maxX = BoardOffset.X + BoardSpan - viewW + 24;
            float minY = BoardOffset.Y - 24, maxY = BoardOffset.Y + BoardSpan - viewH + 24;
            ScreenOrigin.X = maxX < minX ? (minX + maxX) / 2 : MathHelper.Clamp(ScreenOrigin.X, minX, maxX);
            ScreenOrigin.Y = maxY < minY ? (minY + maxY) / 2 : MathHelper.Clamp(ScreenOrigin.Y, minY, maxY);
        }

        public void StartPan(Vector2 screenPoint)
        {
            _anchorScreen = screenPoint;
            _anchorBoard = ScreenOrigin;
        }

        public void Pan(Vector2 screenPoint)
        {
            ScreenOrigin = _anchorBoard + (_anchorScreen - screenPoint) / Zoom;
        }

        public Vector2 ScreenToWorld(Vector2 screenPoint)
        {
            return ScreenOrigin + screenPoint / Zoom;
        }

        /// <summary>Board square (0..63) under a screen point, or Sq.None outside.</summary>
        public int SquareAt(Vector2 screenPoint)
        {
            var p = ScreenToWorld(screenPoint) - BoardOffset;
            if (p.X < 0 || p.Y < 0) return Chess.Core.Sq.None;
            int file = (int)(p.X / 96f), rank = (int)(p.Y / 96f);
            if (file > 7 || rank > 7) return Chess.Core.Sq.None;
            return rank * 8 + file;
        }

        /// <summary>Screen rectangle for a board square, after the camera transform.</summary>
        public Microsoft.Xna.Framework.Rectangle ScreenRect(int square, int viewportW, int viewportH)
        {
            var world = new Vector2(
                BoardOffset.X + Chess.Core.Sq.FileOf(square) * 96f,
                BoardOffset.Y + Chess.Core.Sq.RankOf(square) * 96f);
            var screen = (world - ScreenOrigin) * Zoom;
            return new Microsoft.Xna.Framework.Rectangle((int)screen.X, (int)screen.Y,
                (int)(96 * Zoom), (int)(96 * Zoom));
        }
    }
}
