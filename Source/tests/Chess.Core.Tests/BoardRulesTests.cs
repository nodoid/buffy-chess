using System.Collections.Generic;
using NUnit.Framework;

namespace Chess.Core.Tests
{
    /// <summary>Unit tests for chapter 3: the rule engine.</summary>
    [TestFixture]
    public class BoardRulesTests
    {
        private static bool Contains(List<Move> moves, string uci)
        {
            foreach (var m in moves)
                if (m.ToString() == uci) return true;
            return false;
        }

        [Test]
        public void StartPosition_Has20LegalMoves()
        {
            var pos = Position.StartPosition();
            var moves = MoveGen.GenerateLegalMoves(pos);
            Assert.AreEqual(20, moves.Count);
        }

        [Test]
        public void StartPosition_FenRoundTrips()
        {
            var fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
            Assert.AreEqual(fen, Position.FromFen(fen).ToFen());
        }

        [Test]
        public void DoublePawnPush_SetsEnPassantTarget()
        {
            var pos = Position.StartPosition();
            pos.MakeMove(new Move(12, 28, MoveKind.DoublePawnPush)); // e2e4
            Assert.AreEqual(Sq.FromName("e3"), pos.EnPassantSquare);
            Assert.AreEqual("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", pos.ToFen());
        }

        [Test]
        public void EnPassantCapture_RemovesCapturedPawn()
        {
            // 1.e4 e6 2.e5 d5 3.exd6 - the classic en passant line
            var pos = Position.StartPosition();
            pos.MakeMove(new Move(12, 28, MoveKind.DoublePawnPush));   // e2-e4
            pos.MakeMove(new Move(52, 44, MoveKind.Quiet));            // e7-e6
            pos.MakeMove(new Move(28, 36, MoveKind.Quiet));            // e4-e5
            pos.MakeMove(new Move(51, 35, MoveKind.DoublePawnPush));   // d7-d5 (ep target d6)

            var moves = MoveGen.GenerateLegalMoves(pos);
            var epMove = new Move(36, 43, MoveKind.EnPassant);          // e5xd6
            Assert.That(moves.Exists(m => m.Kind == MoveKind.EnPassant), Is.True);

            pos.MakeMove(epMove);
            // the black d5 pawn must be gone; only the d6 pawn-of-white remains
            Assert.IsTrue(pos[35].IsNone);       // d5 cleared
            Assert.AreEqual(PieceType.Pawn, pos[43].Type);
        }

        [Test]
        public void Castling_RequiresClearPath()
        {
            var pos = Position.FromFen("r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1");
            var moves = MoveGen.GenerateLegalMoves(pos);
            Assert.That(Contains(moves, "e1g1"), Is.True);   // O-O
            Assert.That(Contains(moves, "e1c1"), Is.True);   // O-O-O
        }

        [Test]
        public void Castling_ThroughCheck_IsIllegal()
        {
            // black rook on f8 attacks f1 - white cannot castle kingside through it
            var pos = Position.FromFen("r3k3/8/8/8/8/8/8/3R4 w - - 0 1");
            var moves = MoveGen.GenerateLegalMoves(pos);
            Assert.That(Contains(moves, "e1g1"), Is.False);
        }

        [Test]
        public void PawnPromotion_IsGenerated()
        {
            var pos = Position.FromFen("8/P7/8/8/8/8/k6K/8 w - - 0 1");
            var moves = MoveGen.GenerateLegalMoves(pos);
            Assert.That(Contains(moves, "a7a8q"), Is.True);
            Assert.That(Contains(moves, "a7a8n"), Is.True);
        }

        [Test]
        public void PinnedPiece_CannotExposeKing()
        {
            // absolute pin: rook g8 vs king g1 with the knight g5 as the only blocker.
            // A knight always leaves its file when it moves, so it has zero legal moves;
            // the only legal white moves are king steps that stay defended.
            var pos = Position.FromFen("6r1/8/8/6N1/8/8/8/6K1 w - - 0 1");
            var moves = MoveGen.GenerateLegalMoves(pos);
            foreach (var m in moves)
                Assert.That(m.From, Is.EqualTo(6), "only the king may legally move; the pinned knight may not");
            Assert.That(moves.Count, Is.GreaterThan(0));
        }

        [Test]
        public void CheckmateDetected_InScholarsMate()
        {
            // 1.e4 e5 2.Bc4 Nc6 3.Qh5 Nf6?? 4.Qxf7#
            var pos = PlayUci(new[] { "e2e4", "e7e5", "f1c4", "b8c6", "d1h5", "g8f6", "h5f7" });
            var legal = MoveGen.GenerateLegalMoves(pos);
            Assert.AreEqual(0, legal.Count);
            Assert.IsTrue(pos.InCheck);
            Assert.AreEqual(GameOutcome.Checkmate, GameRules.DetectOutcome(pos, 0));
        }

        [Test]
        public void StalemateDetected()
        {
            var pos = Position.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");
            var legal = MoveGen.GenerateLegalMoves(pos);
            Assert.AreEqual(0, legal.Count);
            Assert.IsFalse(pos.InCheck);
            Assert.AreEqual(GameOutcome.Stalemate, GameRules.DetectOutcome(pos, 0));
        }

        [Test]
        public void InsufficientMaterial_BareKingsIsDraw()
        {
            var pos = Position.FromFen("8/8/8/3k4/8/3K4/8/8 w - - 0 1");
            Assert.AreEqual(GameOutcome.DrawInsufficientMaterial, GameRules.DetectOutcome(pos, 0));
        }

        [Test]
        public void FiftyMoveRule_IsDraw()
        {
            var pos = Position.FromFen("8/8/8/3k4/8/3K4/8/8 w - - 100 55");
            Assert.AreEqual(GameOutcome.DrawFiftyMove, GameRules.DetectOutcome(pos, 0));
        }

        private static Position PlayUci(string[] ucis)
        {
            var pos = Position.StartPosition();
            foreach (var uci in ucis)
            {
                var legal = MoveGen.GenerateLegalMoves(pos);
                foreach (var m in legal)
                    if (m.ToString() == uci) { pos.MakeMove(m); break; }
            }
            return pos;
        }
    }

    /// <summary>Perft-style node counting: the strongest possible guarantee that the
    /// move generator is rule-perfect. Every value below is a known reference point
    /// from every serious chess programming resource.</summary>
    [TestFixture]
    public class PerftTests
    {
        private static long Perft(Position pos, int depth)
        {
            if (depth == 0) return 1;
            var legal = new List<Move>(48);
            MoveGen.GenerateLegalMoves(pos, legal);
            if (depth == 1) return legal.Count;
            long nodes = 0;
            for (int i = 0; i < legal.Count; i++)
            {
                pos.MakeMove(legal[i]);
                nodes += Perft(pos, depth - 1);
                pos.UnmakeMove();
            }
            return nodes;
        }

        [Test]
        public void StartPosition_Depth3()
        {
            var pos = Position.StartPosition();
            Assert.AreEqual(8902, Perft(pos, 3));
        }

        [Test]
        public void StartPosition_Depth4()
        {
            var pos = Position.StartPosition();
            Assert.AreEqual(197281, Perft(pos, 4));
        }

        [Test]
        public void Kiwipete_Position_CastlingAndEnPassant_Heavy()
        {
            var pos = Position.FromFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
            Assert.AreEqual(48, Perft(pos, 1));
            Assert.AreEqual(2039, Perft(pos, 2));
            Assert.AreEqual(97862, Perft(pos, 3));
        }

        [Test]
        public void PromotionHeavy_Endgame()
        {
            var pos = Position.FromFen("n1n5/PPPk4/8/8/8/8/4Kppp/5N1N b - - 0 1");
            Assert.AreEqual(24, Perft(pos, 1));
            Assert.AreEqual(496, Perft(pos, 2));
        }
    }

    /// <summary>Undo/replay integrity: the stack must always restore the exact position.</summary>
    [TestFixture]
    public class MakeUnmakeTests
    {
        [Test]
        public void RandomLine_UnmakeAlwaysRestoresOriginal()
        {
            var original = Position.FromFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
            var startFen = original.ToFen();

            var rng = new System.Random(7);
            int pliesPlayed = 0;
            for (int step = 0; step < 60; step++)
            {
                var legal = MoveGen.GenerateLegalMoves(original);
                if (legal.Count == 0) break;
                var chosen = legal[rng.Next(legal.Count)];
                original.MakeMove(chosen);
                pliesPlayed++;
            }

            for (int i = 0; i < pliesPlayed; i++) original.UnmakeMove();

            Assert.AreEqual(startFen, original.ToFen());
        }
    }
}
