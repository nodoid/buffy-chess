using System;

namespace Chess.Core
{
    /// <summary>
    /// Precomputed attack tables the engine uses everywhere. Built once, at static
    /// initialisation time, so that move generation in a game loop - and inside the
    /// AI's search - never allocates or recomputes anything.
    /// </summary>
    public static class AttackTables
    {
        public const ulong FileA = 0x0101010101010101UL;
        public const ulong FileH = 0x8080808080808080UL;
        public const ulong Rank1 = 0x00000000000000FFUL;
        public const ulong Rank8 = 0xFF00000000000000UL;

        /// <summary>Eight directions, indices are stable so callers can loop over them.</summary>
        public const int North = 0, South = 1, East = 2, West = 3, NorthEast = 4, NorthWest = 5, SouthEast = 6, SouthWest = 7;
        public const int DirCount = 8;

        /// <summary>Rays[d][sq] = every square from sq (exclusive) outward along direction d.</summary>
        public static readonly ulong[][] Rays = BuildRays();

        /// <summary>Knight jump targets for every square.</summary>
        public static readonly ulong[] KnightAttacks = BuildKnightTable();

        /// <summary>King step targets for every square.</summary>
        public static readonly ulong[] KingAttacks = BuildKingTable();

        /// <summary>Pawn capture targets, indexed [side][square].</summary>
        public static readonly ulong[] PawnAttacksWhite = BuildPawnTable(Side.White);
        public static readonly ulong[] PawnAttacksBlack = BuildPawnTable(Side.Black);

        /// <summary>Castling rights lost when a move touches a given square.</summary>
        public static readonly CastlingFlags[] CastlingMaskBySquare = BuildCastlingMasks();

        /// <summary>Squares attacked by a bishop from sq given occupancy.</summary>
        public static ulong BishopAttacks(int sq, ulong occupied)
        {
            return Slide(sq, occupied, NorthEast) | Slide(sq, occupied, NorthWest)
                 | Slide(sq, occupied, SouthEast) | Slide(sq, occupied, SouthWest);
        }

        /// <summary>Squares attacked by a rook from sq given occupancy.</summary>
        public static ulong RookAttacks(int sq, ulong occupied)
        {
            return Slide(sq, occupied, North) | Slide(sq, occupied, South)
                 | Slide(sq, occupied, East) | Slide(sq, occupied, West);
        }

        public static ulong QueenAttacks(int sq, ulong occupied)
            => BishopAttacks(sq, occupied) | RookAttacks(sq, occupied);

        /// <summary>
        /// Walk one direction until a blocker. The blocker itself is included
        /// (a slider can capture it); the squares beyond are not.
        /// Moving "up" the board means LsbIndex finds the nearest blocker;
        /// moving "down" uses MsbIndex.
        /// </summary>
        private static ulong Slide(int sq, ulong occupied, int dir)
        {
            ulong ray = Rays[dir][sq];
            ulong blockers = ray & occupied;
            if (blockers == 0UL) return ray;

            int firstBlocker = IsUpward(dir) ? BitUtils.LsbIndex(blockers) : BitUtils.MsbIndex(blockers);
            // everything up to and including the blocker, nothing beyond:
            return (ray & ~Rays[dir][firstBlocker]) | BitUtils.Bit(firstBlocker);
        }

        private static bool IsUpward(int dir) =>
            dir == North || dir == NorthEast || dir == NorthWest || dir == East;

        private static ulong[,] Directions = BuildDirectionVectors();

        private static ulong[,] BuildDirectionVectors()
        {
            // fileDelta, rankDelta for each of the 8 directions
            var d = new ulong[DirCount, 2];
            d[North, 0] = 0; d[North, 1] = 1;
            d[South, 0] = 0; d[South, 1] = 7;
            d[East, 0] = 1; d[East, 1] = 0;
            d[West, 0] = 7; d[West, 1] = 0;
            d[NorthEast, 0] = 1; d[NorthEast, 1] = 1;
            d[NorthWest, 0] = 7; d[NorthWest, 1] = 1;
            d[SouthEast, 0] = 1; d[SouthEast, 1] = 7;
            d[SouthWest, 0] = 7; d[SouthWest, 1] = 7;
            return d;
        }

        private static ulong[][] BuildRays()
        {
            var deltas = new int[DirCount, 2];
            deltas[North, 0] = 0; deltas[North, 1] = 1;
            deltas[South, 0] = 0; deltas[South, 1] = -1;
            deltas[East, 0] = 1; deltas[East, 1] = 0;
            deltas[West, 0] = -1; deltas[West, 1] = 0;
            deltas[NorthEast, 0] = 1; deltas[NorthEast, 1] = 1;
            deltas[NorthWest, 0] = -1; deltas[NorthWest, 1] = 1;
            deltas[SouthEast, 0] = 1; deltas[SouthEast, 1] = -1;
            deltas[SouthWest, 0] = -1; deltas[SouthWest, 1] = -1;

            var rays = new ulong[DirCount][];
            for (int d = 0; d < DirCount; d++)
            {
                rays[d] = new ulong[64];
                for (int sq = 0; sq < 64; sq++)
                {
                    int f = Sq.FileOf(sq), r = Sq.RankOf(sq);
                    ulong bb = 0;
                    int step = 1;
                    while (true)
                    {
                        int nf = f + deltas[d, 0] * step;
                        int nr = r + deltas[d, 1] * step;
                        if (nf < 0 || nf > 7 || nr < 0 || nr > 7) break;
                        bb |= BitUtils.Bit(Sq.Make(nf, nr));
                        step++;
                    }
                    rays[d][sq] = bb;
                }
            }
            return rays;
        }

        private static ulong[] BuildKnightTable()
        {
            var deltas = new[,] { { 1, 2 }, { 2, 1 }, { 2, -1 }, { 1, -2 }, { -1, -2 }, { -2, -1 }, { -2, 1 }, { -1, 2 } };
            var table = new ulong[64];
            for (int sq = 0; sq < 64; sq++)
            {
                int f = Sq.FileOf(sq), r = Sq.RankOf(sq);
                ulong bb = 0;
                for (int i = 0; i < 8; i++)
                {
                    int nf = f + deltas[i, 0], nr = r + deltas[i, 1];
                    if (nf >= 0 && nf <= 7 && nr >= 0 && nr <= 7) bb |= BitUtils.Bit(Sq.Make(nf, nr));
                }
                table[sq] = bb;
            }
            return table;
        }

        private static ulong[] BuildKingTable()
        {
            var table = new ulong[64];
            for (int sq = 0; sq < 64; sq++)
            {
                int f = Sq.FileOf(sq), r = Sq.RankOf(sq);
                ulong bb = 0;
                for (int df = -1; df <= 1; df++)
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        if (df == 0 && dr == 0) continue;
                        int nf = f + df, nr = r + dr;
                        if (nf >= 0 && nf <= 7 && nr >= 0 && nr <= 7) bb |= BitUtils.Bit(Sq.Make(nf, nr));
                    }
                table[sq] = bb;
            }
            return table;
        }

        private static ulong[] BuildPawnTable(Side side)
        {
            var table = new ulong[64];
            int dr = side == Side.White ? +1 : -1;
            for (int sq = 0; sq < 64; sq++)
            {
                int f = Sq.FileOf(sq), r = Sq.RankOf(sq);
                ulong bb = 0;
                int nr = r + dr;
                if (nr < 0 || nr > 7) continue;
                if (f - 1 >= 0) bb |= BitUtils.Bit(Sq.Make(f - 1, nr));
                if (f + 1 <= 7) bb |= BitUtils.Bit(Sq.Make(f + 1, nr));
                table[sq] = bb;
            }
            return table;
        }

        private static CastlingFlags[] BuildCastlingMasks()
        {
            var mask = new CastlingFlags[64];
            for (int i = 0; i < 64; i++) mask[i] = CastlingFlags.WhiteKingSide | CastlingFlags.WhiteQueenSide
                                                   | CastlingFlags.BlackKingSide | CastlingFlags.BlackQueenSide;
            // departure/hit squares that kill one right each:
            mask[0] &= ~CastlingFlags.WhiteQueenSide;   // a1  (white rook)
            mask[4] &= ~(CastlingFlags.WhiteKingSide | CastlingFlags.WhiteQueenSide); // e1
            mask[7] &= ~CastlingFlags.WhiteKingSide;    // h1
            mask[56] &= ~CastlingFlags.BlackQueenSide;  // a8
            mask[60] &= ~(CastlingFlags.BlackKingSide | CastlingFlags.BlackQueenSide); // e8
            mask[63] &= ~CastlingFlags.BlackKingSide;   // h8
            return mask;
        }

        /// <summary>The whole-board attack set of one side: ever square currently attacked.</summary>
        public static ulong AllAttacks(Side side, Position pos)
        {
            ulong all = 0;
            int pawnIndex = (int)side * 2;
            var pawnAttacks = side == Side.White ? PawnAttacksWhite : PawnAttacksBlack;

            var pawnBb = pos.Pieces[PieceIndex(PieceType.Pawn, side)];
            while (pawnBb != 0UL)
            {
                int sq = BitUtils.LsbIndex(pawnBb);
                all |= pawnAttacks[sq];
                pawnBb &= pawnBb - 1;
            }

            var knightBb = pos.Pieces[PieceIndex(PieceType.Knight, side)];
            while (knightBb != 0UL)
            {
                int sq = BitUtils.LsbIndex(knightBb);
                all |= KnightAttacks[sq];
                knightBb &= knightBb - 1;
            }

            var bishopBb = pos.Pieces[PieceIndex(PieceType.Bishop, side)];
            while (bishopBb != 0UL)
            {
                int sq = BitUtils.LsbIndex(bishopBb);
                all |= BishopAttacks(sq, pos.All);
                bishopBb &= bishopBb - 1;
            }

            var rookBb = pos.Pieces[PieceIndex(PieceType.Rook, side)];
            while (rookBb != 0UL)
            {
                int sq = BitUtils.LsbIndex(rookBb);
                all |= RookAttacks(sq, pos.All);
                rookBb &= rookBb - 1;
            }

            var queenBb = pos.Pieces[PieceIndex(PieceType.Queen, side)];
            while (queenBb != 0UL)
            {
                int sq = BitUtils.LsbIndex(queenBb);
                all |= QueenAttacks(sq, pos.All);
                queenBb &= queenBb - 1;
            }

            all |= KingAttacks[pos.KingSquare[(int)side]];
            return all;
        }

        internal static int PieceIndex(PieceType type, Side side) => (((int)type - 1) << 1) | (int)side;
    }
}
