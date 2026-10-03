using System.Collections.Generic;
using System.Linq;
using TurnTelemetry.Core.Standings;
using Xunit;

namespace TurnTelemetry.Tests
{
    public class StandingsTests
    {
        private static List<DriverEntry> Field(int count, int player)
        {
            return Enumerable.Range(1, count).Select(p => new DriverEntry
            {
                Position = p,
                Name = "Driver " + p,
                IsPlayer = p == player,
                BestLap = 100 + p * 0.1,
                BestS1 = 30 + p * 0.01,
                BestS2 = 40 - p * 0.01,
                BestS3 = 30.2,
                GapToLeader = (p - 1) * 1.5,
            }).ToList();
        }

        [Fact]
        public void Player_near_the_front_shows_the_top_rows()
        {
            var board = new StandingsBoard();
            board.Update(Field(20, 3));
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, board.Rows.Select(r => r.Position));
            Assert.True(board.Rows[2].IsPlayer);
            Assert.Equal(20, board.DriverCount);
        }

        [Fact]
        public void Player_further_back_shows_leader_then_cars_around_player()
        {
            var board = new StandingsBoard();
            board.Update(Field(20, 12));
            Assert.Equal(new[] { 1, 10, 11, 12, 13, 14 }, board.Rows.Select(r => r.Position));

            board.Update(Field(20, 20));
            Assert.Equal(new[] { 1, 16, 17, 18, 19, 20 }, board.Rows.Select(r => r.Position));
        }

        [Fact]
        public void Small_field_hides_unused_rows()
        {
            var board = new StandingsBoard();
            board.Update(Field(2, 2));
            Assert.Equal(new[] { true, true, false, false, false, false }, board.Rows.Select(r => r.Visible));
        }

        [Fact]
        public void Fastest_lap_and_sectors_are_flagged_and_deltas_are_against_the_fastest_lap()
        {
            var board = new StandingsBoard();
            board.Update(Field(6, 4));
            var leader = board.Rows[0];
            Assert.Equal("1:40.100", leader.Best);
            Assert.True(leader.BestFastest);
            Assert.Equal("FASTEST", leader.BestDelta);
            Assert.Equal("+0.300", board.Rows[3].BestDelta);
            Assert.True(leader.SectorFastest[0]);          // S1 fastest for P1
            Assert.True(board.Rows[5].SectorFastest[1]);   // S2 fastest for P6
            Assert.All(board.Rows, r => Assert.True(r.SectorFastest[2]));   // tied S3: everyone is fastest
            Assert.Equal("30.010", leader.Sectors[0]);
            Assert.Equal("LEADER", leader.Gap);
            Assert.Equal("+4.5", board.Rows[3].Gap);
        }

        [Fact]
        public void Missing_times_and_lapped_cars()
        {
            var field = Field(3, 1);
            field[2].BestLap = 0;
            field[2].BestS1 = null;
            field[2].LapsToLeader = 2;
            field[1].BestS2 = 39980;   // milliseconds from a sim that reports them
            var board = new StandingsBoard();
            board.Update(field);
            Assert.Equal("", board.Rows[2].Best);
            Assert.Equal("", board.Rows[2].BestDelta);
            Assert.Equal("", board.Rows[2].Sectors[0]);
            Assert.Equal("+2 LAPS", board.Rows[2].Gap);
            Assert.Equal("39.980", board.Rows[1].Sectors[1]);
        }
    }
}
