using System;
using System.Collections.Generic;
using System.Text;

namespace Chess.Core
{
    /// <summary>
    /// One entry of the undo stack: everything <see cref="Position.UnmakeMove"/> needs
    /// to restore the position exactly.
    /// </summary>
    public sealed class HistoryEntry
    {
        public Move Move;
        public PieceType Captured;      // PieceType.None when quiet
        public int CapturedSquare;      // differs from To on en passant
        public CastlingFlags CastlingBefore;
        public int EnPassantBefore;     // Sq.None when none
        public int HalfMoveClockBefore;
        public ulong ZobristBefore;
    }

    /// <summary>
    /// The board representation: twelve bitboards (one per piece type per side) plus
    /// fast look-up side arrays and full make/unmake state management.
    /// </summary>
    public sealed class Position
    {
        /// <summary>Bitboards indexed by (<c>PieceType*2 + Side</c>) - the BoardIndex of every Piece.</summary>
        public readonly ulong[] Pieces = new ulong[12];

        /// <summary>Fast side[64] look-ups: which piece occupies each square.</summary>
        public readonly Piece[] Mailbox = new Piece[64];

        /// <summary>Aggregates, kept in sync by every make/unmake.</summary>
        public ulong WhitePieces, BlackPieces;

        public ulong All => WhitePieces | BlackPieces;

        public int[] KingSquare = new int[2];

        public Side SideToMove = Side.White;
        public CastlingFlags Castling = CastlingFlags.WhiteKingSide | CastlingFlags.WhiteQueenSide
                                      | CastlingFlags.BlackKingSide | CastlingFlags.BlackQueenSide;
        public int EnPassantSquare = Sq.None;   // target square behind a just-double-pushed pawn
        public int HalfMoveClock;
        public int FullMoveNumber = 1;

        /// <summary>The undo stack. A list is deliberate: matches can run hundreds of moves.</summary>
        public readonly List<HistoryEntry> History = new List<HistoryEntry>(256);

        public ulong Zobrist { get; private set; }

        public readonly ZobristHasher Hasher = new ZobristHasher();

        // - - - construction - - -

        public static Position StartPosition()
        {
            return FromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        }

        public static Position FromFen(string fen)
        {
            var pos = new Position();
            pos.SetFen(fen);
            return pos;
        }

        public void SetFen(string fen)
        {
            Array.Clear(Pieces, 0, Pieces.Length);
            for (int i = 0; i < 64; i++) Mailbox[i] = default;
            WhitePieces = BlackPieces = 0UL;
            History.Clear();
            HalfMoveClock = 0;
            FullMoveNumber = 1;
            EnPassantSquare = Sq.None;
            SideToMove = Side.White;

            var fields = fen.Split(' ');
            int rank = 7, file = 0;
            foreach (char c in fields[0])
            {
                if (c == '/') { rank--; file = 0; continue; }
                if (char.IsDigit(c)) { file += c - '0'; continue; }
                var piece = Piece.FromFen(c);
                var sq = Sq.Make(file, rank);
                Put(piece, sq);
                file++;
            }
            SideToMove = fields.Length > 1 && fields[1] == "b" ? Side.Black : Side.White;
            Castling = 0;
            if (fields.Length > 2 && fields[2] != "-")
            {
                foreach (char c in fields[2])
                {
                    switch (c)
                    {
                        case 'K': Castling |= CastlingFlags.WhiteKingSide; break;
                        case 'Q': Castling |= CastlingFlags.WhiteQueenSide; break;
                        case 'k': Castling |= CastlingFlags.BlackKingSide; break;
                        case 'q': Castling |= CastlingFlags.BlackQueenSide; break;
                    }
                }
            }
            EnPassantSquare = fields.Length > 3 && fields[3] != "-" ? Sq.FromName(fields[3]) : Sq.None;
            HalfMoveClock = fields.Length > 4 ? ParseInt(fields[4]) : 0;
            FullMoveNumber = fields.Length > 5 ? ParseInt(fields[5]) : 1;
            RecomputeHash();
        }

        private static int ParseInt(string s) => int.TryParse(s, out int v) ? v : 0;

        public string ToFen()
        {
            var sb = new StringBuilder();
            for (int rank = 7; rank >= 0; rank--)
            {
                int empty = 0;
                for (int file = 0; file < 8; file++)
                {
                    var p = Mailbox[Sq.Make(file, rank)];
                    if (p.IsNone) { empty++; continue; }
                    if (empty > 0) { sb.Append(empty); empty = 0; }
                    sb.Append(p.ToString());
                }
                if (empty > 0) sb.Append(empty);
                if (rank > 0) sb.Append('/');
            }
            sb.Append(' ').Append(SideToMove == Side.White ? 'w' : 'b').Append(' ');
            if (Castling == 0) sb.Append('-');
            else
            {
                if ((Castling & CastlingFlags.WhiteKingSide) != 0) sb.Append('K');
                if ((Castling & CastlingFlags.WhiteQueenSide) != 0) sb.Append('Q');
                if ((Castling & CastlingFlags.BlackKingSide) != 0) sb.Append('k');
                if ((Castling & CastlingFlags.BlackQueenSide) != 0) sb.Append('q');
            }
            sb.Append(' ').Append(EnPassantSquare == Sq.None ? "-" : Sq.Name(EnPassantSquare));
            sb.Append(' ').Append(HalfMoveClock).Append(' ').Append(FullMoveNumber);
            return sb.ToString();
        }

        public void Put(Piece piece, int square)
        {
            ClearSquare(square);
            if (piece.IsNone) return;
            Pieces[piece.BoardIndex] |= BitUtils.Bit(square);
            Mailbox[square] = piece;
            if (piece.Side == Side.White) WhitePieces |= BitUtils.Bit(square); else BlackPieces |= BitUtils.Bit(square);
            if (piece.Type == PieceType.King) KingSquare[(int)piece.Side] = square;
        }

        public void ClearSquare(int square)
        {
            var existing = Mailbox[square];
            if (!existing.IsNone)
            {
                Pieces[existing.BoardIndex] &= ~BitUtils.Bit(square);
                if (existing.Side == Side.White) WhitePieces &= ~BitUtils.Bit(square); else BlackPieces &= ~BitUtils.Bit(square);
                Mailbox[square] = default;
            }
        }

        public Piece this[int square] => Mailbox[square];

        private void RecomputeHash()
        {
            ulong h = 0;
            for (int i = 0; i < 64; i++)
            {
                var p = Mailbox[i];
                if (!p.IsNone) h ^= Hasher.PieceTable[pieceZoIndex(p, i)];
            }
            if (SideToMove == Side.Black) h ^= Hasher.BlackToMove;
            h ^= ZobristHasher.CastlingKey[(int)Castling];
            if (EnPassantSquare != Sq.None) h ^= Hasher.FileKeys[Sq.FileOf(EnPassantSquare)];
            Zobrist = h;
        }

        private static int pieceZoIndex(Piece p, int square)
        {
            // Zobrist layout: [pieceIndex][square]; reuse BoardIndex.
            return p.BoardIndex * 64 + square;
        }

        // - - - make / unmake - - -

        public void MakeMove(Move move)
        {
            int from = move.From, to = move.To;
            var moving = Mailbox[from];
            if (moving.IsNone) throw new InvalidOperationException("No piece on " + Sq.Name(from));

            var entry = new HistoryEntry
            {
                Move = move,
                Captured = PieceType.None,
                CapturedSquare = to,
                CastlingBefore = Castling,
                EnPassantBefore = EnPassantSquare,
                HalfMoveClockBefore = HalfMoveClock,
                ZobristBefore = Zobrist
            };

            // decrement/refresh bookkeeping
            HalfMoveClock++;
            if (moving.Type == PieceType.Pawn || move.IsCapture) HalfMoveClock = 0;
            if (moving.Side == Side.Black) FullMoveNumber++;

            // hash: remove side & ep keys from the old position
            ulong h = Zobrist;
            if (SideToMove == Side.Black) h ^= Hasher.BlackToMove;
            if (EnPassantSquare != Sq.None) h ^= Hasher.FileKeys[Sq.FileOf(EnPassantSquare)];

            h ^= ZobristHasher.CastlingKey[(int)Castling];

            // en passant capture removes the pawn one rank behind the target square
            if (move.Kind == MoveKind.EnPassant)
            {
                int capturedSq = moving.Side == Side.White ? to - 8 : to + 8;
                RemovePiece(capturedSq, h);
                entry.CapturedSquare = capturedSq;
                entry.Captured = PieceType.Pawn;
            }
            else if (move.IsCapture)
            {
                var captured = Mailbox[to];
                entry.Captured = captured.Type;
                RemovePiece(to, h);
            }

            // move the piece
            h ^= Hasher.PieceTable[pieceZoIndex(moving, from)];
            ClearSquare(from);
            h ^= Hasher.PieceTable[pieceZoIndex(moving, to)];
            Put(moving, to);

            // promotion swaps the pawn out for the chosen piece
            if (move.IsPromotion)
            {
                var promoted = new Piece(move.Promo, moving.Side);
                h ^= Hasher.PieceTable[pieceZoIndex(moving, to)];
                ClearSquare(to);
                h ^= Hasher.PieceTable[pieceZoIndex(promoted, to)];
                Put(promoted, to);
            }

            // castling halves: this is the king move; the rook hop is part of the same move
            if (move.Kind == MoveKind.KingSideCastle || move.Kind == MoveKind.QueenSideCastle)
            {
                bool white = moving.Side == Side.White;
                int rookFrom, rookTo;
                if (move.Kind == MoveKind.KingSideCastle)
                {
                    rookFrom = white ? 7 : 63;      // h1 / h8
                    rookTo = white ? 5 : 61;        // f1 / f8
                }
                else
                {
                    rookFrom = white ? 0 : 56;      // a1 / a8
                    rookTo = white ? 3 : 59;        // d1 / d8
                }
                var rook = Mailbox[rookFrom];
                h ^= Hasher.PieceTable[pieceZoIndex(rook, rookFrom)];
                ClearSquare(rookFrom);
                h ^= Hasher.PieceTable[pieceZoIndex(rook, rookTo)];
                Put(rook, rookTo);
            }

            // castling rights can be lost by moving the king, moving a rook, or a capture
            Castling &= AttackTables.CastlingMaskBySquare[from];
            Castling &= AttackTables.CastlingMaskBySquare[to];
            h ^= ZobristHasher.CastlingKey[(int)Castling];

            // en passant target only exists for a double pawn push
            EnPassantSquare = Sq.None;
            if (move.Kind == MoveKind.DoublePawnPush)
            {
                int behind = moving.Side == Side.White ? from + 8 : from - 8;
                EnPassantSquare = behind;
                h ^= Hasher.FileKeys[Sq.FileOf(behind)];
            }

            h ^= Hasher.BlackToMove;                // side to move flips
            SideToMove = (Side)(1 - (int)SideToMove);

            History.Add(entry);
            Zobrist = h;
        }

        private void RemovePiece(int square, ulong hash_ignored)
        {
            ClearSquare(square);
        }

        public void UnmakeMove()
        {
            var entry = History[History.Count - 1];
            History.RemoveAt(History.Count - 1);
            var move = entry.Move;

            // flip back first
            SideToMove = (Side)(1 - (int)SideToMove);
            if (SideToMove == Side.Black) FullMoveNumber--;

            Castling = entry.CastlingBefore;
            EnPassantSquare = entry.EnPassantBefore;
            HalfMoveClock = entry.HalfMoveClockBefore;
            Zobrist = entry.ZobristBefore;

            int from = move.From, to = move.To;
            var moving = Mailbox[to];

            // promotion: the to-square held the promoted piece; put the pawn back
            if (move.IsPromotion)
            {
                var pawn = new Piece(PieceType.Pawn, moving.Side);
                ClearSquare(to);
                Put(pawn, to);
                moving = pawn;
            }

            // castling: the rook was moved to rookTo as part of the same move - hop it back
            if (move.IsCastle)
            {
                bool white = SideToMove == Side.White;
                int rookFrom, rookTo;
                if (move.Kind == MoveKind.KingSideCastle)
                {
                    rookFrom = white ? 7 : 63;
                    rookTo = white ? 5 : 61;
                }
                else
                {
                    rookFrom = white ? 0 : 56;
                    rookTo = white ? 3 : 59;
                }
                var rook = Mailbox[rookTo];
                ClearSquare(rookTo);
                Put(rook, rookFrom);
            }

            // move back
            ClearSquare(to);
            Put(moving, from);

            // restore the captured piece
            if (entry.Captured != PieceType.None)
            {
                var restored = new Piece(entry.Captured, (Side)(1 - (int)SideToMove));
                Put(restored, entry.CapturedSquare);
            }
        }

        // - - - status queries (all allocation-free) - - -

        public bool IsSquareAttacked(int square, Side bySide)
        {
            ulong target = BitUtils.Bit(square);

            // pawns: to ask "is square X attacked by side S's pawns?" we look up the
            // attack table of the OPPOSITE side standing on X - a white pawn attacks
            // up-left/up-right, so the squares that would host an attacking white pawn
            // are exactly those a black pawn standing on X would attack.
            var pawnTable = bySide == Side.White ? AttackTables.PawnAttacksBlack : AttackTables.PawnAttacksWhite;
            if ((pawnTable[square] & Pieces[AttackTables.PieceIndex(PieceType.Pawn, bySide)]) != 0UL) return true;

            // knights
            if ((AttackTables.KnightAttacks[square] & Pieces[AttackTables.PieceIndex(PieceType.Knight, bySide)]) != 0UL) return true;

            // king
            if ((AttackTables.KingAttacks[square] & Pieces[AttackTables.PieceIndex(PieceType.King, bySide)]) != 0UL) return true;

            // sliders
            if ((AttackTables.BishopAttacks(square, All) & (Pieces[AttackTables.PieceIndex(PieceType.Bishop, bySide)] | Pieces[AttackTables.PieceIndex(PieceType.Queen, bySide)])) != 0UL) return true;
            if ((AttackTables.RookAttacks(square, All) & (Pieces[AttackTables.PieceIndex(PieceType.Rook, bySide)] | Pieces[AttackTables.PieceIndex(PieceType.Queen, bySide)])) != 0UL) return true;

            return false;
        }

        public bool IsKingInCheck(Side side)
        {
            return IsSquareAttacked(KingSquare[(int)side], (Side)(1 - (int)side));
        }

        public bool InCheck => IsKingInCheck(SideToMove);

        public bool IsKingSidePossible(Side side)
        {
            int kingSq = KingSquare[(int)side];
            int rookSq = side == Side.White ? 0x07 : 63; // h1 / h8
            var flag = side == Side.White ? CastlingFlags.WhiteKingSide : CastlingFlags.BlackKingSide;
            if ((Castling & flag) == 0) return false;
            var rook = Mailbox[rookSq];
            if (rook.IsNone || rook.Type != PieceType.Rook || rook.Side != side) return false;
            return Mailbox[kingSq + 1].IsNone && Mailbox[kingSq + 2].IsNone;
        }

        public bool IsQueenSidePossible(Side side)
        {
            int kingSq = KingSquare[(int)side];
            int rookSq = side == Side.White ? 0 : 56;
            var flag = side == Side.White ? CastlingFlags.WhiteQueenSide : CastlingFlags.BlackQueenSide;
            if ((Castling & flag) == 0) return false;
            var rook = Mailbox[rookSq];
            if (rook.IsNone || rook.Type != PieceType.Rook || rook.Side != side) return false;
            return Mailbox[kingSq - 1].IsNone && Mailbox[kingSq - 2].IsNone && Mailbox[kingSq - 3].IsNone;
        }

        public Position Clone()
        {
            var copy = FromFen(ToFen());
            foreach (var h in History) copy.History.EnqueueClone(h);
            copy.Zobrist = Zobrist;
            return copy;
        }
    }

    internal static class HistoryListExtensions
    {
        public static void EnqueueClone(this List<HistoryEntry> list, HistoryEntry h)
            => list.Add(new HistoryEntry
            {
                Move = h.Move,
                Captured = h.Captured,
                CapturedSquare = h.CapturedSquare,
                CastlingBefore = h.CastlingBefore,
                EnPassantBefore = h.EnPassantBefore,
                HalfMoveClockBefore = h.HalfMoveClockBefore,
                ZobristBefore = h.ZobristBefore
            });
    }
}
