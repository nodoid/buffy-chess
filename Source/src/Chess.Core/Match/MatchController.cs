using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Chess.Core
{
    /// <summary>Thread-safe move event for UI and network layers.</summary>
    public sealed class MoveAppliedEventArgs : EventArgs
    {
        public Move Move;
        public string San;
        public GameOutcome Outcome;
        public Side SideThatMoved;
    }

    /// <summary>
    /// Headless match controller shared by every game mode. Owns:
    ///  - the position and move list, with SAN recording for the UI,
    ///  - the clock (per-side remaining time),
    ///  - undo history capped by mode (no undo in online ranked),
    ///  - AI turn orchestration on a background task so no UI thread blocks.
    ///
    /// Firing the AI move is deliberately asynchronous: on Android API 32+ a
    /// synchronous search on the main thread would trip the ANR watchdog.
    /// </summary>
    public sealed class MatchController
    {
        public Position Position { get; private set; }
        public MatchState State { get; private set; }
        public List<string> SanHistory { get; } = new List<string>();
        public List<Move> MoveHistory { get; } = new List<Move>();

        public long WhiteClockMs { get; private set; }
        public long BlackClockMs { get; private set; }
        public GameOutcome Outcome { get; private set; } = GameOutcome.InProgress;

        public event EventHandler<MoveAppliedEventArgs> MoveApplied;
        public event EventHandler<GameOutcome> GameEnded;

        private readonly Stopwatch _turnClock = new Stopwatch();
        private readonly Dictionary<ulong, int> _repetitionCounts = new Dictionary<ulong, int>();
        private readonly IPlatformStorage _storage;
        private MatchPersistence _persistence;

        public MatchController(MatchState state, IPlatformStorage storage = null)
        {
            State = state;
            _storage = storage;
            _persistence = storage != null ? new MatchPersistence(storage) : null;
            Position = Position.FromFen(state.StartingFen ?? Position.StartPosition().ToFen());

            (WhiteClockMs, BlackClockMs) = state.Clock switch
            {
                ClockPreset.Bullet1 => (60_000L, 60_000L),
                ClockPreset.Blitz3 => (180_000L, 180_000L),
                ClockPreset.Blitz10 => (600_000L, 600_000L),
                ClockPreset.Classical30 => (1_800_000L, 1_800_000L),
                _ => (long.MaxValue / 2, long.MaxValue / 2)   // untimed
            };

            // replay a saved move list so an autosave restores the exact position;
            // a corrupt entry simply truncates the replay instead of crashing the app.
            // snapshot first: ApplyMoveCore appends to State.MoveListUci while we loop
            var replayList = new List<string>(state.MoveListUci);
            foreach (var uci in replayList)
            {
                if (!TryFindLegalMoveByUci(uci, out var replayed)) break;
                ApplyMoveCore(replayed, recordSan: true);
            }

            // The move list replays the position, but it cannot express *why* a game
            // ended: a resignation, a timeout and an agreed draw all leave the same
            // legal moves on the board. The stored outcome is authoritative, so a
            // reloaded match comes back finished instead of silently playable.
            if (state.Outcome != (byte)GameOutcome.InProgress)
                Outcome = (GameOutcome)state.Outcome;

            if (Outcome == GameOutcome.InProgress) RegisterRepetition();
            _turnClock.Start();
        }

        public bool IsAiTurn =>
            State.Mode == GameMode.PlayerVsAi && (byte)Position.SideToMove != State.HumanSide;

        /// <summary>Human (or remote) move entry. Returns false if the move was rejected.</summary>
        public bool TryPlayMove(Move move)
        {
            if (Outcome != GameOutcome.InProgress) return false;
            if (!MoveGen.GenerateLegalMoves(Position).Contains(move)) return false;

            TickClock();
            var ev = ApplyMoveCore(move, recordSan: true);
            Save();
            MoveApplied?.Invoke(this, ev);
            CheckForEnd();
            return true;
        }

        /// <summary>Starts the AI thinking. Fire-and-forget; completion marshals via
        /// the event, which callers dispatch to their UI thread.</summary>
        public async Task PlayAiMoveAsync(int workerCount = 1)
        {
            if (Outcome != GameOutcome.InProgress || !IsAiTurn) return;

            var options = SearchOptions.ForLevel((SkillLevel)Math.Max((byte)1, State.AiLevel));
            var root = Position.Clone();
            var result = workerCount > 1
                ? await Task.Run(() => Searcher.SearchWithWorkers(root, options, workerCount))
                : await Task.Run(() => new Searcher().SearchBestMove(root, options));

            if (Outcome != GameOutcome.InProgress) return;
            var legal = MoveGen.GenerateLegalMoves(Position);
            var chosen = legal.Contains(result.Best) ? result.Best : (legal.Count > 0 ? legal[0] : default);
            if (!legal.Contains(chosen)) return;

            TickClock();
            var ev = ApplyMoveCore(chosen, recordSan: true);
            Save();
            MoveApplied?.Invoke(this, ev);
            CheckForEnd();
        }

        public void Resign(Side side)
        {
            if (Outcome != GameOutcome.InProgress) return;
            Outcome = GameOutcome.Resignation;
            GameEnded?.Invoke(this, Outcome);
        }

        public void ResignCurrent() => Resign(Position.SideToMove);

        /// <summary>Undo one full move pair (for Player vs. AI) or one ply (pass and play).</summary>
        public bool UndoLastMove()
        {
            if (MoveHistory.Count == 0) return false;
            int plies = State.Mode == GameMode.PlayerVsAi && MoveHistory.Count >= 2 ? 2 : 1;
            plies = Math.Min(plies, MoveHistory.Count);
            for (int i = 0; i < plies; i++)
            {
                Position.UnmakeMove();
                MoveHistory.RemoveAt(MoveHistory.Count - 1);
                if (SanHistory.Count > 0) SanHistory.RemoveAt(SanHistory.Count - 1);
            }
            State.MoveListUci.RemoveRange(State.MoveListUci.Count - plies, plies);
            Outcome = GameOutcome.InProgress;
            RegisterRepetition();
            Save();
            return true;
        }

        public void TickClock()
        {
            if (State.Clock == ClockPreset.None) return;
            long elapsed = _turnClock.ElapsedMilliseconds;
            _turnClock.Restart();
            if (Position.SideToMove == Side.White) WhiteClockMs -= elapsed;
            else BlackClockMs -= elapsed;

            if ((Position.SideToMove == Side.White && WhiteClockMs <= 0)
                || (Position.SideToMove == Side.Black && BlackClockMs <= 0))
            {
                Outcome = GameOutcome.Timeout;
                GameEnded?.Invoke(this, Outcome);
            }
        }

        private MoveAppliedEventArgs ApplyMoveCore(Move move, bool recordSan)
        {
            var side = Position.SideToMove;
            string san = SanWriter.Write(Position, move);
            Position.MakeMove(move);
            MoveHistory.Add(move);
            if (recordSan)
            {
                SanHistory.Add(san);
                State.MoveListUci.Add(move.ToString());
            }
            RegisterRepetition();
            return new MoveAppliedEventArgs
            {
                Move = move,
                San = san,
                SideThatMoved = side,
                Outcome = GameOutcome.InProgress
            };
        }

        private void RegisterRepetition()
        {
            _repetitionCounts.TryGetValue(Position.Zobrist, out int count);
            _repetitionCounts[Position.Zobrist] = ++count;
        }

        private void CheckForEnd()
        {
            _repetitionCounts.TryGetValue(Position.Zobrist, out int count);
            var outcome = GameRules.DetectOutcome(Position, Math.Max(0, count - 1));
            if (outcome == GameOutcome.InProgress) return;
            Outcome = outcome;
            Save();
            GameEnded?.Invoke(this, Outcome);
        }

        private void Save()
        {
            if (_persistence == null) return;
            State.WhiteClockMs = WhiteClockMs;
            State.BlackClockMs = BlackClockMs;
            State.Outcome = (byte)Outcome;
            _persistence.Save(State);
        }

        private bool TryFindLegalMoveByUci(string uci, out Move move)
        {
            foreach (var m in MoveGen.GenerateLegalMoves(Position))
                if (string.Equals(m.ToString(), uci, StringComparison.OrdinalIgnoreCase))
                {
                    move = m;
                    return true;
                }
            move = default;
            return false;
        }

        /// <summary>Starts the clock for the side to move - called by the UI when a
        /// game becomes visible after a background suspension.</summary>
        public void ResumeClock() => _turnClock.Restart();
    }
}
