using System;

namespace Chess.Core
{
    /// <summary>
    /// Zobrist hashing keys. Incrementally updated on every make/unmake so the
    /// search engine can consult its transposition table in O(1) without ever
    /// re-hashing a position from scratch.
    /// </summary>
    public sealed class ZobristHasher
    {
        public readonly ulong[] PieceTable = new ulong[12 * 64];
        public readonly ulong BlackToMove;
        public static readonly ulong[] CastlingKey = new ulong[16];
        public readonly ulong[] FileKeys = new ulong[8];

        public ZobristHasher()
        {
            // A fixed seed makes every machine agree on the same table - important when
            // you want hash behaviour (and therefore search results) reproducible on
            // desktop CI runners as well as on iOS and Android devices.
            var rng = new SplitMix64(0x9E3779B97F4A7C15UL);
            for (int i = 0; i < PieceTable.Length; i++) PieceTable[i] = rng.Next();
            BlackToMove = rng.Next();
            for (int i = 0; i < CastlingKey.Length; i++) CastlingKey[i] = rng.Next();
            for (int i = 0; i < FileKeys.Length; i++) FileKeys[i] = rng.Next();
        }

        /// <summary>SplitMix64: exactly reproducible across platforms, which is what we want
        /// for a hash table that must agree between Windows, Android and iOS builds.</summary>
        public struct SplitMix64
        {
            private ulong _state;
            public SplitMix64(ulong seed) { _state = seed; }
            public ulong Next()
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
