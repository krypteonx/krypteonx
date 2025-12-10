namespace Krypteonx.Execution.Privacy;

public interface IPrivateVerifier
{
    bool VerifyProof(ReadOnlySpan<byte> proofBytes);
}

