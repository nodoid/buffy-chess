using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Chess.Core
{
    /// <summary>Who the human is playing against, and how the moves flow.</summary>
    public enum GameMode : byte
    {
        PlayerVsAi = 0,          // local single-player vs. engine
        PassAndPlay = 1,         // two humans, one device
        OnlineRanked = 2,        // networked, server-authoritative clocks
        Puzzles = 3
    }

    /// <summary>Chess clock presets.</summary>
    public enum ClockPreset : byte
    {
        None = 0,
        Bullet1 = 1,             // 1 minute
        Blitz3 = 2,              // 3 minutes
        Blitz10 = 3,             // 10 minutes
        Classical30 = 4          // 30 minutes
    }

    /// <summary>
    /// The full snapshot of a live match, serialised to JSON and saved into the
    /// app's local data directory on every move. On mobile the OS can suspend or
    /// kill the process at any moment - usually within a second of the user
    /// switching apps - so the save must always be atomic and always current.
    /// </summary>
    public sealed class MatchState
    {
        public int SchemaVersion { get; set; }

        public GameMode Mode { get; set; }
        public ClockPreset Clock { get; set; }
        public byte AiLevel { get; set; }              // SkillLevel for the engine side
        public byte HumanSide { get; set; }            // 0 = white, 1 = black

        public string StartingFen { get; set; }
        public List<string> MoveListUci { get; set; } = new List<string>();

        public long WhiteClockMs { get; set; }
        public long BlackClockMs { get; set; }
        public long LastSaveUtcTicks { get; set; }

        public byte Outcome { get; set; }              // GameOutcome
        public string PgnHeader { get; set; }

        public static MatchState New(GameMode mode, ClockPreset clock,
            SkillLevel aiLevel = SkillLevel.Normal, Side humanSide = Side.White)
        {
            return new MatchState
            {
                SchemaVersion = 1,
                Mode = mode,
                Clock = clock,
                AiLevel = (byte)aiLevel,
                HumanSide = (byte)humanSide,
                StartingFen = Position.StartPosition().ToFen()
            };
        }

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = false
        };

        public string ToJson() => JsonSerializer.Serialize(this, Options);

        public static MatchState FromJson(string json)
            => JsonSerializer.Deserialize<MatchState>(json, Options);
    }

    /// <summary>
    /// Where platform code stashes persisted match files. Each OS gets its own
    /// correct "local application data" root - the chapter 2 platform abstraction
    /// supplies an implementation, and nothing above it ever touches the file API.
    /// </summary>
    public interface IPlatformStorage
    {
        string AppDataDirectory { get; }
        string SaveDirectory { get; }
    }

    /// <summary>
    /// Load/save orchestration with a single default slot plus named manual
    /// save slots, using atomic write-then-rename so a crash mid-write can never
    /// corrupt a match.
    /// </summary>
    public sealed class MatchPersistence
    {
        private readonly IPlatformStorage _storage;

        public MatchPersistence(IPlatformStorage storage) { _storage = storage; }

        public string DefaultSlotPath =>
            System.IO.Path.Combine(_storage.SaveDirectory, "autosave.json");

        public string SlotPath(int slot)
            => System.IO.Path.Combine(_storage.SaveDirectory, $"slot{slot}.json");

        public void Save(MatchState state, int slot = -1)
        {
            var target = new System.IO.FileInfo(slot < 0 ? DefaultSlotPath : SlotPath(slot));
            var tmpPath = target.FullName + ".tmp";
            System.IO.Directory.CreateDirectory(_storage.SaveDirectory);
            state.LastSaveUtcTicks = DateTime.UtcNow.Ticks;
            System.IO.File.WriteAllText(tmpPath, state.ToJson());
            if (System.IO.File.Exists(target.FullName)) System.IO.File.Delete(target.FullName);
            System.IO.File.Move(tmpPath, target.FullName);
        }

        public bool TryLoad(int slot, out MatchState state)
        {
            state = default;
            var path = slot < 0 ? DefaultSlotPath : SlotPath(slot);
            if (!System.IO.File.Exists(path)) return false;
            try
            {
                state = MatchState.FromJson(System.IO.File.ReadAllText(path));
                return state != null && state.SchemaVersion == 1;
            }
            catch
            {
                return false;    // a corrupt save degrades to "no save", never a crash
            }
        }

        public bool HasAutosave => System.IO.File.Exists(DefaultSlotPath);
    }
}
