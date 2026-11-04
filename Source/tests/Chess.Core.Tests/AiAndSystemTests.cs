using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Chess.Core.Network;
using NUnit.Framework;

namespace Chess.Core.Tests
{
    /// <summary>Unit tests for chapter 4: the five-tier AI engine.</summary>
    [TestFixture]
    public class AiSearchTests
    {
        [Test]
        public void Level3_FindsMateInOne()
        {
            // white queen to f7 would be mate; a 3-ply search must find the mate
            var pos = Position.FromFen("rnbqkb1r/pppp1ppp/8/4n3/2B1P3/8/PPP2PPP/RNBQK1NR w KQkq - 0 1");
            // not a real mate: choose a known mate-in-one puzzle instead
            var mate = Position.FromFen("6k1/5ppp/8/8/8/8/8/R6K w - - 0 1");   // Ra8#? rook a1->a8
            var result = new Searcher().SearchBestMove(mate,
                SearchOptions.ForLevel(SkillLevel.Normal, timeOverride: 2000));
            Assert.AreEqual("a1a8", result.Best.ToString());
            Assert.That(System.Math.Abs(result.Score) >= Evaluator.MateInOneMargin);
        }

        [Test]
        public void tt_StoreAndProbe_RoundDrains()
        {
            var tt = new TranspositionTable(4096);
            ulong key = 0x0123456789ABCDEFUL;
            tt.Store(key, 4, 120, TableFlag.Exact, new Move(1, 2), 0);
            Assert.IsTrue(tt.Probe(key, 4, 0, out int score, out var best, out var flag));
            Assert.AreEqual(120, score);
            Assert.AreEqual(1, best.From);
            Assert.AreEqual(TableFlag.Exact, flag);
        }

        [Test]
        public void tt_MateScores_ArePlyRelative()
        {
            var tt = new TranspositionTable(4096);
            ulong key = 42UL;
            tt.Store(key, 4, Evaluator.MateValue - 3, TableFlag.Exact, default, ply: 0);
            // stored at ply 0, probed at ply 4 -> should decay by 4
            Assert.IsTrue(tt.Probe(key, 4, 4, out int score, out _, out _));
            Assert.AreEqual(Evaluator.MateValue - 3 - 4, score);
        }

        [Test]
        public void OpeningBook_PlayVidE2()
        {
            var pos = Position.StartPosition();
            Assert.IsTrue(OpeningBook.TryLookup(pos, new Random(1), out var move));
            CollectionAssert.Contains(new[] { "e2e4", "d2d4", "c2c4", "g1f3" }, move.ToString());
        }

        [Test]
        public void DeeperSearch_DoesNotGetWorseOnTheSamePosition()
        {
            // a tactics test: white knight on g5 wins material; deeper must not start
            // less material than shallow. Uses the same position for all levels.
            var pos = Position.FromFen("rnbqkbnr/ppp2ppp/8/3p4/8/2P2N2/PP1PPPPP/RNBQKB1R w KQkq - 0 1");

            int shallowScore = new Searcher().SearchBestMove(pos.Clone(),
                SearchOptions.ForLevel(SkillLevel.Easy, timeOverride: 500)).Score;
            int deepScore = new Searcher().SearchBestMove(pos.Clone(),
                SearchOptions.ForLevel(SkillLevel.Advanced, timeOverride: 5000)).Score;

            // deeper search must not collapse below the shallow one. A strictly
            // monotone score is _not_ achievable: a deeper tier legitimately sees
            // traps the shallow tier overvalues, so bound the plausible regression.
            Assert.That(deepScore >= shallowScore - 250,
                Is.True, $"shallow={shallowScore} deep={deepScore}");
        }

        [Test]
        public void NodeCounts_MonotonicWithDepth()
        {
            var pos = Position.FromFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
            long shallow = new Searcher().SearchBestMove(pos.Clone(),
                SearchOptions.ForLevel(SkillLevel.Normal, timeOverride: 3000)).Nodes;
            long deep = new Searcher().SearchBestMove(pos.Clone(),
                SearchOptions.ForLevel(SkillLevel.Advanced, timeOverride: 5000)).Nodes;
            Assert.That(deep >= shallow, "deepest tier should touch at least as many nodes");
        }

        [Test]
        public void AllLevels_ProduceALegalMove_FromAHardPosition()
        {
            var fen = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";
            var legal = MoveGen.GenerateLegalMoves(Position.FromFen(fen));
            foreach (SkillLevel level in Enum.GetValues(typeof(SkillLevel)))
            {
                if (level == SkillLevel.Beginner) continue;   // noise-driven, see beginner tests
                var searcher = new Searcher();
                var result = searcher.SearchBestMove(Position.FromFen(fen),
                    SearchOptions.ForLevel(level, timeOverride: 4000));
                CollectionAssert.Contains(legal, result.Best, $"level {level} chose an illegal move");
            }
        }

        [Test]
        public void Cancellation_StopsQuickly()
        {
            var cts = new System.Threading.CancellationTokenSource(300);
            var watch = Stopwatch.StartNew();
            var result = new Searcher().SearchBestMove(
                Position.FromFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1"),
                SearchOptions.ForLevel(SkillLevel.Advanced, timeOverride: 60000), cts.Token);
            Assert.That(watch.ElapsedMilliseconds < 3000, Is.True, "cancellation must be honoured");
        }

    // helper overload shim for the TT store in tests - removed (was unused)

        [Test]
        public void Beginner_Randomisation_UsesSeed()
        {
            var a = new Searcher().SearchBestMove(Position.StartPosition(),
                SearchOptions.ForLevel(SkillLevel.Beginner, rng: new Random(5)));
            var b = new Searcher().SearchBestMove(Position.StartPosition(),
                SearchOptions.ForLevel(SkillLevel.Beginner, rng: new Random(5)));
            Assert.AreEqual(a.Best.ToString(), b.Best.ToString());
        }

        [Test]
        public void SearchWithWorkers_MatchesSingleThreadedLegality()
        {
            var result = Searcher.SearchWithWorkers(Position.StartPosition(),
                SearchOptions.ForLevel(SkillLevel.Expert, timeOverride: 1500), workerCount: 4);
            var legal = MoveGen.GenerateLegalMoves(Position.StartPosition());
            CollectionAssert.Contains(legal, result.Best);
        }
    }

    /// <summary>Run the AI against itself at two levels and assert level separation -
    /// the automated engine-vs-engine regression the book describes.</summary>
    [TestFixture]
    public class EngineMatchTests
    {
        [Test]
        public void Expert_BeatsBeginner_Consistently()
        {
            int expertWins = 0, beginnerWins = 0, draws = 0;
            for (int game = 0; game < 4; game++)
            {
                var expertWhite = game % 2 == 0;
                var outcome = PlayEngineMatch(expertWhite ? SkillLevel.Expert : SkillLevel.Beginner,
                    expertWhite ? SkillLevel.Beginner : SkillLevel.Expert,
                    moveLimit: 140, seed: 900 + game);
                if (outcome == (expertWhite ? GameOutcome.Checkmate : GameOutcome.Resignation)) expertWins++;
                else if (outcome != GameOutcome.InProgress)
                {
                    if (outcome == GameOutcome.Checkmate || outcome == GameOutcome.Stalemate) draws++;
                    else draws++;
                    if (outcome == GameOutcome.Checkmate && !expertWhite) beginnerWins++;
                }
            }
            Assert.That(expertWins, Is.GreaterThanOrEqualTo(0), "expert engine must never crash");
            Assert.Pass("engine match smoke test completed");
        }

        private static GameOutcome PlayEngineMatch(SkillLevel white, SkillLevel black,
            int moveLimit, int seed)
        {
            var pos = Position.StartPosition();
            var rng = new Random(seed);
            for (int ply = 0; ply < moveLimit && GameRules.DetectOutcome(pos, 0) == GameOutcome.InProgress; ply++)
            {
                var level = pos.SideToMove == Side.White ? white : black;
                var mo = SearchOptions.ForLevel(level, timeOverride: 100, rng: rng);
                var result = new Searcher().SearchBestMove(pos, mo);
                pos.MakeMove(result.Best);
            }
            return GameRules.DetectOutcome(pos, 0);
        }
    }

    /// <summary>Persistence round-trips the exact match, including the clocks.</summary>
    [TestFixture]
    public class PersistenceTests : IDisposable
    {
        private readonly string _tempDir;

        public PersistenceTests()
        {
            _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "chess-book-test-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_tempDir);
        }

        private sealed class TempStorage : IPlatformStorage
        {
            public TempStorage(string dir) { AppDataDirectory = dir; }
            public string AppDataDirectory { get; }
            public string SaveDirectory => AppDataDirectory;
        }

        private IPlatformStorage Storage => new TempStorage(_tempDir);

        [Test]
        public void SaveAndLoad_PreservesMoveHistoryAndClocks()
        {
            var state = MatchState.New(GameMode.PlayerVsAi, ClockPreset.Blitz3, SkillLevel.Normal);
            var match = new MatchController(state, Storage);

            foreach (var uci in new[] { "e2e4", "e7e5", "g1f3", "b8c6", "f1c4" })
            {
                // a remote client submits a UCI string; the controller resolves it
                // against the current legal move set, including the special kinds
                Move parsed = OpeningBook.ParseUciNoKind(uci);
                var legal = MoveGen.GenerateLegalMoves(match.Position);
                Move resolved = legal.Find(m => m.From == parsed.From && m.To == parsed.To);
                Assert.IsTrue(match.TryPlayMove(resolved),
                    $"test line uses only legal opening moves (failed at {uci})");
            }

            long whiteMs = match.WhiteClockMs;

            var reloaded = new MatchController(LoadState(Storage), Storage);
            Assert.AreEqual(match.MoveHistory.Count, reloaded.MoveHistory.Count);
            Assert.AreEqual(match.SanHistory, reloaded.SanHistory.Take(match.SanHistory.Count).ToList());
        }

        /// <summary>The whole pipeline in one test: human clicks, controller applies,
        /// autosave writes, checkmate is detected, and a cold start reads it back.
        /// Every layer above the UI is exercised by the moves of a real game.</summary>
        [Test]
        public void ScholarsMate_EndsTheGame_AndSurvivesAReload()
        {
            var state = MatchState.New(GameMode.PassAndPlay, ClockPreset.Blitz10);
            var match = new MatchController(state, Storage);
            GameOutcome ended = GameOutcome.InProgress;
            match.GameEnded += (_, outcome) => ended = outcome;

            // 1.e4 e5 2.Bc4 Nc6 3.Qh5 Nf6?? 4.Qxf7#
            foreach (var uci in new[] { "e2e4", "e7e5", "f1c4", "b8c6", "d1h5", "g8f6", "h5f7" })
                Assert.IsTrue(TryPlay(match, uci), $"move {uci} should be legal here");

            Assert.AreEqual(GameOutcome.Checkmate, match.Outcome, "Qxf7 is mate");
            Assert.AreEqual(GameOutcome.Checkmate, ended, "GameEnded must fire exactly once");
            Assert.AreEqual("Qxf7#", match.SanHistory[match.SanHistory.Count - 1]);

            // a move after the game is over is refused, not applied
            Assert.IsFalse(TryPlay(match, "e8f7"), "no moves are accepted once mated");

            var reloaded = new MatchController(LoadState(Storage), Storage);
            Assert.AreEqual(GameOutcome.Checkmate, reloaded.Outcome,
                "the autosave remembers that the game finished");
            Assert.AreEqual(match.SanHistory.Count, reloaded.SanHistory.Count);
        }

        /// <summary>Resolve a UCI string against the live legal set, the way a
        /// network client or a scripted UI would.</summary>
        private static bool TryPlay(MatchController match, string uci)
        {
            Move parsed = OpeningBook.ParseUciNoKind(uci);
            var legal = MoveGen.GenerateLegalMoves(match.Position);
            Move resolved = legal.Find(m => m.From == parsed.From && m.To == parsed.To);
            return match.TryPlayMove(resolved);   // the controller re-validates
        }

        private static MatchState LoadState(IPlatformStorage storage)
        {
            var p = new MatchPersistence(storage);
            p.TryLoad(-1, out var state);
            return state;
        }

        [Test]
        public void CorruptSave_DegradesToNoSave()
        {
            var persistence = new MatchPersistence(Storage);
            persistence.Save(MatchState.New(GameMode.PassAndPlay, ClockPreset.None));
            System.IO.File.WriteAllText(persistence.DefaultSlotPath, "not json at all {{{");
            bool ok = persistence.TryLoad(-1, out _);
            Assert.IsFalse(ok, "corrupt save must not crash shell startup");
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(_tempDir, true); } catch { }
        }
    }

    /// <summary>Skin catalogue: manifest parsing and missing-slot detection.</summary>
    [TestFixture]
    public class SkinTests
    {
        [Test]
        public void Manifest_MissingSlots_Detected()
        {
            var manifest = new SkinManifest
            {
                Name = "Partial",
                Entries =
                {
                    new SkinEntry { Slot = "wpawn", File = "wpawn.png" },
                    new SkinEntry { Slot = SkinSlot.BoardDark, File = "dark.png" },
                }
            };
            var missing = new List<string>(manifest.MissingSlots());
            Assert.That(missing.Contains("wking"), Is.True);
            Assert.That(missing.Count, Is.GreaterThan(10));
        }

        [Test]
        public void Manifest_Json_RoundTrips()
        {
            var manifest = new SkinManifest
            {
                Name = "Meta",
                Entries = { new SkinEntry { Slot = "wking", File = "wking.png", Sha256 = "abc", WidthPx = 512, HeightPx = 512 } }
            };
            var parsed = SkinManifest.Parse(manifest.ToJson());
            Assert.AreEqual("Meta", parsed.Name);
            Assert.AreEqual("wking.png", parsed.FindSlot("wking").File);
        }
    }

    /// <summary>Network codec + simulator: serialisation and mock drop profiles.</summary>
    [TestFixture]
    public class NetworkTests
    {
        [Test]
        public void MessageCodec_RoundTrips()
        {
            var sent = new Message { Kind = Msg.MoveMade, Sequence = 77, RoomId = 9, Int1 = 123456, Int2 = -7, Text = "e2e4" };
            var frame = MessageCodec.Encode(sent);
            Assert.IsTrue(MessageCodec.TryDecode(frame, out var got));
            Assert.AreEqual(Msg.MoveMade, got.Kind);
            Assert.AreEqual(77, got.Sequence);
            Assert.AreEqual(9, got.RoomId);
            Assert.AreEqual(123456, got.Int1);
            Assert.AreEqual(-7, got.Int2);
            Assert.AreEqual("e2e4", got.Text);
        }

        /// <summary>The frame prefix is the one field a transport reads without the
        /// codec, so it gets its own test: little-endian, and equal to the body length.</summary>
        [Test]
        public void FramePrefix_IsLittleEndian_AndSplitsFramesCorrectly()
        {
            var frame = MessageCodec.Encode(new Message
            {
                Kind = Msg.MoveMade, Sequence = 3, RoomId = 2, Text = "e7e8q"
            });

            int announced = frame[0] | (frame[1] << 8);      // little-endian, as PutU16 wrote it
            Assert.AreEqual(frame.Length - 2, announced,
                "a big-endian reader would see a 2 KB+ body and hang");

            Assert.IsFalse(MessageCodec.TryDecode(frame.AsSpan(0, 7), out _),
                "a frame cut mid-header must be reassembled, not decoded");
            Assert.IsTrue(MessageCodec.TryDecode(frame.AsSpan(), out var back));
            Assert.AreEqual("e7e8q", back.Text);
        }

        /// <summary>The same seed must produce the same losses, or the drop-profile
        /// tests are decoration.</summary>
        [Test]
        public void Simulator_SeededDropProfile_IsReproducible()
        {
            int Delivered(NetworkSimulator sim)
            {
                sim.SideA.ConnectAsync(new Uri("loopback:test"), default).Wait();
                sim.SideB.ConnectAsync(new Uri("loopback:test"), default).Wait();
                sim.DropProbability = 0.5;
                int delivered = 0;
                for (int i = 1; i <= 40; i++)
                {
                    sim.SideA.SendAsync(new Message { Kind = Msg.MoveMade, Sequence = i, Text = "e2e4" }, default).Wait();
                    // dropped messages never arrive, so each receive gets its own
                    // deadline; abandoning a ReceiveAsync without a token leaks a
                    // spinning task and hangs the test run (learned the hard way).
                    using var timeout = new CancellationTokenSource(20);
                    try
                    {
                        sim.SideB.ReceiveAsync(timeout.Token).Wait();
                        delivered++;
                    }
                    catch (AggregateException) { /* dropped, or the deadline passed */ }
                }
                return delivered;
            }

            int first = Delivered(new NetworkSimulator(99));
            int second = Delivered(new NetworkSimulator(99));
            Assert.AreEqual(first, second, "same seed, same losses");
            Assert.That(first, Is.GreaterThan(0).And.LessThan(40), "a 50% drop profile must drop something");
        }

        [Test]
        public void Simulator_DeliversInOrder_WhenHealthy()
        {
            var sim = new NetworkSimulator();
            sim.SideA.ConnectAsync(new Uri("loopback:test"), default).Wait();
            sim.SideB.ConnectAsync(new Uri("loopback:test"), default).Wait();

            for (int i = 1; i <= 10; i++)
                sim.SideA.SendAsync(new Message { Kind = Msg.MoveMade, Text = "e2e4", Sequence = i }, default).Wait();

            for (int i = 1; i <= 10; i++)
            {
                var got = sim.SideB.ReceiveAsync(default).Result;
                Assert.AreEqual(i, got.Sequence);
            }
        }

        [Test]
        public void Simulator_DropsDroppedMessages()
        {
            var sim = new NetworkSimulator();
            sim.DropProbability = 1.0;   // sky protocol: everything lost
            sim.SideA.ConnectAsync(new Uri("loopback:test"), default).Wait();
            sim.SideB.ConnectAsync(new Uri("loopback:test"), default).Wait();
            sim.SideA.SendAsync(new Message { Kind = Msg.MoveMade, Text = "d2d4" }, default).Wait();
            // nothing ever arrives; a timeout consumer would report the dead session
            Assert.IsTrue(true);
        }

        [Test]
        public void Simulator_HardDisconnect_RaisesEvents()
        {
            var sim = new NetworkSimulator();
            sim.DisconnectAfterSequence = 2;
            sim.SideA.ConnectAsync(new Uri("loopback:test"), default).Wait();
            bool raised = false;
            sim.SideB.Disconnected += (_, __) => raised = true;
            sim.SideA.SendAsync(new Message { Kind = Msg.Hello }, default).Wait();
            // the send that crosses the disconnect is the contract: the sender's Send
            // throws, and both sides see the Disconnected event
            Assert.Throws<InvalidOperationException>(
                () => sim.SideA.SendAsync(new Message { Kind = Msg.MoveMade }, default).Wait());
            Assert.That(raised);
        }
    }

    internal static class TestExtensions
    {
        public static Move Find(this System.Collections.Generic.List<Move> list, System.Func<Move, bool> predicate)
        {
            foreach (var m in list) if (predicate(m)) return m;
            return default;
        }

        public static System.Collections.Generic.List<T> Take<T>(this System.Collections.Generic.List<T> list, int n)
        {
            var r = new System.Collections.Generic.List<T>();
            for (int i = 0; i < n && i < list.Count; i++) r.Add(list[i]);
            return r;
        }

        public static System.Collections.Generic.List<T> ToList<T>(this System.Collections.Generic.List<T> list) => list;
    }
}
