using Krypteonx.Core.Models;

namespace Krypteonx.Consensus.GhostDag;

public interface IGhostDag
{
    void AddBlock(Block block);
    IReadOnlyList<Block> OrderBlocks(IReadOnlyList<Block> parallelBlocks);
    IReadOnlyList<string> GetTips();
    int GetBlueScore(string blockId);
    string? GetSelectedParent(string blockId);
    int GetMergesetBlueCount(string blockId);
    int GetMergesetRedCount(string blockId);
    IReadOnlyList<string> GetMergesetBlues(string blockId);
    IReadOnlyList<string> GetMergesetReds(string blockId);
    string? GetHeavyTip();
    IReadOnlyList<string> GetSelectedChain(string blockId, int limit);
    GhostDagStats GetStats();
}
