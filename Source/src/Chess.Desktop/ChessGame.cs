using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Chess.Core;
using Chess.Desktop.Input;
using Chess.Desktop.Rendering;
using Chess.Desktop.Rendering3D;
using Chess.Desktop.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Chess.Desktop
{
    /// <summary>
    /// The desktop shell: owns the MatchController (chapter 8), renders the board
    /// through BoardRenderer + Camera2D (chapter 6), and drives the AI on its own
    /// background task so the window never blocks (chapter 4's async contract).
    /// This is the reference harness the mobile reimplementations copy.
    /// </summary>
    public sealed class ChessGame : Game
    {
        private readonly GraphicsDeviceManager _graphics;
        private SpriteBatch _sb;
        private Camera2D _camera;
        private PieceAtlas _atlas;
        private BoardRenderer _board;
        private BitmapFont _font;
        private UiStack _ui;
        private readonly LocalStorage _storage = new LocalStorage();

        private MatchController _match;

        private MouseState _prevMouse;
        private InputSnapshot _prevSnap;
        private readonly Input.TouchInputMapper _touch = new();
        private KeyboardState _prevKeys;

        // ---- chapter 6/7: the 3D product view, layered over the same match
        private readonly OrbitCamera3D _cam3d = new OrbitCamera3D(16f / 9f);
        private readonly Board3DRenderer _board3d = new Board3DRenderer();
        private readonly PieceAnimator _anim3d = new PieceAnimator();
        private readonly FrameProfiler _profiler = new FrameProfiler();
        private bool _view3D = true;      // V toggles to the 2D teaching view
        private int _fitW, _fitH;         // backbuffer size the 3D camera last fitted

        private BootSplash _splash;       // chapter 12: brand card over the first frame

        private int _selected = Sq.None;
        private List<Move> _legal = new List<Move>(64);     // reuse: no per-frame allocations
        private List<Move> _dests = new List<Move>(32);
        private Task _aiTask;                               // null when not thinking

        public ChessGame()
        {
            _graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 1160,
                PreferredBackBufferHeight = 800,
            };
            IsMouseVisible = true;
            Content.RootDirectory = "Content";      // unused on purpose: zero-asset shell
        }

        protected override void Initialize()
        {
            // The title is set here, not in the constructor: on iOS the platform
            // window does not exist until Run() creates it, so touching
            // Game.Window in the constructor throws (a device-only crash that the
            // simulator happily hides - chapter 12's reason to always test on
            // hardware).
            if (Window != null) Window.Title = "Buffy Chess — book shell";

            _camera = new Camera2D();
            _match = NewMatch();
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _sb = new SpriteBatch(GraphicsDevice);
            _atlas = new PieceAtlas();
            _atlas.Build(GraphicsDevice, null);     // pass a skin folder to dress up
            _board = new BoardRenderer(_atlas, GraphicsDevice);
            _font = new BitmapFont(GraphicsDevice);
            _ui = new UiStack(_font, BoardWhiteSprite());

            // fixed panel buttons, the entire desktop UI
            _ui.Add("NEW GAME", new Rectangle(760, 300, 160, 44), () => { _match = NewMatch(); ClearSelection(); });
            _ui.Add("UNDO", new Rectangle(760, 352, 160, 44), () => { _match.UndoLastMove(); ClearSelection(); });
            _ui.Add("RESIGN", new Rectangle(760, 404, 160, 44), () => { _match.ResignCurrent(); ClearSelection(); });

            _board3d.Build(GraphicsDevice, null);   // chapter 6: no content pipeline
            // Windows has no system launch screen, so the shell paints its own
            // brand card. iOS (UILaunchScreen) and Android (Theme.Splash) have
            // already shown the plate by the time this runs, and a second splash
            // would just be a delay - so the card is skipped there.
            _splash = new BootSplash(GraphicsDevice, _font);
#if __IOS__ || __ANDROID__
            _splash.Skip();
#endif
            RefreshLegal();
            _prevMouse = Mouse.GetState();
            _prevKeys = Keyboard.GetState();
        }

        private Texture2D BoardWhiteSprite()
        {
            var white = new Texture2D(GraphicsDevice, 1, 1);
            white.SetData(new[] { Color.White });
            return white;
        }

        private MatchController NewMatch()
        {
            var state = MatchState.New(GameMode.PlayerVsAi, ClockPreset.Blitz3, SkillLevel.Normal);
            var m = new MatchController(state, new LocalStorage());
            m.MoveApplied += OnMoveApplied;          // chapter 7: drives the tweens
            _anim3d.Clear();
            return m;
        }

        private void OnMoveApplied(object sender, MoveAppliedEventArgs ev)
        {
            if (ev.Outcome == GameOutcome.InProgress)
                _anim3d.PlayMove(ev.Move, _match.Position, ev.SideThatMoved);
        }

        private void ClearSelection()
        {
            _selected = Sq.None;
            _dests.Clear();
        }

        private void RefreshLegal()
        {
            _legal.Clear();
            MoveGen.GenerateLegalMoves(_match.Position, _legal);
        }

        protected override void Update(GameTime gameTime)
        {
#if !__IOS__ && !__MACCATALYST__
            // iOS (CS0619) forbids programmatic exit: the home gesture owns lifetime
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed) Exit();
#endif

            var mouse = Mouse.GetState();

            // one AI task at a time; PollTask, never BlockUpdate (chapter 4 rule)
            if (_aiTask != null)
            {
                if (_aiTask.IsCompleted)
                {
                    _aiTask = null;
                    RefreshLegal();
                    _match.ResumeClock();
                }
            }
            else if (_match.Outcome == GameOutcome.InProgress && _match.IsAiTurn)
            {
                _aiTask = _match.PlayAiMoveAsync();
            }

            var snap = InputSnapshot.FromMouse(mouse, _prevMouse);
            _touch.CollectInto(ref snap);      // Android/iOS: one finger click, two-finger pan, pinch zoom

            // the brand card runs on wall-clock seconds and yields to a click/tap;
            // input is consumed (not queued) while it is up, so the click that
            // dismisses it can never also select a square behind it
            _splash.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
            if (snap.PrimaryPressed) _splash.Skip();
            if (_splash.Done) HandleInput(snap);
            else _prevSnap = snap;
            _prevMouse = mouse;

            _anim3d.Advance((float)gameTime.ElapsedGameTime.TotalSeconds);
            _profiler.Sample((float)gameTime.ElapsedGameTime.TotalMilliseconds);
            base.Update(gameTime);
        }

        private void HandleInput(InputSnapshot snap)
        {
            var keys = Keyboard.GetState();
            if (keys.IsKeyDown(Keys.V) && !_prevKeys.IsKeyDown(Keys.V)) _view3D = !_view3D;
            _prevKeys = keys;

            // auto-fit the 3D camera when the backbuffer size changes (android
        // rotation, desktop resize, splash -> game on iOs): keeps the whole
        // board on screen without fighting the player's own zoom afterwards
            var vp = GraphicsDevice.Viewport;
            if (vp.Width != _fitW || vp.Height != _fitH)
            {
                _fitW = vp.Width; _fitH = vp.Height;
                if (_view3D)
                    _cam3d.Fit((float)vp.Width / vp.Height);
            }

            if (_view3D)
            {
                // chapter 6: orbit on drag, pan on right-drag, zoom on wheel/pinch
                if (snap.ScrollDelta != 0) _cam3d.ZoomBy(snap.ScrollDelta > 0 ? 1.12f : 1 / 1.12f);
                var d = snap.Point - _prevSnap.Point;
                if (snap.MiddleDown && _prevSnap.MiddleDown)
                    _cam3d.OrbitBy(d.X * 0.006f, d.Y * 0.005f);
                else if (snap.SecondaryDown && _prevSnap.SecondaryDown)
                {
                    float scale = _cam3d.Distance * 0.0012f;
                    _cam3d.PanBy(-d.X * scale, -d.Y * scale);
                }
                Route3DCamera(snap);
                return;
            }

            // camera routing is hooks-agnostic: wheel notch or pinch both land here
            bool panning = snap.MiddleDown && _prevSnap.MiddleDown;
            if (panning) _camera.Pan(snap.Point);
            else if (snap.MiddleDown && !_prevSnap.MiddleDown) _camera.StartPan(snap.Point);
            snap.RouteCamera(_camera, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

            // click: UI first (it owns the right panel), then board squares
            if (!snap.PrimaryPressed) { _prevSnap = snap; return; }
            _prevSnap = snap;

            var point = new Point((int)snap.Point.X, (int)snap.Point.Y);
            if (_ui.HandleClick(point)) return;

            var pos = _match.Position;
            int sq = _view3D ? Pick3D(snap) : snap.SquareUnder(_camera);
            if (sq == Sq.None || _match.Outcome != GameOutcome.InProgress || _match.IsAiTurn) return;

            if (_selected != Sq.None)
            {
                foreach (var m in _dests)
                {
                    if (m.To == sq && _match.TryPlayMove(m))
                    {
                        RefreshLegal();
                        ClearSelection();
                        _match.ResumeClock();
                        return;
                    }
                }
            }

            var piece = pos[sq];
            if (!piece.IsNone && (int)piece.Side == (int)pos.SideToMove)
            {
                _selected = sq;
                _dests.Clear();
                foreach (var m in _legal)
                    if (m.From == sq) _dests.Add(m);
            }
            else ClearSelection();
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(32, 32, 40));

            // ---- board: 3D product view, or 2D teaching view (V key toggles)
            if (_view3D)
            {
                _cam3d.SetAspect((float)GraphicsDevice.Viewport.Width / GraphicsDevice.Viewport.Height);
                _board3d.Draw(_match.Position, _anim3d, _cam3d);
            }
            else
            {
            _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                      SamplerState.PointClamp, transformMatrix: _camera.View);
            var scene = new BoardRenderer.Scene
            {
                Position = _match.Position,
                Selected = _selected,
                DestinationMoves = _dests,
                LastMove = LastMoveOf(),
            };
            _board.Draw(_sb, scene, _camera);
            _sb.End();
            } // end 2D-only draw

            // ---- HUD + buttons in plain screen space
            _sb.Begin();
            DrawHud();
            _ui.Draw(_sb);
            if (_aiTask != null && !_aiTask.IsCompleted)
                _font.Draw(_sb, "THINKING...", new Vector2(760, 140), new Color(210, 200, 120));
            _sb.End();

            // drawn last: the card covers the HUD while the game settles
            if (!_splash.Done)
            {
                _sb.Begin();
                _splash.Draw(_sb, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
                _sb.End();
            }

            base.Draw(gameTime);
        }

        /// <summary>Chapter 7's pick: screen point -> ray -> nearest piece
        /// sphere, else the board plane. Occupancy comes from the logical board.</summary>
        private int Pick3D(InputSnapshot snap)
        {
            _cam3d.SetAspect((float)GraphicsDevice.Viewport.Width / GraphicsDevice.Viewport.Height);
            var (origin, dir) = _cam3d.RayThrough(snap.Point.X, snap.Point.Y,
                GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
            var pos = _match.Position;
            return BoardRay.Pick(origin, dir, sq => !pos[sq].IsNone).Square;
        }

        private void Route3DCamera(InputSnapshot snap)
        {
            _cam3d.SetAspect((float)GraphicsDevice.Viewport.Width / GraphicsDevice.Viewport.Height);
        }

        private Move LastMoveOf()
        {
            var history = _match.MoveHistory;
            return history.Count > 0 ? history[history.Count - 1] : default;
        }

        private void DrawHud()
        {
            var pos = _match.Position;
            string turn = pos.SideToMove == Side.White ? "WHITE" : "BLACK";
            string status = _match.Outcome switch
            {
                GameOutcome.InProgress => pos.InCheck ? turn + " TO MOVE - CHECK!" : turn + " TO MOVE",
                GameOutcome.Checkmate => "CHECKMATE - " + (pos.SideToMove == Side.White ? "BLACK" : "WHITE") + " WINS",
                GameOutcome.Stalemate => "STALEMATE - DRAW",
                GameOutcome.DrawFiftyMove => "DRAW - FIFTY MOVE RULE",
                GameOutcome.DrawThreefoldRepetition => "DRAW - REPETITION",
                GameOutcome.DrawInsufficientMaterial => "DRAW - INSUFFICIENT MATERIAL",
                GameOutcome.Resignation => "RESIGNED",
                GameOutcome.Timeout => "FLAG FELL - TIMEOUT",
                _ => _match.Outcome.ToString().ToUpperInvariant(),
            };
            _font.Draw(_sb, status, new Vector2(760, 60), new Color(236, 232, 220), 3);

            long ms = (pos.SideToMove == Side.White ? _match.WhiteClockMs : _match.BlackClockMs);
            string clock = $"{ms / 60000}:{(ms / 1000 % 60):00}";
            _font.Draw(_sb, "CLOCK " + clock, new Vector2(760, 100), new Color(180, 200, 255));

            // last six SAN entries as a mini move list
            var san = _match.SanHistory;
            int start = Math.Max(0, san.Count - 6);
            for (int i = start; i < san.Count; i++)
            {
                string line = $"{(start % 2 == 0 ? 1 + i / 2 : 1 + i / 2)}.  {san[i]}";
                _font.Draw(_sb, line, new Vector2(760, 460 + (i - start) * 22),
                    new Color(200, 200, 210));
            }
        }
    }

    /// <summary>Platform storage for local saves; the OS-level path plumbing lives
    /// in the platform projects (folders differ per store, code identical).</summary>
    public sealed class LocalStorage : IPlatformStorage
    {
        public string AppDataDirectory =>
            System.IO.Path.Combine(System.AppContext.BaseDirectory, "saves");

        public string SaveDirectory => AppDataDirectory;
    }
}
