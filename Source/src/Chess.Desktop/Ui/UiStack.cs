using System;
using System.Collections.Generic;
using Chess.Desktop.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chess.Desktop.Ui
{
    /// <summary>
    /// Immediate-mode button stack: rectangles defined in screen space, drawn with
    /// the shared white sprite, hit-tested against the raw MouseState. Lambda
    /// actions keep the game logic (chapter 8's MatchController calls) right next
    /// to the button declaration.
    /// </summary>
    public sealed class UiStack
    {
        private readonly BitmapFont _font;
        private readonly Texture2D _white;

        private struct Button
        {
            public string Label;
            public Rectangle Rect;
            public Action OnClick;
        }

        private readonly List<Button> _buttons = new List<Button>();

        public UiStack(BitmapFont font, Texture2D white)
        {
            _font = font;
            _white = white;
        }

        public void Add(string label, Rectangle rect, Action onClick)
        {
            _buttons.Add(new Button { Label = label, Rect = rect, OnClick = onClick });
        }

        public void Clear() => _buttons.Clear();

        /// <returns>true when a button consumed the click.</returns>
        public bool HandleClick(Point p)
        {
            foreach (var b in _buttons)
                if (p.X >= b.Rect.X && p.X < b.Rect.X + b.Rect.Width &&
                    p.Y >= b.Rect.Y && p.Y < b.Rect.Y + b.Rect.Height)
                {
                    b.OnClick();
                    return true;
                }
            return false;
        }

        public void Draw(SpriteBatch sb)
        {
            foreach (var b in _buttons)
            {
                sb.Draw(_white, b.Rect, new Color(58, 56, 70));
                sb.Draw(_white, new Rectangle(b.Rect.X, b.Rect.Y, b.Rect.Width, 1), new Color(96, 92, 110));
                var size = _font.Measure(b.Label);
                _font.Draw(sb, b.Label,
                    new Vector2(b.Rect.X + (b.Rect.Width - size) / 2, b.Rect.Y + 8),
                    new Color(236, 232, 220));
            }
        }
    }
}
