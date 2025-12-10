using System.Collections.Concurrent;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading;
using Krypteonx.Core.Models;

namespace Krypteonx.Networking.P2P;

public sealed class P2PServer
{
    // Active transport peers mapped by peer id
    private readonly ConcurrentDictionary<string, IPeer> _peers = new();
    // Known peer metadata (heartbeat state, capabilities, etc.)
    private readonly ConcurrentDictionary<string, PeerInfo> _peerInfo = new();
    private string _localId = string.Empty;
    private ECDsa? _signer;
    private byte[]? _localSpki;
    private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(15);
    private long _seqCounter = 0;
    private string _protocolVersion = "1.0";
    private readonly HashSet<string> _capabilities = new();
    // Dynamic heartbeat delivery reliability toggle
    private volatile bool _reliableHeartbeat = false;

    public IEnumerable<IPeer> Peers => _peers.Values;

    // Register a transport-level peer connection (no handshake yet)
    public void Register(IPeer peer)
    {
        _peers[peer.Id] = peer;
        if (_peerInfo.TryGetValue(peer.Id, out var pi))
        {
            if (!pi.Connected)
            {
                pi.Connected = true;
                PeerConnected?.Invoke(peer.Id);
                ClearReconnectTracking(peer.Id);
            }
        }
    }

    // Register peer identity and mark as connected
    public void RegisterPeer(string peerId, ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        _peerInfo[peerId] = new PeerInfo { PublicKeySpki = subjectPublicKeyInfo.ToArray(), LastHeartbeat = DateTime.UtcNow, Connected = true, LastSeq = 0 };
        PeerConnected?.Invoke(peerId);
    }

    // Configure local node identity used for envelope signing and handshake
    public void ConfigureIdentity(string localId, ReadOnlySpan<byte> subjectPublicKeyInfo, ECDsa signer)
    {
        _localId = localId;
        _localSpki = subjectPublicKeyInfo.ToArray();
        _signer = signer;
    }

    // Events for incoming messages and peer status
    public event Action<Block>? BlockReceived;
    public event Action<Transaction>? TransactionReceived;
    public event Action<Handshake>? HandshakeReceived;
    public event Action<HandshakeAck>? HandshakeAckReceived;
    public event Action<Heartbeat>? HeartbeatReceived;
    public event Action<HeartbeatAck>? HeartbeatAckReceived;
    public event Action<string>? PeerConnected;
    public event Action<string>? PeerDisconnected;
    // Raised when an incoming message is ignored due to unsupported capability
    public event Action<string, MessageKind, string>? UnsupportedMessageReceived;
    // Raised when reconnect attempts exceed the configured maximum
    public event Action<string>? PeerReconnectExhausted;

    public void ReceiveBlock(Block block)
    {
        BlockReceived?.Invoke(block);
    }

    // Broadcast a new block to all connected peers
    public void BroadcastBlock(Block block)
    {
        BlockReceived?.Invoke(block);
        var env = Envelope.FromBlock(block, _localId, DateTime.UtcNow, NextSequence(), _localSpki, Sign);
        // Send only to peers compatible with and agreeing on 'block' capability
        foreach (var p in _peers.Values)
        {
            if (ShouldSendTo(p.Id, "block"))
                SendEnvelopeTo(p.Id, env);
        }
    }

    // Broadcast a new transaction to all connected peers
    public void BroadcastTransaction(Transaction tx)
    {
        TransactionReceived?.Invoke(tx);
        var env = Envelope.FromTransaction(tx, _localId, DateTime.UtcNow, NextSequence(), _localSpki, Sign);
        // Send only to peers compatible with and agreeing on 'tx' capability
        foreach (var p in _peers.Values)
        {
            if (ShouldSendTo(p.Id, "tx"))
                SendEnvelopeTo(p.Id, env);
        }
    }

    // Broadcast handshake containing protocol version and capabilities
    public void BroadcastHandshake(Handshake hs)
    {
        HandshakeReceived?.Invoke(hs);
        var env = Envelope.FromHandshake(hs, _localId, DateTime.UtcNow, NextSequence(), _localSpki, Sign);
        var bytes = SerializeEnvelope(env);
        foreach (var p in _peers.Values)
            p.Send(bytes);
    }

    // Broadcast heartbeat to all peers, track sent timestamp for RTT
    public void BroadcastHeartbeat(Heartbeat hb)
    {
        HeartbeatReceived?.Invoke(hb);
        foreach (var p in _peers.Values)
        {
            if (!ShouldSendTo(p.Id, "heartbeat")) continue;
            var seq = NextSequence();
            var env = Envelope.FromHeartbeat(hb, _localId, DateTime.UtcNow, seq, _localSpki, Sign);
            if (_peerInfo.TryGetValue(p.Id, out var pi))
            {
                pi.PendingHbSentTimes[seq] = DateTime.UtcNow;
                pi.SentHb++;
            }
            SendEnvelopeTo(p.Id, env);
        }
    }

    public byte[] SerializeEnvelope(Envelope env) => JsonSerializer.SerializeToUtf8Bytes(env);
    public Envelope? DeserializeEnvelope(ReadOnlySpan<byte> message) => JsonSerializer.Deserialize<Envelope>(message);

    // Send a single envelope to specific peer
    public void SendEnvelopeTo(string peerId, Envelope env)
    {
        if (_peers.TryGetValue(peerId, out var peer))
        {
            // Prefer transport-specific envelope sending for optimal delivery semantics
            if (peer is Peer ln)
            {
                if (env.Kind == MessageKind.Heartbeat || env.Kind == MessageKind.HeartbeatAck)
                    ln.SendEnvelopeWithReliability(env, _reliableHeartbeat);
                else
                    ln.SendEnvelope(env);
                return;
            }
            var bytes = SerializeEnvelope(env);
            peer.Send(bytes);
        }
    }

    // Configure local protocol version and supported capabilities
    public void ConfigureProtocol(string version, IEnumerable<string> capabilities)
    {
        _protocolVersion = version;
        _capabilities.Clear();
        foreach (var c in capabilities) _capabilities.Add(c);
    }

    public string ProtocolVersion => _protocolVersion;
    public IReadOnlyList<string> Capabilities => _capabilities.ToArray();

    // Receive a raw message, verify signature, update peer state, and dispatch
    public void ReceiveMessage(ReadOnlySpan<byte> message)
    {
        var env = DeserializeEnvelope(message);
        if (env is null) return;
        if (!Verify(env)) return;
        if (string.IsNullOrEmpty(env.SenderId)) return;
        if (!_peerInfo.TryGetValue(env.SenderId, out var info))
        {
            if (env.SubjectPublicKeyInfo is null) return;
            RegisterPeer(env.SenderId, env.SubjectPublicKeyInfo);
            info = _peerInfo[env.SenderId];
            info.LastSeq = env.Sequence;
        }
        else
        {
            if (!info.Connected)
            {
                info.Connected = true;
                PeerConnected?.Invoke(env.SenderId);
                ClearReconnectTracking(env.SenderId);
            }
            if (env.Sequence <= info.LastSeq) return;
            info.LastSeq = env.Sequence;
        }
        // Capability-based accept gating for incoming messages
        var requiredCap = GetCapabilityFor(env.Kind);
        if (requiredCap is not null)
        {
            if (!ShouldAcceptFrom(env.SenderId, requiredCap))
            {
                if (_peerInfo.TryGetValue(env.SenderId, out var piWarn))
                {
                    piWarn.IncrementUnsupported(requiredCap);
                }
                UnsupportedMessageReceived?.Invoke(env.SenderId, env.Kind, requiredCap);
                return;
            }
        }

        switch (env.Kind)
        {
            case MessageKind.Block:
                {
                    var b = JsonSerializer.Deserialize<Block>(env.Payload);
                    if (b is not null) BlockReceived?.Invoke(b);
                    break;
                }
            case MessageKind.Transaction:
                {
                    var t = JsonSerializer.Deserialize<Transaction>(env.Payload);
                    if (t is not null) TransactionReceived?.Invoke(t);
                    break;
                }
            case MessageKind.Handshake:
            {
                // Persist peer's advertised version and capabilities
                var h = JsonSerializer.Deserialize<Handshake>(env.Payload);
                if (h is not null) HandshakeReceived?.Invoke(h);
                if (!string.IsNullOrEmpty(env.SenderId))
                {
                    if (_peerInfo.TryGetValue(env.SenderId, out var pi))
                    {
                        pi.PeerVersion = h?.Version ?? string.Empty;
                        pi.PeerCapabilities = new HashSet<string>(h?.Capabilities ?? Array.Empty<string>());
                        var agreed = new List<string>();
                        foreach (var c in _capabilities) if (pi.PeerCapabilities.Contains(c)) agreed.Add(c);
                        pi.AgreedCapabilities = new HashSet<string>(agreed);
                    }
                    // Build handshake ack using object initializer to avoid init-only reassignment
                    if (_peerInfo.TryGetValue(env.SenderId, out var pi2))
                    {
                        var ok = h is null || h.Version == _protocolVersion;
                        var agreedCapsList = new List<string>();
                        foreach (var c in _capabilities) if (pi2.PeerCapabilities.Contains(c)) agreedCapsList.Add(c);
                        var ack = new HandshakeAck { NodeId = _localId, Ok = ok, Timestamp = DateTime.UtcNow, AgreedVersion = _protocolVersion, Capabilities = agreedCapsList.ToArray() };
                        // Mark compatibility state for broadcast gating
                        pi2.Compatible = ok;
                        var ackEnv = Envelope.FromHandshakeAck(ack, _localId, DateTime.UtcNow, NextSequence(), _localSpki, Sign);
                        SendEnvelopeTo(env.SenderId, ackEnv);
                        // Actively disconnect if protocol versions are incompatible
                        if (!ok)
                        {
                            DisconnectPeer(env.SenderId);
                        }
                    }
                }
                break;
            }
            case MessageKind.Heartbeat:
            {
                // Update heartbeat timestamp and echo back to measure RTT
                var hb = JsonSerializer.Deserialize<Heartbeat>(env.Payload);
                if (hb is not null)
                {
                    HeartbeatReceived?.Invoke(hb);
                    if (_peerInfo.TryGetValue(env.SenderId, out var pi))
                        pi.LastHeartbeat = DateTime.UtcNow;
                    var ack = new HeartbeatAck { PeerId = _localId, Timestamp = DateTime.UtcNow, EchoSequence = env.Sequence };
                    var ackEnv = Envelope.FromHeartbeatAck(ack, _localId, DateTime.UtcNow, NextSequence(), _localSpki, Sign);
                    SendEnvelopeTo(env.SenderId, ackEnv);
                }
                break;
            }
            case MessageKind.HeartbeatAck:
            {
                var ha = JsonSerializer.Deserialize<HeartbeatAck>(env.Payload);
                if (ha is not null)
                {
                    // Notify heartbeat ack and update RTT/loss metrics
                    HeartbeatAckReceived?.Invoke(ha);
                    if (_peerInfo.TryGetValue(env.SenderId, out var pi))
                    {
                        if (pi.PendingHbSentTimes.TryRemove(ha.EchoSequence, out var sentAt))
                        {
                            var rtt = (DateTime.UtcNow - sentAt).TotalMilliseconds;
                            pi.Rtts.Add(rtt);
                            if (pi.Rtts.Count > PeerInfo.RttWindow) pi.Rtts.RemoveAt(0);
                            pi.RecvHbAck++;
                        }
                    }
                }
                break;
            }
            case MessageKind.HandshakeAck:
            {
                // Receive handshake ack from remote
                var ha = JsonSerializer.Deserialize<HandshakeAck>(env.Payload);
                if (ha is not null)
                {
                    if (_peerInfo.TryGetValue(env.SenderId, out var pi)) pi.Compatible = ha.Ok;
                    HandshakeAckReceived?.Invoke(ha);
                    // Actively disconnect if remote reports incompatibility
                    if (!ha.Ok)
                    {
                        DisconnectPeer(env.SenderId);
                    }
                }
                break;
            }
        }
    }

    // Receive message from specific transport peer id, and normalize mapping to sender's logical id
    public void ReceiveMessage(string peerId, ReadOnlySpan<byte> message)
    {
        var env = DeserializeEnvelope(message);
        if (env is not null && !string.IsNullOrEmpty(env.SenderId) && env.SenderId != peerId)
        {
            if (_peers.TryGetValue(peerId, out var transportPeer))
            {
                _peers[env.SenderId] = transportPeer;
                _peers.TryRemove(peerId, out _);
                // Persist transport endpoint for reconnect
                UpdatePeerRemoteEndpoint(env.SenderId, peerId);
            }
        }
        ReceiveMessage(message);
    }

    public sealed class LoopbackPeer : IPeer
    {
        private readonly P2PServer _server;
        public string Id { get; }
        public LoopbackPeer(string id, P2PServer server)
        {
            Id = id;
            _server = server;
        }
        public void Send(ReadOnlySpan<byte> message) => _server.ReceiveMessage(Id, message);
    }

    public sealed class Envelope
    {
        public required MessageKind Kind { get; init; }
        public required byte[] Payload { get; init; }
        public required string SenderId { get; init; }
        public required DateTime Timestamp { get; init; }
        public required long Sequence { get; init; }
        public byte[]? Signature { get; set; }
        public byte[]? SubjectPublicKeyInfo { get; init; }
        public static Envelope FromBlock(Block b, string senderId, DateTime ts, long seq, byte[]? spki, Func<Envelope, byte[]?> signer)
        {
            var env = new Envelope { Kind = MessageKind.Block, Payload = JsonSerializer.SerializeToUtf8Bytes(b), SenderId = senderId, Timestamp = ts, Sequence = seq, SubjectPublicKeyInfo = spki };
            env.Signature = signer(env);
            return env;
        }
        public static Envelope FromTransaction(Transaction t, string senderId, DateTime ts, long seq, byte[]? spki, Func<Envelope, byte[]?> signer)
        {
            var env = new Envelope { Kind = MessageKind.Transaction, Payload = JsonSerializer.SerializeToUtf8Bytes(t), SenderId = senderId, Timestamp = ts, Sequence = seq, SubjectPublicKeyInfo = spki };
            env.Signature = signer(env);
            return env;
        }
        public static Envelope FromHandshake(Handshake h, string senderId, DateTime ts, long seq, byte[]? spki, Func<Envelope, byte[]?> signer)
        {
            var env = new Envelope { Kind = MessageKind.Handshake, Payload = JsonSerializer.SerializeToUtf8Bytes(h), SenderId = senderId, Timestamp = ts, Sequence = seq, SubjectPublicKeyInfo = spki };
            env.Signature = signer(env);
            return env;
        }
        public static Envelope FromHeartbeat(Heartbeat h, string senderId, DateTime ts, long seq, byte[]? spki, Func<Envelope, byte[]?> signer)
        {
            var env = new Envelope { Kind = MessageKind.Heartbeat, Payload = JsonSerializer.SerializeToUtf8Bytes(h), SenderId = senderId, Timestamp = ts, Sequence = seq, SubjectPublicKeyInfo = spki };
            env.Signature = signer(env);
            return env;
        }
        public static Envelope FromHeartbeatAck(HeartbeatAck ha, string senderId, DateTime ts, long seq, byte[]? spki, Func<Envelope, byte[]?> signer)
        {
            var env = new Envelope { Kind = MessageKind.HeartbeatAck, Payload = JsonSerializer.SerializeToUtf8Bytes(ha), SenderId = senderId, Timestamp = ts, Sequence = seq, SubjectPublicKeyInfo = spki };
            env.Signature = signer(env);
            return env;
        }
        public static Envelope FromHandshakeAck(HandshakeAck ha, string senderId, DateTime ts, long seq, byte[]? spki, Func<Envelope, byte[]?> signer)
        {
            var env = new Envelope { Kind = MessageKind.HandshakeAck, Payload = JsonSerializer.SerializeToUtf8Bytes(ha), SenderId = senderId, Timestamp = ts, Sequence = seq, SubjectPublicKeyInfo = spki };
            env.Signature = signer(env);
            return env;
        }
    }

    public enum MessageKind
    {
        Block,
        Transaction,
        Handshake,
        Heartbeat,
        HeartbeatAck,
        HandshakeAck
    }

    public sealed class Handshake
    {
        public required string NodeId { get; init; }
        public required string NetworkName { get; init; }
        public required string Version { get; init; }
        public string[]? Capabilities { get; init; }
    }

    public sealed class Heartbeat
    {
        public required string PeerId { get; init; }
        public required DateTime Timestamp { get; init; }
    }

    public sealed class HandshakeAck
    {
        public required string NodeId { get; init; }
        public required bool Ok { get; init; }
        public required DateTime Timestamp { get; init; }
        public string? AgreedVersion { get; init; }
        public string[]? Capabilities { get; init; }
    }

    public sealed class HeartbeatAck
    {
        public required string PeerId { get; init; }
        public required DateTime Timestamp { get; init; }
        public required long EchoSequence { get; init; }
    }

    private sealed class PeerInfo
    {
        public byte[]? PublicKeySpki { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public bool Connected { get; set; }
        public long LastSeq { get; set; }
        public int SentHb { get; set; }
        public int RecvHbAck { get; set; }
        public static int RttWindow => 10;
        public List<double> Rtts { get; } = new();
        public ConcurrentDictionary<long, DateTime> PendingHbSentTimes { get; } = new();
        public string PeerVersion { get; set; } = string.Empty;
        public HashSet<string> PeerCapabilities { get; set; } = new();
        public HashSet<string> AgreedCapabilities { get; set; } = new();
        // Compatibility reflects protocol version match for safe communication
        public bool Compatible { get; set; } = true;
        // Per-capability counters for unsupported incoming messages
        public ConcurrentDictionary<string, int> UnsupportedCounts { get; } = new();
        public void IncrementUnsupported(string cap) => UnsupportedCounts.AddOrUpdate(cap, 1, (_, v) => v + 1);
        // Last known transport endpoint (e.g., udp://host:port)
        public string RemoteEndpoint { get; set; } = string.Empty;
        // Cumulative reconnect failure count
        public int ReconnectFailures { get; set; }
        // Disabled after reconnect attempts exhausted
        public bool Disabled { get; set; }
        public DateTime? DisabledUntil { get; set; }
    }

    // Sign an envelope using local identity
    private byte[]? Sign(Envelope env)
    {
        if (_signer is null) return null;
        var data = JsonSerializer.SerializeToUtf8Bytes(new { env.Kind, env.SenderId, env.Timestamp, env.Sequence, env.Payload });
        return _signer.SignData(data, HashAlgorithmName.SHA256);
    }

    // Verify envelope signature against known or provided SPKI
    private bool Verify(Envelope env)
    {
        if (env.Signature is null) return false;
        byte[]? spki = null;
        if (!string.IsNullOrEmpty(env.SenderId) && _peerInfo.TryGetValue(env.SenderId, out var info))
            spki = info.PublicKeySpki;
        if (spki is null) spki = env.SubjectPublicKeyInfo;
        if (spki is null) return false;
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
            var data = JsonSerializer.SerializeToUtf8Bytes(new { env.Kind, env.SenderId, env.Timestamp, env.Sequence, env.Payload });
            return ecdsa.VerifyData(data, env.Signature, HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }

    // Disconnect peers that have not heartbeated within timeout
    public void CheckPeerTimeouts()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _peerInfo)
        {
            var id = kv.Key;
            var info = kv.Value;
            if (!info.Connected) continue;
            if (now - info.LastHeartbeat > _heartbeatTimeout)
            {
                info.Connected = false;
                _peers.TryRemove(id, out _);
                PeerDisconnected?.Invoke(id);
            }
        }
        // Process scheduled reconnect attempts
        foreach (var kv in _pendingReconnects)
        {
            var peerId = kv.Key;
            var dueAt = kv.Value;
            if (now >= dueAt)
            {
                // If still not connected, request reconnect and reschedule
                if (!_peerInfo.TryGetValue(peerId, out var pi) || !pi.Connected)
                {
                    PeerReconnectRequested?.Invoke(peerId);
                    var attempts = _reconnectAttempts.TryGetValue(peerId, out var a) ? a : 0;
                    var interval = _reconnectIntervals.TryGetValue(peerId, out var iv) ? iv : _reconnectBaseInterval;
                    attempts++;
                    if (attempts >= _reconnectMaxAttempts)
                    {
                    // Stop scheduling further attempts
                    _pendingReconnects.TryRemove(peerId, out _);
                    if (_peerInfo.TryGetValue(peerId, out var pi2)) pi2.Disabled = true;
                    if (_peerInfo.TryGetValue(peerId, out var pi3)) pi3.DisabledUntil = now + _reconnectBlacklistInterval;
                    PeerReconnectExhausted?.Invoke(peerId);
                    continue;
                }
                    // Exponential backoff with cap
                    var nextInterval = interval + interval; // double
                    if (nextInterval > _reconnectMaxInterval) nextInterval = _reconnectMaxInterval;
                    _reconnectAttempts[peerId] = attempts;
                    _reconnectIntervals[peerId] = nextInterval;
                    _pendingReconnects[peerId] = now + nextInterval;
                }
                else
                {
                    // Clear tracking upon successful connection
                    ClearReconnectTracking(peerId);
                }
            }
        }
    }

    // Number of currently connected peers
    public int ConnectedPeerCount
    {
        get
        {
            var c = 0;
            foreach (var kv in _peerInfo.Values)
                if (kv.Connected) c++;
            return c;
        }
    }

    // Average RTT in milliseconds over the sliding window
    public double GetAverageRttMs(string peerId)
    {
        if (!_peerInfo.TryGetValue(peerId, out var pi) || pi.Rtts.Count == 0) return 0;
        double sum = 0; foreach (var v in pi.Rtts) sum += v; return sum / pi.Rtts.Count;
    }

    // Heartbeat loss rate estimated as 1 - (acks/sent)
    public double GetHeartbeatLossRate(string peerId)
    {
        if (!_peerInfo.TryGetValue(peerId, out var pi)) return 0;
        if (pi.SentHb == 0) return 0;
        return 1.0 - (double)pi.RecvHbAck / pi.SentHb;
    }

    // Snapshot unsupported message counters by capability for a peer
    public IReadOnlyDictionary<string, int> GetUnsupportedCounts(string peerId)
    {
        if (!_peerInfo.TryGetValue(peerId, out var pi)) return new Dictionary<string, int>();
        return new Dictionary<string, int>(pi.UnsupportedCounts);
    }

    // Update last known transport endpoint for a logical peer
    public void UpdatePeerRemoteEndpoint(string peerId, string remoteEndpoint)
    {
        if (!_peerInfo.TryGetValue(peerId, out var pi))
        {
            pi = new PeerInfo { Connected = false };
            _peerInfo[peerId] = pi;
        }
        pi.RemoteEndpoint = remoteEndpoint;
    }

    // Get last known transport endpoint for reconnect
    public string? GetRemoteEndpoint(string peerId)
    {
        if (_peerInfo.TryGetValue(peerId, out var pi) && !string.IsNullOrEmpty(pi.RemoteEndpoint)) return pi.RemoteEndpoint;
        return null;
    }

    // Toggle heartbeat delivery reliability at runtime
    public void SetHeartbeatReliability(bool reliable)
    {
        _reliableHeartbeat = reliable;
    }

    // Decide if a message with a given capability should be sent to peer
    private bool ShouldSendTo(string peerId, string capability)
    {
        if (!_peerInfo.TryGetValue(peerId, out var pi)) return true; // no info, allow by default
        if (!pi.Connected) return false;
        if (!pi.Compatible) return false;
        if (pi.AgreedCapabilities.Count == 0) return true; // no negotiated set yet
        return pi.AgreedCapabilities.Contains(capability);
    }

    // Decide if we should accept an incoming message from a peer
    private bool ShouldAcceptFrom(string peerId, string capability)
    {
        if (!_peerInfo.TryGetValue(peerId, out var pi)) return false; // unknown identity cannot be verified
        if (!pi.Connected) return false;
        if (!pi.Compatible) return false;
        if (pi.AgreedCapabilities.Count == 0) return true; // accept until negotiation completes
        return pi.AgreedCapabilities.Contains(capability);
    }

    // Map message kind to capability label for gating
    private static string? GetCapabilityFor(MessageKind kind)
    {
        return kind switch
        {
            MessageKind.Block => "block",
            MessageKind.Transaction => "tx",
            MessageKind.Heartbeat => "heartbeat",
            MessageKind.HeartbeatAck => "heartbeat",
            MessageKind.Handshake => null,
            MessageKind.HandshakeAck => null,
            _ => null
        };
    }

    // Disconnect a peer and emit PeerDisconnected
    private void DisconnectPeer(string peerId)
    {
        if (_peerInfo.TryGetValue(peerId, out var pi))
        {
            pi.Connected = false;
            // Track reconnect failures
            pi.ReconnectFailures++;
        }
        _peers.TryRemove(peerId, out _);
        PeerDisconnected?.Invoke(peerId);
        // Schedule reconnect attempts if enabled
        if (_enableReconnectOnDisconnect)
        {
            var now = DateTime.UtcNow;
            if (_peerInfo.TryGetValue(peerId, out var pi2) && pi2.Disabled && pi2.DisabledUntil.HasValue && now < pi2.DisabledUntil.Value)
            {
                // Blacklisted: skip scheduling until blacklist expires
                return;
            }
            if (_peerInfo.TryGetValue(peerId, out var pi3) && pi3.Disabled && pi3.DisabledUntil.HasValue && now >= pi3.DisabledUntil.Value)
            {
                // Blacklist expired: allow reconnect attempts again
                pi3.Disabled = false;
                pi3.DisabledUntil = null;
            }
            _reconnectAttempts[peerId] = 0;
            _reconnectIntervals[peerId] = _reconnectBaseInterval;
            _pendingReconnects[peerId] = now + _reconnectBaseInterval;
            PeerReconnectRequested?.Invoke(peerId);
        }
    }

    // Notify transport-level disconnection
    public void OnTransportDisconnected(string peerId)
    {
        if (_peerInfo.TryGetValue(peerId, out var pi))
        {
            pi.Connected = false;
        }
        _peers.TryRemove(peerId, out _);
        PeerDisconnected?.Invoke(peerId);
    }

    // Reconnect settings and tracking
    private readonly ConcurrentDictionary<string, DateTime> _pendingReconnects = new();
    private readonly ConcurrentDictionary<string, int> _reconnectAttempts = new();
    private readonly ConcurrentDictionary<string, TimeSpan> _reconnectIntervals = new();
    private TimeSpan _reconnectBaseInterval = TimeSpan.FromSeconds(30);
    private TimeSpan _reconnectMaxInterval = TimeSpan.FromMinutes(5);
    private int _reconnectMaxAttempts = 10;
    private TimeSpan _reconnectBlacklistInterval = TimeSpan.FromMinutes(10);
    private bool _enableReconnectOnDisconnect = true;
    public event Action<string>? PeerReconnectRequested;
    // Configure reconnect behavior (enable flag and interval)
    public void ConfigureReconnect(bool enable, TimeSpan? baseInterval = null, TimeSpan? maxInterval = null, int? maxAttempts = null, TimeSpan? blacklistInterval = null)
    {
        _enableReconnectOnDisconnect = enable;
        if (baseInterval is not null) _reconnectBaseInterval = baseInterval.Value;
        if (maxInterval is not null) _reconnectMaxInterval = maxInterval.Value;
        if (maxAttempts is not null) _reconnectMaxAttempts = Math.Max(1, maxAttempts.Value);
        if (blacklistInterval is not null) _reconnectBlacklistInterval = blacklistInterval.Value;
    }

    // Clear reconnect tracking data for peer
    private void ClearReconnectTracking(string peerId)
    {
        _pendingReconnects.TryRemove(peerId, out _);
        _reconnectAttempts.TryRemove(peerId, out _);
        _reconnectIntervals.TryRemove(peerId, out _);
    }

    private long NextSequence() => Interlocked.Increment(ref _seqCounter);
}
