using SoundBar.Helpers;
using Windows.Graphics;
using Xunit;

namespace SoundBar.Tests
{
    public class WindowPlacementHelperTests
    {
        private static readonly RectInt32 WorkArea = new RectInt32(0, 0, 1920, 1040);

        [Fact]
        public void ClampToWorkArea_WhenWindowFitsOnScreen_LeavesItAlone()
        {
            var window = new RectInt32(100, 100, 400, 500);

            var result = WindowPlacementHelper.ClampToWorkArea(window, WorkArea);

            Assert.Equal(window, result);
        }

        [Fact]
        public void ClampToWorkArea_WhenWindowIsOffTheRightEdge_PullsItBackOn()
        {
            // e.g. it was last on a second monitor that's now unplugged
            var window = new RectInt32(3000, 200, 400, 500);

            var result = WindowPlacementHelper.ClampToWorkArea(window, WorkArea);

            Assert.Equal(1920 - 400, result.X);
            Assert.Equal(200, result.Y);
        }

        [Fact]
        public void ClampToWorkArea_WhenWindowIsAboveAndLeftOfScreen_PullsItBackOn()
        {
            var window = new RectInt32(-500, -300, 400, 500);

            var result = WindowPlacementHelper.ClampToWorkArea(window, WorkArea);

            Assert.Equal(0, result.X);
            Assert.Equal(0, result.Y);
        }

        [Fact]
        public void ClampToWorkArea_WhenWindowIsBiggerThanScreen_ShrinksIt()
        {
            var window = new RectInt32(0, 0, 4000, 3000);

            var result = WindowPlacementHelper.ClampToWorkArea(window, WorkArea);

            Assert.Equal(1920, result.Width);
            Assert.Equal(1040, result.Height);
        }

        [Fact]
        public void ClampToWorkArea_RespectsMonitorsThatDontStartAtZero()
        {
            var secondMonitor = new RectInt32(-1920, 0, 1920, 1040);
            var window = new RectInt32(-100, 100, 400, 500);

            var result = WindowPlacementHelper.ClampToWorkArea(window, secondMonitor);

            Assert.Equal(-400, result.X);
        }

        [Theory]
        [InlineData(-32000, -32000, true)]
        [InlineData(-32000, 100, true)]
        [InlineData(-1920, 100, false)]
        [InlineData(100, 100, false)]
        public void IsMinimisedPosition_DetectsWindowsParkingSpot(double left, double top, bool expected)
        {
            Assert.Equal(expected, WindowPlacementHelper.IsMinimisedPosition(left, top));
        }
    }
}
