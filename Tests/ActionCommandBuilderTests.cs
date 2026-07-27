using System.Collections.Generic;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class ActionCommandBuilderTests
    {
        [Fact]
        public void TryBuild_PreservesConnectionOptionsAndAddsRemoteCommand()
        {
            var profile = new SshProfile
            {
                HostName = "server.example",
                User = "admin",
                Port = "2222",
                IdentityFile = @"C:\Keys\admin key",
                IdentitiesOnly = true,
                ProxyJump = "jump.example",
                LocalForward = new List<string> { "8080:127.0.0.1:80" }
            };
            var action = new CommandProfile { Command = "sudo systemctl restart nginx" };

            var success = ActionCommandBuilder.TryBuild(profile, action, out var command);

            Assert.True(success);
            Assert.Contains("ssh", command);
            Assert.Contains("-p 2222", command);
            Assert.Contains("admin@server.example", command);
            Assert.Contains("-J jump.example", command);
            Assert.Contains("sudo systemctl restart nginx", command);
        }

        [Fact]
        public void TryBuild_DoesNotMutateStoredProfile()
        {
            var profile = new SshProfile
            {
                HostName = "server.example",
                RemoteCommand = "original-command",
                RequestTTY = "yes"
            };
            var action = new CommandProfile
            {
                Command = "uptime",
                RequestTTY = "force"
            };

            Assert.True(ActionCommandBuilder.TryBuild(profile, action, out var command));
            Assert.Equal("original-command", profile.RemoteCommand);
            Assert.Equal("yes", profile.RequestTTY);
            Assert.Contains("uptime", command);
            Assert.Contains("-t -t", command);
        }

        [Fact]
        public void TryBuild_RejectsScpUnsupportedAndUnsafeInputs()
        {
            Assert.False(ActionCommandBuilder.TryBuild(
                new SshProfile { Type = "scp", HostName = "server" },
                new CommandProfile { Command = "uptime" },
                out _));

            Assert.False(ActionCommandBuilder.TryBuild(
                new SshProfile { HostName = "server" },
                new CommandProfile { Kind = "unknown", Command = "uptime" },
                out _));

            Assert.False(ActionCommandBuilder.TryBuild(
                new SshProfile { HostName = "server" },
                new CommandProfile { Command = "line1\nline2" },
                out _));
        }

        [Fact]
        public void TryBuildDisplay_KeepsWindowsPathHumanReadable()
        {
            var profile = new SshProfile
            {
                HostName = "server.example",
                User = "admin",
                IdentityFile = @"C:\Users\info\.ssh\private_key"
            };
            var action = new CommandProfile { Command = "hostname" };

            Assert.True(ActionCommandBuilder.TryBuildDisplay(profile, action, out var display));
            Assert.Contains(@"C:\Users\info\.ssh\private_key", display);
            Assert.DoesNotContain(@"C:\\Users", display);
            Assert.Contains("hostname", display);
        }
    }
}
