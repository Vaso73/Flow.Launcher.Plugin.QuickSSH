using System.Collections.Generic;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class CommandInputGuardTests
    {
        [Theory]
        [InlineData("restart-nginx sudo systemctl restart nginx", "restart-nginx sudo systemctl restart nginx")]
        [InlineData("actions add restart-nginx uptime", "restart-nginx uptime")]
        [InlineData("ssh actions add restart-nginx uptime", "restart-nginx uptime")]
        [InlineData("ssh actions add ssh actions add restart-nginx uptime", "restart-nginx uptime")]
        public void NormalizeNestedCommandInput_RemovesRepeatedMenuPrefix(
            string input,
            string expected)
        {
            Assert.Equal(expected, CommandInputGuard.NormalizeNestedCommandInput(
                input, "ssh", "actions add"));
        }

        [Theory]
        [InlineData("server", true)]
        [InlineData("server-prod_1.example", true)]
        [InlineData("Žilina", true)]
        [InlineData("", false)]
        [InlineData("-server", false)]
        [InlineData("server name", false)]
        [InlineData("server/one", false)]
        public void IsValidSavedName_UsesQuerySafeNames(string name, bool expected)
        {
            Assert.Equal(expected, CommandInputGuard.IsValidSavedName(name));
        }

        [Theory]
        [InlineData("ssh")]
        [InlineData("Actions")]
        [InlineData("run")]
        [InlineData("use")]
        [InlineData("manage")]
        [InlineData("copy-pub")]
        public void ReservedNames_AreCaseInsensitive(string name)
        {
            Assert.True(CommandInputGuard.IsReservedSavedName(name));
        }

        [Fact]
        public void FindExistingName_IsCaseInsensitiveAndPreservesStoredSpelling()
        {
            var values = new Dictionary<string, int> { ["Prod-Server"] = 1 };
            Assert.Equal("Prod-Server", CommandInputGuard.FindExistingName(values, "prod-server"));
        }
    }
}
