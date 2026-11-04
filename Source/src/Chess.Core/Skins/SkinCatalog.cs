using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Chess.Core
{
    /// <summary>Named slot for each 3D piece's texture set and tint choices.</summary>
    public static class SkinSlot
    {
        public static readonly string[] Pieces =
        {
            "wpawn", "wknight", "wbishop", "wrook", "wqueen", "wking",
            "bpawn", "bknight", "bbishop", "brook", "bqueen", "bking"
        };
        public const string BoardLight = "board_light";
        public const string BoardDark = "board_dark";
        public const string BoardFrame = "board_frame";
    }

    /// <summary>Manifest entry for one skin texture file.</summary>
    public sealed class SkinEntry
    {
        public string Slot { get; set; }
        public string File { get; set; }
        public string Sha256 { get; set; }        // content-addressed cache validation
        public int WidthPx { get; set; }
        public int HeightPx { get; set; }
    }

    /// <summary>A skin bundle: metadata plus its texture files, either zipped into a
    /// folder in local storage or served from a web catalog.</summary>
    public sealed class SkinManifest
    {
        public string Name { get; set; }
        public string Author { get; set; } = "";
        public int SchemaVersion { get; set; } = 1;
        public List<SkinEntry> Entries { get; set; } = new List<SkinEntry>();

        public const string FileName = "skin.json";

        public string ToJson() => JsonSerializer.Serialize(this);

        public static SkinManifest Parse(string json) => JsonSerializer.Deserialize<SkinManifest>(json);

        public SkinEntry FindSlot(string slot)
        {
            foreach (var e in Entries) if (e.Slot == slot) return e;
            return null;
        }

        /// <summary>Every slot the renderer will ask for; used to flag missing files.</summary>
        public IEnumerable<string> RequiredSlots
        {
            get
            {
                foreach (var s in SkinSlot.Pieces) yield return s;
                yield return SkinSlot.BoardLight;
                yield return SkinSlot.BoardDark;
                yield return SkinSlot.BoardFrame;
            }
        }

        public List<string> MissingSlots()
        {
            var missing = new List<string>();
            foreach (var slot in RequiredSlots)
                if (FindSlot(slot) == null) missing.Add(slot);
            return missing;
        }
    }
}
