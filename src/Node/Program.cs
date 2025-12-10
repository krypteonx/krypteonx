using Krypteonx.Core.Config;
using Krypteonx.Core.Services;
using Krypteonx.Execution.Bridge;
using Krypteonx.Execution.DualState;
using Krypteonx.Execution.Privacy;
using Krypteonx.Networking.P2P;
using Krypteonx.Consensus.GhostDag;
using Krypteonx.Core.Models;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Security.Cryptography;

// Node bootstrap and component wiring
Console.WriteLine($"Starting {ChainParameters.NetworkName} node with block target {ChainParameters.BlockTargetTime.TotalSeconds}s");

var mempool = new Mempool();
var ledger = new DualStateLedger();
var shield = new ShieldService(ledger.Public, ledger.Private);
var verifier = new NoopPrivateVerifier();
// Initialize P2P server and attach a loopback peer for local testing
var p2p = new P2PServer();
var loop = new P2PServer.LoopbackPeer("local", p2p);
p2p.Register(loop);
using var nodeSigner = ECDsa.Create();
var nodeSpki = nodeSigner.ExportSubjectPublicKeyInfo();
p2p.ConfigureIdentity("local", nodeSpki, nodeSigner); // identity used to sign envelopes
p2p.ConfigureProtocol("1.0", new[] { "block", "tx", "heartbeat" }); // protocol version and capabilities
// Start LiteNetLib connection manager: listen and connect (demo)
var conn = new ConnectionManager(p2p);
// Read connection parameters from environment
var portEnv = Environment.GetEnvironmentVariable("KRY_PORT");
var keyEnv = Environment.GetEnvironmentVariable("KRY_KEY") ?? "Krypteonx";
var port = 9050;
if (!string.IsNullOrEmpty(portEnv) && int.TryParse(portEnv, out var parsedPort)) port = parsedPort;
// Override with CLI args if provided (takes precedence over environment)
var argv = Environment.GetCommandLineArgs();
static string? Arg(string[] args, string name)
{
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == name && i + 1 < args.Length) return args[i + 1];
        if (args[i].StartsWith(name + "=")) return args[i].Substring(name.Length + 1);
    }
    return null;
}
// CLI help output
if (argv.Contains("--help"))
{
    Console.WriteLine("Usage: krypteonx.node [options]");
    Console.WriteLine("Options:");
    Console.WriteLine("  --help                              Show this help and exit");
    Console.WriteLine("  --config <path>                     JSON config file path");
    Console.WriteLine("  --port <num>                        UDP listen port (default 9050)");
    Console.WriteLine("  --key <str>                         Connection key (default Krypteonx)");
    Console.WriteLine("  --seeds <host:port,...>             Comma-separated seed endpoints");
    Console.WriteLine("  --reconnect-base-sec <num>          Reconnect base backoff in seconds");
    Console.WriteLine("  --reconnect-max-sec <num>           Reconnect max backoff in seconds");
    Console.WriteLine("  --reconnect-max-attempts <num>      Max reconnect attempts");
    Console.WriteLine("  --reconnect-blacklist-sec <num>     Blacklist duration after exhaustion");
    Console.WriteLine("  --seed-timeout-sec <num>            Seed connect timeout in seconds");
    Console.WriteLine("  --seed-base-backoff-sec <num>       Seed base backoff in seconds");
    Console.WriteLine("  --seed-max-backoff-sec <num>        Seed max backoff in seconds");
    Console.WriteLine("  --seed-max-failures <num>           Seed max failures before blacklist");
    Console.WriteLine("  --seed-max-concurrency <num>        Seed max concurrent connect attempts");
    Console.WriteLine("Examples:");
    Console.WriteLine("  krypteonx.node --port 10000 --key MyNet --seeds \"node1:10000,node2:10000\"");
    Console.WriteLine("  krypteonx.node --config ./net.json --reconnect-base-sec 5 --reconnect-max-sec 60 --reconnect-max-attempts 8 --reconnect-blacklist-sec 600 --seed-timeout-sec 3 --seed-base-backoff-sec 5 --seed-max-backoff-sec 60 --seed-max-failures 5 --seed-max-concurrency 2");
    Environment.Exit(0);
}
// Load centralized JSON config if present (CLI overrides env and config)
var argConfig = Arg(argv, "--config");
var configPath = argConfig ?? Environment.GetEnvironmentVariable("KRY_CONFIG") ?? Path.Combine(AppContext.BaseDirectory, "krypteonx.network.json");
string? cfgJson = null;
if (File.Exists(configPath))
{
    try { cfgJson = File.ReadAllText(configPath); }
    catch { cfgJson = null; }
}
var cfg = new NetworkConfig();
try { if (cfgJson is not null) cfg = JsonSerializer.Deserialize<NetworkConfig>(cfgJson) ?? new NetworkConfig(); } catch { }
var argPort = Arg(argv, "--port");
var argKey = Arg(argv, "--key");
var argSeeds = Arg(argv, "--seeds");
var argBase = Arg(argv, "--reconnect-base-sec");
var argMax = Arg(argv, "--reconnect-max-sec");
var argAttempts = Arg(argv, "--reconnect-max-attempts");
var argSeedTimeout = Arg(argv, "--seed-timeout-sec");
var argSeedBaseBackoff = Arg(argv, "--seed-base-backoff-sec");
var argSeedMaxBackoff = Arg(argv, "--seed-max-backoff-sec");
var argSeedMaxFailures = Arg(argv, "--seed-max-failures");
var argSeedMaxConcurrency = Arg(argv, "--seed-max-concurrency");
// Merge precedence: CLI > ENV > CONFIG > defaults
if (cfg.Port.HasValue) port = cfg.Port.Value;
if (!string.IsNullOrEmpty(cfg.Key)) keyEnv = cfg.Key!;
if (cfg.Seeds is { Length: > 0 })
{
    var seedsValueCfg = string.Join(',', cfg.Seeds);
    Environment.SetEnvironmentVariable("KRY_SEEDS", seedsValueCfg);
}
if (!string.IsNullOrEmpty(argPort) && int.TryParse(argPort, out var cliPort)) port = cliPort;
if (!string.IsNullOrEmpty(argKey)) keyEnv = argKey!;
conn.StartServer(port, keyEnv);
// Configure reconnect backoff via environment
var baseEnv = Environment.GetEnvironmentVariable("KRY_RECONNECT_BASE_SEC");
var maxEnv = Environment.GetEnvironmentVariable("KRY_RECONNECT_MAX_SEC");
var attemptsEnv = Environment.GetEnvironmentVariable("KRY_RECONNECT_MAX_ATTEMPTS");
var blacklistEnv = Environment.GetEnvironmentVariable("KRY_RECONNECT_BLACKLIST_SEC");
TimeSpan? baseIv = null, maxIv = null; int? maxAttempts = null;
if (int.TryParse(baseEnv, out var baseSec)) baseIv = TimeSpan.FromSeconds(baseSec);
if (int.TryParse(maxEnv, out var maxSec)) maxIv = TimeSpan.FromSeconds(maxSec);
if (int.TryParse(attemptsEnv, out var maxA)) maxAttempts = maxA;
TimeSpan? blacklistIv = null;
if (int.TryParse(blacklistEnv, out var blSec)) blacklistIv = TimeSpan.FromSeconds(blSec);
if (cfg.ReconnectBaseSec.HasValue) baseIv = TimeSpan.FromSeconds(cfg.ReconnectBaseSec.Value);
if (cfg.ReconnectMaxSec.HasValue) maxIv = TimeSpan.FromSeconds(cfg.ReconnectMaxSec.Value);
if (cfg.ReconnectMaxAttempts.HasValue) maxAttempts = cfg.ReconnectMaxAttempts.Value;
if (cfg.ReconnectBlacklistSec.HasValue) blacklistIv = TimeSpan.FromSeconds(cfg.ReconnectBlacklistSec.Value);
if (int.TryParse(argBase, out var baseSecArg)) baseIv = TimeSpan.FromSeconds(baseSecArg);
if (int.TryParse(argMax, out var maxSecArg)) maxIv = TimeSpan.FromSeconds(maxSecArg);
if (int.TryParse(argAttempts, out var maxAttemptsArg)) maxAttempts = maxAttemptsArg;
p2p.ConfigureReconnect(true, baseIv, maxIv, maxAttempts, blacklistIv);
// Seed connect behavior (timeouts/backoff/blacklist/concurrency)
var seedTimeout = TimeSpan.FromSeconds(cfg.SeedTimeoutSec ?? 3);
var seedBaseBackoff = TimeSpan.FromSeconds(cfg.SeedBaseBackoffSec ?? 5);
var seedMaxBackoff = TimeSpan.FromSeconds(cfg.SeedMaxBackoffSec ?? 60);
var seedMaxFailures = cfg.SeedMaxFailures ?? 5;
var seedMaxConcurrency = cfg.SeedMaxConcurrency ?? 2;
if (int.TryParse(argSeedTimeout, out var seedTimeoutArg)) seedTimeout = TimeSpan.FromSeconds(seedTimeoutArg);
if (int.TryParse(argSeedBaseBackoff, out var seedBaseBackoffArg)) seedBaseBackoff = TimeSpan.FromSeconds(seedBaseBackoffArg);
if (int.TryParse(argSeedMaxBackoff, out var seedMaxBackoffArg)) seedMaxBackoff = TimeSpan.FromSeconds(seedMaxBackoffArg);
if (int.TryParse(argSeedMaxFailures, out var seedMaxFailuresArg)) seedMaxFailures = seedMaxFailuresArg;
if (int.TryParse(argSeedMaxConcurrency, out var seedMaxConcurrencyArg)) seedMaxConcurrency = seedMaxConcurrencyArg;
conn.ConfigureSeedConnect(timeout: seedTimeout, baseBackoff: seedBaseBackoff, maxBackoff: seedMaxBackoff, maxFailuresBeforeBlacklist: seedMaxFailures, maxConcurrency: seedMaxConcurrency);
// Parse seed endpoints from environment (format: host:port,host2:port2)
var seedsEnv = Environment.GetEnvironmentVariable("KRY_SEEDS");
var seedsValue = argSeeds ?? seedsEnv ?? "localhost:9050";
var seeds = seedsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
foreach (var s in seeds)
{
    var parts = s.Split(':');
    if (parts.Length == 2 && int.TryParse(parts[1], out var sport))
        conn.ConnectTo(parts[0], sport, keyEnv);
}
// Log seed connect failures
conn.SeedConnectFailed += (endpoint, failures, backoff) => Console.WriteLine($"Seed connect failed: {endpoint} failures={failures} next_backoff={backoff.TotalSeconds:F0}s");
var ghostDag = new GhostDag();
var counter = 0;

var cts = new CancellationTokenSource();

void ExecuteBlock(Block block)
{
    ghostDag.AddBlock(block);
    foreach (var tx in block.Transactions)
    {
        if (tx.Kind == TransactionKind.Private)
        {
            var ok = verifier.VerifyProof(tx.Payload);
            if (!ok) continue;
            var nullifier = Convert.ToHexString(tx.Payload);
            if (ledger.Private.IsSpent(nullifier)) continue;
            ledger.Private.MarkSpent(nullifier);
            continue;
        }
        if (tx.Kind == TransactionKind.Public)
        {
            var s = Encoding.UTF8.GetString(tx.Payload);
            var parts = s.Split(',');
            if (parts.Length != 5) continue;
            var from = parts[0];
            var to = parts[1];
            if (!long.TryParse(parts[2], out var amount)) continue;
            var nonce = parts[3];
            var sigB64 = parts[4];
            byte[] signature;
            try { signature = Convert.FromBase64String(sigB64); }
            catch { continue; }
            if (!ledger.Public.TryGetPublicKey(from, out var spki) || spki is null) continue;
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
                var message = Encoding.UTF8.GetBytes($"{from},{to},{amount},{nonce}");
                var ok = ecdsa.VerifyData(message, signature, HashAlgorithmName.SHA256);
                if (!ok) continue;
            }
            catch
            {
                continue;
            }
            if (ledger.Public.IsNonceUsed(nonce)) continue;
            var bal = ledger.Public.GetBalance(from);
            if (bal < amount) continue;
            ledger.Public.SetBalance(from, bal - amount);
            var tbal = ledger.Public.GetBalance(to);
            ledger.Public.SetBalance(to, tbal + amount);
            ledger.Public.MarkNonce(nonce);
            continue;
        }
    }
    Console.WriteLine($"Tick: block {block.Id} parents [{string.Join(',', block.ParentIds)}] blueScore {ghostDag.GetBlueScore(block.Id)}");
}

// Subscribe to P2P events for visibility and metrics
p2p.BlockReceived += ExecuteBlock;
p2p.TransactionReceived += tx => mempool.Add(tx);
p2p.HandshakeReceived += hs => Console.WriteLine($"Handshake: {hs.NodeId} {hs.NetworkName} v{hs.Version}");
p2p.HandshakeAckReceived += ha => Console.WriteLine($"HandshakeAck: {ha.NodeId} ok={ha.Ok} ver={ha.AgreedVersion} caps=[{string.Join(',', ha.Capabilities ?? Array.Empty<string>())}] {ha.Timestamp:O}");
p2p.HeartbeatReceived += hb => Console.WriteLine($"Heartbeat: {hb.PeerId} {hb.Timestamp:O}");
p2p.HeartbeatAckReceived += ha => Console.WriteLine($"HeartbeatAck: {ha.PeerId} echo={ha.EchoSequence} {ha.Timestamp:O}");
p2p.PeerConnected += id => Console.WriteLine($"PeerConnected: {id}");
p2p.PeerDisconnected += id => Console.WriteLine($"PeerDisconnected: {id}");
// Alert when reconnect attempts are exhausted
p2p.PeerReconnectExhausted += id => Console.WriteLine($"Reconnect exhausted for {id}, entering degraded mode");
// Log unsupported incoming messages and capability labels
p2p.UnsupportedMessageReceived += (peer, kind, cap) => Console.WriteLine($"Unsupported incoming: peer={peer} kind={kind} cap={cap}");
// On reconnect request, attempt transport-level reconnect (example: loopback)
p2p.PeerReconnectRequested += peerId =>
{
    Console.WriteLine($"Reconnect requested for {peerId}");
    // Attempt reconnect using last known transport endpoint
    var ep = p2p.GetRemoteEndpoint(peerId);
    if (!string.IsNullOrEmpty(ep) && ep.StartsWith("udp://"))
    {
        var addr = ep.Substring("udp://".Length);
        var parts = addr.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[1], out var port))
        {
            conn.ConnectTo(parts[0], port, "Krypteonx");
        }
        else
        {
            Console.WriteLine($"Reconnect endpoint parse failed: {ep}");
        }
    }
    else
    {
        Console.WriteLine("Reconnect skipped: no endpoint available");
    }
};

_ = Task.Run(async () =>
{
    var lastHeartbeatAt = DateTime.UtcNow;
    var heartbeatLow = TimeSpan.FromSeconds(cfg.HeartbeatLowSec ?? 5);
    var heartbeatMed = TimeSpan.FromSeconds(cfg.HeartbeatMediumSec ?? 7);
    var heartbeatHigh = TimeSpan.FromSeconds(cfg.HeartbeatHighSec ?? 10);
    var rttMed = (double)(cfg.RttMediumMs ?? 200);
    var rttHigh = (double)(cfg.RttHighMs ?? 500);
    var lossMed = cfg.LossMedium ?? 0.15;
    var lossHigh = cfg.LossHigh ?? 0.30;
    var heartbeatInterval = heartbeatLow;
    while (!cts.IsCancellationRequested)
    {
        // Poll transport events
        conn.Poll();
        await Task.Delay(ChainParameters.BlockTargetTime, cts.Token);
        var txs = mempool.Snapshot();
        var parents = ghostDag.GetTips();
        var block = new Block
        {
            Id = $"B{Interlocked.Increment(ref counter)}",
            ParentIds = parents,
            Timestamp = DateTime.UtcNow,
            Transactions = txs,
            Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() }
        };
        p2p.BroadcastBlock(block);

        // Periodically broadcast recent block, handshake and heartbeat
        var hs = new P2PServer.Handshake { NodeId = "local", NetworkName = ChainParameters.NetworkName, Version = p2p.ProtocolVersion, Capabilities = p2p.Capabilities.ToArray() };
        p2p.BroadcastHandshake(hs);
        // Dynamic heartbeat interval and reliability based on average RTT
        var connectedPeers = p2p.Peers.ToArray();
        double avgRtt = 0; int rttCount = 0;
        double avgLoss = 0; int lossCount = 0;
        foreach (var peer in connectedPeers)
        {
            var r = p2p.GetAverageRttMs(peer.Id);
            if (r > 0) { avgRtt += r; rttCount++; }
            var l = p2p.GetHeartbeatLossRate(peer.Id);
            if (l > 0) { avgLoss += l; lossCount++; }
        }
        if (rttCount > 0) avgRtt /= rttCount;
        if (lossCount > 0) avgLoss /= lossCount;
        // Adjust interval: higher RTT -> longer interval, and reliability toggle
        if (avgLoss > lossHigh || avgRtt > rttHigh) { heartbeatInterval = heartbeatHigh; p2p.SetHeartbeatReliability(true); }
        else if (avgLoss > lossMed || avgRtt > rttMed) { heartbeatInterval = heartbeatMed; p2p.SetHeartbeatReliability(false); }
        else { heartbeatInterval = heartbeatLow; p2p.SetHeartbeatReliability(false); }
        if (DateTime.UtcNow - lastHeartbeatAt >= heartbeatInterval)
        {
            var hb = new P2PServer.Heartbeat { PeerId = "local", Timestamp = DateTime.UtcNow };
            p2p.BroadcastHeartbeat(hb);
            lastHeartbeatAt = DateTime.UtcNow;
        }
        p2p.CheckPeerTimeouts();
        // Print connection metrics
        Console.WriteLine($"Connections: {p2p.ConnectedPeerCount}");
        foreach (var peer in p2p.Peers)
        {
            var rtt = p2p.GetAverageRttMs(peer.Id);
            var loss = p2p.GetHeartbeatLossRate(peer.Id);
            Console.WriteLine($"Peer {peer.Id}: avgRTT={rtt:F1}ms loss={(loss*100):F1}%");
            // Periodically print unsupported counts per capability
            var counts = p2p.GetUnsupportedCounts(peer.Id);
            if (counts.Count > 0)
            {
                var items = string.Join(',', counts.Select(kv => $"{kv.Key}:{kv.Value}"));
                Console.WriteLine($"Peer {peer.Id}: unsupported={{ {items} }}");
            }
            // Print disabled state due to reconnect exhaustion
            var ep = p2p.GetRemoteEndpoint(peer.Id);
            if (!string.IsNullOrEmpty(ep)) Console.WriteLine($"Peer {peer.Id}: endpoint={ep}");
        }
    }
});

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    // Stop transport on exit
    conn.Stop();
};

await Task.Delay(Timeout.InfiniteTimeSpan);

// Network configuration model for JSON loading
public sealed class NetworkConfig
{
    public int? Port { get; set; }
    public string? Key { get; set; }
    public string[]? Seeds { get; set; }
    public int? ReconnectBaseSec { get; set; }
    public int? ReconnectMaxSec { get; set; }
    public int? ReconnectMaxAttempts { get; set; }
    public int? ReconnectBlacklistSec { get; set; }
    public int? HeartbeatLowSec { get; set; }
    public int? HeartbeatMediumSec { get; set; }
    public int? HeartbeatHighSec { get; set; }
    public int? RttMediumMs { get; set; }
    public int? RttHighMs { get; set; }
    public double? LossMedium { get; set; }
    public double? LossHigh { get; set; }
    public int? SeedTimeoutSec { get; set; }
    public int? SeedBaseBackoffSec { get; set; }
    public int? SeedMaxBackoffSec { get; set; }
    public int? SeedMaxFailures { get; set; }
    public int? SeedMaxConcurrency { get; set; }
}
