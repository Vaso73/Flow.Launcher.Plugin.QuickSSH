using System;
using System.IO;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public sealed class ProfileWizardTests : IDisposable
    {
        private readonly string _tempDir;

        public ProfileWizardTests()
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "quickssh-profile-wizard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [Fact]
        public void BuildPrefilledRenameQuery_DuplicatesOnlyEditableName()
        {
            Assert.Equal(
                "ssh profiles rename dev-runtime dev-runtime",
                ProfileWizard.BuildPrefilledRenameQuery(
                    "ssh", "profiles rename", "dev-runtime"));
        }


        [Fact]
        public void BuildSuggestedName_SkipsExistingSuffixes()
        {
            Assert.Equal(
                "dev-runtime-4",
                ProfileWizard.BuildSuggestedName(
                    "dev-runtime",
                    new[] { "dev-runtime", "dev-runtime-2", "DEV-RUNTIME-3" }));

            Assert.Equal(
                "ssh profiles rename dev-runtime dev-runtime-4",
                ProfileWizard.BuildRenameQuery(
                    "ssh", "profiles rename", "dev-runtime", "dev-runtime-4"));

            Assert.Equal(
                "dev-runtime-4",
                ProfileWizard.BuildSuggestedName(
                    "dev-runtime-3",
                    new[] { "dev-runtime-3" }));

            var longSuggestion = ProfileWizard.BuildSuggestedName(
                new string('a', 64),
                Array.Empty<string>());
            Assert.Equal(64, longSuggestion.Length);
            Assert.EndsWith("-2", longSuggestion);
        }

        [Fact]
        public void BuildAvailableName_UsesPreferredNameOrNextFreeSuffix()
        {
            Assert.Equal(
                "server",
                ProfileWizard.BuildAvailableName("server", Array.Empty<string>()));

            Assert.Equal(
                "server-3",
                ProfileWizard.BuildAvailableName(
                    "server",
                    new[] { "server", "SERVER-2" }));
        }

        [Theory]
        [InlineData("server", null, "server")]
        [InlineData("vaio@10.0.0.10", "vaio", "10.0.0.10")]
        [InlineData("root@host_name.local", "root", "host_name.local")]
        [InlineData("root@[2001:db8::1]", "root", "[2001:db8::1]")]
        public void TryParseDestination_AcceptsSafeBeginnerTargets(
            string input,
            string? expectedUser,
            string expectedHost)
        {
            Assert.True(ProfileWizard.TryParseDestination(
                input, out var user, out var host));
            Assert.Equal(expectedUser, user);
            Assert.Equal(expectedHost, host);
        }

        [Theory]
        [InlineData("root@host;reboot")]
        [InlineData("root@host && whoami")]
        [InlineData("root@@host")]
        [InlineData("-oProxyCommand=bad")]
        public void TryParseDestination_RejectsUnsafeOrComplexInput(string input)
        {
            Assert.False(ProfileWizard.TryParseDestination(
                input, out _, out _));
        }

        [Fact]
        public void TryParseBasicInput_RecognizesPortAndAuthenticationChoice()
        {
            Assert.True(ProfileWizard.TryParseBasicInput(
                "vaio@dev --port 2222 --key private_key",
                out var destination,
                out var keyAlias,
                out var useDefault,
                out var port));

            Assert.Equal("vaio@dev", destination);
            Assert.Equal("private_key", keyAlias);
            Assert.False(useDefault);
            Assert.Equal("2222", port);

            Assert.True(ProfileWizard.TryParseBasicInput(
                "vaio@dev --port 22 --default",
                out destination,
                out keyAlias,
                out useDefault,
                out port));

            Assert.Equal("vaio@dev", destination);
            Assert.Null(keyAlias);
            Assert.True(useDefault);
            Assert.Equal("22", port);
        }

        [Theory]
        [InlineData("vaio@dev --port 0")]
        [InlineData("vaio@dev --port 65536")]
        [InlineData("vaio@dev --port abc")]
        [InlineData("vaio@dev --port")]
        public void TryParseBasicInput_RejectsInvalidPort(string input)
        {
            Assert.False(ProfileWizard.TryParseBasicInput(
                input, out _, out _, out _, out _));
        }

        [Fact]
        public void TryCreateBasicProfile_AddsPortIdentityAndIdentitiesOnly()
        {
            Assert.True(ProfileWizard.TryCreateBasicProfile(
                "vaio@dev",
                @"C:\Users\info\.ssh\private_key",
                "2222",
                out var profile));

            Assert.Equal("ssh", profile.Type);
            Assert.Equal("vaio", profile.User);
            Assert.Equal("dev", profile.HostName);
            Assert.Equal("2222", profile.Port);
            Assert.Equal(@"C:\Users\info\.ssh\private_key", profile.IdentityFile);
            Assert.True(profile.IdentitiesOnly);
            Assert.Contains("-p 2222", profile.ToCommandLine());
            Assert.Contains("-o IdentitiesOnly=yes", profile.ToCommandLine());

            Assert.True(ProfileWizard.TryCreateBasicProfile(
                "vaio@dev", null, "22", out var defaultPortProfile));
            Assert.Null(defaultPortProfile.Port);
        }

        [Fact]
        public void IsUsablePrivateKey_ValidatesContentNotOnlyFileName()
        {
            var privatePath = Path.Combine(_tempDir, "private_key");
            var publicPathWithoutPubSuffix = Path.Combine(_tempDir, "public_key");
            var unknownPath = Path.Combine(_tempDir, "not_a_key");
            File.WriteAllText(privatePath, "-----BEGIN OPENSSH PRIVATE KEY-----\nAAAA");
            File.WriteAllText(publicPathWithoutPubSuffix, "ssh-ed25519 AAAATEST user@host");
            File.WriteAllText(unknownPath, "plain text");

            Assert.Equal(
                ProfileWizard.SshKeyFileKind.Private,
                ProfileWizard.GetKeyFileKind(privatePath));
            Assert.Equal(
                ProfileWizard.SshKeyFileKind.Public,
                ProfileWizard.GetKeyFileKind(publicPathWithoutPubSuffix));
            Assert.Equal(
                ProfileWizard.SshKeyFileKind.Unknown,
                ProfileWizard.GetKeyFileKind(unknownPath));
            Assert.Equal(
                ProfileWizard.SshKeyFileKind.Missing,
                ProfileWizard.GetKeyFileKind(Path.Combine(_tempDir, "missing")));

            Assert.True(ProfileWizard.IsUsablePrivateKey(
                new SshKeyEntry { Path = privatePath }));
            Assert.False(ProfileWizard.IsUsablePrivateKey(
                new SshKeyEntry { Path = publicPathWithoutPubSuffix }));
            Assert.False(ProfileWizard.IsUsablePrivateKey(
                new SshKeyEntry { Path = unknownPath }));
        }

        [Theory]
        [InlineData("ssh root@server")]
        [InlineData("scp file root@server:/tmp/file")]
        public void IsAdvancedCommand_PreservesFullCommandWorkflow(string input)
        {
            Assert.True(ProfileWizard.IsAdvancedCommand(input));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
