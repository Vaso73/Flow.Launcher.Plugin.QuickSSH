using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class SearchMatcherTests
    {
        [Fact]
        public void ContainsIgnoreAccents_MatchesEquivalentText()
        {
            Assert.True(SearchMatcher.ContainsIgnoreAccents("sérver", "server"));
        }

        [Fact]
        public void ScoreProfile_TransposedLongSearch_UsesFuzzyMatch()
        {
            Assert.Equal(4, SearchMatcher.ScoreProfile("sevrer", "server", "ssh host"));
        }

        [Fact]
        public void ScoreProfile_ShortTransposition_DoesNotEnableFuzzyMatch()
        {
            Assert.Equal(int.MaxValue, SearchMatcher.ScoreProfile("wbe", "web", "ssh host"));
        }

        [Fact]
        public void QuickSshWrapper_PreservesSearchMatcherResult()
        {
            int expected = SearchMatcher.ScoreProfile("prod", "myserver", "ssh user@prod.example.com");
            int actual = QuickSsh.ScoreProfile("prod", "myserver", "ssh user@prod.example.com");

            Assert.Equal(expected, actual);
        }
    }
}
