using System;
using System.Collections.Generic;

namespace Chess.Desktop.Rendering3D
{
    /// <summary>
    /// Rolling frame-time statistics - the chapter 6 "measure, don't guess"
    /// instrument. Samples from the game loop on device, then the same class is
    /// driven by NUnit with synthetic samples, which is how the benchmark
    /// assertions run in CI without a GPU.
    /// </summary>
    public sealed class FrameProfiler
    {
        readonly Queue<float> _ms = new();
        readonly int _window;

        public FrameProfiler(int window = 120) => _window = window;

        public void Sample(float frameMs)
        {
            _ms.Enqueue(frameMs);
            while (_ms.Count > _window) _ms.Dequeue();
        }

        public struct Stats
        {
            public float Avg, Min, Max;       // milliseconds
            public int Jank;                  // frames over the 33 ms (sub-30-fps) line
            public float LongestStall;        // worst single frame, ms
            public int Count => _count;
            public int _count;
        }

        public Stats Snapshot()
        {
            if (_ms.Count == 0) return default;
            var s = new Stats { _count = _ms.Count, Min = float.MaxValue };
            foreach (float v in _ms)
            {
                s.Avg += v;
                if (v < s.Min) s.Min = v;
                if (v > s.Max) s.Max = v;
                if (v > 33f) s.Jank++;
            }
            s.Avg /= _ms.Count;
            s.LongestStall = s.Max;
            return s;
        }

        public void Reset() => _ms.Clear();
    }
}
