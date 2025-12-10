using Krypteonx.Core.Models;
using Krypteonx.Consensus.GhostDag;
using Xunit;

namespace Krypteonx.Tests;

public class LargeDagTests
{
    [Fact]
    public void AntichainWidth_Respected_InLargeParallel()
    {
        var dag = new GhostDag();
        var g = new Block { Id = "G", ParentIds = Array.Empty<string>(), Timestamp = DateTime.UtcNow.AddSeconds(-40), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(g);

        var parents = new List<string>();
        for (int i = 0; i < 32; i++)
        {
            var id = $"P{i}";
            var p = new Block { Id = id, ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-30 + i), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
            dag.AddBlock(p);
            parents.Add(id);
        }

        var c = new Block { Id = "C", ParentIds = parents.ToArray(), Timestamp = DateTime.UtcNow, Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(c);

        var blue = dag.GetMergesetBlueCount("C");
        var red = dag.GetMergesetRedCount("C");
        Assert.Equal(1 + 8, blue); // 1 + k
        Assert.Equal(32 - 1 - 8, red);
    }

    [Fact]
    public void Determinism_SelectedParent_And_Blues_Stable()
    {
        var dag = new GhostDag();
        var g = new Block { Id = "G", ParentIds = Array.Empty<string>(), Timestamp = DateTime.UtcNow.AddSeconds(-100), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(g);

        var a = new Block { Id = "A", ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-90), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        var b = new Block { Id = "B", ParentIds = new[] { "G" }, Timestamp = DateTime.UtcNow.AddSeconds(-89), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(a);
        dag.AddBlock(b);

        var c = new Block { Id = "C", ParentIds = new[] { "A", "B" }, Timestamp = DateTime.UtcNow.AddSeconds(-10), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        var d = new Block { Id = "D", ParentIds = new[] { "C" }, Timestamp = DateTime.UtcNow.AddSeconds(-5), Transactions = Array.Empty<Transaction>(), Header = new BlockHeader { MerkleRoot = string.Empty, PowData = Array.Empty<byte>() } };
        dag.AddBlock(c);
        dag.AddBlock(d);

        var spC = dag.GetSelectedParent("C");
        var bsD = dag.GetBlueScore("D");
        var bluesC = dag.GetMergesetBlues("C");

        Assert.NotNull(spC);
        Assert.True(bsD > 0);
        Assert.Contains("C", bluesC);
    }
}

