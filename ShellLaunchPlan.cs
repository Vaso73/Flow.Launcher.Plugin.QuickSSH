using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Flow.Launcher.Plugin.QuickSSH
{
    internal enum ShellLaunchPlanError
    {
        None,
        SelectedShellMissing,
        InvalidShellDefinition,
        ExecutableNotFound
    }

    /// <summary>
    /// Immutable process-launch plan for one SSH/SCP command.
    /// Custom-shell failures are represented as errors and never fall back to another shell.
    /// </summary>
    internal sealed class ShellLaunchPlan
    {
        internal ShellLaunchPlan(
            string fileName,
            string arguments,
            string shellName,
            bool usesDefaultShell)
        {
            FileName = fileName;
            Arguments = arguments;
            ShellName = shellName;
            UsesDefaultShell = usesDefaultShell;
        }

        internal string FileName { get; }
        internal string Arguments { get; }
        internal string ShellName { get; }
        internal bool UsesDefaultShell { get; }

        internal static bool TryCreate(
            string command,
            string? selectedShell,
            IDictionary<string, string>? customShells,
            Func<string, string?> resolveExecutable,
            string defaultCmdPath,
            out ShellLaunchPlan? plan,
            out ShellLaunchPlanError error)
        {
            plan = null;
            error = ShellLaunchPlanError.None;

            if (string.IsNullOrWhiteSpace(command))
            {
                error = ShellLaunchPlanError.InvalidShellDefinition;
                return false;
            }

            var shellName = selectedShell?.Trim();
            if (string.IsNullOrEmpty(shellName))
            {
                if (string.IsNullOrWhiteSpace(defaultCmdPath))
                {
                    error = ShellLaunchPlanError.ExecutableNotFound;
                    return false;
                }

                plan = new ShellLaunchPlan(
                    defaultCmdPath,
                    "/k " + command,
                    "cmd.exe",
                    usesDefaultShell: true);
                return true;
            }

            if (customShells == null || !customShells.TryGetValue(shellName, out var storedDefinition))
            {
                error = ShellLaunchPlanError.SelectedShellMissing;
                return false;
            }

            var definition = string.IsNullOrWhiteSpace(storedDefinition)
                ? shellName
                : storedDefinition.Trim();

            if (!TrySplitDefinition(definition, out var executable, out var prefixArguments))
            {
                error = ShellLaunchPlanError.InvalidShellDefinition;
                return false;
            }

            var resolvedExecutable = resolveExecutable(executable);
            if (string.IsNullOrWhiteSpace(resolvedExecutable))
            {
                error = ShellLaunchPlanError.ExecutableNotFound;
                return false;
            }

            var arguments = string.IsNullOrWhiteSpace(prefixArguments)
                ? command
                : prefixArguments + " " + command;

            plan = new ShellLaunchPlan(
                resolvedExecutable,
                arguments,
                shellName,
                usesDefaultShell: false);
            return true;
        }

        private static bool TrySplitDefinition(
            string definition,
            out string executable,
            out string prefixArguments)
        {
            executable = string.Empty;
            prefixArguments = string.Empty;

            if (string.IsNullOrWhiteSpace(definition) || ContainsForbiddenControl(definition))
                return false;

            var value = definition.Trim();
            if (value[0] == '"')
            {
                var closingQuote = value.IndexOf('"', 1);
                if (closingQuote <= 1)
                    return false;

                executable = value.Substring(1, closingQuote - 1);
                if (closingQuote + 1 < value.Length && !char.IsWhiteSpace(value[closingQuote + 1]))
                    return false;

                prefixArguments = closingQuote + 1 < value.Length
                    ? value.Substring(closingQuote + 1).TrimStart()
                    : string.Empty;
            }
            else
            {
                var separator = -1;
                for (var i = 0; i < value.Length; i++)
                {
                    if (!char.IsWhiteSpace(value[i]))
                        continue;

                    separator = i;
                    break;
                }

                if (separator < 0)
                {
                    executable = value;
                }
                else
                {
                    executable = value.Substring(0, separator);
                    prefixArguments = value.Substring(separator).TrimStart();
                }
            }

            return !string.IsNullOrWhiteSpace(executable) && executable.IndexOf('"') < 0;
        }

        private static bool ContainsForbiddenControl(string value) =>
            value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\0') >= 0;
    }

    /// <summary>
    /// Starts exactly one process from a validated launch plan.
    /// </summary>
    internal static class ShellCommandLauncher
    {
        internal static bool TryStart(
            ShellLaunchPlan plan,
            string workingDirectory,
            Func<ProcessStartInfo, Process?> processStarter,
            out Exception? error)
        {
            error = null;

            if (plan == null)
            {
                error = new ArgumentNullException(nameof(plan));
                return false;
            }

            if (processStarter == null)
            {
                error = new ArgumentNullException(nameof(processStarter));
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = plan.FileName,
                    Arguments = plan.Arguments,
                    UseShellExecute = true,
                    WorkingDirectory = workingDirectory
                };

                var process = processStarter(startInfo);
                if (process == null)
                {
                    error = new InvalidOperationException("The selected shell did not start a process.");
                    return false;
                }

                process.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }
    }
}
