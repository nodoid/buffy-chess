using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chess.Desktop.Rendering
{
    /// <summary>
    /// A 3x5 bitmap font rasterised at runtime so the shell needs no content
    /// pipeline or font files. Ten thousand mobile apps ship less readable HUDs;
    /// for the book this doubles as the "zero-asset rendering" example.
    /// </summary>
    public sealed class BitmapFont
    {
        private readonly Texture2D _white;
        private const int CellW = 3, CellH = 5;

        private static readonly Dictionary<char, string[]> Glyphs = new()
        {
            ['A'] = new[] { "111", "101", "111", "101", "101" },
            ['B'] = new[] { "110", "101", "110", "101", "110" },
            ['C'] = new[] { "011", "100", "100", "100", "011" },
            ['D'] = new[] { "110", "101", "101", "101", "110" },
            ['E'] = new[] { "111", "100", "111", "100", "111" },
            ['F'] = new[] { "111", "100", "111", "100", "100" },
            ['G'] = new[] { "011", "100", "101", "101", "011" },
            ['H'] = new[] { "101", "101", "111", "101", "101" },
            ['I'] = new[] { "111", "010", "010", "010", "111" },
            ['J'] = new[] { "001", "001", "001", "101", "111" },
            ['K'] = new[] { "101", "110", "100", "110", "101" },
            ['L'] = new[] { "100", "100", "100", "100", "111" },
            ['M'] = new[] { "101", "111", "111", "101", "101" },
            ['N'] = new[] { "110", "101", "101", "101", "101" },
            ['O'] = new[] { "111", "101", "101", "101", "111" },
            ['P'] = new[] { "111", "101", "111", "100", "100" },
            ['Q'] = new[] { "111", "101", "101", "111", "001" },
            ['R'] = new[] { "111", "101", "110", "101", "101" },
            ['S'] = new[] { "011", "100", "010", "001", "110" },
            ['T'] = new[] { "111", "010", "010", "010", "010" },
            ['U'] = new[] { "101", "101", "101", "101", "111" },
            ['V'] = new[] { "101", "101", "101", "101", "010" },
            ['W'] = new[] { "101", "101", "111", "111", "101" },
            ['X'] = new[] { "101", "101", "010", "101", "101" },
            ['Y'] = new[] { "101", "101", "010", "010", "010" },
            ['Z'] = new[] { "111", "001", "010", "100", "111" },
            ['0'] = new[] { "111", "101", "101", "101", "111" },
            ['1'] = new[] { "010", "110", "010", "010", "111" },
            ['2'] = new[] { "111", "001", "111", "100", "111" },
            ['3'] = new[] { "111", "001", "111", "001", "111" },
            ['4'] = new[] { "101", "101", "111", "001", "001" },
            ['5'] = new[] { "111", "100", "111", "001", "111" },
            ['6'] = new[] { "111", "100", "111", "101", "111" },
            ['7'] = new[] { "111", "001", "001", "001", "001" },
            ['8'] = new[] { "111", "101", "111", "101", "111" },
            ['9'] = new[] { "111", "101", "111", "001", "111" },
            ['-'] = new[] { "000", "000", "111", "000", "000" },
            ['+'] = new[] { "000", "010", "111", "010", "000" },
            ['='] = new[] { "000", "111", "000", "111", "000" },
            ['!'] = new[] { "010", "010", "010", "000", "010" },
            ['?'] = new[] { "111", "001", "011", "000", "010" },
            ['.'] = new[] { "000", "000", "000", "000", "010" },
            [':'] = new[] { "000", "010", "000", "010", "000" },
            [' '] = new[] { "000", "000", "000", "000", "000" },
        };

        public BitmapFont(GraphicsDevice gd)
        {
            _white = new Texture2D(gd, 1, 1);
            _white.SetData(new[] { Color.White });
        }

        public int Measure(string text) => text.Length * (CellW + 1) - 1;

        /// <summary>Pixel size of a string at a given scale - needed to centre
        /// the boot card's title without hard-coding character widths.</summary>
        public Vector2 Measure(string text, int scale) =>
            new Vector2((text.Length * (CellW + 1) - 1) * scale, CellH * scale);

        /// <summary>Draws one string; each "on" cell is a stretched pixel quad.
        /// HUD strings are short, so a few hundred draws per frame is nothing.</summary>
        public void Draw(SpriteBatch sb, string text, Vector2 position, Color color, int scale = 2)
        {
            int penX = (int)position.X;
            int y0 = (int)position.Y;
            foreach (char raw in text)
            {
                char c = char.ToUpperInvariant(raw);
                if (c == '\n') { penX = (int)position.X; y0 += (CellH + 1) * scale; continue; }
                var rows = Glyphs.TryGetValue(c, out var found) ? found : Glyphs['?'];
                for (int gy = 0; gy < CellH; gy++)
                    for (int gx = 0; gx < CellW; gx++)
                        if (rows[gy][gx] == '1')
                        {
                            sb.Draw(_white, new Rectangle(
                                penX + gx * scale, y0 + gy * scale, scale, scale), color);
                        }
                penX += (CellW + 1) * scale;
            }
        }
    }
}
