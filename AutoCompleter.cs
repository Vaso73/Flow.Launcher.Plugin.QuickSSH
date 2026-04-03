using System.Collections.Generic;
using System.Linq;

namespace Flow.Launcher.Plugin.QuickSSH
{
    /// <summary>
    /// Provides TAB auto-completion suggestions for the plugin.
    /// </summary>
    public class AutoCompleter
    {
        /// <summary>
        /// Top-level commands visible in the autocomplete / suggestion UI.
        /// </summary>
        private static readonly string[] VisibleCommands = new[]
        {
            "profiles", "shell", "config", "help"
        };

        /// <summary>
        /// Score step used to separate adjacent top-level commands.
        /// A gap of this size ensures Flow Launcher's usage-history bonus
        /// (typically &lt; 1 000 points) cannot reorder the top-level menu.
        /// </summary>
        private const int TopLevelScoreStep = 10000;

        /// <summary>
        /// Sub-commands of "profiles" that appear in suggestions after "profiles ".
        /// </summary>
        private static readonly string[] ProfilesSubCommands = new[]
        {
            "add", "remove", "rename", "copy", "export", "import"
        };

        /// <summary>
        /// Sub-commands of "shell" that appear in suggestions after "shell ".
        /// </summary>
        private static readonly string[] ShellSubCommands = new[]
        {
            "add", "remove"
        };

        /// <summary>
        /// Returns auto-completion suggestions for the given partial input.
        /// </summary>
        public static List<Result> GetSuggestions(
            string actionKeyword,
            string input,
            UserData userData,
            string iconPath,
            IPublicAPI api = null)
        {
            var results = new List<Result>();
            var trimmed = input.Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(trimmed))
            {
                // Show all visible top-level commands in defined order.
                // Assign descending scores with a gap of TopLevelScoreStep (10 000) so that
                // Flow Launcher's usage-history bonus — which can add hundreds to ~1 000 points
                // for frequently-selected results — can never bridge the gap between adjacent
                // commands and reorder them at runtime.
                // A gap of only 1 (e.g. 4/3/2/1) is not safe: a user who has previously
                // selected "config" more than "shell" would see config boosted above shell.
                for (int i = 0; i < VisibleCommands.Length; i++)
                {
                    var cmd = VisibleCommands[i];
                    var autoText = actionKeyword + " " + cmd + " ";
                    results.Add(new Result
                    {
                        Title = cmd,
                        SubTitle = GetCommandDescription(cmd),
                        IcoPath = iconPath,
                        Score = (VisibleCommands.Length - i) * TopLevelScoreStep,
                        Action = _ =>
                        {
                            api?.ChangeQuery(autoText, true);
                            return false;
                        },
                        AutoCompleteText = autoText
                    });
                }
                return results;
            }

            // When the input is exactly a known top-level command name with no trailing space
            // or arguments, return no suggestions. The Query() switch routes that verb to its
            // dedicated handler, which owns the result list.
            // "profiles " (with trailing space) is intentionally excluded from this guard —
            // it signals the user is about to type a sub-command or profile name.
            if (!input.TrimStart().Contains(' ') && System.Array.IndexOf(VisibleCommands, trimmed) >= 0)
                return results;

            // Match visible top-level commands that start with the input
            var matchingCommands = VisibleCommands
                .Where(c => c.StartsWith(trimmed))
                .ToList();

            foreach (var cmd in matchingCommands)
            {
                var autoText = actionKeyword + " " + cmd + " ";
                results.Add(new Result
                {
                    Title = cmd,
                    SubTitle = GetCommandDescription(cmd),
                    IcoPath = iconPath,
                    Action = _ =>
                    {
                        api?.ChangeQuery(autoText, true);
                        return false;
                    },
                    AutoCompleteText = autoText
                });
            }

            // After "profiles " suggest sub-commands and profile names.
            var prefixCheck = input.TrimStart().ToLowerInvariant();
            bool isProfilesPrefix = prefixCheck.StartsWith("profiles ");
            if (isProfilesPrefix)
            {
                var search = prefixCheck.Substring(9); // length of "profiles "

                // Suggest matching sub-commands first
                foreach (var sub in ProfilesSubCommands)
                {
                    if (string.IsNullOrEmpty(search) || sub.StartsWith(search))
                    {
                        var autoText = actionKeyword + " profiles " + sub + " ";
                        results.Add(new Result
                        {
                            Title = sub,
                            SubTitle = GetProfilesSubCommandDescription(sub),
                            IcoPath = iconPath,
                            Action = _ =>
                            {
                                api?.ChangeQuery(autoText, true);
                                return false;
                            },
                            AutoCompleteText = autoText
                        });
                    }
                }

                // Suggest profile names when the search is not an exact sub-command match.
                // This lets "profiles add" route to the add handler while "profiles wor" still
                // shows matching profile names.
                bool isExactSubCmd = ProfilesSubCommands.Any(s => s == search);
                if (!isExactSubCmd && userData?.Profiles != null)
                {
                    foreach (var entry in userData.Profiles)
                    {
                        var profileName = entry.Key;
                        var profileDisplay = entry.Value?.ToDisplayString() ?? "";

                        if (string.IsNullOrEmpty(search) ||
                            profileName.ToLowerInvariant().Contains(search))
                        {
                            var autoText = actionKeyword + " profiles " + profileName;
                            results.Add(new Result
                            {
                                Title = profileName,
                                SubTitle = profileDisplay,
                                IcoPath = iconPath,
                                Action = _ =>
                                {
                                    api?.ChangeQuery(autoText, true);
                                    return false;
                                },
                                AutoCompleteText = autoText
                            });
                        }
                    }
                }
            }

            // After "shell " suggest sub-commands.
            bool isShellPrefix = prefixCheck.StartsWith("shell ");
            if (isShellPrefix)
            {
                var search = prefixCheck.Substring(6); // length of "shell "

                foreach (var sub in ShellSubCommands)
                {
                    if (string.IsNullOrEmpty(search) || sub.StartsWith(search))
                    {
                        var autoText = actionKeyword + " shell " + sub + " ";
                        results.Add(new Result
                        {
                            Title = sub,
                            SubTitle = GetShellSubCommandDescription(sub),
                            IcoPath = iconPath,
                            Action = _ =>
                            {
                                api?.ChangeQuery(autoText, true);
                                return false;
                            },
                            AutoCompleteText = autoText
                        });
                    }
                }
            }

            return results;
        }

        private static string GetCommandDescription(string command)
        {
            var key = command switch
            {
                "profiles" => "plugin_quickssh_subtitle_commandprofiles",
                "config"   => "plugin_quickssh_subtitle_commandconfig_usage",
                "shell"    => "plugin_quickssh_subtitle_commandshell_help",
                "help"     => "plugin_quickssh_subtitle_commandhelp_usage",
                _ => null
            };
            return key != null ? QuickSsh.GetTranslation(key) : "";
        }

        private static string GetProfilesSubCommandDescription(string subCmd)
        {
            var key = subCmd switch
            {
                "add"    => "plugin_quickssh_subtitle_commandprofiles_add",
                "remove" => "plugin_quickssh_subtitle_commandprofiles_remove",
                "rename" => "plugin_quickssh_subtitle_commandprofiles_rename",
                "copy"   => "plugin_quickssh_subtitle_commandprofiles_copy_usage",
                "export" => "plugin_quickssh_subtitle_commandprofiles_export_usage",
                "import" => "plugin_quickssh_subtitle_commandprofiles_import_usage",
                _ => null
            };
            return key != null ? QuickSsh.GetTranslation(key) : "";
        }

        private static string GetShellSubCommandDescription(string subCmd)
        {
            var key = subCmd switch
            {
                "add"    => "plugin_quickssh_subtitle_commandshell_add_usage",
                "remove" => "plugin_quickssh_subtitle_commandshell_remove",
                _ => null
            };
            return key != null ? QuickSsh.GetTranslation(key) : "";
        }
    }
}
