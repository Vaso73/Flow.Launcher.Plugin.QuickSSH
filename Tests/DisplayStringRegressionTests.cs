using System;
using System.IO;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    /// <summary>
    /// Source-inspection regression tests that verify Main.cs uses the correct
    /// display vs execution string methods for saved-profile UI subtitles
    /// and clipboard operations.
    /// </summary>
    public class DisplayStringRegressionTests
    {
        private static string? GetMainCsSource()
        {
            var mainCsPath = Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "Main.cs");
            if (!File.Exists(mainCsPath))
                mainCsPath = Path.GetFullPath(mainCsPath);
            return File.Exists(mainCsPath) ? File.ReadAllText(mainCsPath) : null;
        }

        // ── SubTitle display uses ToDisplayString ─────────────────────────────────

        [Fact]
        public void MainCs_ProfileListSubTitle_UsesToDisplayString()
        {
            var source = GetMainCsSource();
            Assert.NotNull(source); // Fail clearly if source not available

            // Verify ToDisplayString() is called for display purposes
            Assert.Contains("ToDisplayString()", source);
        }

        [Fact]
        public void MainCs_ProfileSubTitle_NeverDirectlyUsesToCommandLine()
        {
            var source = GetMainCsSource();
            Assert.NotNull(source);

            // After the fix, SubTitle assignments for saved profiles must not use
            // ToCommandLine() directly — regardless of variable name.
            Assert.DoesNotMatch(
                @"SubTitle\s*=\s*\w+\.ToCommandLine\(\)",
                source);
            Assert.DoesNotMatch(
                @"SubTitle\s*=\s*\w+\?\s*\.ToCommandLine\(\)",
                source);
        }

        // ── Execution uses ToCommandLine, clipboard uses ToDisplayString ─────────────────────────────────────

        [Fact]
        public void MainCs_RunCommand_UsesToCommandLine()
        {
            var source = GetMainCsSource();
            Assert.NotNull(source);

            // RunCommand must be called with 'cmd' derived from ToCommandLine(),
            // not from ToDisplayString(). Verify RunCommand(cmd) is present.
            Assert.Contains("RunCommand(cmd)", source);
        }

        [Fact]
        public void MainCs_CopyToClipboard_UsesDisplayString()
        {
            var source = GetMainCsSource();
            Assert.NotNull(source);

            // CopyToClipboard for profiles copy must receive the display-friendly string
            // (displayCmd from ToDisplayString), not the execution string (cmd from ToCommandLine).
            Assert.Matches(
                @"CopyToClipboard\(displayCmd\b",
                source);
        }

        [Fact]
        public void MainCs_CopyToClipboard_DoesNotUseExecutionString()
        {
            var source = GetMainCsSource();
            Assert.NotNull(source);

            // After the fix, CopyToClipboard must NOT receive 'cmd' (the execution string).
            Assert.DoesNotMatch(
                @"CopyToClipboard\(cmd\b",
                source);
        }

        // ── ToDisplayString exists and is separate from ToCommandLine ─────────────

        [Fact]
        public void SshProfile_ToDisplayString_IsNotDelegatingToToCommandLine()
        {
            // ToDisplayString must NOT simply delegate to ToCommandLine().
            // Verify by constructing a profile with a Windows path and checking they differ.
            var p = new SshProfile
            {
                Type = "ssh",
                User = "root",
                HostName = "host",
                IdentityFile = @"C:\Users\test\.ssh\key"
            };
            Assert.NotEqual(p.ToCommandLine(), p.ToDisplayString());
        }

        // ── Display quoting: conditional quoting based on whitespace ────────────

        [Fact]
        public void ToDisplayString_WindowsPathWithoutSpaces_NoQuotes()
        {
            // Path without spaces must appear unquoted in display output.
            var p = new SshProfile
            {
                Type = "ssh",
                User = "root",
                HostName = "10.0.0.150",
                IdentityFile = @"C:\Users\info\.ssh\public_key"
            };
            var display = p.ToDisplayString();

            Assert.Equal(@"ssh -i C:\Users\info\.ssh\public_key root@10.0.0.150", display);
        }

        [Fact]
        public void ToDisplayString_WindowsPathWithSpaces_Quoted()
        {
            // Path with spaces must be wrapped in double-quotes in display output.
            var p = new SshProfile
            {
                Type = "ssh",
                User = "root",
                HostName = "10.0.0.150",
                IdentityFile = @"C:\Users\info\My Keys\public key"
            };
            var display = p.ToDisplayString();

            Assert.Equal("ssh -i \"C:\\Users\\info\\My Keys\\public key\" root@10.0.0.150", display);
        }

        [Fact]
        public void CopiedCommand_WindowsIdentityPath_ContainsSingleBackslashes()
        {
            // Display/copy output must contain single backslashes, not escaped doubles.
            var p = new SshProfile
            {
                Type = "ssh",
                User = "root",
                HostName = "10.0.0.150",
                IdentityFile = @"C:\Users\info\.ssh\public_key"
            };
            var display = p.ToDisplayString();

            Assert.Contains(@"C:\Users\info\.ssh\public_key", display);
            Assert.DoesNotContain(@"C:\\Users", display);
        }

        // ── ToCommandLine execution contract unchanged ───────────────────────────

        [Fact]
        public void ToCommandLine_WindowsPath_UsesExecutionEscaping()
        {
            // ToCommandLine must preserve shell-escaping: backslashes doubled, path quoted.
            var p = new SshProfile
            {
                Type = "ssh",
                User = "root",
                HostName = "10.0.0.150",
                IdentityFile = @"C:\Users\info\.ssh\public_key"
            };
            var cmd = p.ToCommandLine();

            // Execution string must have escaped backslashes
            Assert.Contains(@"C:\\Users\\info\\.ssh\\public_key", cmd);
            // Must be different from display string
            Assert.NotEqual(cmd, p.ToDisplayString());
        }
    }
}
