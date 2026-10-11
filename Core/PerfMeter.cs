using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KeyboardWorker;

// How much of the computer this program itself uses, so "is it the pet that makes my game lag?" has a number for an answer.
public sealed class PerfMeter
{
    TimeSpan lastCpu = Environment.CpuUsage.TotalTime; long lastTick = Environment.TickCount64;
    double recent, normalCpu, normalWall, lightCpu, lightWall;
    public void Sample(long now, bool light)
    {
        long wall = now - lastTick; if (wall < 500) return; var cpu = Environment.CpuUsage.TotalTime; double used = (cpu - lastCpu).TotalMilliseconds; lastCpu = cpu; lastTick = now;
        if (wall > 15000 || used < 0) return;   // slept or paused: not a usable sample
        double share = used / wall / Environment.ProcessorCount * 100; recent = recent * .7 + share * .3;
        if (light) { lightCpu += used; lightWall += wall; } else { normalCpu += used; normalWall += wall; }
    }
    static string Average(double cpu, double wall) => wall < 30000 ? "아직 측정 전" : $"{cpu / wall / Environment.ProcessorCount * 100:0.00}% (측정 {wall / 60000:0}분)";
    // Same scale as Task Manager: share of all processor cores together.
    public string Summary => $"지금 CPU {recent:0.00}% · 메모리 {Environment.WorkingSet / 1048576.0:0}MB\n평소 평균 CPU {Average(normalCpu, normalWall)}\n게임·전체 화면 중 평균 CPU {Average(lightCpu, lightWall)}";
    // GPU share used by this process over about one second (sum of its engines). Null when Windows does not provide the counter.
    public static double? Gpu()
    {
        try
        {
            string mark = "pid_" + Environment.ProcessId + "_"; var category = new System.Diagnostics.PerformanceCounterCategory("GPU Engine");
            var counters = category.GetInstanceNames().Where(n => n.Contains(mark)).Select(n => new System.Diagnostics.PerformanceCounter("GPU Engine", "Utilization Percentage", n, true)).ToList();
            try { foreach (var counter in counters) counter.NextValue(); System.Threading.Thread.Sleep(1000); return counters.Sum(c => (double)c.NextValue()); }
            finally { foreach (var counter in counters) counter.Dispose(); }
        }
        catch { return null; }
    }
}
