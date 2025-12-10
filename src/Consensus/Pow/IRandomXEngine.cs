namespace Krypteonx.Consensus.Pow;

public interface IRandomXEngine
{
    byte[] ComputeHash(ReadOnlySpan<byte> input);
}

