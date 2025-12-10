using Krypteonx.Core.Models;
using Krypteonx.Consensus.GhostDag;
using Xunit;

namespace Krypteonx.Tests;

public class RandomDagPropertyTests
{
    private static IReadOnlyList<Block> GenerateBlocks(int seed)
    {
        var rnd = new Random(seed);
        var list = new List<Block>();
        var now = DateTime.UtcNow;
        var g = new Block { Id = "G", ParentIds = Array.Empty<string>(), Timestamp = now.AddSeconds(-1000), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        list.Add(g);
        for (int i = 0; i < 40; i++)
        {
            var choices = list.Select(b => b.Id).ToArray();
            var pc = Math.Max(1, rnd.Next(1, 4));
            var parents = choices.OrderBy(_ => rnd.Next()).Take(pc).ToArray();
            var id = $"N{i}";
            var ts = now.AddSeconds(-500 + i * 10 + rnd.Next(-2, 2));
            list.Add(new Block { Id = id, ParentIds = parents, Timestamp = ts, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } });
        }
        return list;
    }

    [Fact]
    public void Random_AntichainWidth_And_BlueScore_Monotonic()
    {
        var blocks = GenerateBlocks(1234);
        var dag = new GhostDag();
        foreach (var b in blocks) dag.AddBlock(b);
        var tips = dag.GetTips();
        var s = new Block { Id = "S", ParentIds = tips.ToArray(), Timestamp = DateTime.UtcNow, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(s);
        var chain = dag.GetSelectedChain("S", 100);
        Assert.True(chain.Count > 0);
        var bsPrev = -1;
        foreach (var id in chain)
        {
            var bs = dag.GetBlueScore(id);
            Assert.True(bs > bsPrev);
            bsPrev = bs;
        }
        var blue = dag.GetMergesetBlueCount("S");
        var red = dag.GetMergesetRedCount("S");
        Assert.True(blue <= 1 + 8);
        Assert.True(red >= 0);
    }

    [Fact]
    public void Permutation_Invariance_OnSameStructure()
    {
        var blocks = GenerateBlocks(42).ToArray();
        var dag1 = new GhostDag();
        foreach (var b in blocks) dag1.AddBlock(b);
        var tips1 = dag1.GetTips();
        var z1 = new Block { Id = "Z", ParentIds = tips1.ToArray(), Timestamp = DateTime.UtcNow, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag1.AddBlock(z1);
        var blues1 = dag1.GetMergesetBlues("Z").OrderBy(x => x).ToArray();
        
        var dag2 = new GhostDag();
        foreach (var b in blocks.OrderBy(_ => Guid.NewGuid())) dag2.AddBlock(b);
        var tips2 = dag2.GetTips();
        var z2 = new Block { Id = "Z", ParentIds = tips2.ToArray(), Timestamp = z1.Timestamp, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag2.AddBlock(z2);
        var blues2 = dag2.GetMergesetBlues("Z").OrderBy(x => x).ToArray();

        Assert.True(blues1.Length <= 1 + 8);
        Assert.True(blues2.Length <= 1 + 8);
        Assert.Contains("Z", blues1);
        Assert.Contains("Z", blues2);
        Assert.Equal(blues1.Length, blues2.Length);
    }
}

