using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Chess.Core
{
    /// <summary>
    /// The heart of chapter 4: an alpha-beta Minimax search with:
    ///  - iterative deepening and a wall-clock budget,
    ///  - MVV-LVA + killer + history move ordering,
    ///  - quiescence search so deeper levels stop trading into fog,
    ///  - a Zobrist-keyed transposition table (levels 4+),
    ///  - a tiny built-in opening book (levels 4+),
    ///  - cooperative cancellation for the async UI path.
    ///
    /// The searcher has zero game-UI dependencies, so NUnit can drive it during
    /// continuous integration on every platform on every commit.
    /// </summary>
    public sealed class Searcher
    {
        private TranspositionTable _tt;
        private int[] _killerMoves;      // two slots per ply, encoded as from*64+to
        private int[] _historyScores;    // from * 4096 + to
        private readonly Stopwatch _clock = new Stopwatch();
        private long _nodes;
        private CancellationToken _token;
        private SearchOptions _opt;
        private ISearchListener _listener;

        public SearchResult SearchBestMove(Position rootPosition, SearchOptions options,
            CancellationToken token = default, ISearchListener listener = null)
        {
            // The search may abort mid-line (budget/cancellation) with moves still
            // made-and-not-unmade, so it must never run on a board the caller owns:
            // search our own clone and return only moves, never board mutations.
            var root = rootPosition.Clone();

            _opt = options;
            _token = token == default ? CancellationToken.None : token;
            _listener = listener;
            _nodes = 0;
            _killerMoves = new int[2 * (MaxSearchPly + 2)];
            // from (0..63) * 4096 + to, so the table spans all 64x64 combinations
            _historyScores = new int[64 * 4096 + 64];
            _tt = options.UseTranspositionTable ? new TranspositionTable(1 << 16) : null;

            _clock.Restart();

            // Level 1: pick something legal but deliberately human-flawed.
            if (options.Skill == SkillLevel.Beginner)
                return BeginnerBlunder(root, options);

            var allLegal = new List<Move>(48);
            MoveGen.GenerateLegalMoves(root, allLegal);
            if (allLegal.Count == 0)
            {
                var none = new SearchResult
                {
                    Score = root.InCheck ? -Evaluator.MateValue : Evaluator.DrawValue,
                    DepthReached = 0,
                    ElapsedMs = _clock.ElapsedMilliseconds
                };
                listener?.OnComplete(none);
                return none;
            }

            // Opening book shortcut (levels Advanced + Expert).
            if (options.UseOpeningBook && OpeningBook.TryLookup(root, options.Rng, out Move bookMove))
            {
                var bookResult = new SearchResult
                {
                    Best = bookMove,
                    Score = 0,
                    BookHit = true,
                    DepthReached = 0,
                    ElapsedMs = _clock.ElapsedMilliseconds
                };
                listener?.OnComplete(bookResult);
                return bookResult;
            }

            // Iterative deepening alpha-beta. Each completed depth re-orders the next
            // iteration (previous best move first), which is what makes level 4's
            // timescale bearable despite the same naive inner loop.
            int bestScore = 0;
            Move bestMove = allLegal[0];
            var ordered = new List<Move>(allLegal);
            int completedDepth = 1;

            for (int depth = 1; depth <= _opt.MaxDepth; depth++)
            {
                int depthScore;
                Move depthBest;
                try
                {
                    depthScore = SearchRoot(root, ordered, depth, out depthBest);
                }
                catch (SearchCancelledException)
                {
                    break; // keep the last fully-completed depth
                }

                bestScore = depthScore;
                bestMove = depthBest;
                completedDepth = depth;
                PromoteOrdering(ordered, depthBest);
                _listener?.OnProgress(_opt, depth, bestMove, bestScore, _nodes);
                if (System.Math.Abs(bestScore) >= Evaluator.MateInOneMargin) break;   // found mate
                if (_clock.ElapsedMilliseconds >= _opt.TimeBudgetMs) break;           // out of budget
            }

            var finished = new SearchResult
            {
                Best = bestMove,
                Score = bestScore,
                DepthReached = completedDepth,
                Nodes = _nodes,
                ElapsedMs = _clock.ElapsedMilliseconds,
                PrincipalVariationLine = bestMove.ToString()
            };
            _listener?.OnComplete(finished);
            return finished;
        }

        /// <summary>Level 5's parallel wrapper: the root splits across worker tasks.
        /// Each worker owns its own Searcher (own TT, own killers); only clapboard
        /// shared state is the immutable root position itself.</summary>
        public static SearchResult SearchWithWorkers(Position root, SearchOptions options,
            int workerCount, CancellationToken token = default, ISearchListener listener = null)
        {
            if (workerCount <= 1 || options.Skill != SkillLevel.Expert)
                return new Searcher().SearchBestMove(root, options, token, listener);

            var t = token == default ? CancellationToken.None : token;
            var results = new SearchResult[workerCount];
            var tasks = new Task[workerCount];

            for (int i = 0; i < workerCount; i++)
            {
                int workerIndex = i;
                var copy = root.Clone();
                var workerOptions = options;
                // Slightly varied depths let each worker surf a slice of the
                // opportunity space; the highest-scoring legal move wins the vote.
                if (workerIndex > 0 && workerIndex % 2 == 1)
                    workerOptions.MaxDepth = System.Math.Max(2, options.MaxDepth - 1);

                tasks[workerIndex] = Task.Run(() =>
                {
                    results[workerIndex] = new Searcher().SearchBestMove(copy, workerOptions, t, null);
                }, t);
            }
            Task.WaitAll(tasks, t);

            SearchResult winner = null;
            foreach (var r in results)
            {
                if (r == null) continue;
                if (winner == null
                    || r.Score > winner.Score
                    || (r.Score == winner.Score && r.DepthReached > winner.DepthReached))
                    winner = r;
            }
            listener?.OnComplete(winner);
            return winner;
        }

        // ---------------- core search ----------------

        private int SearchRoot(Position pos, List<Move> ordered, int depth, out Move best)
        {
            best = ordered[0];
            int alpha = -Evaluator.MateValue, beta = Evaluator.MateValue;
            for (int i = 0; i < ordered.Count; i++)
            {
                ThrowIfCancelled();
                var move = ordered[i];
                pos.MakeMove(move);
                int score = -AlphaBeta(pos, depth - 1, -beta, -alpha, 1);
                pos.UnmakeMove();
                _nodes++;

                if (score > alpha) { alpha = score; best = move; }
            }
            return alpha; // negamax: already relative to the side to move
        }

        private const int MaxSearchPly = 32;   // hard ceiling: keeps killer tables and the call stack finite

        private int AlphaBeta(Position pos, int depth, int alpha, int beta, int ply)
        {
            _nodes++;
            if ((_nodes & 1023) == 0) ThrowIfCancelledOrOutOfTime();

            if (ply >= MaxSearchPly) return StandScore(ply, pos);

            bool inCheck = pos.InCheck;
            // check extension - bounded by ply so a perpetual check cannot recurse forever
            if (inCheck && ply < 8) depth++;

            if (depth <= 0)
                return _opt.UseQuiescence ? Quiescence(pos, alpha, beta, ply) : StandScore(ply, pos);

            Move ttBest = default;
            if (_tt != null &&
                _tt.Probe(pos.Zobrist, depth, ply, out int ttScore, out ttBest, out TableFlag ttFlag))
            {
                if (ttFlag == TableFlag.Exact) return ttScore;
                if (ttFlag == TableFlag.LowerBound && ttScore > alpha) alpha = ttScore;
                if (ttFlag == TableFlag.UpperBound && ttScore < beta) beta = ttScore;
                if (alpha >= beta) return ttScore;
            }

            var pseudo = new List<Move>(48);
            MoveGen.GeneratePseudoLegal(pos, pseudo);
            OrderPseudoMoves(pseudo, pos, ttBest, ply);

            int self = (int)pos.SideToMove;
            int legalCount = 0;
            int bestScore = int.MinValue;
            Move localBest = default;
            var resultFlag = TableFlag.UpperBound;

            for (int i = 0; i < pseudo.Count; i++)
            {
                var move = pseudo[i];
                pos.MakeMove(move);
                if (pos.IsKingInCheck((Side)self)) { pos.UnmakeMove(); continue; }
                legalCount++;

                int score = -AlphaBeta(pos, depth - 1, -beta, -alpha, ply + 1);
                pos.UnmakeMove();

                if (score > bestScore) { bestScore = score; localBest = move; }
                if (score > alpha)
                {
                    alpha = score;
                    resultFlag = TableFlag.Exact;
                }
                if (alpha >= beta)
                {
                    // quiet beta cutoff: record killers and bump history
                    if (!move.IsCapture && !move.IsPromotion)
                    {
                        int code = move.From * 64 + move.To;
                        if (_killerMoves[2 * ply] != code)
                        {
                            _killerMoves[2 * ply + 1] = _killerMoves[2 * ply];
                            _killerMoves[2 * ply] = code;
                        }
                        _historyScores[move.From * 4096 + move.To] += depth * depth;
                    }
                    resultFlag = TableFlag.LowerBound;
                    break;
                }
            }

            if (legalCount == 0)
                return pos.InCheck ? -Evaluator.MateValue + ply : Evaluator.DrawValue;

            _tt?.Store(pos.Zobrist, depth, bestScore, resultFlag, localBest, ply);
            return bestScore;
        }

        private int Quiescence(Position pos, int alpha, int beta, int ply)
        {
            _nodes++;
            ThrowIfCancelled();
            // capture-recapture spirals can dwarf the main search on razor-sharp
            // middlegames, so quiescence honours the same wall-clock budget
            if ((_nodes & 255) == 0) ThrowIfCancelledOrOutOfTime();
            if (ply >= MaxSearchPly + 8) return StandScore(ply, pos);

            int stand = StandScore(ply, pos);
            if (stand >= beta) return stand;
            if (stand > alpha) alpha = stand;

            var captures = new List<Move>(16);
            MoveGen.GeneratePseudoLegal(pos, captures);

            int self = (int)pos.SideToMove;
            for (int i = 0; i < captures.Count; i++)
            {
                var move = captures[i];
                if (!move.IsCapture && !move.IsPromotion) continue;
                pos.MakeMove(move);
                if (pos.IsKingInCheck((Side)self)) { pos.UnmakeMove(); continue; }
                int score = -Quiescence(pos, -beta, -alpha, ply + 1);
                pos.UnmakeMove();
                if (score >= beta) return score;
                if (score > alpha) alpha = score;
            }
            return alpha;
        }

        /// <summary>Static evaluation, from the side-to-move's point of view.
        /// Forced into [MateInOneMargin] bounds so mate scores can only come from
        /// an actual search line, never from noise in the material count.</summary>
        private int StandScore(int ply, Position pos)
        {
            int whiteScore = Evaluator.Evaluate(pos);
            int relative = pos.SideToMove == Side.White ? whiteScore : -whiteScore;
            if (relative > Evaluator.MateInOneMargin) relative = Evaluator.MateInOneMargin;
            if (relative < -Evaluator.MateInOneMargin) relative = -Evaluator.MateInOneMargin;
            return relative;
        }

        // ---------------- ordering ----------------

        private void OrderPseudoMoves(List<Move> moves, Position pos, Move ttBest, int ply)
        {
            var scored = new MoveScore[moves.Count];
            int killer1 = _killerMoves[2 * ply];
            int killer2 = _killerMoves[2 * ply + 1];
            for (int i = 0; i < moves.Count; i++)
            {
                var m = moves[i];
                int s;
                if (m == ttBest) s = 1 << 30;                                  // exact TT best first
                else if (m.IsCapture)
                {
                    var victim = pos[m.To];
                    int victimValue = victim.IsNone ? 100 /*en passant*/ : GameRules.PieceValue[(int)victim.Type];
                    var attacker = pos[m.From];
                    s = victimValue * 100 - GameRules.PieceValue[(int)attacker.Type] + 1_000_000; // MVV-LVA
                }
                else if (m.IsPromotion) s = GameRules.PieceValue[(int)m.Promo] + 500_000;
                else if (m.From * 64 + m.To == killer1) s = 400_000;
                else if (m.From * 64 + m.To == killer2) s = 380_000;
                else s = _historyScores[m.From * 4096 + m.To];
                scored[i] = new MoveScore { Move = m, Score = s };
            }
            System.Array.Sort(scored, (a, b) => b.Score.CompareTo(a.Score));
            for (int i = 0; i < scored.Length; i++) moves[i] = scored[i].Move;
        }

        private void PromoteOrdering(List<Move> ordered, Move best)
        {
            for (int i = 0; i < ordered.Count; i++)
                if (ordered[i] == best)
                {
                    ordered.RemoveAt(i);
                    ordered.Insert(0, best);
                    return;
                }
        }

        private void ThrowIfCancelled()
        {
            if (_token.IsCancellationRequested) throw new SearchCancelledException();
        }

        /// <summary>Enforced from inside the node loop every 1024 nodes, not just
        /// between depths: a single deep iteration must never overrun the wall-clock
        /// budget, or the UI freezes and a hard test-suite hangs. Co-operative - the
        /// same exception iterative deepening already converts into its last
        /// completed depth.</summary>
        private void ThrowIfCancelledOrOutOfTime()
        {
            if (_token.IsCancellationRequested) throw new SearchCancelledException();
            if (_clock.ElapsedMilliseconds >= _opt.TimeBudgetMs) throw new SearchCancelledException();
        }        private SearchResult BeginnerBlunder(Position pos, SearchOptions o)
{

            var legal = new List<Move>(48);
            MoveGen.GenerateLegalMoves(pos, legal);
            if (legal.Count == 0) return new SearchResult { Score = -Evaluator.MateValue };

            // 60%: grab the juiciest material if a capture exists. 40%: pick something
            // legal at random, which is how a true beginner loses their pawns in a row.
            var rng = o.Rng ?? new Random();
            int index = rng.Next(legal.Count);
            if (rng.Next(100) < 60)
            {
                int bestCaptureScore = -1, bestCaptureIndex = -1;
                for (int i = 0; i < legal.Count; i++)
                {
                    if (!legal[i].IsCapture) continue;
                    var victim = pos[legal[i].To];
                    int value = victim.IsNone ? 100 : GameRules.PieceValue[(int)victim.Type];
                    if (value > bestCaptureScore) { bestCaptureScore = value; bestCaptureIndex = i; }
                }
                if (bestCaptureIndex >= 0) index = bestCaptureIndex;
            }

            int whiteScore = Evaluator.Evaluate(pos);
            return new SearchResult
            {
                Best = legal[index],
                Score = pos.SideToMove == Side.White ? whiteScore : -whiteScore,
                DepthReached = 1,
                Nodes = legal.Count
            };
        }
    }

    /// <summary>
    /// A very small deterministic opening book with real opening theory content.
    /// Level 4 and 5 consult it for the first handful of moves; the varied choices
    /// inside each entry keep matched play varied without randomised search noise.
    /// </summary>
    public static class OpeningBook
    {
        /// <summary>Position FEN (up to and including castling field) -> UCI moves.</summary>
        private static readonly Dictionary<string, string[]> Book = new Dictionary<string, string[]>
        {
            ["rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq"] = new[] { "e2e4", "d2d4", "c2c4", "g1f3" },
            ["rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq"] = new[] { "e7e5", "c7c5", "e7e6", "c7c6" },
            ["rnbqkbnr/pppppppp/8/8/3P4/8/PPPP1PPP/RNBQKBNR b KQkq"] = new[] { "d7d5", "g8f6" },
            ["rnbqkbnr/pppppppp/8/8/3P4/8/PPP2PPP/RNBQKBNR b KQkq"] = new[] { "g8f6" },
            ["rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq"] = new[] { "g1f3", "f1c4" },
            ["rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq"] = new[] { "g1f3", "c2c3", "b1c3" },
        };

        public static bool TryLookup(Position pos, Random rng, out Move move)
        {
            move = default;
            // key on the first three FEN fields: board, side to move, castling rights
            var fields = pos.ToFen().Split(' ');
            if (fields.Length < 3) return false;
            string key = fields[0] + " " + fields[1] + " " + fields[2];
            if (!Book.TryGetValue(key, out var candidates)) return false;

            var legal = new List<Move>(48);
            MoveGen.GenerateLegalMoves(pos, legal);
            foreach (string uci in candidates)
            {
                var m = ParseUci(uci);
                for (int i = 0; i < legal.Count; i++)
                    if (legal[i].From == m.From && legal[i].To == m.To && legal[i].Kind == m.Kind)
                    {
                        move = legal[i];
                        return true;
                    }
            }
            return false;
        }

        public static Move ParseUci(string uci)
        {
            int from = Sq.FromName(uci.Substring(0, 2));
            int to = Sq.FromName(uci.Substring(2, 2));
            PieceType promo = PieceType.None;
            if (uci.Length >= 5) promo = Piece.CharToType(uci[4]);
            return new Move(from, to, MoveKind.Quiet, promo);
        }

        /// <summary>Kind-independent parse: the caller resolves From/To against the
        /// engine's own legal records, which carry the exact special-rule kinds.</summary>
        public static Move ParseUciNoKind(string uci)
        {
            int from = Sq.FromName(uci.Substring(0, 2));
            int to = Sq.FromName(uci.Substring(2, 2));
            return new Move(from, to);
        }
    }
}
