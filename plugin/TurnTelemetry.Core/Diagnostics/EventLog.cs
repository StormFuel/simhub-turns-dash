using System;
using System.Collections.Generic;
using System.Linq;

namespace TurnTelemetry.Core.Diagnostics
{
    /// <summary>
    /// The most recent notable plugin events (sessions, turn data, laps, sectors, track limits, errors), kept in memory
    /// for the problem report. Thread-safe: the engine writes from SimHub's data thread, the report reads from the UI.
    /// </summary>
    public sealed class EventLog
    {
        public const int Capacity = 500;

        private readonly Queue<string> _entries = new Queue<string>();
        private readonly object _gate = new object();
        private readonly Func<DateTime> _clock;

        public EventLog(Func<DateTime> clock = null)
        {
            _clock = clock ?? (() => DateTime.Now);
        }

        public void Add(string message)
        {
            var line = $"{_clock():HH:mm:ss.f}  {message}";
            lock (_gate)
            {
                _entries.Enqueue(line);
                while (_entries.Count > Capacity) _entries.Dequeue();
            }
        }

        public IReadOnlyList<string> Snapshot()
        {
            lock (_gate) return _entries.ToList();
        }
    }
}
