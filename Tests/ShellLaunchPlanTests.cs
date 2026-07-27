using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class ShellLaunchPlanTests
    {
        [Fact]
        public void TryCreate_QuotedExecutablePathAndPrefixArguments_ArePreserved()
        {
            var shells = new Dictionary<string, string>
            {
                ["PowerShell"] = "\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\" -NoLogo"
            };
            string? observedExecutable = null;

            var success = ShellLaunchPlan.TryCreate(
                "ssh admin@server",
                "PowerShell",
                shells,
                executable =>
                {
                    observedExecutable = executable;
                    return executable;
                },
                @"C:\Windows\System32\cmd.exe",
                out var plan,
                out var error);

            Assert.True(success);
            Assert.Equal(ShellLaunchPlanError.None, error);
            var actualPlan = Assert.IsType<ShellLaunchPlan>(plan);
            Assert.Equal(@"C:\Program Files\PowerShell\7\pwsh.exe", observedExecutable);
            Assert.Equal(@"C:\Program Files\PowerShell\7\pwsh.exe", actualPlan.FileName);
            Assert.Equal("-NoLogo ssh admin@server", actualPlan.Arguments);
            Assert.False(actualPlan.UsesDefaultShell);
        }

        [Fact]
        public void TryCreate_SelectedShellMissing_FailsClosed()
        {
            var resolverCalls = 0;

            var success = ShellLaunchPlan.TryCreate(
                "ssh admin@server",
                "PowerShell",
                new Dictionary<string, string>(),
                executable =>
                {
                    resolverCalls++;
                    return executable;
                },
                @"C:\Windows\System32\cmd.exe",
                out var plan,
                out var error);

            Assert.False(success);
            Assert.Null(plan);
            Assert.Equal(ShellLaunchPlanError.SelectedShellMissing, error);
            Assert.Equal(0, resolverCalls);
        }

        [Fact]
        public void TryCreate_SelectedExecutableMissing_DoesNotCreateCmdFallback()
        {
            var shells = new Dictionary<string, string>
            {
                ["PowerShell"] = "missing-pwsh.exe -NoLogo"
            };

            var success = ShellLaunchPlan.TryCreate(
                "ssh admin@server",
                "PowerShell",
                shells,
                _ => null,
                @"C:\Windows\System32\cmd.exe",
                out var plan,
                out var error);

            Assert.False(success);
            Assert.Null(plan);
            Assert.Equal(ShellLaunchPlanError.ExecutableNotFound, error);
        }

        [Fact]
        public void TryCreate_NoSelectedShell_UsesExplicitDefaultCmdPlan()
        {
            var success = ShellLaunchPlan.TryCreate(
                "ssh admin@server",
                null,
                new Dictionary<string, string>(),
                _ => throw new InvalidOperationException("Resolver must not be called for the default shell."),
                @"C:\Windows\System32\cmd.exe",
                out var plan,
                out var error);

            Assert.True(success);
            Assert.Equal(ShellLaunchPlanError.None, error);
            var actualPlan = Assert.IsType<ShellLaunchPlan>(plan);
            Assert.True(actualPlan.UsesDefaultShell);
            Assert.Equal(@"C:\Windows\System32\cmd.exe", actualPlan.FileName);
            Assert.Equal("/k ssh admin@server", actualPlan.Arguments);
        }

        [Fact]
        public void TryStart_WhenProcessStartThrows_AttemptsExactlyOnce()
        {
            var plan = new ShellLaunchPlan(
                @"C:\Program Files\PowerShell\7\pwsh.exe",
                "-NoLogo ssh admin@server",
                "PowerShell",
                usesDefaultShell: false);
            var attempts = 0;

            var success = ShellCommandLauncher.TryStart(
                plan,
                @"C:\Users\test",
                _ =>
                {
                    attempts++;
                    throw new InvalidOperationException("launch failed");
                },
                out var error);

            Assert.False(success);
            Assert.Equal(1, attempts);
            var actualError = Assert.IsType<InvalidOperationException>(error);
            Assert.Equal("launch failed", actualError.Message);
        }

        [Fact]
        public void TryStart_Success_UsesPlanWithoutChangingItsShell()
        {
            var plan = new ShellLaunchPlan(
                @"C:\Program Files\PowerShell\7\pwsh.exe",
                "-NoLogo ssh admin@server",
                "PowerShell",
                usesDefaultShell: false);
            ProcessStartInfo? captured = null;

            var success = ShellCommandLauncher.TryStart(
                plan,
                @"C:\Users\test",
                startInfo =>
                {
                    captured = startInfo;
                    return new Process();
                },
                out var error);

            Assert.True(success);
            Assert.Null(error);
            var actualStartInfo = Assert.IsType<ProcessStartInfo>(captured);
            Assert.Equal(plan.FileName, actualStartInfo.FileName);
            Assert.Equal(plan.Arguments, actualStartInfo.Arguments);
            Assert.True(actualStartInfo.UseShellExecute);
            Assert.Equal(@"C:\Users\test", actualStartInfo.WorkingDirectory);
        }
    }
}
