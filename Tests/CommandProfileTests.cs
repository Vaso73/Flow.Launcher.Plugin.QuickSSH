using Newtonsoft.Json;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class CommandProfileTests
    {
        [Fact]
        public void Defaults_ToSupportedRemoteCommandKind()
        {
            var action = new CommandProfile { Command = "uptime" };
            Assert.Equal(CommandProfile.RemoteCommandKind, action.Kind);
            Assert.True(action.IsSupportedKind);
        }

        [Fact]
        public void UnknownKind_IsNotSupported()
        {
            var action = new CommandProfile { Kind = "unknown", Command = "uptime" };
            Assert.False(action.IsSupportedKind);
        }

        [Theory]
        [InlineData("uptime", true)]
        [InlineData("sudo systemctl restart nginx", true)]
        [InlineData("", false)]
        [InlineData("line1\nline2", false)]
        [InlineData("abc\0def", false)]
        [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----", false)]
        [InlineData("-----BEGIN RSA PRIVATE KEY-----", false)]
        public void IsSafeToStore_RejectsUnsafePayloads(string command, bool expected)
        {
            Assert.Equal(expected, CommandProfile.IsSafeToStore(command));
        }

        [Fact]
        public void RoundTrip_PreservesSupportedFieldsWithoutDeadConfirmationState()
        {
            var source = new CommandProfile
            {
                Command = "systemctl restart nginx",
                Description = "Restart nginx",
                RequestTTY = "force"
            };

            var json = JsonConvert.SerializeObject(source);
            var loaded = JsonConvert.DeserializeObject<CommandProfile>(json);

            Assert.NotNull(loaded);
            Assert.Equal(source.Command, loaded!.Command);
            Assert.Equal(source.Description, loaded.Description);
            Assert.Equal("force", loaded.RequestTTY);
            Assert.DoesNotContain("RequireConfirmation", json);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void LegacyJson_WithRequireConfirmation_IsStillReadable(bool legacyValue)
        {
            var json = $$"""
                {
                  "Kind": "remote-command",
                  "Command": "uptime",
                  "RequireConfirmation": {{legacyValue.ToString().ToLowerInvariant()}}
                }
                """;

            var loaded = JsonConvert.DeserializeObject<CommandProfile>(json);

            Assert.NotNull(loaded);
            Assert.Equal("uptime", loaded!.Command);
            Assert.True(loaded.IsSupportedKind);
            Assert.Null(typeof(CommandProfile).GetProperty("RequireConfirmation"));
        }

        [Fact]
        public void Model_HasNoPrivateKeyContentProperty()
        {
            Assert.Null(typeof(CommandProfile).GetProperty("PrivateKey"));
            Assert.Null(typeof(CommandProfile).GetProperty("PrivateKeyContent"));
        }
    }
}
