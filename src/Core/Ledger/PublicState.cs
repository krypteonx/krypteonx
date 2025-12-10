namespace Krypteonx.Core.Ledger;

public sealed class PublicState
{
    private readonly Dictionary<string, long> _balances = new();
    private readonly HashSet<string> _nonces = new();
    private readonly Dictionary<string, byte[]> _pubKeys = new();

    public long GetBalance(string address) => _balances.TryGetValue(address, out var v) ? v : 0;
    public void SetBalance(string address, long value) => _balances[address] = value;

    public bool IsNonceUsed(string nonce) => _nonces.Contains(nonce);
    public void MarkNonce(string nonce) => _nonces.Add(nonce);

    public void SetPublicKey(string address, ReadOnlySpan<byte> subjectPublicKeyInfo) => _pubKeys[address] = subjectPublicKeyInfo.ToArray();
    public bool TryGetPublicKey(string address, out byte[]? subjectPublicKeyInfo) => _pubKeys.TryGetValue(address, out subjectPublicKeyInfo);
}
