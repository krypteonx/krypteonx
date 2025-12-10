using Krypteonx.Core.Models;
using Krypteonx.Core.Config;
using Krypteonx.Consensus.GhostDag;
using Xunit;

namespace Krypteonx.Tests;

public class RandomDagSamplingTests
{
    private static IReadOnlyList<Block> Generate(int seed, int count, int maxParents)
    {
        var rnd = new Random(seed);
        var list = new List<Block>();
        var now = DateTime.UtcNow;
        var g = new Block { Id = "G", ParentIds = Array.Empty<string>(), Timestamp = now.AddSeconds(-6000), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        list.Add(g);
        for (int i = 0; i < count; i++)
        {
            var choices = list.Select(b => b.Id).ToArray();
            var pc = Math.Max(1, rnd.Next(1, Math.Max(2, maxParents + 1)));
            var parents = choices.OrderBy(_ => rnd.Next()).Take(pc).ToArray();
            var id = $"S{i}";
            var ts = now.AddSeconds(-5000 + i * 4 + rnd.Next(-1, 1));
            list.Add(new Block { Id = id, ParentIds = parents, Timestamp = ts, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } });
        }
        return list;
    }

    [Fact]
    public void Sampling_LargeGraphs_MetricsAverages()
    {
        var dag = new GhostDag();
        foreach (var b in Generate(3001, 400, 5)) dag.AddBlock(b);
        var tips = dag.GetTips();
        var w = new Block { Id = "W", ParentIds = tips.ToArray(), Timestamp = DateTime.UtcNow, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(w);

        var stats = dag.GetStats();
        var bp = Math.Max(1, stats.BlocksProcessed);
        var avgOrdered = (double)stats.OrderedExtrasTotal / bp;
        var avgDistinct = (double)stats.OrderedDistinctTotal / bp;
        var avgWidth = (double)stats.IncomparableWidthTotal / bp;

        Assert.True(avgOrdered <= ChainParameters.GhostDagFrontierMax);
        Assert.True(avgDistinct >= avgOrdered);
        Assert.True(stats.OrderedTruncations >= 0);
        Assert.True(avgWidth <= ChainParameters.GhostDagK);
        Assert.True(stats.CandidateIdsTotal > 0);
    }
}

