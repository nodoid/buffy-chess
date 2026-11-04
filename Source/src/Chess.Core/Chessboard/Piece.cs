using System;
using System.Text;

namespace Chess.Core
{
    /// <summary>The type of a chess piece, matching the standard SAN letters.</summary>
    public enum PieceType : byte
    {
        None = 0, Pawn = 1, Knight = 2, Bishop = 3, Rook = 4, Queen = 5, King = 6
    }

    /// <summary>Which army a piece belongs to. Indexable for array lookups.</summary>
    public enum Side : byte
    {
        White = 0, Black = 1
    }

    /// <summary>
    /// A square of the board, encoded 0..63 with a1 = 0, h1 = 7, a8 = 56, h8 = 63.
    /// This is the "little-endian rank-file" (LERF) mapping the engine uses internally.
    /// </summary>
    public static class Sq
    {
        public const int None = -1;

        public static byte Make(int file, int rank) => (byte)(rank * 8 + file);
        public static int FileOf(int square) => square & 7;
        public static int RankOf(int square) => square >> 3;

        public static string Name(int square)
        {
            if (square < 0) return "-";
            return $"{(char)('a' + FileOf(square))}{(char)('1' + RankOf(square))}";
        }

        public static int FromName(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "-") return None;
            return (name[1] - '1') * 8 + (name[0] - 'a');
        }
    }

    /// <summary>A piece on a square: a type plus a side, packed into one byte.</summary>
    public struct Piece
    {
        public PieceType Type;
        public Side Side;

        public Piece(PieceType type, Side side) { Type = type; Side = side; }

        public bool IsNone => Type == PieceType.None;
        public bool IsWhite => !IsNone && Side == Side.White;

        /// <summary>Index into the engine's 12-slot bitboard arrays:
        /// (type-1)*2 + side, because PieceType.None has no bitboard.</summary>
        public int BoardIndex => (((int)Type - 1) << 1) | (int)Side;

        public override string ToString()
        {
            if (IsNone) return ".";
            const string white = ".PNBRQK";
            const string black = ".pnbrqk";
            // '.' occupies index 0 so "None" renders as a dot just like FEN does.
            return (Side == Side.White ? white : black)[(int)Type].ToString();
        }

        public static Piece FromFen(char c)
        {
            var type = CharToType(c);
            return new Piece(type, char.IsUpper(c) ? Side.White : Side.Black);
        }

        public static PieceType CharToType(char c)
        {
            switch (char.ToUpperInvariant(c))
            {
                case 'P': return PieceType.Pawn;
                case 'N': return PieceType.Knight;
                case 'B': return PieceType.Bishop;
                case 'R': return PieceType.Rook;
                case 'Q': return PieceType.Queen;
                case 'K': return PieceType.King;
                default: return PieceType.None;
            }
        }

        public static char TypeLetter(PieceType t) =>
            ".PNBRQK"[(int)t];
    }

    /// <summary>The four castling right flags, packed as a bitmask.</summary>
    [Flags]
    public enum CastlingFlags : byte
    {
        None = 0,
        WhiteKingSide = 1,
        WhiteQueenSide = 2,
        BlackKingSide = 4,
        BlackQueenSide = 8
    }

    /// <summary>Static bit-manipulation helpers. Nifty - no .NET 8-only APIs here,
    /// because the core must build as a netstandard2.0 library and run inside
    /// MonoGame's shared mobile projects too.</summary>
    public static class BitUtils
    {
        public static ulong Bit(int square) => 1UL << square;

        public static bool Contains(ulong bb, int square) => (bb & (1UL << square)) != 0UL;

        /// <summary>Index of the least significant set bit, via binary descent on the
        /// low half word. Returns -1 for 0. Six predictable steps, no branches into
        /// a lookup table, so it runs identically on every platform and every CPU.</summary>
        public static int LsbIndex(ulong bb)
        {
            if (bb == 0UL) return -1;
            int index = 0;
            if ((bb & 0x00000000FFFFFFFFUL) == 0UL) { bb >>= 32; index += 32; }
            if ((bb & 0x000000000000FFFFUL) == 0UL) { bb >>= 16; index += 16; }
            if ((bb & 0x00000000000000FFUL) == 0UL) { bb >>= 8;  index += 8; }
            if ((bb & 0x000000000000000FUL) == 0UL) { bb >>= 4;  index += 4; }
            if ((bb & 0x0000000000000003UL) == 0UL) { bb >>= 2;  index += 2; }
            if ((bb & 0x0000000000000001UL) == 0UL) { index += 1; }
            return index;
        }

        /// <summary>Index of the most significant set bit. Returns -1 for 0.
        /// Spreads the highest bit downwards, then counts bits minus one.</summary>
        public static int MsbIndex(ulong bb)
        {
            if (bb == 0UL) return -1;
            ulong spread = bb;
            spread |= spread >> 1; spread |= spread >> 2; spread |= spread >> 4;
            spread |= spread >> 8; spread |= spread >> 16; spread |= spread >> 32;
            return PopCount(spread) - 1;
        }

        public static int PopCount(ulong bb)
        {
            // SWAR popcount: works everywhere, no intrinsic dependency.
            ulong m1 = 0x5555555555555555UL, m2 = 0x3333333333333333UL, m4 = 0x0f0f0f0f0f0f0f0fUL;
            ulong h = 0x0101010101010101UL;
            ulong v = bb;
            v = v - ((v >> 1) & m1);
            v = (v & m2) + ((v >> 2) & m2);
            v = (v + (v >> 4)) & m4;
            return (int)((v * h) >> 56);
        }

        public static string BitsToString(ulong bb, Side whiteSide = Side.White)
        {
            var sb = new StringBuilder();
            for (int rank = 7; rank >= 0; rank--)
            {
                sb.Append(' ');
                for (int file = 0; file < 8; file++)
                    sb.Append(Contains(bb, rank * 8 + file) ? "1 " : ". ");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
