using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;

namespace Flow.Launcher.Plugin.QuickSSH
{
    /// <summary>
    /// Pure helpers for the guided profile-create and rename flows.
    /// Keeps query parsing and profile construction outside the Flow Launcher UI handler.
    /// </summary>
    internal static class ProfileWizard
    {
        internal const string SavedKeyOption = "--key";
        internal const string DefaultAuthOption = "--default";
        internal const string PortOption = "--port";

        internal enum SshKeyFileKind
        {
            Missing,
            Private,
            Public,
            Unknown
        }

        /// <summary>
        /// Builds a rename query with the existing name duplicated as the editable value.
        /// The cursor remains at the end, so the user changes only the final token.
        /// </summary>
        internal static string BuildPrefilledRenameQuery(
            string actionKeyword,
            string commandPath,
            string currentName)
        {
            return string.Join(" ", new[]
            {
                (actionKeyword ?? string.Empty).Trim(),
                (commandPath ?? string.Empty).Trim(),
                currentName ?? string.Empty,
                currentName ?? string.Empty
            }).Trim();
        }

        /// <summary>
        /// Builds an explicit rename query with a chosen editable value.
        /// </summary>
        internal static string BuildRenameQuery(
            string actionKeyword,
            string commandPath,
            string currentName,
            string newName)
        {
            return string.Join(" ", new[]
            {
                (actionKeyword ?? string.Empty).Trim(),
                (commandPath ?? string.Empty).Trim(),
                currentName ?? string.Empty,
                newName ?? string.Empty
            }).Trim();
        }

        /// <summary>
        /// Returns the preferred example name when it is free, otherwise the next
        /// case-insensitively available numeric suffix.
        /// </summary>
        internal static string BuildAvailableName(
            string preferredName,
            IEnumerable<string> existingNames)
        {
            var preferred = string.IsNullOrWhiteSpace(preferredName)
                ? "item"
                : preferredName.Trim();
            var existing = new HashSet<string>(
                existingNames ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            return existing.Contains(preferred)
                ? BuildSuggestedName(preferred, existing)
                : preferred;
        }

        /// <summary>
        /// Returns a simple, case-insensitively unique rename suggestion.
        /// </summary>
        internal static string BuildSuggestedName(
            string currentName,
            IEnumerable<string> existingNames)
        {
            var baseName = string.IsNullOrWhiteSpace(currentName)
                ? "item"
                : currentName.Trim();
            var startSuffix = 2;
            var separator = baseName.LastIndexOf('-');
            if (separator > 0 && separator < baseName.Length - 1 &&
                int.TryParse(baseName.Substring(separator + 1), out var parsedSuffix) &&
                parsedSuffix >= 2 && parsedSuffix < int.MaxValue)
            {
                baseName = baseName.Substring(0, separator);
                startSuffix = parsedSuffix + 1;
            }

            var existing = new HashSet<string>(
                existingNames ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            for (var suffix = startSuffix; suffix < int.MaxValue; suffix++)
            {
                var suffixText = suffix.ToString();
                var maxBaseLength = 64 - suffixText.Length - 1;
                var candidateBase = baseName.Length > maxBaseLength
                    ? baseName.Substring(0, maxBaseLength)
                    : baseName;
                var candidate = candidateBase + "-" + suffixText;
                if (!existing.Contains(candidate))
                    return candidate;
            }

            return baseName + "-new";
        }

        /// <summary>
        /// Returns true when the input is an explicit advanced SSH/SCP command.
        /// Advanced commands keep the legacy free-form profile workflow intact.
        /// </summary>
        internal static bool IsAdvancedCommand(string? input)
        {
            var value = (input ?? string.Empty).TrimStart();
            return value.Equals("ssh", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("ssh ", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("scp", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("scp ", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Parses the guided syntax:
        /// destination, optional --port, and optional --default or --key alias.
        /// </summary>
        internal static bool TryParseBasicInput(
            string? input,
            out string destination,
            out string? keyAlias,
            out bool useDefaultAuthentication,
            out string? port)
        {
            destination = string.Empty;
            keyAlias = null;
            useDefaultAuthentication = false;
            port = null;

            var tokens = SshProfile.TokenizeShellLine((input ?? string.Empty).Trim());
            if (tokens.Count == 0)
                return false;

            destination = tokens[0];
            if (!TryParseDestination(destination, out _, out _))
                return false;

            var index = 1;
            if (index < tokens.Count &&
                tokens[index].Equals(PortOption, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= tokens.Count ||
                    !TryNormalizePort(tokens[index + 1], out port))
                    return false;
                index += 2;
            }

            if (index == tokens.Count)
                return true;

            if (index + 1 == tokens.Count &&
                tokens[index].Equals(DefaultAuthOption, StringComparison.OrdinalIgnoreCase))
            {
                useDefaultAuthentication = true;
                return true;
            }

            if (index + 2 == tokens.Count &&
                tokens[index].Equals(SavedKeyOption, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(tokens[index + 1]))
            {
                keyAlias = tokens[index + 1];
                return true;
            }

            return false;
        }

        internal static bool TryParseBasicInput(
            string? input,
            out string destination,
            out string? keyAlias,
            out bool useDefaultAuthentication)
        {
            return TryParseBasicInput(
                input, out destination, out keyAlias,
                out useDefaultAuthentication, out _);
        }

        internal static bool TryNormalizePort(string? value, out string? port)
        {
            port = null;
            if (!int.TryParse(
                (value ?? string.Empty).Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed) ||
                parsed < 1 || parsed > 65535)
                return false;

            port = parsed.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>
        /// Creates a structured SSH profile from a beginner-friendly destination, port, and optional key.
        /// </summary>
        internal static bool TryCreateBasicProfile(
            string destination,
            string? identityFile,
            string? port,
            out SshProfile profile)
        {
            profile = new SshProfile { Type = "ssh" };
            if (!TryParseDestination(destination, out var user, out var host))
                return false;

            string? normalizedPort = null;
            if (!string.IsNullOrWhiteSpace(port) &&
                !TryNormalizePort(port, out normalizedPort))
                return false;

            profile.User = user;
            profile.HostName = host;
            profile.Port = normalizedPort == "22" ? null : normalizedPort;
            profile.IdentityFile = string.IsNullOrWhiteSpace(identityFile)
                ? null
                : identityFile;
            profile.IdentitiesOnly = !string.IsNullOrWhiteSpace(identityFile);
            return true;
        }

        internal static bool TryCreateBasicProfile(
            string destination,
            string? identityFile,
            out SshProfile profile)
        {
            return TryCreateBasicProfile(destination, identityFile, null, out profile);
        }

        /// <summary>
        /// Expands environment variables and a leading ~ for local file checks.
        /// </summary>
        internal static string ExpandLocalPath(string? path)
        {
            var value = (path ?? string.Empty).Trim().Trim('"');
            value = Environment.ExpandEnvironmentVariables(value);

            if (value.Equals("~", StringComparison.Ordinal))
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (value.StartsWith("~/", StringComparison.Ordinal) ||
                value.StartsWith("~\\", StringComparison.Ordinal))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                value = Path.Combine(home, value.Substring(2));
            }

            return value;
        }

        /// <summary>
        /// Classifies a registered SSH key by its file content.
        /// This catches public keys even when their file name does not end in .pub.
        /// Unknown files fail closed and are never offered as connection identities.
        /// </summary>
        internal static SshKeyFileKind GetKeyFileKind(SshKeyEntry? entry)
        {
            return GetKeyFileKind(entry?.Path);
        }

        internal static SshKeyFileKind GetKeyFileKind(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return SshKeyFileKind.Missing;

            var expandedPath = ExpandLocalPath(path);
            if (!File.Exists(expandedPath))
                return SshKeyFileKind.Missing;

            if (expandedPath.EndsWith(".pub", StringComparison.OrdinalIgnoreCase))
                return SshKeyFileKind.Public;

            try
            {
                using var reader = new StreamReader(expandedPath, detectEncodingFromByteOrderMarks: true);
                var buffer = new char[4096];
                var length = reader.ReadBlock(buffer, 0, buffer.Length);
                var content = new string(buffer, 0, length).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

                if (content.StartsWith("-----BEGIN OPENSSH PRIVATE KEY-----", StringComparison.Ordinal) ||
                    content.StartsWith("-----BEGIN RSA PRIVATE KEY-----", StringComparison.Ordinal) ||
                    content.StartsWith("-----BEGIN DSA PRIVATE KEY-----", StringComparison.Ordinal) ||
                    content.StartsWith("-----BEGIN EC PRIVATE KEY-----", StringComparison.Ordinal) ||
                    content.StartsWith("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal) ||
                    content.StartsWith("-----BEGIN ENCRYPTED PRIVATE KEY-----", StringComparison.Ordinal) ||
                    content.StartsWith("PuTTY-User-Key-File-", StringComparison.Ordinal) ||
                    content.StartsWith("SSH PRIVATE KEY FILE FORMAT 1.1", StringComparison.Ordinal))
                    return SshKeyFileKind.Private;

                if (content.StartsWith("ssh-", StringComparison.Ordinal) ||
                    content.StartsWith("ecdsa-", StringComparison.Ordinal) ||
                    content.StartsWith("sk-ssh-", StringComparison.Ordinal) ||
                    content.StartsWith("sk-ecdsa-", StringComparison.Ordinal) ||
                    content.StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal) ||
                    content.StartsWith("---- BEGIN SSH2 PUBLIC KEY ----", StringComparison.Ordinal))
                    return SshKeyFileKind.Public;
            }
            catch (IOException)
            {
                return SshKeyFileKind.Unknown;
            }
            catch (UnauthorizedAccessException)
            {
                return SshKeyFileKind.Unknown;
            }

            return SshKeyFileKind.Unknown;
        }

        internal static bool IsUsablePrivateKey(SshKeyEntry? entry)
        {
            return GetKeyFileKind(entry) == SshKeyFileKind.Private;
        }

        /// <summary>
        /// Parses a conservative single-token SSH destination for the guided flow.
        /// Complex options remain available through the advanced full-command path.
        /// </summary>
        internal static bool TryParseDestination(
            string? destination,
            out string? user,
            out string host)
        {
            user = null;
            host = string.Empty;

            var value = (destination ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value) || value.Length > 255)
                return false;

            foreach (var c in value)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c) ||
                    c == '&' || c == '|' || c == ';' || c == '<' || c == '>' ||
                    c == '(' || c == ')' || c == '$' || c == '`' || c == '"' || c == '\'')
                    return false;
            }

            var at = value.IndexOf('@');
            if (at >= 0)
            {
                if (at == 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
                    return false;

                var candidateUser = value.Substring(0, at);
                if (!IsSafeUser(candidateUser))
                    return false;

                user = candidateUser;
                value = value.Substring(at + 1);
            }

            if (!IsSafeHost(value))
                return false;

            host = value;
            return true;
        }

        private static bool IsSafeUser(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
                return false;

            foreach (var c in value)
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-'))
                    return false;

            return true;
        }

        private static bool IsSafeHost(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("-", StringComparison.Ordinal))
                return false;

            // Bracketed IPv6 is accepted as a single safe destination token.
            if (value.Length >= 3 && value[0] == '[' && value[value.Length - 1] == ']')
            {
                for (var i = 1; i < value.Length - 1; i++)
                {
                    var c = value[i];
                    if (!(Uri.IsHexDigit(c) || c == ':' || c == '.'))
                        return false;
                }
                return true;
            }

            foreach (var c in value)
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-'))
                    return false;

            return true;
        }
    }
}
