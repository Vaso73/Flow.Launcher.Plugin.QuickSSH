using System;
using System.Diagnostics;
using System.IO;

namespace Flow.Launcher.Plugin.QuickSSH
{
    /// <summary>
    /// Utility helpers for SSH detection and executable resolution.
    /// </summary>
    public abstract class Utils
    {
        /// <summary>
        /// Resolves the full path of an executable using the 'where' command.
        /// Returns the original name if resolution fails.
        /// </summary>
        public static string ResolveExecutable(string exeName) =>
            TryResolveExecutable(exeName, out var resolvedPath)
                ? resolvedPath
                : exeName;

        /// <summary>
        /// Resolves an executable to an existing file. Unlike <see cref="ResolveExecutable"/>,
        /// this method fails when neither an explicit path nor PATH lookup can find the program.
        /// </summary>
        internal static bool TryResolveExecutable(string exeName, out string resolvedPath)
        {
            resolvedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(exeName))
                return false;

            var candidate = exeName.Trim();
            if (Path.IsPathRooted(candidate))
            {
                if (!File.Exists(candidate))
                    return false;

                resolvedPath = Path.GetFullPath(candidate);
                return true;
            }

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where.exe",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.StartInfo.ArgumentList.Add(candidate);
                process.Start();
                var output = process.StandardOutput.ReadLine();
                process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                    return false;

                var path = output.Trim();
                if (!File.Exists(path))
                    return false;

                resolvedPath = Path.GetFullPath(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks whether ssh-keygen is available on the system.
        /// </summary>
        public static bool IsSshKeygenInstalled()
        {
            // Check the default Windows built-in OpenSSH location first.
            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (File.Exists(Path.Combine(systemDir, "OpenSSH", "ssh-keygen.exe")))
                return true;

            // Fallback: search PATH via the 'where' command.
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "ssh-keygen",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sanitises an alias string so it is safe to use as a file name.
        /// Replaces spaces with underscores and removes characters that are
        /// illegal in Windows file names.
        /// Returns <see langword="null"/> if the result is empty.
        /// </summary>
        public static string? SanitizeKeyFileName(string? alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
                return null;

            var name = alias.Trim().Replace(' ', '_');

            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in invalid)
                name = name.Replace(c.ToString(), "");

            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>
        /// Splits a generate-command argument string into an alias and an optional
        /// custom path.  Uses <see cref="System.Char.IsWhiteSpace(char)"/> to find the
        /// boundary so that non-breaking spaces and other Unicode whitespace
        /// characters are handled correctly.
        /// Surrounding quotes on the custom path are stripped.
        /// </summary>
        internal static (string alias, string customPath) ParseGenerateArgs(string? rest)
        {
            if (string.IsNullOrEmpty(rest))
                return ("", "");

            // Advance past the alias (first non-whitespace token).
            int i = 0;
            while (i < rest.Length && !char.IsWhiteSpace(rest[i]))
                i++;

            var alias = rest.Substring(0, i);

            // Everything after the first whitespace run is the custom path.
            var customPath = i < rest.Length
                ? rest.Substring(i).TrimStart()
                : "";

            // Strip surrounding quotes.
            if (customPath.Length >= 2 &&
                customPath[0] == '"' &&
                customPath[customPath.Length - 1] == '"')
            {
                customPath = customPath.Substring(1, customPath.Length - 2);
            }

            return (alias, customPath);
        }

        /// <summary>
        /// Checks whether an SSH client is installed on the system.
        /// </summary>
        public static bool IsSshInstalled()
        {
            // First, check the default Windows built-in OpenSSH location
            // (available since Windows 10 version 1809 / Windows Server 2019).
            // Environment.SpecialFolder.System resolves to %SystemRoot%\System32.
            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (File.Exists(Path.Combine(systemDir, "OpenSSH", "ssh.exe")))
                return true;

            // Fallback: search PATH via the 'where' command and rely on its exit code.
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "ssh",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                // Drain both streams to prevent deadlock; we only need the exit code.
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}