using Krypteonx.Core.Models;

namespace Krypteonx.Core.Services;

public sealed class Mempool
{
    private readonly List<Transaction> _txs = new();

    public IReadOnlyList<Transaction> Snapshot() => _txs.ToArray();

    public void Add(Transaction tx)
    {
        _txs.Add(tx);
    }
}

