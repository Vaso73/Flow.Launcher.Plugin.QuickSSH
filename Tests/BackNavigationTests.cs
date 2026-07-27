using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class BackNavigationTests
    {
        [Fact]
        public void BackNav_IsAlwaysTopScore()
        {
            Assert.Equal(int.MaxValue, QuickSsh.ScoreBackNavigation);
            Assert.Equal(int.MaxValue - 1, QuickSsh.ScoreSubMenuManagement);
            Assert.True(QuickSsh.ScoreBackNavigation > QuickSsh.ScoreSubMenuManagement);
        }

        [Fact]
        public void BackNav_OutranksEveryPrimarySubmenuRow()
        {
            Assert.True(QuickSsh.ScoreBackNavigation > QuickSsh.ScoreProfilesSavedItem);
            Assert.True(QuickSsh.ScoreBackNavigation > QuickSsh.ScoreActionsSavedItem);
            Assert.True(QuickSsh.ScoreBackNavigation > QuickSsh.ScoreShellSelected);
            Assert.True(QuickSsh.ScoreBackNavigation > QuickSsh.ScoreKeysSavedItem);
            Assert.True(QuickSsh.ScoreBackNavigation > QuickSsh.ScoreToolsKeys);
        }

        [Fact]
        public void ActionConfirmation_BackIsAboveRunAndCopy()
        {
            Assert.True(QuickSsh.ScoreActionsConfirmBack > QuickSsh.ScoreActionsConfirmRun);
            Assert.True(QuickSsh.ScoreActionsConfirmRun > QuickSsh.ScoreActionsConfirmCommand);
        }
    }
}
