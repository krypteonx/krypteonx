namespace Krypteonx.Core.Ledger;

public sealed class PrivateState
{
    private readonly HashSet<string> _nullifiers = new();

    public bool IsSpent(string nullifier) => _nullifiers.Contains(nullifier);
    public void MarkSpent(string nullifier) => _nullifiers.Add(nullifier);
}

