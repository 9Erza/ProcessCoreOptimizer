using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>One sampled point. Nulls mean the OS refused that read (Unknown).</summary>
    public sealed class InsightReading
    {
        public DateTime AtUtc { get; set; }
        public double? CpuPercent { get; set; }
        public long? WorkingSetBytes { get; set; }
        public long? PrivateBytes { get; set; }
        public bool Readable { get; set; }
    }

    /// <summary>
    /// Light monitor for one selected process. Bounded in-memory ring buffer,
    /// no timers of its own: the caller samples on the existing refresh tick
    /// and only while a selection exists. PID reuse is detected via start time;
    /// a recycled PID resets the baseline instead of mixing two processes.
    /// </summary>
    public sealed class ProcessInsightsSampler
    {
        private readonly int _capacity;
        private readonly Func<int, (bool ok, DateTime startUtc, TimeSpan cpuTime, long ws, long priv)>? _reader;
        private readonly List<InsightReading> _history = new();
        private readonly object _lock = new();

        private int _trackedPid;
        private DateTime _trackedStartUtc;
        private TimeSpan _lastCpuTime;
        private DateTime _lastAtUtc;

        public ProcessInsightsSampler(int capacity = 120, Func<int, (bool, DateTime, TimeSpan, long, long)>? reader = null)
        {
            _capacity = Math.Clamp(capacity, 10, 600);
            _reader = reader;
        }

        public IReadOnlyList<InsightReading> History
        {
            get { lock (_lock) return _history.ToList(); }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _history.Clear();
                _trackedPid = 0;
                _trackedStartUtc = default;
            }
        }

        public static double? ComputeCpuPercent(TimeSpan currentCpu, TimeSpan lastCpu, DateTime nowUtc, DateTime lastUtc)
        {
            double elapsed = (nowUtc - lastUtc).TotalSeconds;
            if (elapsed <= 0) return null;
            double usageMs = Math.Max((currentCpu - lastCpu).TotalMilliseconds, 0);
            return (usageMs / (elapsed * 1000 * Math.Max(Environment.ProcessorCount, 1))) * 100;
        }

        public InsightReading Sample(int pid)
        {
            DateTime now = DateTime.UtcNow;
            try
            {
                (bool ok, DateTime startUtc, TimeSpan cpuTime, long ws, long priv) r;
                if (_reader != null)
                {
                    r = _reader(pid);
                }
                else
                {
                    using var proc = Process.GetProcessById(pid);
                    if (proc.HasExited) return AddUnreadable(now);
                    r = (true, proc.StartTime.ToUniversalTime(), proc.TotalProcessorTime, proc.WorkingSet64, proc.PrivateMemorySize64);
                }

                if (!r.ok) return AddUnreadable(now);

                lock (_lock)
                {
                    if (pid != _trackedPid || r.startUtc != _trackedStartUtc)
                    {
                        // New or recycled PID: fresh baseline, first point Unknown (honest).
                        _trackedPid = pid;
                        _trackedStartUtc = r.startUtc;
                        _lastCpuTime = r.cpuTime;
                        _lastAtUtc = now;
                        return Add(new InsightReading
                        {
                            AtUtc = now,
                            CpuPercent = null,
                            WorkingSetBytes = r.ws,
                            PrivateBytes = r.priv,
                            Readable = true
                        });
                    }

                    double? cpu = ComputeCpuPercent(r.cpuTime, _lastCpuTime, now, _lastAtUtc);
                    _lastCpuTime = r.cpuTime;
                    _lastAtUtc = now;
                    return Add(new InsightReading
                    {
                        AtUtc = now,
                        CpuPercent = cpu,
                        WorkingSetBytes = r.ws,
                        PrivateBytes = r.priv,
                        Readable = true
                    });
                }
            }
            catch
            {
                return AddUnreadable(now);
            }
        }

        private InsightReading Add(InsightReading reading)
        {
            lock (_lock)
            {
                _history.Add(reading);
                while (_history.Count > _capacity) _history.RemoveAt(0);
            }

            return reading;
        }

        private InsightReading AddUnreadable(DateTime now)
        {
            return Add(new InsightReading
            {
                AtUtc = now,
                CpuPercent = null,
                WorkingSetBytes = null,
                PrivateBytes = null,
                Readable = false
            });
        }
    }
}
