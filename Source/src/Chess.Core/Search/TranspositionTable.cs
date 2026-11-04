using System;

namespace Chess.Core
{
    /// <summary>The two stored scores of a transposition table entry: exact, lower
    /// or upper bound depending on the search that wrote it.</summary>
    public enum TableFlag : byte
    {
        Unused = 0,
        Exact,
        LowerBound,   // fail-high: the true score is at least this
        UpperBound    // fail-low: the true score is at most this
    }

    /// <summary>One slot of the shared transposition table.</summary>
    public struct TableEntry
    {
        public ulong Key64;      // first 32 bits are in the index; store the full key too
        public short Score;
        public byte Depth;
        public TableFlag Flag;
        public Move Best;
    }

    /// <summary>
    /// A fixed-size, lock-free-ish transposition table with the textbook two-slot
    /// replacement policy. Allocated once per search session: on mobile, that keeps
    /// the GC from ever seeing a table resize.
    /// </summary>
    public sealed class TranspositionTable
    {
        private readonly TableEntry[] _slots;
        private readonly int _mask;

        public TranspositionTable(int powerOfTwoSlots)
        {
            int size = 1;
            while (size < powerOfTwoSlots) size <<= 1;
            if (size < 1024) size = 1024;
            _slots = new TableEntry[size];
            _mask = size - 1;
        }

        public void Store(ulong key, int depth, int score, TableFlag flag, Move best, int ply)
        {
            // Mate scores must be stored relative to the *node*, not the root ply,
            // otherwise they go stale once replayed at a different depth.
            if (score > Evaluator.MateInOneMargin) score += ply;
            else if (score < -Evaluator.MateInOneMargin) score -= ply;

            int index = (int)((uint)key & _mask);
            for (int slot = 0; slot < 2; slot++)
            {
                int i = index ^ slot;
                var e = _slots[i];
                bool replace =
                    e.Flag == TableFlag.Unused
                    || e.Key64 == 0UL
                    || e.Key64 == key && e.Depth <= depth
                    || e.Key64 != key && e.Depth <= depth;
                if (replace)
                {
                    _slots[i] = new TableEntry
                    {
                        Key64 = key,
                        Depth = (byte)depth,
                        Score = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, score)),
                        Flag = flag,
                        Best = best
                    };
                    return;
                }
            }
        }

        public bool Probe(ulong key, int depth, int ply, out int score, out Move best, out TableFlag flag)
        {
            best = default;
            flag = TableFlag.Unused;
            int index = (int)((uint)key & _mask);
            for (int slot = 0; slot < 2; slot++)
            {
                var e = _slots[index ^ slot];
                if (e.Key64 == key && e.Depth >= depth && e.Flag != TableFlag.Unused)
                {
                    int s = e.Score;
                    if (s > Evaluator.MateInOneMargin) s -= ply;
                    else if (s < -Evaluator.MateInOneMargin) s += ply;
                    score = s;
                    best = e.Best;
                    flag = e.Flag;
                    return true;
                }
            }
            score = 0;
            return false;
        }

        public void Clear()
        {
            Array.Clear(_slots, 0, _slots.Length);
        }
    }
}
