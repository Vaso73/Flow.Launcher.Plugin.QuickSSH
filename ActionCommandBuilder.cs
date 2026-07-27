using System;
using System.Collections.Generic;

namespace Flow.Launcher.Plugin.QuickSSH
{
    /// <summary>
    /// Builds an SSH command for a saved action without mutating the stored connection profile.
    /// </summary>
    public static class ActionCommandBuilder
    {
        /// <summary>
        /// Creates a complete SSH command from a connection profile and a reusable remote action.
        /// Returns false for missing data, SCP profiles, unsupported action kinds, or unsafe commands.
        /// </summary>
        public static bool TryBuild(
            SshProfile profile,
            CommandProfile action,
            out string command)
        {
            command = string.Empty;
            if (!TryCreateExecutionProfile(profile, action, out var executionProfile))
                return false;

            command = executionProfile.ToCommandLine();
            return !string.IsNullOrWhiteSpace(command);
        }

        /// <summary>
        /// Creates the same command in display form so Windows paths remain human-readable.
        /// </summary>
        public static bool TryBuildDisplay(
            SshProfile profile,
            CommandProfile action,
            out string command)
        {
            command = string.Empty;
            if (!TryCreateExecutionProfile(profile, action, out var executionProfile))
                return false;

            command = executionProfile.ToDisplayString();
            return !string.IsNullOrWhiteSpace(command);
        }

        private static bool TryCreateExecutionProfile(
            SshProfile profile,
            CommandProfile action,
            out SshProfile executionProfile)
        {
            executionProfile = null;

            if (profile == null || action == null)
                return false;

            if (string.Equals(profile.Type, "scp", StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrWhiteSpace(profile.HostName))
                return false;

            if (!action.IsSupportedKind || !CommandProfile.IsSafeToStore(action.Command))
                return false;

            executionProfile = new SshProfile
            {
                Type = "ssh",
                HostName = profile.HostName,
                User = profile.User,
                Port = profile.Port,
                IdentityFile = profile.IdentityFile,
                IdentitiesOnly = profile.IdentitiesOnly,
                RemoteCommand = action.Command,
                RequestTTY = string.IsNullOrWhiteSpace(action.RequestTTY)
                    ? profile.RequestTTY
                    : action.RequestTTY,
                LocalForward = profile.LocalForward == null
                    ? null
                    : new List<string>(profile.LocalForward),
                RemoteForward = profile.RemoteForward == null
                    ? null
                    : new List<string>(profile.RemoteForward),
                DynamicForward = profile.DynamicForward,
                ProxyJump = profile.ProxyJump,
                ProxyCommand = profile.ProxyCommand,
                ExtraArgs = profile.ExtraArgs
            };

            return true;
        }
    }
}
