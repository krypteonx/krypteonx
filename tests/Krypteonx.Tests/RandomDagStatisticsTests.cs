using Krypteonx.Core.Models;
using Krypteonx.Consensus.GhostDag;
using Xunit;

namespace Krypteonx.Tests;

public class RandomDagStatisticsTests
{
    private static IReadOnlyList<Block> Generate(int seed, int count)
    {
        var rnd = new Random(seed);
        var list = new List<Block>();
        var now = DateTime.UtcNow;
        var g = new Block { Id = "G", ParentIds = Array.Empty<string>(), Timestamp = now.AddSeconds(-2000), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        list.Add(g);
        for (int i = 0; i < count; i++)
        {
            var choices = list.Select(b => b.Id).ToArray();
            var pc = Math.Max(1, rnd.Next(1, 4));
            var parents = choices.OrderBy(_ => rnd.Next()).Take(pc).ToArray();
            var id = $"R{i}";
            var ts = now.AddSeconds(-1000 + i * 7 + rnd.Next(-2, 2));
            list.Add(new Block { Id = id, ParentIds = parents, Timestamp = ts, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } });
        }
        return list;
    }

    [Fact]
    public void Randomized_Seeds_Invariants()
    {
        int seeds = 30;
        int blocksPerSeed = 60;
        int k = 8;

        int maxBlue = 0;
        int minBlue = int.MaxValue;
        int redNonNegativeCount = 0;

        for (int s = 0; s < seeds; s++)
        {
            var dag = new GhostDag();
            foreach (var b in Generate(1000 + s, blocksPerSeed)) dag.AddBlock(b);
            var tips = dag.GetTips();
            var m = new Block { Id = $"M{s}", ParentIds = tips.ToArray(), Timestamp = DateTime.UtcNow, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
            dag.AddBlock(m);

            var chain = dag.GetSelectedChain(m.Id, 200);
            Assert.True(chain.Count > 0);
            var prev = -1;
            foreach (var id in chain)
            {
                var bs = dag.GetBlueScore(id);
                Assert.True(bs > prev);
                prev = bs;
            }

            var blue = dag.GetMergesetBlueCount(m.Id);
            var red = dag.GetMergesetRedCount(m.Id);
            Assert.True(blue <= 1 + k);
            Assert.True(red >= 0);
            maxBlue = Math.Max(maxBlue, blue);
            minBlue = Math.Min(minBlue, blue);
            if (red >= 0) redNonNegativeCount++;
        }

        Assert.True(maxBlue <= 1 + k);
        Assert.True(minBlue >= 1);
        Assert.Equal(seeds, redNonNegativeCount);
    }
}

