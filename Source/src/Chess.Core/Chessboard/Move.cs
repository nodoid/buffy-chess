namespace Chess.Core
{
    /// <summary>What kind of move this is, used by the rules engine and the UI.</summary>
    public enum MoveKind : byte
    {
        Quiet = 0,
        Capture = 1,
        DoublePawnPush = 2,   // two-square pawn advance (enables en passant)
        EnPassant = 3,
        KingSideCastle = 4,
        QueenSideCastle = 5,
        Promotion = 6,
        PromotionCapture = 7
    }

    /// <summary>
    /// A chess move as a compact struct: from-square, to-square, kind and the
    /// promotion piece when relevant. Deliberately a struct (no heap pressure)
    /// so that search and UI code can hammer it without GC churn.
    /// </summary>
    public struct Move
    {
        public byte From;        // 0..63
        public byte To;          // 0..63
        public MoveKind Kind;
        public PieceType Promo;  // valid when Kind is Promotion / PromotionCapture

        public Move(int from, int to, MoveKind kind = MoveKind.Quiet, PieceType promo = PieceType.None)
        {
            From = (byte)from; To = (byte)to; Kind = kind; Promo = promo;
        }

        public bool IsCapture => Kind == MoveKind.Capture || Kind == MoveKind.EnPassant || Kind == MoveKind.PromotionCapture;
        public bool IsPromotion => Kind == MoveKind.Promotion || Kind == MoveKind.PromotionCapture;
        public bool IsCastle => Kind == MoveKind.KingSideCastle || Kind == MoveKind.QueenSideCastle;

        /// <summary>Long algebraic (UCI-style) text, e.g. e2e4, e7e8q, e1g1.</summary>
        public override string ToString()
        {
            string s = Sq.Name(From) + Sq.Name(To);
            if (IsPromotion) s += char.ToLowerInvariant(Piece.TypeLetter(Promo));
            return s;
        }

        public static bool operator ==(Move a, Move b) => a.From == b.From && a.To == b.To && a.Kind == b.Kind && a.Promo == b.Promo;
        public static bool operator !=(Move a, Move b) => !(a == b);
        public override bool Equals(object obj) => obj is Move m && m == this;
        public override int GetHashCode() => unchecked(From * 4096 + To * 4 + (int)Kind);
    }

    /// <summary>Compact per-search state used by move ordering heuristics.</summary>
    public struct MoveScore
    {
        public Move Move;
        public int Score;
    }
}
