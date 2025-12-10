namespace Krypteonx.Execution.Privacy;

public sealed class NoopPrivateVerifier : IPrivateVerifier
{
    public bool VerifyProof(ReadOnlySpan<byte> proofBytes) => proofBytes.Length > 0;
}

