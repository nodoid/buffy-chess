using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Chess.Core.Network
{
    /// <summary>
    /// One bidirectional game channel. Implementations live per transport:
    /// the real WebSocket client, and the deterministic loopback/simulator used
    /// by the automated tests in chapter 9.
    /// </summary>
    public interface ITransport
    {
        bool IsConnected { get; }
        Task ConnectAsync(Uri endpoint, CancellationToken token);
        Task SendAsync(Message message, CancellationToken token);
        Task<Message> ReceiveAsync(CancellationToken token);
        void Close();
        event EventHandler Disconnected;
    }

    /// <summary>
    /// The production transport. WebSocket is the one real-time framing protocol
    /// supported natively by Windows 11, Android API 32+ and iOS 18+ with zero
    /// native dependencies - which is exactly why the game standardised on it.
    /// </summary>
    public sealed class WebSocketTransport : ITransport
    {
        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly byte[] _receiveBuffer = new byte[4096];

        public bool IsConnected => _socket.State == WebSocketState.Open;
        public event EventHandler Disconnected;

        public async Task ConnectAsync(Uri endpoint, CancellationToken token)
        {
            await _socket.ConnectAsync(endpoint, token);
        }

        public async Task SendAsync(Message message, CancellationToken token)
        {
            var frame = MessageCodec.Encode(message);
            await _socket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, token);
        }

        public async Task<Message> ReceiveAsync(CancellationToken token)
        {
            int filled = 0;
            while (true)
            {
                if (filled >= _receiveBuffer.Length) Close();
                var result = await _socket.ReceiveAsync(new ArraySegment<byte>(_receiveBuffer, filled, _receiveBuffer.Length - filled), token);
                if (result.Count == 0 || result.MessageType == WebSocketMessageType.Close)
                {
                    Disconnected?.Invoke(this, EventArgs.Empty);
                    throw new InvalidOperationException("socket closed");
                }
                filled += result.Count;
                // Frame-complete? The codec is self-describing via its length prefix,
                // which is LITTLE-endian like every other field (see PutU16). Reading
                // it big-endian here hung the first real socket test: a 20-byte frame
                // announced itself as 5120 bytes and the receive loop waited forever.
                if (filled >= 2)
                {
                    int bodyLen = _receiveBuffer[0] | (_receiveBuffer[1] << 8);
                    if (filled >= bodyLen + 2)
                    {
                        var frame = new byte[filled];
                        Array.Copy(_receiveBuffer, frame, filled);
                        if (!MessageCodec.TryDecode(frame, out var message)) throw new InvalidOperationException("bad frame");
                        return message;
                    }
                }
            }
        }

        public void Close()
        {
            try { _socket.Abort(); } catch { }
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Deterministic loopback transport: two endpoints exchange messages in
    /// memory with configurable packet loss and an optional hard disconnect at
    /// a chosen sequence number. This is the mock network layer the automated
    /// tests replay against the same session state machine on every platform.
    /// </summary>
    public sealed class NetworkSimulator
    {
        public double DropProbability = 0.0;
        public int DisconnectAfterSequence = -1;

        public readonly SimEndpoint SideA;
        public readonly SimEndpoint SideB;

        public NetworkSimulator(int seed = 1234)
        {
            // The seed reaches both endpoints so a test that runs twice drops the
            // same messages twice; a simulator that ignores it is a flaky test suite
            // with a deterministic-looking constructor.
            SideA = new SimEndpoint(this, seed);
            SideB = new SimEndpoint(this, seed + 1);
            SideA.Peer = SideB;
            SideB.Peer = SideA;
        }

        public sealed class SimEndpoint : ITransport
        {
            public SimEndpoint Peer { get; internal set; }
            private readonly ConcurrentQueue<Message> _inbox = new ConcurrentQueue<Message>();
            private readonly Random _rng;
            private int _sentSequences;
            private readonly NetworkSimulator _sim;

            internal SimEndpoint(NetworkSimulator sim, int seed)
            {
                _sim = sim;
                _rng = new Random(seed);
            }

            public bool IsConnected => _connected;
            private bool _connected;
            public event EventHandler Disconnected;

            public Task ConnectAsync(Uri endpoint, CancellationToken token)
            {
                _connected = true;
                return Task.CompletedTask;
            }

            public Task SendAsync(Message message, CancellationToken token)
            {
                if (!_connected) throw new InvalidOperationException("sim endpoint not connected");
                int seq = message.Sequence == 0 ? ++_sentSequences : message.Sequence;

                if (_sim.DisconnectAfterSequence > 0 && seq == _sim.DisconnectAfterSequence)
                {
                    HardDisconnect();
                    throw new InvalidOperationException("simulated drop");
                }
                if (_rng.NextDouble() < _sim.DropProbability)
                    return Task.CompletedTask;   // silently dropped, as if never sent

                var delivered = message;
                delivered.Sequence = seq;
                Peer._inbox.Enqueue(delivered);
                return Task.CompletedTask;
            }

            /// <summary>Waits for the next message. Written as a true async method so
            /// that every failure - cancellation, disconnect - surfaces as a faulted or
            /// cancelled *task*. A synchronous throw here would make ITransport's
            /// contract differ between transports, and a deadline in a test would
            /// arrive as an exception from the call rather than from the await.</summary>
            public async Task<Message> ReceiveAsync(CancellationToken token)
            {
                while (true)
                {
                    if (_inbox.TryDequeue(out var m)) return m;
                    if (!_connected) throw new InvalidOperationException("closed");
                    await Task.Delay(1, token).ConfigureAwait(false);
                }
            }

            public void HardDisconnect()
            {
                _connected = false;
                Peer._connected = false;
                Disconnected?.Invoke(this, EventArgs.Empty);
                Peer.Disconnected?.Invoke(this, EventArgs.Empty);
            }

            public void Close() => HardDisconnect();
        }
    }
}
