using System;
using TurnTelemetry.Core.Diagnostics;
using Xunit;

namespace TurnTelemetry.Tests
{
    public class EventLogTests
    {
        [Fact]
        public void Keeps_the_most_recent_entries_with_timestamps()
        {
            var log = new EventLog(() => new DateTime(2026, 10, 3, 14, 5, 9, 300));
            for (var i = 0; i < EventLog.Capacity + 10; i++) log.Add("event " + i);
            var entries = log.Snapshot();
            Assert.Equal(EventLog.Capacity, entries.Count);
            Assert.Equal("14:05:09.3  event 10", entries[0]);
            Assert.EndsWith("event " + (EventLog.Capacity + 9), entries[entries.Count - 1]);
        }
    }
}
