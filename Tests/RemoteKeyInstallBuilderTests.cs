using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class RemoteKeyInstallBuilderTests
    {
        // ── ValidatePublicKeyLine ─────────────────────────────────────────────────

        [Theory]
        [InlineData("ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIExampleBase64Key user@host")]
        [InlineData("ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAAB user@host")]
        [InlineData("ecdsa-sha2-nistp256 AAAAE2VjZHNhLXNoYTItbmlzdHAyNTY user@host")]
        [InlineData("ecdsa-sha2-nistp384 AAAAE2VjZHNhLXNoYTItbmlzdHAzODQ user@host")]
        [InlineData("ecdsa-sha2-nistp521 AAAAE2VjZHNhLXNoYTItbmlzdHA1MjE user@host")]
        [InlineData("sk-ssh-ed25519@openssh.com AAAAGnNrLXNzaC1lZDI1NTE5 user@host")]
        [InlineData("sk-ecdsa-sha2-nistp256@openssh.com AAAAInNrLWVjZHNh user@host")]
        public void ValidatePublicKeyLine_AcceptsValidKeyTypes(string line)
        {
            Assert.True(RemoteKeyInstallBuilder.ValidatePublicKeyLine(line));
        }

        [Fact]
        public void ValidatePublicKeyLine_AcceptsKeyWithoutComment()
        {
            // Only <type> <base64>, no comment — still valid.
            Assert.True(RemoteKeyInstallBuilder.ValidatePublicKeyLine(
                "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIExampleBase64Key"));
        }

        [Fact]
        public void ValidatePublicKeyLine_AcceptsKeyWithMultiWordComment()
        {
            // <type> <base64> <multi-word comment> — optional comment payload is allowed.
            Assert.True(RemoteKeyInstallBuilder.ValidatePublicKeyLine(
                "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAI user@host some extra text"));
        }

        [Fact]
        public void ValidatePublicKeyLine_RejectsNull()
        {
            Assert.False(RemoteKeyInstallBuilder.ValidatePublicKeyLine(null));
        }

        [Fact]
        public void ValidatePublicKeyLine_RejectsEmpty()
        {
            Assert.False(RemoteKeyInstallBuilder.ValidatePublicKeyLine(""));
        }

        [Fact]
        public void ValidatePublicKeyLine_RejectsUnknownKeyType()
        {
            Assert.False(RemoteKeyInstallBuilder.ValidatePublicKeyLine(
                "unknown-type AAAAB3NzaC1yc2EAAAADAQABAAAB user@host"));
        }

        [Fact]
        public void ValidatePublicKeyLine_RejectsSingleToken()
        {
            // Only key type, no base64.
            Assert.False(RemoteKeyInstallBuilder.ValidatePublicKeyLine("ssh-ed25519"));
        }

        [Fact]
        public void ValidatePublicKeyLine_RejectsSingleQuote()
        {
            Assert.False(RemoteKeyInstallBuilder.ValidatePublicKeyLine(
                "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAI user's key"));
        }

        [Fact]
        public void ValidatePublicKeyLine_RejectsDoubleQuote()
        {
            Assert.False(RemoteKeyInstallBuilder.ValidatePublicKeyLine(
                "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAI user\"s key"));
        }

        [Fact]
        public void ValidatePublicKeyLine_RejectsNewline()
        {
            Assert.False(RemoteKeyInstallBuilder.ValidatePublicKeyLine(
                "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAI\nuser@host"));
        }

        [Fact]
        public void ValidatePublicKeyLine_RejectsCarriageReturn()
        {
            Assert.False(RemoteKeyInstallBuilder.ValidatePublicKeyLine(
                "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAI\ruser@host"));
        }

        [Fact]
        public void ValidatePublicKeyLine_RejectsNullByte()
        {
            Assert.False(RemoteKeyInstallBuilder.ValidatePublicKeyLine(
                "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAI\0user@host"));
        }

        [Fact]
        public void ValidatePublicKeyLine_RejectsKeyTypeWithTrailingSpaceOnly()
        {
            // "ssh-ed25519 " — space after type but nothing else.
            Assert.False(RemoteKeyInstallBuilder.ValidatePublicKeyLine("ssh-ed25519 "));
        }

        // ── BuildBootstrapCommand ─────────────────────────────────────────────────

        [Fact]
        public void BuildBootstrapCommand_ContainsUmask()
        {
            var cmd = RemoteKeyInstallBuilder.BuildBootstrapCommand("ssh-ed25519 AAAA user@host");
            Assert.Contains("umask 077", cmd);
        }

        [Fact]
        public void BuildBootstrapCommand_ContainsMkdirAndChmod()
        {
            var cmd = RemoteKeyInstallBuilder.BuildBootstrapCommand("ssh-ed25519 AAAA user@host");
            Assert.Contains("mkdir -p ~/.ssh", cmd);
            Assert.Contains("chmod 700 ~/.ssh", cmd);
            Assert.Contains("chmod 600 ~/.ssh/authorized_keys", cmd);
        }

        [Fact]
        public void BuildBootstrapCommand_ContainsGrepForIdempotency()
        {
            var pubKey = "ssh-ed25519 AAAA user@host";
            var cmd = RemoteKeyInstallBuilder.BuildBootstrapCommand(pubKey);
            Assert.Contains("grep -qxF '" + pubKey + "'", cmd);
        }

        [Fact]
        public void BuildBootstrapCommand_UsesPrintfInsteadOfEcho()
        {
            var cmd = RemoteKeyInstallBuilder.BuildBootstrapCommand("ssh-ed25519 AAAA user@host");
            Assert.Contains("printf '%s\\n'", cmd);
        }

        [Fact]
        public void BuildBootstrapCommand_EmbedsFullKeyLine()
        {
            var pubKey = "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAAB admin@server";
            var cmd = RemoteKeyInstallBuilder.BuildBootstrapCommand(pubKey);
            // Key should appear twice: once in grep and once in printf
            var count = 0;
            var idx = 0;
            while ((idx = cmd.IndexOf(pubKey, idx)) >= 0)
            {
                count++;
                idx += pubKey.Length;
            }
            Assert.Equal(2, count);
        }

        [Fact]
        public void BuildBootstrapCommand_ContainsSuccessMessage()
        {
            var cmd = RemoteKeyInstallBuilder.BuildBootstrapCommand("ssh-ed25519 AAAA user@host");
            Assert.Contains(RemoteKeyInstallBuilder.SuccessMessage, cmd);
        }

        [Fact]
        public void BuildBootstrapCommand_ContainsFailureMessage()
        {
            var cmd = RemoteKeyInstallBuilder.BuildBootstrapCommand("ssh-ed25519 AAAA user@host");
            Assert.Contains(RemoteKeyInstallBuilder.FailureMessage, cmd);
        }

        [Fact]
        public void BuildBootstrapCommand_SuccessAfterBootstrapSuccess()
        {
            // The success echo must be guarded by && so it only fires on success.
            var cmd = RemoteKeyInstallBuilder.BuildBootstrapCommand("ssh-ed25519 AAAA user@host");
            var successIdx = cmd.IndexOf(RemoteKeyInstallBuilder.SuccessMessage);
            Assert.True(successIdx > 0, "success message must be present");

            // Find the "&&" that precedes the success echo
            var beforeSuccess = cmd.Substring(0, successIdx);
            Assert.Contains("&& echo", beforeSuccess.Substring(beforeSuccess.LastIndexOf(';')));
        }

        [Fact]
        public void BuildBootstrapCommand_FailureAfterBootstrapFailure()
        {
            // The failure echo must be guarded by || so it only fires on failure.
            var cmd = RemoteKeyInstallBuilder.BuildBootstrapCommand("ssh-ed25519 AAAA user@host");
            var failureIdx = cmd.IndexOf(RemoteKeyInstallBuilder.FailureMessage);
            Assert.True(failureIdx > 0, "failure message must be present");

            // Find the "||" that precedes the failure echo
            var beforeFailure = cmd.Substring(0, failureIdx);
            Assert.Contains("|| echo", beforeFailure.Substring(beforeFailure.LastIndexOf("&&")));
        }

        [Fact]
        public void BuildBootstrapCommand_IdempotencyLogicPreserved()
        {
            // The grep || printf pattern must still be present, ensuring
            // the key is only appended when not already present.
            var pubKey = "ssh-ed25519 AAAA user@host";
            var cmd = RemoteKeyInstallBuilder.BuildBootstrapCommand(pubKey);
            Assert.Contains("grep -qxF '" + pubKey + "' ~/.ssh/authorized_keys || " +
                "printf '%s\\n' '" + pubKey + "' >> ~/.ssh/authorized_keys", cmd);
        }

        [Fact]
        public void BuildBootstrapCommand_MessagingDoesNotContainDoubleQuotes()
        {
            // Success/failure messages use single quotes to stay safe inside
            // the double-quoted outer wrapper of BuildFullSshCommand.
            var cmd = RemoteKeyInstallBuilder.BuildBootstrapCommand("ssh-ed25519 AAAA user@host");
            // Extract just the messaging tail (after the last "; }")
            var messagingStart = cmd.LastIndexOf("; }");
            Assert.True(messagingStart > 0);
            var tail = cmd.Substring(messagingStart);
            Assert.DoesNotContain("\"", tail);
        }

        // ── BuildFullSshCommand ───────────────────────────────────────────────────

        [Fact]
        public void BuildFullSshCommand_WrapsWithSshAndDoubleQuotes()
        {
            var bootstrap = RemoteKeyInstallBuilder.BuildBootstrapCommand("ssh-ed25519 AAAA user@host");
            var full = RemoteKeyInstallBuilder.BuildFullSshCommand("admin@10.0.0.1", bootstrap);

            Assert.StartsWith("ssh admin@10.0.0.1 \"", full);
            Assert.EndsWith("\"", full);
        }

        [Fact]
        public void BuildFullSshCommand_ContainsBootstrapContent()
        {
            var bootstrap = RemoteKeyInstallBuilder.BuildBootstrapCommand("ssh-ed25519 AAAA user@host");
            var full = RemoteKeyInstallBuilder.BuildFullSshCommand("admin@server", bootstrap);

            Assert.Contains("umask 077", full);
            Assert.Contains("authorized_keys", full);
        }

        [Fact]
        public void BuildFullSshCommand_NoNestedSingleQuoteConflict()
        {
            // The inner single-quoted segments ('KEY', '%s\n') must NOT collide
            // with the outer wrapper.  Previous bug: outer single quotes conflicted
            // with inner single quotes, producing shell-invalid nesting.
            var pubKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIExampleBase64Key testuser@testhost";
            var bootstrap = RemoteKeyInstallBuilder.BuildBootstrapCommand(pubKey);
            var full = RemoteKeyInstallBuilder.BuildFullSshCommand("root@10.0.0.150", bootstrap);

            // Outer wrapper must be double quotes — not single quotes.
            Assert.StartsWith("ssh root@10.0.0.150 \"", full);
            Assert.EndsWith("\"", full);

            // The inner bootstrap must still contain its single-quoted segments.
            Assert.Contains("'" + pubKey + "'", full);
            Assert.Contains("'%s\\n'", full);

            // There must be no pattern where an outer single quote opens and
            // an inner single quote prematurely closes it.  With double-quote
            // wrapping the outer and single-quote wrapping the inner, this is
            // structurally impossible — verify anyway.
            var outerContent = full.Substring(full.IndexOf('"') + 1,
                full.LastIndexOf('"') - full.IndexOf('"') - 1);
            Assert.DoesNotContain("\"", outerContent);
        }

        [Fact]
        public void BuildFullSshCommand_BootstrapHasNoDoubleQuotes()
        {
            // The bootstrap command itself must not contain double quotes,
            // otherwise wrapping it in double quotes would break.
            var pubKey = "ssh-ed25519 AAAA user@host";
            var bootstrap = RemoteKeyInstallBuilder.BuildBootstrapCommand(pubKey);
            Assert.DoesNotContain("\"", bootstrap);
        }

        [Fact]
        public void BuildFullSshCommand_CopyCommandIsCleanSsh()
        {
            // The COPY command must be a clean SSH command without any local
            // shell wrapper (no trailing || or &&).
            var pubKey = "ssh-ed25519 AAAA user@host";
            var bootstrap = RemoteKeyInstallBuilder.BuildBootstrapCommand(pubKey);
            var copyCmd = RemoteKeyInstallBuilder.BuildFullSshCommand("admin@server", bootstrap);
            Assert.StartsWith("ssh admin@server \"", copyCmd);
            Assert.EndsWith("\"", copyCmd);
            // Must NOT contain a local failure guard after the closing quote.
            var afterLastQuote = copyCmd.Substring(copyCmd.LastIndexOf('"') + 1);
            Assert.Empty(afterLastQuote);
        }

        // ── BuildRunCommand ───────────────────────────────────────────────────────

        [Fact]
        public void BuildRunCommand_ContainsLocalFailureWrapper()
        {
            // The RUN command must contain a local || echo guard that fires
            // when the SSH connection itself fails.
            var pubKey = "ssh-ed25519 AAAA user@host";
            var bootstrap = RemoteKeyInstallBuilder.BuildBootstrapCommand(pubKey);
            var runCmd = RemoteKeyInstallBuilder.BuildRunCommand("admin@server", bootstrap);
            Assert.Contains("|| echo " + RemoteKeyInstallBuilder.FailureMessage, runCmd);
        }

        [Fact]
        public void BuildRunCommand_ContainsSshCommand()
        {
            // The RUN command must still contain the full SSH invocation
            // so the success path (remote bootstrap) works.
            var pubKey = "ssh-ed25519 AAAA user@host";
            var bootstrap = RemoteKeyInstallBuilder.BuildBootstrapCommand(pubKey);
            var copyCmd = RemoteKeyInstallBuilder.BuildFullSshCommand("admin@server", bootstrap);
            var runCmd = RemoteKeyInstallBuilder.BuildRunCommand("admin@server", bootstrap);
            Assert.StartsWith(copyCmd, runCmd);
        }

        [Fact]
        public void BuildRunCommand_FailureGuardIsAfterSshCommand()
        {
            // The local failure guard (|| echo ...) must appear AFTER the full
            // SSH command so that it only triggers on SSH connection failure.
            var pubKey = "ssh-ed25519 AAAA user@host";
            var bootstrap = RemoteKeyInstallBuilder.BuildBootstrapCommand(pubKey);
            var copyCmd = RemoteKeyInstallBuilder.BuildFullSshCommand("admin@server", bootstrap);
            var runCmd = RemoteKeyInstallBuilder.BuildRunCommand("admin@server", bootstrap);
            // The run command should be: copyCmd + " || echo FAILURE"
            var suffix = runCmd.Substring(copyCmd.Length);
            Assert.StartsWith(" || echo ", suffix);
        }

        [Fact]
        public void BuildRunCommand_RunAndCopyAreDifferent()
        {
            // Run and Copy intentionally use different commands: Run has
            // a local failure wrapper, Copy does not.
            var pubKey = "ssh-ed25519 AAAA user@host";
            var bootstrap = RemoteKeyInstallBuilder.BuildBootstrapCommand(pubKey);
            var copyCmd = RemoteKeyInstallBuilder.BuildFullSshCommand("admin@server", bootstrap);
            var runCmd = RemoteKeyInstallBuilder.BuildRunCommand("admin@server", bootstrap);
            Assert.NotEqual(copyCmd, runCmd);
        }

        // ── IsValidUserAtHost ─────────────────────────────────────────────────────

        [Theory]
        [InlineData("admin@10.0.0.1", true)]
        [InlineData("root@server.example.com", true)]
        [InlineData("user@host", true)]
        [InlineData("user.name_1@host-1.example_lab", true)]
        [InlineData("", false)]
        [InlineData("noatsign", false)]
        [InlineData("@host", false)]       // empty user
        [InlineData("user@", false)]       // empty host
        [InlineData("user @host", false)]  // space in destination
        [InlineData(" user@host", false)]
        [InlineData("user@host ", false)]
        [InlineData("user@@host", false)]
        [InlineData("-oProxyCommand@host", false)]
        [InlineData("user@-oProxyCommand", false)]
        [InlineData("user@host:22", false)]
        public void IsValidUserAtHost_ValidatesCorrectly(string input, bool expected)
        {
            Assert.Equal(expected, RemoteKeyInstallBuilder.IsValidUserAtHost(input));
        }

        [Theory]
        [InlineData("user@host&calc")]
        [InlineData("user@host|whoami")]
        [InlineData("user@host;whoami")]
        [InlineData("user@host>file")]
        [InlineData("user@host<file")]
        [InlineData("user@host^whoami")]
        [InlineData("user@\"host")]
        [InlineData("\"user@host")]
        [InlineData("user@host/path")]
        [InlineData("user@host\\path")]
        public void IsValidUserAtHost_RejectsShellMetacharacters(string input)
        {
            Assert.False(RemoteKeyInstallBuilder.IsValidUserAtHost(input));
        }

        [Fact]
        public void BuildFullSshCommand_RejectsUnsafeDestination()
        {
            var bootstrap = RemoteKeyInstallBuilder.BuildBootstrapCommand("ssh-ed25519 AAAA user@host");

            Assert.Throws<System.ArgumentException>(() =>
                RemoteKeyInstallBuilder.BuildFullSshCommand("user@host&calc", bootstrap));
        }

        [Fact]
        public void BuildRunCommand_RejectsUnsafeDestination()
        {
            var bootstrap = RemoteKeyInstallBuilder.BuildBootstrapCommand("ssh-ed25519 AAAA user@host");

            Assert.Throws<System.ArgumentException>(() =>
                RemoteKeyInstallBuilder.BuildRunCommand("user@host|whoami", bootstrap));
        }

        [Fact]
        public void IsValidUserAtHost_RejectsNull()
        {
            Assert.False(RemoteKeyInstallBuilder.IsValidUserAtHost(null));
        }
    }
}
