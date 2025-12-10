namespace Krypteonx.Core.Config;

public static class ChainParameters
{
    public static readonly TimeSpan BlockTargetTime = TimeSpan.FromSeconds(3);
    public const string NetworkName = "Krypteonx";
    public const int GhostDagK = 8;
    public static readonly TimeSpan GhostDagPastWindow = TimeSpan.FromSeconds(30);
    public const int GhostDagFrontierMax = 64;
}
