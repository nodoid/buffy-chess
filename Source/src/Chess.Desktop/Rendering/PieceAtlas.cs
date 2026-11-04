using System;
using System.Collections.Generic;
using System.IO;
using Chess.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chess.Desktop.Rendering
{
    /// <summary>
    /// Builds the 12 piece sprites (6 types x 2 sides) into one runtime atlas.
    /// Drawn with nothing but SetData on a Color buffer, so the shell needs no
    /// content pipeline at all. If a skin folder with pieces in the SkinManifest's
    /// slot names is present, its PNGs are loaded per slot instead.
    /// </summary>
    public sealed class PieceAtlas
    {
        public const int Cell = 96;

        private Texture2D _atlas;          // 6 columns (P N B R Q K) x 2 rows (white/black)
        private readonly Dictionary<(int type, int side), Texture2D> _skinPieces = new();
        private Texture2D _boardLight, _boardDark;

        public void Build(GraphicsDevice gd, string skinDirectory = null)
        {
            _atlas = BuildProceduralAtlas(gd);

            TryLoadBoardTextures(gd, skinDirectory);
            TryLoadPieceSkins(gd, skinDirectory);
        }

        public Texture2D BoardLight => _boardLight;
        public Texture2D BoardDark => _boardDark;

        /// <summary>[type-1]*2 + side -> atlas source rect, matching BoardIndex in the engine.</summary>
        public Rectangle SourceRect(int type, int side)
        {
            int col = (int)type - 1, row = (int)side;
            return new Rectangle(col * Cell, row * Cell, Cell, Cell);
        }

        public Texture2D Atlas => _atlas;

        public Texture2D Piece(int type, int side) =>
            _skinPieces.TryGetValue(((int)type, side), out var t) ? t : _atlas;

        // ------------------------------------------------------------ procedural

        private static Texture2D BuildProceduralAtlas(GraphicsDevice gd)
        {
            var canvas = new Color[6 * Cell * 2 * Cell];
            for (PieceType type = PieceType.Pawn; type <= PieceType.King; type++)
            {
                for (Side side = Side.White; side <= Side.Black; side++)
                {
                    var origin = ((int)type - 1) * Cell + (int)side * 6 * Cell;
                    DrawPiece(canvas, origin, type, side);
                }
            }
            var tex = new Texture2D(gd, 6 * Cell, 2 * Cell);
            tex.SetData(canvas);
            return tex;
        }

        private readonly struct Prim
        {
            public readonly char Kind;      // 'c' circle, 'r' rect, 't' triangle-up
            public readonly float X, Y, W, H;
            public Prim(char kind, float x, float y, float w, float h)
            { Kind = kind; X = x; Y = y; W = w; H = h; }
        }

        /// <summary>Silhouette shapes for one piece, in design coords (0..96).</summary>
        private static List<Prim> Shapes(PieceType type)
        {
            switch (type)
            {
                case PieceType.Pawn: return new List<Prim>
                {
                    new Prim('r', 30, 74, 36, 14),          // base
                    new Prim('r', 38, 56, 20, 22),          // body
                    new Prim('c', 48, 40, 30, 30),          // head
                };
                case PieceType.Knight: return new List<Prim>
                {
                    new Prim('r', 28, 74, 40, 14),          // base
                    new Prim('r', 36, 44, 18, 34),          // neck
                    new Prim('c', 50, 36, 30, 30),          // head
                    new Prim('r', 58, 42, 18, 12),          // muzzle
                    new Prim('r', 36, 60, 12, 18),          // mane
                };
                case PieceType.Bishop: return new List<Prim>
                {
                    new Prim('r', 30, 74, 36, 14),          // base
                    new Prim('r', 40, 52, 16, 24),          // neck
                    new Prim('c', 48, 38, 32, 32),          // mitre
                    new Prim('c', 48, 20, 12, 12),          // top ball
                };
                case PieceType.Rook: return new List<Prim>
                {
                    new Prim('r', 30, 74, 36, 14),          // base
                    new Prim('r', 36, 40, 24, 36),          // tower
                    new Prim('r', 32, 28, 10, 14),          // teeth
                    new Prim('r', 43, 28, 10, 14),
                    new Prim('r', 54, 28, 10, 14),
                };
                case PieceType.Queen: return new List<Prim>
                {
                    new Prim('r', 30, 74, 36, 14),          // base
                    new Prim('r', 38, 48, 20, 28),          // body
                    new Prim('r', 40, 36, 16, 14),          // collar
                    new Prim('t', 48, 20, 26, 18),          // crown
                };
                case PieceType.King: return new List<Prim>
                {
                    new Prim('r', 30, 74, 36, 14),          // base
                    new Prim('r', 38, 46, 20, 30),          // body
                    new Prim('c', 48, 38, 28, 28),          // crown
                    new Prim('r', 45, 12, 6, 14),           // cross stem
                    new Prim('r', 39, 16, 18, 6),           // cross bar
                };
                default: return new List<Prim>();
            }
        }

        private static void Plot(Color[] canvas, int origin, Prim p, Color color)
        {
            if (p.Kind == 'r')
            {
                int baseX = (int)(p.X), baseY = (int)(p.Y);
                for (int y = 0; y < (int)p.H; y++)
                    for (int x = 0; x < (int)p.W; x++)
                        Put(canvas, origin + baseX + x + (baseY + y) * 6 * Cell, color);
            }
            else if (p.Kind == 'c')
            {
                int cx = (int)p.X, cy = (int)p.Y, r = (int)(p.W / 2);
                for (int y = -r; y <= r; y++)
                    for (int x = -r; x <= r; x++)
                        if (x * x + y * y <= r * r)
                            Put(canvas, origin + cx + x + (cy + y) * 6 * Cell, color);
            }
            else // triangle-up
            {
                int apexX = (int)p.X, apexY = (int)p.Y;
                int w = (int)p.W, h = (int)p.H;
                for (int dy = 0; dy < h; dy++)
                {
                    int half = (w - 1) * dy / h / 2;
                    for (int dx = -half; dx <= half; dx++)
                        Put(canvas, origin + apexX + dx + (apexY + dy) * 6 * Cell, color);
                }
            }
        }

        private static void Put(Color[] canvas, int index, Color color)
        {
            if (index < 0 || index >= canvas.Length) return;
            canvas[index] = color;
        }

        private static void DrawPiece(Color[] canvas, int origin, PieceType type, Side side)
        {
            var fill = side == Side.White ? new Color(238, 233, 216) : new Color(38, 40, 52);
            var line = new Color(18, 16, 24);
            var shapes = Shapes(type);

            // cheap outline: the same silhouette in the line colour, offset both ways
            var offsets = new[] { -1, 1 };
            foreach (float dx in offsets)
                foreach (float dy in offsets)
                    foreach (var s in shapes)
                        Plot(canvas, origin + (int)(dx + dy * 6 * Cell), s, line);

            foreach (var s in shapes)
                Plot(canvas, origin, s, fill);
        }

        // ------------------------------------------------------------ skins

        // BoardFloor textures keep the manifest slot names from chapter 5.
        private void TryLoadBoardTextures(GraphicsDevice gd, string dir)
        {
            _boardLight = MakeChecker(gd, new Color(0xE8, 0xE3, 0xD3));
            _boardDark = MakeChecker(gd, new Color(0x69, 0x76, 0x62));
            if (dir == null || !Directory.Exists(dir)) return;
            string light = Path.Combine(dir, SkinSlot.BoardLight + ".png");
            string dark = Path.Combine(dir, SkinSlot.BoardDark + ".png");
            if (File.Exists(light)) _boardLight = LoadTransparentless(gd, light);
            if (File.Exists(dark)) _boardDark = LoadTransparentless(gd, dark);
        }

        private static Texture2D MakeChecker(GraphicsDevice gd, Color c)
        {
            var tex = new Texture2D(gd, 4, 4);
            var data = new Color[16];
            for (int i = 0; i < data.Length; i++) data[i] = c;
            tex.SetData(data);
            return tex;
        }

        private static Texture2D LoadTransparentless(GraphicsDevice gd, string path)
        {
            using var stream = File.OpenRead(path);
            return Texture2D.FromStream(gd, stream);
        }

        private void TryLoadPieceSkins(GraphicsDevice gd, string dir)
        {
            if (dir == null || !Directory.Exists(dir)) return;
            for (PieceType type = PieceType.Pawn; type <= PieceType.King; type++)
            {
                string slot = "w" + type.ToString().ToLowerInvariant();
                string file = Path.Combine(dir, slot + ".png");
                if (!File.Exists(file)) continue;
                try { _skinPieces[((int)type, 0)] = LoadTransparentless(gd, file); } catch { }
                string bslot = "b" + type.ToString().ToLowerInvariant();
                string bfile = Path.Combine(dir, bslot + ".png");
                if (File.Exists(bfile))
                {
                    try { _skinPieces[((int)type, 1)] = LoadTransparentless(gd, bfile); } catch { }
                }
            }
        }

        public void ExportFallbackAtlasPng(string path)
        {
            // debugging hook for the book: the procedural atlas as a real PNG
            using var stream = File.Create(path);
            _atlas.SaveAsPng(stream, _atlas.Width, _atlas.Height);
        }
    }
}
