using System;
using System.Collections.Generic;
using Chess.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chess.Desktop.Rendering
{
    /// <summary>
    /// Draws the board itself: squares, selection, legal-move markers, last move
    /// trace and the pieces on top of the atlas. Board pixels are 96 per square
    /// in board space; the Camera2D turns those into screen pixels.
    /// </summary>
    public sealed class BoardRenderer
    {
        private readonly PieceAtlas _atlas;
        private Texture2D _white;

        public const float SquarePx = PieceAtlas.Cell;

        public BoardRenderer(PieceAtlas atlas, GraphicsDevice gd)
        {
            _atlas = atlas;
            _white = new Texture2D(gd, 1, 1);
            _white.SetData(new[] { Color.White });
        }

        public struct Scene
        {
            public Position Position;
            public int Selected;                 // Sq.None when nothing is selected
            public List<Move> DestinationMoves;  // legal moves from Selected
            public Move LastMove;
            public int SideToMoveHint;           // for the frame colour pulse
        }

        public void Draw(SpriteBatch sb, in Scene scene, Camera2D camera)
        {
            var pos = scene.Position;
            var view = camera.ScreenRect(0, 1, 1);   // sized for square a1 → derive step from zoom
            float step = 96f * camera.Zoom;

            var boardOrigin = (camera.BoardOffset - camera.ScreenOrigin) * camera.Zoom;
            var boardRect = new Rectangle(
                (int)boardOrigin.X, (int)boardOrigin.Y,
                (int)(8 * step) + 1, (int)(8 * step) + 1);

            sb.Draw(_atlas.BoardLight, new Rectangle(boardRect.X - 12, boardRect.Y - 12,
                boardRect.Width + 24, boardRect.Height + 24), new Color(52, 48, 60));  // frame

            // squares (two per colour via a stretched white sprite)
            for (int rank = 0; rank < 8; rank++)
                for (int file = 0; file < 8; file++)
                {
                    int sq = rank * 8 + file;
                    var rect = new Rectangle(
                        (int)(boardOrigin.X + file * step), (int)(boardOrigin.Y + rank * step),
                        (int)step + 1, (int)step + 1);
                    bool light = ((file + rank) & 1) == 0;

                    Color tint = Color.White;
                    if (scene.LastMove != default &&
                        (sq == scene.LastMove.From || sq == scene.LastMove.To))
                        tint = new Color(255, 236, 150);
                    else if (scene.Selected == sq)
                        tint = new Color(180, 226, 255);

                    sb.Draw(light ? _atlas.BoardLight : _atlas.BoardDark, rect, tint);

                    var piece = pos[sq];
                    if (!piece.IsNone) DrawPiece(sb, scene, camera, piece, sq, file, rank, step);
                }

            // legal destination markers
            if (scene.DestinationMoves != null)
            {
                foreach (var m in scene.DestinationMoves)
                {
                    var center = SquareCenter(scene, camera, m.To, step);
                    int dot = (int)(step / 5);
                    var rect = new Rectangle((int)(center.X - dot / 2), (int)(center.Y - dot / 2), dot, dot);
                    sb.Draw(_white, rect, new Color(30, 90, 40, 140));
                }
            }
        }

        private static Vector2 SquareCenter(in Scene scene, Camera2D camera, int sq, float step)
        {
            var origin = (camera.BoardOffset - camera.ScreenOrigin) * camera.Zoom;
            return new Vector2(
                origin.X + (Sq.FileOf(sq) + 0.5f) * step,
                origin.Y + (Sq.RankOf(sq) + 0.5f) * step);
        }

        private void DrawPiece(SpriteBatch sb, in Scene scene, Camera2D camera,
            Piece piece, int sq, int file, int rank, float step)
        {
            var tex = _atlas.Piece((int)piece.Type, (int)piece.Side);
            var rect = camera.ScreenRect(sq, 0, 0);
            sb.Draw(tex, rect, Color.White);
        }
    }
}
