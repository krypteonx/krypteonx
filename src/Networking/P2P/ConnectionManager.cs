using System.Collections.Concurrent;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Krypteonx.Networking.P2P;

public sealed class ConnectionManager
{
    private readonly P2PServer _p2p;
    private readonly EventBasedNetListener _listener;
    private readonly NetManager _net;
    private readonly ConcurrentDictionary<NetPeer, Peer> _peerMap = new();
    private string _connectionKey = "Krypteonx";
    private readonly ConcurrentDictionary<string, DateTime> _seedAttemptDue = new();
    private readonly ConcurrentDictionary<string, DateTime> _seedRetryDue = new();
    private readonly ConcurrentDictionary<string, int> _seedFailures = new();
    private readonly ConcurrentDictionary<string, DateTime> _seedBlacklistUntil = new();
    private TimeSpan _seedConnectTimeout = TimeSpan.FromSeconds(3);
    private TimeSpan _seedBaseBackoff = TimeSpan.FromSeconds(5);
    private TimeSpan _seedMaxBackoff = TimeSpan.FromMinutes(1);
    private int _seedMaxFailuresBeforeBlacklist = 5;
    private int _seedMaxConcurrency = 2;
    public event Action<string, int, TimeSpan>? SeedConnectFailed;

    public ConnectionManager(P2PServer p2p)
    {
        _p2p = p2p;
        _listener = new EventBasedNetListener();
        _net = new NetManager(_listener);

        _listener.ConnectionRequestEvent += request =>
        {
            if (_net.ConnectedPeersCount < 1024)
                request.AcceptIfKey(_connectionKey);
            else
                request.Reject();
        };

        _listener.PeerConnectedEvent += peer =>
        {
            var lp = new Peer(peer);
            _peerMap[peer] = lp;
            _p2p.Register(lp);
            _p2p.UpdatePeerRemoteEndpoint(lp.Id, lp.Id);
            _seedAttemptDue.TryRemove(lp.Id, out _);
            _seedFailures.TryRemove(lp.Id, out _);
            _seedBlacklistUntil.TryRemove(lp.Id, out _);
        };

        _listener.PeerDisconnectedEvent += (peer, info) =>
        {
            if (_peerMap.TryRemove(peer, out var lp))
            {
                _p2p.OnTransportDisconnected(lp.Id);
            }
        };

        _listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod, channel) =>
        {
            if (_peerMap.TryGetValue(fromPeer, out var lp))
            {
                var data = dataReader.GetRemainingBytes();
                _p2p.ReceiveMessage(lp.Id, data);
            }
            dataReader.Recycle();
        };
    }

    public void StartServer(int port, string connectionKey)
    {
        _connectionKey = connectionKey;
        _net.Start(port);
    }

    public void ConnectTo(string host, int port, string connectionKey)
    {
        _connectionKey = connectionKey;
        if (!_net.IsRunning)
            _net.Start();
        var endpointId = $"udp://{host}:{port}";
        if (_seedBlacklistUntil.TryGetValue(endpointId, out var until) && DateTime.UtcNow < until)
        {
            return;
        }
        var now = DateTime.UtcNow;
        var inFlight = 0;
        foreach (var kv in _seedAttemptDue)
        {
            if (kv.Value > now) inFlight++;
        }
        if (inFlight >= _seedMaxConcurrency)
        {
            _seedRetryDue[endpointId] = now;
            return;
        }
        try
        {
            _net.Connect(host, port, connectionKey);
            _seedAttemptDue[endpointId] = now + _seedConnectTimeout;
        }
        catch
        {
            var f = _seedFailures.TryGetValue(endpointId, out var v) ? v + 1 : 1;
            _seedFailures[endpointId] = f;
            var backoff = _seedBaseBackoff;
            if (f > 1)
            {
                for (int i = 1; i < f; i++) backoff += backoff;
                if (backoff > _seedMaxBackoff) backoff = _seedMaxBackoff;
            }
            if (f >= _seedMaxFailuresBeforeBlacklist)
            {
                _seedBlacklistUntil[endpointId] = DateTime.UtcNow + _seedMaxBackoff;
            }
            SeedConnectFailed?.Invoke(endpointId, f, backoff);
            _seedRetryDue[endpointId] = now + backoff;
        }
    }

    public void Poll()
    {
        _net.PollEvents();
        foreach (var kv in _seedAttemptDue)
        {
            var endpointId = kv.Key;
            var due = kv.Value;
            if (DateTime.UtcNow >= due)
            {
                var connected = false;
                foreach (var lp in _peerMap.Values)
                {
                    if (lp.Id == endpointId) { connected = true; break; }
                }
                if (!connected)
                {
                    var f = _seedFailures.TryGetValue(endpointId, out var v) ? v + 1 : 1;
                    _seedFailures[endpointId] = f;
                    var backoff = _seedBaseBackoff;
                    if (f > 1)
                    {
                        for (int i = 1; i < f; i++) backoff += backoff;
                        if (backoff > _seedMaxBackoff) backoff = _seedMaxBackoff;
                    }
                    SeedConnectFailed?.Invoke(endpointId, f, backoff);
                    if (f >= _seedMaxFailuresBeforeBlacklist)
                    {
                        _seedBlacklistUntil[endpointId] = DateTime.UtcNow + _seedMaxBackoff;
                        _seedAttemptDue.TryRemove(endpointId, out _);
                        _seedRetryDue.TryRemove(endpointId, out _);
                        continue;
                    }
                    var backoffTimeout = _seedBaseBackoff;
                    if (f > 1)
                    {
                        for (int i = 1; i < f; i++) backoffTimeout += backoffTimeout;
                        if (backoffTimeout > _seedMaxBackoff) backoffTimeout = _seedMaxBackoff;
                    }
                    _seedAttemptDue.TryRemove(endpointId, out _);
                    _seedRetryDue[endpointId] = DateTime.UtcNow + backoffTimeout;
                }
                else
                {
                    _seedAttemptDue.TryRemove(endpointId, out _);
                    _seedFailures.TryRemove(endpointId, out _);
                }
            }
        }
        var now2 = DateTime.UtcNow;
        var inFlight2 = 0;
        foreach (var kv in _seedAttemptDue)
        {
            if (kv.Value > now2) inFlight2++;
        }
        foreach (var kv in _seedRetryDue)
        {
            if (inFlight2 >= _seedMaxConcurrency) break;
            var endpointId = kv.Key;
            var due = kv.Value;
            if (now2 >= due)
            {
                if (_seedBlacklistUntil.TryGetValue(endpointId, out var until) && now2 < until) continue;
                var id = endpointId;
                var s = id.Substring("udp://".Length);
                var parts = s.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out var port))
                {
                    try
                    {
                        _net.Connect(parts[0], port, _connectionKey);
                        _seedAttemptDue[endpointId] = now2 + _seedConnectTimeout;
                        _seedRetryDue.TryRemove(endpointId, out _);
                        inFlight2++;
                    }
                    catch
                    {
                        var f2 = _seedFailures.TryGetValue(endpointId, out var v2) ? v2 : 0;
                        var backoff2 = _seedBaseBackoff;
                        if (f2 > 1)
                        {
                            for (int i = 1; i < f2; i++) backoff2 += backoff2;
                            if (backoff2 > _seedMaxBackoff) backoff2 = _seedMaxBackoff;
                        }
                        SeedConnectFailed?.Invoke(endpointId, f2, backoff2);
                    }
                }
            }
        }
    }

    public void ConfigureSeedConnect(TimeSpan? timeout = null, TimeSpan? baseBackoff = null, TimeSpan? maxBackoff = null, int? maxFailuresBeforeBlacklist = null, int? maxConcurrency = null)
    {
        if (timeout is not null) _seedConnectTimeout = timeout.Value;
        if (baseBackoff is not null) _seedBaseBackoff = baseBackoff.Value;
        if (maxBackoff is not null) _seedMaxBackoff = maxBackoff.Value;
        if (maxFailuresBeforeBlacklist is not null) _seedMaxFailuresBeforeBlacklist = Math.Max(1, maxFailuresBeforeBlacklist.Value);
        if (maxConcurrency is not null) _seedMaxConcurrency = Math.Max(1, maxConcurrency.Value);
    }

    public void Stop()
    {
        _net.Stop();
    }
}
