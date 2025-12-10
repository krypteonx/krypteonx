using Krypteonx.Core.Models;
using Krypteonx.Core.Config;
using Krypteonx.Consensus.GhostDag;
using Xunit;

namespace Krypteonx.Tests;

public class RandomDagMetricsTests
{
    private static IReadOnlyList<Block> Generate(int seed, int count)
    {
        var rnd = new Random(seed);
        var list = new List<Block>();
        var now = DateTime.UtcNow;
        var g = new Block { Id = "G", ParentIds = Array.Empty<string>(), Timestamp = now.AddSeconds(-4000), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        list.Add(g);
        for (int i = 0; i < count; i++)
        {
            var choices = list.Select(b => b.Id).ToArray();
            var pc = Math.Max(1, rnd.Next(1, 4));
            var parents = choices.OrderBy(_ => rnd.Next()).Take(pc).ToArray();
            var id = $"M{i}";
            var ts = now.AddSeconds(-3000 + i * 5 + rnd.Next(-2, 2));
            list.Add(new Block { Id = id, ParentIds = parents, Timestamp = ts, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } });
        }
        return list;
    }

    [Fact]
    public void LargeRandomGraph_MetricsWithinBounds()
    {
        var dag = new GhostDag();
        foreach (var b in Generate(777, 200)) dag.AddBlock(b);
        var tips = dag.GetTips();
        var u = new Block { Id = "U", ParentIds = tips.ToArray(), Timestamp = DateTime.UtcNow, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(u);

        var stats = dag.GetStats();
        Assert.True(stats.BlocksProcessed >= 1);
        Assert.True(stats.MaxIncomparableWidth <= 8);
        Assert.True(stats.OrderedExtrasTotal / Math.Max(1, stats.BlocksProcessed) <= ChainParameters.GhostDagFrontierMax);
        Assert.True(stats.RedTotal >= 0);
        Assert.True(stats.FrontierMaximaTotal <= stats.FrontierUnionTotal);
    }
}
