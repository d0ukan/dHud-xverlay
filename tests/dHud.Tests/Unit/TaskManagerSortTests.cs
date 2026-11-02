using System.IO;
using DHud.Services;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>Görev yöneticisi: GPU sütunu, çift yönlü sıralama ve işlem başına GPU hesapları.</summary>
[Trait(Cat.Name, Cat.Fast)]
public class TaskManagerSortTests
{
    private static readonly ProcRow[] Rows =
    [
        new("alpha", 1, 10, 300, 5, 0, 70),
        new("Bravo", 1, 30, 100, 1, 9, 0),
        new("charlie", 1, 20, 200, 9, 4, 25)
    ];

    private static string[] Names(ProcSort sort, bool descending) => ProcessTable.Top(Rows, sort, 10, descending).Select(r => r.Name).ToArray();

    [Theory]
    [InlineData(ProcSort.Cpu, new[] { "Bravo", "charlie", "alpha" })]
    [InlineData(ProcSort.Gpu, new[] { "alpha", "charlie", "Bravo" })]
    [InlineData(ProcSort.Memory, new[] { "alpha", "charlie", "Bravo" })]
    [InlineData(ProcSort.Disk, new[] { "charlie", "alpha", "Bravo" })]
    [InlineData(ProcSort.Network, new[] { "Bravo", "charlie", "alpha" })]
    public void Each_column_sorts_from_most_to_least_and_back(ProcSort sort, string[] mostFirst)
    {
        Assert.Equal(mostFirst, Names(sort, true));
        Assert.Equal(mostFirst.Reverse(), Names(sort, false));
    }

    [Fact]
    public void Name_sorts_alphabetically_ignoring_case_in_both_directions()
    {
        Assert.Equal(["alpha", "Bravo", "charlie"], Names(ProcSort.Name, false));
        Assert.Equal(["charlie", "Bravo", "alpha"], Names(ProcSort.Name, true));
    }

    [Fact]
    public void Ties_are_broken_by_memory_then_name_so_the_order_never_flickers()
    {
        ProcRow[] tied = [new("b", 1, 5, 100, 0, 0, 0), new("a", 1, 5, 100, 0, 0, 0), new("c", 1, 5, 500, 0, 0, 0)];
        Assert.Equal(["c", "a", "b"], ProcessTable.Top(tied, ProcSort.Gpu, 3).Select(r => r.Name));
        Assert.Equal(["c", "a", "b"], ProcessTable.Top(tied, ProcSort.Gpu, 3, descending: false).Select(r => r.Name));
    }

    [Fact]
    public void Gpu_usage_is_summed_per_app_capped_at_100_and_ignores_unknown_processes()
    {
        ProcSample P(int pid, string name) => new(pid, name, 0, 0, 10);
        var gpu = new Dictionary<int, double> { [1] = 60, [2] = 55, [3] = 12.5, [99] = 40 };
        var rows = ProcessTable.Compute([P(1, "game"), P(2, "game"), P(3, "other"), P(4, "idle")], [P(1, "game"), P(2, "game"), P(3, "other"), P(4, "idle")], 1, 1, null, gpu);

        Assert.Equal(100, rows.Single(r => r.Name == "game").GpuPercent);
        Assert.Equal(12.5, rows.Single(r => r.Name == "other").GpuPercent);
        Assert.Equal(0, rows.Single(r => r.Name == "idle").GpuPercent);
        Assert.All(ProcessTable.Compute([P(1, "x")], [P(1, "x")], 1, 1), r => Assert.Equal(0, r.GpuPercent));
    }

    [Theory]
    [InlineData("pid_1234_luid_0x00000000_0x0000F2A1_phys_0_eng_0_engtype_3D", true, 1234, "3D")]
    [InlineData("pid_77_luid_0x0_0x1_phys_0_eng_5_engtype_VideoEncode", true, 77, "VideoEncode")]
    [InlineData("PID_8_LUID_0x0_0x1_PHYS_0_ENG_1_ENGTYPE_Copy", true, 8, "Copy")]
    [InlineData("pid_0_luid_0x0_0x1_phys_0_eng_0_engtype_3D", false, 0, "")]
    [InlineData("pid_abc_luid_x_engtype_3D", false, 0, "")]
    [InlineData("pid_5", false, 0, "")]
    [InlineData("", false, 0, "")]
    [InlineData("garbage", false, 0, "")]
    public void Gpu_counter_instance_names_are_parsed(string name, bool ok, int pid, string engine)
    {
        Assert.Equal(ok, GpuCounters.TryParseInstance(name, out var gotPid, out var gotEngine));
        Assert.Equal(pid, gotPid);
        Assert.Equal(engine, gotEngine);
    }

    [Fact]
    public void Gpu_engines_take_the_busiest_engine_type_not_the_sum_of_all_types()
    {
        var result = GpuCounters.Aggregate(
        [
            ("pid_10_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 30.0),
            ("pid_10_luid_0x0_0x1_phys_0_eng_1_engtype_3D", 25.0),     // aynı tür: toplanır (55)
            ("pid_10_luid_0x0_0x1_phys_0_eng_2_engtype_Copy", 40.0),   // başka tür: en büyüğü alınır
            ("pid_11_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 90.0),
            ("pid_11_luid_0x0_0x1_phys_0_eng_1_engtype_3D", 80.0),     // 170 -> %100
            ("pid_12_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 0.0),
            ("pid_13_luid_0x0_0x1_phys_0_eng_0_engtype_3D", double.NaN),
            ("pid_14_luid_0x0_0x1_phys_0_eng_0_engtype_3D", -5.0),
            ("not-a-gpu-instance", 50.0)
        ]);

        Assert.Equal(55, result[10]);
        Assert.Equal(100, result[11]);
        Assert.False(result.ContainsKey(12));
        Assert.False(result.ContainsKey(13));
        Assert.False(result.ContainsKey(14));
        Assert.Equal(2, result.Count);
    }

    /// <summary>Gerçek Windows sayaçları: bu makinede GPU sayacı varsa değerler [0,100] içinde olmalı; yoksa Available false kalır (hata fırlatmaz).</summary>
    [Fact]
    public void Real_gpu_counters_never_throw_and_stay_in_range()
    {
        using var counters = new GpuCounters();
        counters.Read();                 // hazırlık
        Thread.Sleep(600);
        var result = counters.Read();
        if (Environment.GetEnvironmentVariable("DHUD_GPU_OUT") is { Length: > 0 } outPath)
        {
            File.WriteAllText(outPath, $"available={counters.Available} processes={result.Count} top=" + string.Join(", ", result.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key}:{kv.Value:0.0}")));
        }

        Assert.All(result, kv => Assert.InRange(kv.Value, 0, 100));
        if (counters.Available)
        {
            Assert.All(result.Keys, pid => Assert.True(pid > 0));
        }
    }
}
