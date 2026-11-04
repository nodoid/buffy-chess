namespace Chess.Core
{
    /// <summary>
    /// Hand-tuned evaluation: material plus piece-square tables (PST). Values follow
    /// classical 1/3/3/5/9-weights, and each table is written from White's point of
    /// view with a1 at the bottom-left, exactly as the FEN encoder lays it out.
    /// </summary>
    public static class Evaluator
    {
        public const int MateValue = 30000;
        public const int MateInOneMargin = MateValue - 65;    // room for the ply score
        public const int DrawValue = 0;

        public static readonly int[] Material = { 0, 100, 320, 330, 500, 900, 0 };

        // Tables are from White's perspective, index = LERF square. Black mirrors index.
        private static readonly int[] PawnPST =
        {
              0,   0,   0,   0,   0,   0,   0,   0,
             50,  50,  50,  50,  50,  50,  50,  50,
             10,  10,  20,  30,  30,  20,  10,  10,
              5,   5,  10,  25,  25,  10,   5,   5,
              0,   0,   0,  20,  20,   0,   0,   0,
              5,  -5, -10,   0,   0, -10,  -5,   5,
              5,  10,  10, -20, -20,  10,  10,   5,
              0,   0,   0,   0,   0,   0,   0,   0
        };

        private static readonly int[] KnightPST =
        {
            -50, -40, -30, -30, -30, -30, -40, -50,
            -40, -20,   0,   0,   0,   0, -20, -40,
            -30,   0,  10,  15,  15,  10,   0, -30,
            -30,   5,  15,  20,  20,  15,   5, -30,
            -30,   0,  15,  20,  20,  15,   0, -30,
            -30,   5,  10,  15,  15,  10,   5, -30,
            -40, -20,   0,   5,   5,   0, -20, -40,
            -50, -40, -30, -30, -30, -30, -40, -50
        };

        private static readonly int[] BishopPST =
        {
            -20, -10, -10, -10, -10, -10, -10, -20,
            -10,   0,   0,   0,   0,   0,   0, -10,
            -10,   0,   5,  10,  10,   5,   0, -10,
            -10,   5,   5,  10,  10,   5,   5, -10,
            -10,   0,  10,  10,  10,  10,   0, -10,
            -10,  10,  10,  10,  10,  10,  10, -10,
            -10,   5,   0,   0,   0,   0,   5, -10,
            -20, -10, -10, -10, -10, -10, -10, -20
        };

        private static readonly int[] RookPST =
        {
              0,   0,   0,   0,   0,   0,   0,   0,
              5,  10,  10,  10,  10,  10,  10,   5,
             -5,   0,   0,   0,   0,   0,   0,  -5,
             -5,   0,   0,   0,   0,   0,   0,  -5,
             -5,   0,   0,   0,   0,   0,   0,  -5,
             -5,   0,   0,   0,   0,   0,   0,  -5,
             -5,   0,   0,   0,   0,   0,   0,  -5,
              0,   0,   0,   5,   5,   0,   0,   0
        };

        private static readonly int[] QueenPST =
        {
            -20, -10, -10,  -5,  -5, -10, -10, -20,
            -10,   0,   0,   0,   0,   0,   0, -10,
            -10,   0,   5,   5,   5,   5,   0, -10,
             -5,   0,   5,   5,   5,   5,   0,  -5,
              0,   0,   5,   5,   5,   5,   0,  -5,
            -10,   5,   5,   5,   5,   5,   0, -10,
            -10,   0,   5,   0,   0,   0,   0, -10,
            -20, -10, -10,  -5,  -5, -10, -10, -20
        };

        private static readonly int[] KingPstMiddle =
        {
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -20, -30, -30, -40, -40, -30, -30, -20,
            -10, -20, -20, -20, -20, -20, -20, -10,
             20,  20,   0,   0,   0,   0,  20,  20,
             20,  30,  10,   0,   0,  10,  30,  20
        };

        private static readonly int[] KingPstEnd =
        {
            -50, -40, -30, -20, -20, -30, -40, -50,
            -30, -20, -10,   0,   0, -10, -20, -30,
            -30, -10,  20,  30,  30,  20, -10, -30,
            -30, -10,  30,  40,  40,  30, -10, -30,
            -30, -10,  30,  40,  40,  30, -10, -30,
            -30, -10,  20,  30,  30,  20, -10, -30,
            -30, -30,   0,   0,   0,   0, -30, -30,
            -50, -30, -30, -30, -30, -30, -30, -50
        };

        public static int PieceSquare(PieceType type, int square, Side side)
        {
            // Mirror for black: flip rank, keep file (a1 stays a1, a8 -> a1).
            int index = side == Side.White ? square : (7 - (square >> 3)) * 8 + (square & 7);
            switch (type)
            {
                case PieceType.Pawn: return PawnPST[index];
                case PieceType.Knight: return KnightPST[index];
                case PieceType.Bishop: return BishopPST[index];
                case PieceType.Rook: return RookPST[index];
                case PieceType.Queen: return QueenPST[index];
                case PieceType.King: return KingPstMiddle[index];
                default: return 0;
            }
        }

        public static int KingEndPst(int square, Side side)
        {
            int index = side == Side.White ? square : (7 - (square >> 3)) * 8 + (square & 7);
            return KingPstEnd[index];
        }

        /// <summary>Full evaluation from White's point of view (centipawns).</summary>
        public static int Evaluate(Position pos)
        {
            int score = 0;
            int percentEndgame = EndgameScale(pos);
            for (int pieceIndex = 0; pieceIndex < 12; pieceIndex++)
            {
                var type = (PieceType)((pieceIndex >> 1) + 1);
                var side = (Side)(pieceIndex & 1);
                ulong bb = pos.Pieces[pieceIndex];
                int sign = side == Side.White ? 1 : -1;
                while (bb != 0UL)
                {
                    int sq = BitUtils.LsbIndex(bb);
                    bb &= bb - 1;
                    int value = Material[(int)type] + PieceSquare(type, sq, side);
                    if (type == PieceType.King && percentEndgame > 50)
                        value += KingEndPst(sq, side);
                    score += sign * value;
                }
            }
            return score;
        }

        /// <summary>0 = pure middlegame, 100 = pure pawn-and-king endgame.</summary>
        public static int EndgameScale(Position pos)
        {
            int minorOrRookTotal = 0;
            for (int sideI = 0; sideI < 2; sideI++)
            {
                var side = (Side)sideI;
                minorOrRookTotal += Count(pos.Pieces[AttackTables.PieceIndex(PieceType.Rook, side)]);
                minorOrRookTotal += 2 * Count(pos.Pieces[AttackTables.PieceIndex(PieceType.Bishop, side)]);
                minorOrRookTotal += 2 * Count(pos.Pieces[AttackTables.PieceIndex(PieceType.Queen, side)]);
            }
            if (minorOrRookTotal <= 4) return 100;
            if (minorOrRookTotal >= 20) return 0;
            return 100 - (minorOrRookTotal - 4) * 100 / 16;
        }

        private static int Count(ulong bb)
        {
            int n = 0;
            while (bb != 0UL) { bb &= bb - 1; n++; }
            return n;
        }
    }
}
