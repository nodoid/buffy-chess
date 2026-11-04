using System.Collections.Generic;
using System.Text;

namespace Chess.Core
{
    /// <summary>
    /// Standard algebraic notation for the move-list UI. Works purely from the
    /// position: generate the legal moves, find the one being played, and tag on
    /// the minimal disambiguation required by the SAN standard.
    /// </summary>
    public static class SanWriter
    {
        public static string Write(Position pos, Move move)
        {
            if (move.IsCastle)
            {
                var sb0 = new StringBuilder(move.Kind == MoveKind.KingSideCastle ? "O-O" : "O-O-O");
                AppendSuffix(pos, move, sb0);
                return sb0.ToString();
            }

            var piece = pos[move.From];
            var sb = new StringBuilder();

            if (piece.Type == PieceType.Pawn)
            {
                if (move.IsCapture) sb.Append(Sq.Name(move.From)[0]);        // e.g. "exd5"
                if (move.IsPromotion)
                {
                    sb.Append(Sq.Name(move.To));
                    sb.Append('=').Append(Piece.TypeLetter(move.Promo));
                }
                else sb.Append(Sq.Name(move.To));
            }
            else
            {
                sb.Append(Piece.TypeLetter(piece.Type));
                // disambiguation: any OTHER piece of the same type that could also reach To
                var legal = MoveGen.GenerateLegalMoves(pos);
                bool fileClash = false, rankClash = false, ambiguous = false;
                var fromFile = Sq.FileOf(move.From);
                var fromRank = Sq.RankOf(move.From);
                foreach (var other in legal)
                {
                    if (other.To != move.To || other.From == move.From) continue;
                    if (pos[other.From].Type != piece.Type) continue;
                    ambiguous = true;
                    if (Sq.FileOf(other.From) == fromFile) rankClash = true;
                    if (Sq.RankOf(other.From) == fromRank) fileClash = true;
                }
                if (ambiguous)
                {
                    if (!fileClash) sb.Append((char)('a' + fromFile));
                    else if (!rankClash) sb.Append((char)('1' + fromRank));
                    else sb.Append(Sq.Name(move.From));
                }
                if (move.IsCapture) sb.Append('x');
                sb.Append(Sq.Name(move.To));
            }

            AppendSuffix(pos, move, sb);
            return sb.ToString();
        }

        private static void AppendSuffix(Position pos, Move move, StringBuilder sb)
        {
            pos.MakeMove(move);
            var reply = new List<Move>(32);
            MoveGen.GenerateLegalMoves(pos, reply);
            if (pos.InCheck)
                sb.Append(reply.Count == 0 ? '#' : '+');
            pos.UnmakeMove();
        }
    }
}
