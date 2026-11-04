using System.Collections.Generic;

namespace Chess.Core
{
    /// <summary>
    /// Terminal position detection: checkmate, stalemate, the three draw rules the
    /// UI has to act on. Kept separate from move generation so the tests can drive
    /// them independently, and so the AI can bail out of a search branch early.
    /// </summary>
    public static class GameRules
    {
        /// <summary>Least-valuable capture targets, for MVV-LVA move ordering.</summary>
        public static readonly int[] PieceValue = { 0, 100, 320, 330, 500, 900, 20000 };

        /// <summary>
        /// Detects whether the position is terminal.
        /// <paramref name="repetitionCount"/> is how many times the *current* position's
        /// Zobrist hash has appeared earlier in the game history (the match controller
        /// supplies this, since only it knows the full move list).
        /// </summary>
        public static GameOutcome DetectOutcome(Position pos, int repetitionCount)
        {
            var moves = new List<Move>(32);
            MoveGen.GenerateLegalMoves(pos, moves);

            if (moves.Count == 0)
                return pos.InCheck ? GameOutcome.Checkmate : GameOutcome.Stalemate;

            if (pos.HalfMoveClock >= 100) return GameOutcome.DrawFiftyMove;
            if (repetitionCount >= 2) return GameOutcome.DrawThreefoldRepetition;
            if (DrawByInsufficientMaterial(pos)) return GameOutcome.DrawInsufficientMaterial;

            return GameOutcome.InProgress;
        }

        /// <summary>K vs K, K+B vs K, K+N vs K, and K+B vs K+B with same-colour bishops.</summary>
        public static bool DrawByInsufficientMaterial(Position pos)
        {
            // No pawns on the board?
            if ((pos.Pieces[AttackTables.PieceIndex(PieceType.Pawn, Side.White)]
               | pos.Pieces[AttackTables.PieceIndex(PieceType.Pawn, Side.Black)]) != 0UL)
                return false;

            if ((pos.Pieces[AttackTables.PieceIndex(PieceType.Queen, Side.White)]
               | pos.Pieces[AttackTables.PieceIndex(PieceType.Queen, Side.Black)]) != 0UL)
                return false;

            if ((pos.Pieces[AttackTables.PieceIndex(PieceType.Rook, Side.White)]
               | pos.Pieces[AttackTables.PieceIndex(PieceType.Rook, Side.Black)]) != 0UL)
                return false;

            var whiteKnights = Count(pos.Pieces[AttackTables.PieceIndex(PieceType.Knight, Side.White)]);
            var blackKnights = Count(pos.Pieces[AttackTables.PieceIndex(PieceType.Knight, Side.Black)]);
            var whiteBishops = Count(pos.Pieces[AttackTables.PieceIndex(PieceType.Bishop, Side.White)]);
            var blackBishops = Count(pos.Pieces[AttackTables.PieceIndex(PieceType.Bishop, Side.Black)]);

            int minors = whiteKnights + blackKnights + whiteBishops + blackBishops;
            if (minors == 0) return true;              // bare kings -> automatic draw
            if (minors == 1) return true;              // one minor piece at most -> automatic draw

            // Several minors: only same-colour bishops, with no knights, is a dead draw.
            if (whiteKnights == 0 && blackKnights == 0 && SameColourBishopsOnly(pos)) return true;
            return false;
        }

        private static bool SameColourBishopsOnly(Position pos)
        {
            bool sawLight = false, sawDark = false;
            ulong both = pos.Pieces[AttackTables.PieceIndex(PieceType.Bishop, Side.White)]
                       | pos.Pieces[AttackTables.PieceIndex(PieceType.Bishop, Side.Black)];
            while (both != 0UL)
            {
                int sq = BitUtils.LsbIndex(both);
                both &= both - 1;
                // a square is light when (file + rank) is odd with our a1 = 0 encoding
                if (((sq >> 3) + (sq & 7)) % 2 == 0) sawDark = true; else sawLight = true;
            }
            return !(sawLight && sawDark);
        }

        private static int Count(ulong bb)
        {
            int n = 0;
            while (bb != 0UL) { bb &= bb - 1; n++; }
            return n;
        }
    }
}
