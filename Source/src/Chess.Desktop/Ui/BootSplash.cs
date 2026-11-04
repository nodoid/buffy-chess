using System;
using Chess.Desktop.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chess.Desktop.Ui
{
    /// <summary>
    /// The launch card every shell shows for its first second and a half: navy
    /// plate, the game's title, and the checker strip that doubles as the app
    /// icon's motif. Drawn from one white pixel and the bitmap font, so the
    /// splash adds no content pipeline and no files - the same rule the rest of
    /// the shell follows. iOS and Android also get a system launch screen
    /// (UILaunchScreen / Theme.Splash); this overlay covers the window between
    /// the OS handing over and the first chess frame, which is exactly the white
    /// flash Windows 11 users would otherwise see.
    /// </summary>
    public sealed class BootSplash
    {
        private const float Duration = 1.6f;

        private static readonly Color Navy = new Color(24, 26, 38);
        private static readonly Color Gold = new Color(214, 178, 94);
        private static readonly Color Cream = new Color(240, 233, 214);
        private static readonly Color Bronze = new Color(110, 88, 60);

        private readonly Texture2D _white;
        private readonly BitmapFont _font;
        private float _elapsed;

        public BootSplash(GraphicsDevice device, BitmapFont font)
        {
            _font = font;
            _white = new Texture2D(device, 1, 1);
            _white.SetData(new[] { Color.White });
        }

        /// <summary>True once the card has had its time (or been skipped).</summary>
        public bool Done => _elapsed >= Duration;

        /// <summary>Advance the card. Called from Update with the frame delta so
        /// the splash runs at the same speed on a 60 Hz phone and a 144 Hz
        /// monitor.</summary>
        public void Update(float deltaSeconds)
        {
            if (_elapsed < Duration)
                _elapsed = MathF.Min(_elapsed + deltaSeconds, Duration);
        }

        /// <summary>A tap or click dismisses the card immediately - never make a
        /// player wait for an animation they did not ask for.</summary>
        public void Skip() => _elapsed = Duration;

        public void Draw(SpriteBatch sb, int width, int height)
        {
            Fill(sb, new Rectangle(0, 0, width, height), Navy);

            int scale = Math.Max(2, height / 240);
            const string title = "BUFFY CHESS";
            var size = _font.Measure(title, scale);
            var pos = new Vector2((width - size.X) / 2f, height * 0.42f);
            _font.Draw(sb, title, pos, Gold, scale);

            // the icon's four-square checker strip, centred under the title
            int sq = Math.Max(10, height / 34);
            int gap = Math.Max(4, sq / 3);
            int stripW = 8 * sq + 7 * gap;
            int x0 = (width - stripW) / 2;
            int y0 = (int)(pos.Y + size.Y + sq);
            for (int i = 0; i < 8; i++)
                Fill(sb, new Rectangle(x0 + i * (sq + gap), y0, sq, sq),
                     i % 2 == 0 ? Cream : Bronze);

            // quiet progress hint, faded in over the last third of the card
            float fade = MathHelper.Clamp((_elapsed / Duration - 0.66f) * 3f, 0f, 1f);
            if (fade > 0f)
            {
                const string hint = "LOADING";
                var hs = _font.Measure(hint, 1);
                _font.Draw(sb, hint,
                    new Vector2((width - hs.X) / 2f, height * 0.72f),
                    Gold * fade, 1);
            }
        }

        private void Fill(SpriteBatch sb, Rectangle r, Color c)
        {
            sb.Draw(_white, r, c);
        }
    }
}
