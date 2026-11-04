using System;
using System.Collections.Generic;
using System.Text;

namespace Chess.Core
{
    /// <summary>
    /// Rule-based move generation. Produces every strictly *legal* move for the side
    /// to move, including the special rules: castling, en passant and promotion.
    /// The generator is allocation-frugal: one reusable list per call site is the
    /// pattern the mobile performance work in chapter 11 builds on.
    /// </summary>
    public static class MoveGen
    {
        /// <summary>All legal moves for the side to move, into a caller-supplied buffer.</summary>
        public static List<Move> GenerateLegalMoves(Position pos, List<Move> results = null)
        {
            var pseudo = new List<Move>(64);
            GeneratePseudoLegal(pos, pseudo);

            var legal = results ?? new List<Move>(32);
            var self = pos.SideToMove;
            for (int i = 0; i < pseudo.Count; i++)
            {
                var move = pseudo[i];
                pos.MakeMove(move);
                bool kingLeftInCheck = pos.IsKingInCheck(self);
                pos.UnmakeMove();
                if (!kingLeftInCheck) legal.Add(move);
            }
            return legal;
        }

        /// <summary>Every pseudo-legal move: fully powered by the attack tables,
        /// but not yet filtered for leaving the king in capture range.</summary>
        public static void GeneratePseudoLegal(Position pos, List<Move> results)
        {
            var me = pos.SideToMove;
            var theirPawnIdx = AttackTables.PieceIndex(PieceType.Pawn, (Side)(1 - (int)me));

            for (int pieceIndex = (int)me; pieceIndex < 12; pieceIndex += 2)
            {
                ulong bb = pos.Pieces[pieceIndex];
                var type = (PieceType)((pieceIndex >> 1) + 1);
                while (bb != 0UL)
                {
                    int sq = BitUtils.LsbIndex(bb);
                    bb &= bb - 1;
                    switch (type)
                    {
                        case PieceType.Pawn: GeneratePawnMoves(pos, sq, me, results); break;
                        case PieceType.Knight: GenerateSimpleMoves(pos, sq, AttackTables.KnightAttacks[sq], me, results); break;
                        case PieceType.Bishop: GenerateSlidingMoves(pos, sq, AttackTables.BishopAttacks(sq, pos.All), me, results); break;
                        case PieceType.Rook: GenerateSlidingMoves(pos, sq, AttackTables.RookAttacks(sq, pos.All), me, results); break;
                        case PieceType.Queen: GenerateSlidingMoves(pos, sq, AttackTables.QueenAttacks(sq, pos.All), me, results); break;
                        case PieceType.King: GenerateKingMoves(pos, sq, me, results); break;
                    }
                }
            }
        }

        private static void GeneratePawnMoves(Position pos, int sq, Side me, List<Move> results)
        {
            int dir = me == Side.White ? 8 : -8;
            int startRank = me == Side.White ? 1 : 6;      // rank index of pawn's home rank
            int promoRank = me == Side.White ? 7 : 0;      // rank index of promotion rank
            int pawnIdx = AttackTables.PieceIndex(PieceType.Pawn, me);

            int to = sq + dir;
            // pushes
            if (pos[to].IsNone)
            {
                if (SqRank(to) == promoRank)
                    AddPromotions(sq, to, false, results);
                else
                {
                    results.Add(new Move(sq, to, MoveKind.Quiet));
                    // double push only from the start rank and only into a free pair
                    if (SqRank(sq) == startRank)
                    {
                        int to2 = to + dir;
                        if (pos[to2].IsNone) results.Add(new Move(sq, to2, MoveKind.DoublePawnPush));
                    }
                }
            }

            // captures (incl. en passant)
            var captures = me == Side.White ? AttackTables.PawnAttacksWhite[sq] : AttackTables.PawnAttacksBlack[sq];
            while (captures != 0UL)
            {
                int ct = BitUtils.LsbIndex(captures);
                captures &= captures - 1;
                var target = pos[ct];
                if (!target.IsNone && target.Side != me)
                {
                    if (SqRank(ct) == promoRank) AddPromotions(sq, ct, true, results);
                    else results.Add(new Move(sq, ct, MoveKind.Capture));
                }
                else if (target.IsNone && pos.EnPassantSquare == ct)
                {
                    results.Add(new Move(sq, ct, MoveKind.EnPassant));
                }
            }
        }

        private static int SqRank(int sq) => sq >> 3;

        private static void AddPromotions(int from, int to, bool capture, List<Move> results)
        {
            var kind = capture ? MoveKind.PromotionCapture : MoveKind.Promotion;
            results.Add(new Move(from, to, kind, PieceType.Queen));
            results.Add(new Move(from, to, kind, PieceType.Rook));
            results.Add(new Move(from, to, kind, PieceType.Bishop));
            results.Add(new Move(from, to, kind, PieceType.Knight));
        }

        private static void GenerateSimpleMoves(Position pos, int sq, ulong targets, Side me, List<Move> results)
        {
            while (targets != 0UL)
            {
                int to = BitUtils.LsbIndex(targets);
                targets &= targets - 1;
                var target = pos[to];
                if (target.IsNone) results.Add(new Move(sq, to, MoveKind.Quiet));
                else if (target.Side != me) results.Add(new Move(sq, to, MoveKind.Capture));
            }
        }

        private static void GenerateSlidingMoves(Position pos, int sq, ulong attackMask, Side me, List<Move> results)
        {
            GenerateSimpleMoves(pos, sq, attackMask, me, results);
        }

        private static void GenerateKingMoves(Position pos, int sq, Side me, List<Move> results)
        {
            GenerateSimpleMoves(pos, sq, AttackTables.KingAttacks[sq], me, results);
            GenerateCastlingMoves(pos, sq, me, results);
        }

        private static void GenerateCastlingMoves(Position pos, int kingSquare, Side me, List<Move> results)
        {
            var enemy = (Side)(1 - (int)me);
            if (pos.IsKingInCheck(me)) return;  // never castle out of check

            if (me == Side.White)
            {
                if (pos.IsKingSidePossible(Side.White)
                    && !pos.IsSquareAttacked(5, enemy) && !pos.IsSquareAttacked(6, enemy))
                    results.Add(new Move(4, 6, MoveKind.KingSideCastle));
                if (pos.IsQueenSidePossible(Side.White)
                    && !pos.IsSquareAttacked(3, enemy) && !pos.IsSquareAttacked(2, enemy))
                    results.Add(new Move(4, 2, MoveKind.QueenSideCastle));
            }
            else
            {
                if (pos.IsKingSidePossible(Side.Black)
                    && !pos.IsSquareAttacked(61, enemy) && !pos.IsSquareAttacked(62, enemy))
                    results.Add(new Move(60, 62, MoveKind.KingSideCastle));
                if (pos.IsQueenSidePossible(Side.Black)
                    && !pos.IsSquareAttacked(59, enemy) && !pos.IsSquareAttacked(58, enemy))
                    results.Add(new Move(60, 58, MoveKind.QueenSideCastle));
            }
        }
    }

    /// <summary>Game outcome, detected on demand by the UI and the match controller.</summary>
    public enum GameOutcome : byte
    {
        InProgress = 0,
        Checkmate,
        Stalemate,
        DrawFiftyMove,
        DrawThreefoldRepetition,
        DrawInsufficientMaterial,
        Resignation,
        Timeout
    }
}
