using System;
using System.Threading;

namespace Chess.Core
{
    /// <summary>The five shipped difficulty levels, matching the product design.</summary>
    public enum SkillLevel : byte
    {
        Beginner = 1,       // noisy one-ply blunderer
        Easy = 2,           // shallow two-ply with jitter
        Normal = 3,         // three-ply alpha-beta with move ordering
        Advanced = 4,       // deeper search, killer moves, TT
        Expert = 5          // iterative deepening + TT + book + parallel root split
    }

    /// <summary>Everything the search needs = one immutable settings record.</summary>
    public struct SearchOptions
    {
        public SkillLevel Skill;
        public int MaxDepth;
        public int TimeBudgetMs;
        public bool UseTranspositionTable;
        public bool UseQuiescence;
        public bool UseOpeningBook;
        public Random Rng;

        public static SearchOptions ForLevel(SkillLevel level, int? timeOverride = null, Random rng = null)
        {
            SearchOptions o = new SearchOptions
            {
                Skill = level,
                Rng = rng ?? new Random(0xC0FFEE),
                UseTranspositionTable = level >= SkillLevel.Advanced,
                UseQuiescence = level >= SkillLevel.Normal,
                UseOpeningBook = level >= SkillLevel.Advanced
            };
            switch (level)
            {
                case SkillLevel.Beginner:
                    o.MaxDepth = 1; o.TimeBudgetMs = timeOverride ?? 50; o.UseOpeningBook = false; break;
                case SkillLevel.Easy:
                    o.MaxDepth = 2; o.TimeBudgetMs = timeOverride ?? 120; break;
                case SkillLevel.Normal:
                    o.MaxDepth = 3; o.TimeBudgetMs = timeOverride ?? 250; break;
                case SkillLevel.Advanced:
                    o.MaxDepth = 5; o.TimeBudgetMs = timeOverride ?? 800; break;
                case SkillLevel.Expert:
                    o.MaxDepth = 7; o.TimeBudgetMs = timeOverride ?? 2500; break;
            }
            return o;
        }
    }

    /// <summary>Result of one completed search.</summary>
    public sealed class SearchResult
    {
        public Move Best;
        public int Score;          // centipawns, from the searching side's perspective
        public int DepthReached;
        public long Nodes;
        public long ElapsedMs;
        public string PrincipalVariationLine = "";
        public bool BookHit;
    }

    /// <summary>Search progress telemetry for the UI's spinner and telemetry panel.</summary>
    public interface ISearchListener
    {
        void OnProgress(SearchOptions options, int depth, Move currentBest, int score, long nodes);
        void OnComplete(SearchResult result);
    }

    /// <summary>Shared cancel token for cooperative cancellation.</summary>
    public sealed class SearchCancelledException : OperationCanceledException { }
}
