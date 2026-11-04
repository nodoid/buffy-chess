using System;
using System.Buffers.Binary;
using System.Text;

namespace Chess.Core.Network
{
    /// <summary>Message type tags on the wire. Kept stable: a client and a server
    /// many versions apart must still be able to play the same move.</summary>
    public enum Msg : byte
    {
        Hello = 1,          // identify + capabilities
        Welcome = 2,        // server's reply; assigns seat colour + room id
        JoinRoom = 3,
        LeaveRoom = 4,
        MoveMade = 5,       // the payload: one UCI move + counters
        MoveRejected = 6,
        ClockTick = 7,
        Resign = 8,
        GameOver = 9,
        Ping = 10,
        Pong = 11
    }

    /// <summary>A decoded message. Everything is little-endian on the wire.</summary>
    public struct Message
    {
        public Msg Kind;
        public int Sequence;        // sender-incremented, echoed in MoveRejected
        public int RoomId;
        public int Int1;            // kind-specific: e.g. clock remaining for White
        public int Int2;            // kind-specific: e.g. clock remaining for Black
        public int Int3;
        public string Text;         // kind-specific: e.g. UCI move "e2e4", reason

        public override string ToString()
            => $"{Kind} seq={Sequence} room={RoomId} ints=({Int1},{Int2},{Int3}) text={Text ?? "(none)"}";
    }

    /// <summary>
    /// Hand-rolled compact wire codec. Protocol Buffers would work too (chapter 9
    /// shows the schema side by side); this codec has zero dependencies - which
    /// matters when the same bytes must parse identically on a Windows desktop,
    /// an Android API 32+ handset and an iOS 18+ phone.
    /// </summary>
    public static class MessageCodec
    {
        // Header: [u16 length][u8 kind][i16 seq][i16 room][i32 x3][u8 textLength][text]
        private const int MinHeaderBytes = 2 + 1 + 2 + 2;

        public static byte[] Encode(in Message m)
        {
            byte[] text = Encoding.UTF8.GetBytes(m.Text ?? "");
            if (text.Length > 255) text = new byte[255];    // protocol hard limit

            int bodyLen = 1 + 2 + 2 + 12 + 1 + text.Length;
            var buffer = new byte[2 + bodyLen];

            PutU16(buffer, 0, bodyLen);                     // frame length prefix
            buffer[2] = (byte)m.Kind;
            PutU16(buffer, 3, m.Sequence & 0xFFFF);
            PutU16(buffer, 5, m.RoomId & 0xFFFF);
            PutU32(buffer, 7, (uint)m.Int1);
            PutU32(buffer, 11, (uint)m.Int2);
            PutU32(buffer, 15, (uint)m.Int3);
            buffer[19] = (byte)text.Length;
            Array.Copy(text, 0, buffer, 20, text.Length);
            return buffer;
        }

        /// <summary>Decodes one complete frame. The caller is responsible for reassembly
        /// when(tcp-style) partial frames straddle receive boundaries.</summary>
        public static bool TryDecode(ReadOnlySpan<byte> frame, out Message m)
        {
            m = default;
            if (frame.Length < MinHeaderBytes + 1) return false;
            int bodyLen = GetU16(frame, 0);
            if (frame.Length < 2 + bodyLen) return false;

            m.Kind = (Msg)frame[2];
            m.Sequence = GetU16(frame, 3) - (GetU16(frame, 3) >= 0x8000 ? 0x10000 : 0);
            m.RoomId = GetU16(frame, 5) - (GetU16(frame, 5) >= 0x8000 ? 0x10000 : 0);
            m.Int1 = unchecked((int)GetU32(frame, 7));
            m.Int2 = unchecked((int)GetU32(frame, 11));
            m.Int3 = unchecked((int)GetU32(frame, 15));
            int textLen = frame[19];
            m.Text = Encoding.UTF8.GetString(frame.ToArray(), 20, textLen);
            return true;
        }

        // explicit little-endian byte packing - one source of truth for the wire layout,
        // identical on every endianness every target platform pretends to be
        private static void PutU16(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        private static void PutU32(byte[] b, int o, uint v)
        { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
        private static int GetU16(ReadOnlySpan<byte> b, int o) => b[o] | (b[o + 1] << 8);
        private static uint GetU32(ReadOnlySpan<byte> b, int o)
            => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    }
}
