using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Flow.Launcher.Plugin.QuickSSH
{
    /// <summary>
    /// Flow Launcher plugin for managing and launching SSH connections.
    /// </summary>
    public class QuickSsh : IPlugin, IPluginI18n
    {
        private static PluginInitContext _pluginContext;
        private ProfileManager _profileManager;

        private const string CommandProfiles = "profiles";
        private const string CommandCustomShell = "shell";
        private const string CommandKeys = "keys";
        private const string CommandConfig = "config";
        private const string CommandHelp = "help";

        /// <summary>
        /// All recognised top-level command verbs.
        /// Used in the Query default case to prevent exact command names from
        /// accidentally being routed to the autocomplete / implicit-SSH paths.
        /// </summary>
        private static readonly string[] AllCommandVerbs = new[]
        {
            CommandProfiles, CommandCustomShell, CommandKeys, CommandConfig, CommandHelp, "add"
        };

        // Sub-commands of "profiles"
        private const string ProfilesSubAdd    = "add";
        private const string ProfilesSubRemove = "remove";
        private const string ProfilesSubRename = "rename";
        private const string ProfilesSubCopy   = "copy";
        private const string ProfilesSubExport = "export";
        private const string ProfilesSubImport = "import";

        private static readonly string[] ProfilesSubCommands = new[]
        {
            ProfilesSubAdd, ProfilesSubRemove, ProfilesSubRename,
            ProfilesSubCopy, ProfilesSubExport, ProfilesSubImport
        };

        // Sub-commands of "shell"
        private static readonly string[] ShellSubCommands = new[]
        {
            "add", "remove"
        };

        // Sub-commands of "keys"
        private const string KeysSubAdd      = "add";
        private const string KeysSubGenerate = "generate";
        private const string KeysSubInstall  = "install";
        private const string KeysSubRemove   = "remove";
        private const string KeysSubRename   = "rename";
        private const string KeysSubCopyPath = "copy-path";
        private const string KeysSubCopyPub  = "copy-pub";
        private const string KeysSubScan     = "scan";

        private static readonly string[] KeysSubCommands = new[]
        {
            KeysSubAdd, KeysSubGenerate, KeysSubInstall, KeysSubRemove, KeysSubRename, KeysSubCopyPath, KeysSubCopyPub, KeysSubScan
        };

        private const string AppIconPath = "Images\\app.png";
        private const string AppIconGreenPath = "Images\\app-green.png";
        private const string AppIconRedPath = "Images\\app-red.png";

        // ── Submenu ordering scores (Flow Launcher sorts higher score first) ──────
        // Consistent layout for every submenu:
        //   1. management/usage row  (ScoreSubMenuManagement = int.MaxValue)
        //   2. action rows           (ScoreXxxAction* range, ≥ 1010)
        //   3. saved items           (starting from ScoreXxxSavedItem = 500, decremented per entry)
        //
        // Action row scores must be in the 1000+ range so they cannot be bridged by
        // Flow Launcher's built-in fuzzy-match bonus that can boost Score=0 results.
        internal const int ScoreSubMenuManagement   = int.MaxValue;

        // Back-navigation row — always pinned directly below the usage/management hint and
        // above every action row.  Allows users to return to the parent command level by
        // pressing Enter on the first actionable row instead of manually clearing text.
        internal const int ScoreBackNavigation = int.MaxValue - 1;

        // "profiles" submenu — mirrors the scale used by the "shell" submenu.
        internal const int ScoreProfilesActionAdd    = 1060;
        internal const int ScoreProfilesActionRemove = 1050;
        internal const int ScoreProfilesActionRename = 1040;
        internal const int ScoreProfilesActionCopy   = 1030;
        internal const int ScoreProfilesActionExport = 1020;
        internal const int ScoreProfilesActionImport = 1010;
        internal const int ScoreProfilesSavedItem    = 500;  // decremented per additional profile

        // "shell" submenu — action rows must be strictly above any shell entry
        internal const int ScoreShellActionAdd    = 1100;
        internal const int ScoreShellActionRemove = 1050;
        internal const int ScoreShellSelected     = 1000;
        internal const int ScoreShellOtherStart   = 500; // decremented per additional shell

        // ── Top-level command ordering (root "ssh" menu) ────────────────────────
        // Gaps of 100 000 ensure Flow Launcher's internal usage-history / fuzzy-match
        // bonus (which can add thousands of points for frequently-selected items)
        // cannot reorder the root menu.
        internal const int ScoreTopLevelProfiles = 500_000;
        internal const int ScoreTopLevelKeys     = 400_000;
        internal const int ScoreTopLevelShell    = 300_000;
        internal const int ScoreTopLevelConfig   = 200_000;
        internal const int ScoreTopLevelHelp     = 100_000;

        // "keys" submenu — action rows above saved key entries
        // Gaps of 1000 prevent Flow Launcher's usage-history bonus from reordering rows.
        internal const int ScoreKeysActionInstall  = 9000;
        internal const int ScoreKeysActionAdd      = 8000;
        internal const int ScoreKeysActionGenerate = 7000;
        internal const int ScoreKeysActionRemove   = 6000;
        internal const int ScoreKeysActionRename   = 5000;
        internal const int ScoreKeysActionCopyPath = 4000;
        internal const int ScoreKeysActionCopyPub  = 3000;
        internal const int ScoreKeysActionScan     = 2000;
        internal const int ScoreKeysSavedItem      = 500;  // decremented per additional key

        private string _databasePath;
        private string _dataDir;
        private bool _isSshInstalled = true;
        private bool _isDatabaseCreated = true;

        public void Init(PluginInitContext context)
        {
            _pluginContext = context;

            _dataDir = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "data");

            try
            {
                _databasePath = ProfileStorage.PrepareProfilesPath(
                    context.CurrentPluginMetadata.PluginSettingsDirectoryPath);
                _profileManager = new ProfileManager(_databasePath);
            }
            catch
            {
                _isDatabaseCreated = false;
            }

            _isSshInstalled = Utils.IsSshInstalled();
        }

        public List<Result> Query(Query query)
        {
            var results = new List<Result>();

            if (!_isSshInstalled)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_sshnotinstalled_title"),
                    SubTitle = GetTranslation("plugin_quickssh_sshnotinstalled_subtitle"),
                    IcoPath = AppIconPath
                });
                return results;
            }

            if (!_isDatabaseCreated)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_databasenotcreated_title"),
                    SubTitle = GetTranslation("plugin_quickssh_databasenotcreated_subtitle"),
                    IcoPath = AppIconPath
                });
                return results;
            }

            var input = query.Search?.Trim() ?? "";

            if (string.IsNullOrEmpty(input))
            {
                // Show all command suggestions for TAB auto-completion
                results.AddRange(AutoCompleter.GetSuggestions(
                    query.ActionKeyword, "",
                    _profileManager?.UserData, AppIconPath,
                    _pluginContext?.API));
                return results;
            }

            var parts = input.Split(new[] { ' ' }, 2);
            var verb = parts[0].ToLowerInvariant();
            var rest = parts.Length > 1 ? parts[1].Trim() : "";

            switch (verb)
            {
                case CommandProfiles:
                    results.AddRange(HandleProfiles(query, rest));
                    break;
                case CommandCustomShell:
                    results.AddRange(HandleShell(query, rest));
                    break;
                case CommandKeys:
                    results.AddRange(HandleKeys(query, rest));
                    break;
                case CommandConfig:
                    results.AddRange(HandleConfig(query, rest));
                    break;
                case CommandHelp:
                    results.AddRange(HandleDocs(query));
                    break;

                // Legacy top-level "add" is no longer the canonical command.
                // Show an explicit user-facing redirect so the user is never left confused.
                case "add":
                    results.AddRange(HandleLegacyAddRedirect(query, rest));
                    break;
                default:
                    // Guard: if the verb exactly matches any known command it should have
                    // been handled by one of the cases above. Reaching here means either a
                    // future refactoring gap or an unexpected call path. Return empty to
                    // prevent unrelated top-level suggestions from appearing in a command view.
                    if (System.Array.IndexOf(AllCommandVerbs, verb) >= 0)
                        break;

                    // If the input looks like a direct SSH destination or option string,
                    // treat it as an implicit direct-connect.
                    if (IsImplicitSshInput(input))
                    {
                        results.AddRange(HandleDirectConnect(query, input));
                    }
                    else
                    {
                        // Show auto-complete suggestions for partial command names.
                        results.AddRange(AutoCompleter.GetSuggestions(
                            query.ActionKeyword, input,
                            _profileManager?.UserData, AppIconPath,
                            _pluginContext?.API));
                    }
                    break;
            }

            return results;
        }

        #region Command Handlers

        private List<Result> HandleProfiles(Query query, string rest)
        {
            var parts = rest.Split(new[] { ' ' }, 2);
            var subCmd = parts[0].ToLowerInvariant();
            var subRest = parts.Length > 1 ? parts[1].Trim() : "";

            switch (subCmd)
            {
                case ProfilesSubAdd:    return HandleProfilesAdd(query, subRest);
                case ProfilesSubRemove: return HandleProfilesRemove(query, subRest);
                case ProfilesSubRename: return HandleProfilesRename(query, subRest);
                case ProfilesSubCopy:   return HandleProfilesCopy(query, subRest);
                case ProfilesSubExport: return HandleProfilesExport(query);
                case ProfilesSubImport: return HandleProfilesImport(query, subRest);
                default:
                    // Mirror the top-level matching pattern: when the partial input
                    // is a prefix of one or more sub-commands, delegate to the
                    // autocompleter so that "profiles a" suggests "add" the same way
                    // "ssh p" suggests "profiles" at the top level.
                    if (!string.IsNullOrEmpty(subCmd) &&
                        ProfilesSubCommands.Any(s => s.StartsWith(subCmd)))
                    {
                        return new List<Result>(AutoCompleter.GetSuggestions(
                            query.ActionKeyword, "profiles " + rest,
                            _profileManager?.UserData, AppIconPath,
                            _pluginContext?.API));
                    }
                    return HandleProfilesList(query, rest);
            }
        }

        // ── profiles (list / connect) ─────────────────────────────────────────────

        private List<Result> HandleProfilesList(Query query, string search)
        {
            var results = new List<Result>();
            var profiles = _profileManager.UserData.Profiles;

            // 1. Management/usage hint — always pinned at the top.
            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandprofiles"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandprofiles"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " profiles ",
                Score = ScoreSubMenuManagement
            });

            // 2. Back-navigation row — returns to top-level command list.
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " ", query.ActionKeyword));

            // 3. Action rows — always above saved profiles.
            //    Only shown when no search text is active (user is browsing, not filtering).
            if (string.IsNullOrEmpty(search))
            {
                var profileSubCmds = new[]
                {
                    ("add",    GetTranslation("plugin_quickssh_title_commandprofiles_add"),    GetTranslation("plugin_quickssh_subtitle_commandprofiles_add"),          ScoreProfilesActionAdd),
                    ("remove", GetTranslation("plugin_quickssh_title_commandprofiles_remove"), GetTranslation("plugin_quickssh_subtitle_commandprofiles_remove"),       ScoreProfilesActionRemove),
                    ("rename", GetTranslation("plugin_quickssh_title_commandprofiles_rename"), GetTranslation("plugin_quickssh_subtitle_commandprofiles_rename"),       ScoreProfilesActionRename),
                    ("copy",   GetTranslation("plugin_quickssh_title_commandprofiles_copy"),   GetTranslation("plugin_quickssh_subtitle_commandprofiles_copy_usage"),   ScoreProfilesActionCopy),
                    ("export", GetTranslation("plugin_quickssh_title_commandprofiles_export"), GetTranslation("plugin_quickssh_subtitle_commandprofiles_export_usage"), ScoreProfilesActionExport),
                    ("import", GetTranslation("plugin_quickssh_title_commandprofiles_import"), GetTranslation("plugin_quickssh_subtitle_commandprofiles_import_usage"), ScoreProfilesActionImport),
                };
                foreach (var (scName, scTitle, scSubTitle, scScore) in profileSubCmds)
                {
                    var autoText = query.ActionKeyword + " profiles " + scName + " ";
                    results.Add(new Result
                    {
                        Title = scTitle,
                        SubTitle = scSubTitle,
                        IcoPath = AppIconPath,
                        AutoCompleteText = autoText,
                        Score = scScore,
                        Action = _ =>
                        {
                            _pluginContext?.API?.ChangeQuery(autoText, true);
                            return false;
                        }
                    });
                }
            }

            // 4. Saved profiles — always below action rows.
            //    Use a decremented score (starting from ScoreProfilesSavedItem) so each
            //    profile has a distinct, explicit value — mirroring how the shell submenu
            //    uses ScoreShellOtherStart--. This prevents Flow Launcher's fuzzy-match
            //    bonus from boosting any profile above the action rows.
            if (profiles.Count == 0)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandprofiles"),
                    SubTitle = GetTranslation("plugin_quickssh_noprofiles"),
                    IcoPath = AppIconPath,
                    Score = ScoreProfilesSavedItem
                });
            }
            else
            {
                var scored = new List<(int score, string name, SshProfile profile)>();

                foreach (var entry in profiles)
                {
                    var displayCmd = entry.Value?.ToDisplayString() ?? "";
                    if (string.IsNullOrEmpty(search))
                    {
                        scored.Add((0, entry.Key, entry.Value));
                    }
                    else
                    {
                        int score = ScoreProfile(search, entry.Key, displayCmd);
                        if (score < int.MaxValue)
                            scored.Add((score, entry.Key, entry.Value));
                    }
                }

                int profileScore = ScoreProfilesSavedItem;
                foreach (var item in scored.OrderBy(s => s.score))
                {
                    var name = item.name;
                    var profile = item.profile;
                    var cmd = profile?.ToCommandLine() ?? "";
                    var displayCmd = profile?.ToDisplayString() ?? "";
                    results.Add(new Result
                    {
                        Title = name,
                        SubTitle = displayCmd,
                        IcoPath = AppIconGreenPath,
                        Score = profileScore--,
                        Action = _ =>
                        {
                            RunCommand(cmd);
                            return true;
                        },
                        AutoCompleteText = query.ActionKeyword + " profiles " + name
                    });
                }
            }

            return results;
        }

        // ── legacy "add" redirect ─────────────────────────────────────────────────

        /// <summary>
        /// The top-level "add" command was removed in v2.  All profile operations are now
        /// sub-commands of "profiles".  This handler shows an unambiguous user-facing message
        /// rather than silently falling through to autocomplete or implicit-SSH detection.
        /// </summary>
        private List<Result> HandleLegacyAddRedirect(Query query, string rest)
        {
            var redirectTarget = query.ActionKeyword + " profiles add " + rest;
            return new List<Result>
            {
                // Pinned hint at the top.
                new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandadd_legacy"),
                    SubTitle = GetTranslation("plugin_quickssh_subtitle_commandadd_legacy"),
                    IcoPath = AppIconPath,
                    AutoCompleteText = redirectTarget,
                    Score = int.MaxValue,
                    Action = _ =>
                    {
                        _pluginContext?.API?.ChangeQuery(redirectTarget, true);
                        return false;
                    }
                }
            };
        }

        // ── profiles add ──────────────────────────────────────────────────────────

        private List<Result> HandleProfilesAdd(Query query, string rest)
        {
            var results = new List<Result>();

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandprofiles_add"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandprofiles_add"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " profiles add ",
                Score = int.MaxValue
            });
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " profiles ", query.ActionKeyword + " profiles"));

            if (string.IsNullOrEmpty(rest))
                return results;

            var addParts = rest.Split(new[] { ' ' }, 2);
            var profileName = addParts[0];
            var rawCommand = addParts.Length > 1 ? addParts[1].Trim() : "";

            if (!string.IsNullOrEmpty(rawCommand))
            {
                // Normalise: strip cmd-style /flags, ensure "ssh " prefix.
                var sshCommand = NormalizeSshCommand(rawCommand) ?? "";
                if (!string.IsNullOrEmpty(sshCommand))
                {
                    var profile = SshProfile.ParseFromLegacyCommand(sshCommand);
                    var displayCmd = profile.ToDisplayString();
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_save_label") + " " + profileName,
                        SubTitle = displayCmd,
                        IcoPath = AppIconGreenPath,
                        Action = _ =>
                        {
                            _profileManager.UserData.Profiles[profileName] = profile;
                            _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " profiles ", true);
                            return false;
                        }
                    });
                }
            }

            return results;
        }

        // ── profiles remove ───────────────────────────────────────────────────────

        private List<Result> HandleProfilesRemove(Query query, string rest)
        {
            var results = new List<Result>();
            var profiles = _profileManager.UserData.Profiles;

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandprofiles_remove"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandprofiles_remove"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " profiles remove ",
                Score = int.MaxValue
            });
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " profiles ", query.ActionKeyword + " profiles"));

            if (profiles.Count == 0)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandprofiles_remove"),
                    SubTitle = GetTranslation("plugin_quickssh_noprofiles"),
                    IcoPath = AppIconPath
                });
                return results;
            }

            foreach (var entry in profiles)
            {
                if (!string.IsNullOrEmpty(rest) &&
                    !entry.Key.ToLowerInvariant().Contains(rest.ToLowerInvariant()))
                    continue;

                var cmd = entry.Value?.ToDisplayString() ?? "";
                results.Add(new Result
                {
                    Title = entry.Key,
                    SubTitle = cmd,
                    IcoPath = AppIconRedPath,
                    AutoCompleteText = query.ActionKeyword + " profiles remove " + entry.Key,
                    Action = _ =>
                    {
                        _profileManager.UserData.Profiles.Remove(entry.Key);
                        _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " profiles ", true);
                        return false;
                    }
                });
            }

            return results;
        }

        // ── profiles rename ───────────────────────────────────────────────────────

        private List<Result> HandleProfilesRename(Query query, string rest)
        {
            var results = new List<Result>();
            var profiles = _profileManager.UserData.Profiles;

            var parts = rest.Split(new[] { ' ' }, 2);
            var oldName = parts[0].Trim();
            var newName = parts.Length > 1 ? parts[1].Trim() : "";

            if (string.IsNullOrEmpty(oldName))
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandprofiles_rename"),
                    SubTitle = GetTranslation("plugin_quickssh_subtitle_commandprofiles_rename"),
                    IcoPath = AppIconPath,
                    AutoCompleteText = query.ActionKeyword + " profiles rename ",
                    Score = int.MaxValue
                });
                results.Add(MakeBackNavResult(query, query.ActionKeyword + " profiles ", query.ActionKeyword + " profiles"));

                if (profiles.Count == 0)
                {
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_title_commandprofiles_rename"),
                        SubTitle = GetTranslation("plugin_quickssh_noprofiles"),
                        IcoPath = AppIconPath
                    });
                    return results;
                }

                foreach (var entry in profiles)
                {
                    var name = entry.Key;
                    var autoText = query.ActionKeyword + " profiles rename " + name + " ";
                    results.Add(new Result
                    {
                        Title = name,
                        SubTitle = entry.Value?.ToDisplayString() ?? "",
                        IcoPath = AppIconPath,
                        AutoCompleteText = autoText,
                        Action = _ =>
                        {
                            _pluginContext?.API?.ChangeQuery(autoText, true);
                            return false;
                        }
                    });
                }
                return results;
            }

            if (!profiles.ContainsKey(oldName))
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandprofiles_rename"),
                    SubTitle = GetTranslation("plugin_quickssh_subtitle_commandprofiles_rename"),
                    IcoPath = AppIconPath,
                    AutoCompleteText = query.ActionKeyword + " profiles rename ",
                    Score = int.MaxValue
                });
                results.Add(MakeBackNavResult(query, query.ActionKeyword + " profiles ", query.ActionKeyword + " profiles"));
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandprofiles_rename") + ": " + oldName,
                    SubTitle = GetTranslation("plugin_quickssh_rename_notfound"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandprofiles_rename"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandprofiles_rename"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " profiles rename " + oldName + " ",
                Score = int.MaxValue
            });
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " profiles ", query.ActionKeyword + " profiles"));

            if (!string.IsNullOrEmpty(newName))
            {
                var profileValue = profiles[oldName];
                results.Add(new Result
                {
                    Title = oldName + " → " + newName,
                    SubTitle = profileValue?.ToDisplayString() ?? "",
                    IcoPath = AppIconGreenPath,
                    Action = _ =>
                    {
                        var value = profiles[oldName];
                        profiles.SetCallback(null);
                        try
                        {
                            profiles.Remove(oldName);
                            profiles[newName] = value;
                        }
                        finally
                        {
                            profiles.SetCallback(_profileManager.SaveConfiguration);
                        }
                        _profileManager.SaveConfiguration();
                        _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " profiles ", true);
                        return false;
                    }
                });
            }

            return results;
        }

        // ── profiles copy ─────────────────────────────────────────────────────────

        private List<Result> HandleProfilesCopy(Query query, string search)
        {
            var results = new List<Result>();
            var profiles = _profileManager.UserData.Profiles;

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandprofiles_copy"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandprofiles_copy_usage"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " profiles copy ",
                Score = int.MaxValue
            });
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " profiles ", query.ActionKeyword + " profiles"));

            if (profiles.Count == 0)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandprofiles_copy"),
                    SubTitle = GetTranslation("plugin_quickssh_noprofiles"),
                    IcoPath = AppIconPath
                });
                return results;
            }

            foreach (var entry in profiles)
            {
                if (!string.IsNullOrEmpty(search) &&
                    !entry.Key.ToLowerInvariant().Contains(search.ToLowerInvariant()))
                    continue;

                var name = entry.Key;
                var cmd = entry.Value?.ToCommandLine() ?? "";
                var displayCmd = entry.Value?.ToDisplayString() ?? "";
                results.Add(new Result
                {
                    Title = name,
                    SubTitle = GetTranslation("plugin_quickssh_subtitle_commandprofiles_copy") + " " + displayCmd,
                    IcoPath = AppIconGreenPath,
                    AutoCompleteText = query.ActionKeyword + " profiles copy " + name,
                    Action = _ =>
                    {
                        _pluginContext?.API?.CopyToClipboard(displayCmd, false, false);
                        _pluginContext?.API?.ShowMsg("QuickSSH",
                            GetTranslation("plugin_quickssh_copy_command_success"));
                        _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " profiles copy ", true);
                        return false;
                    }
                });
            }

            return results;
        }

        // ── profiles export ───────────────────────────────────────────────────────

        private List<Result> HandleProfilesExport(Query query)
        {
            var results = new List<Result>();
            var exportPath = Path.Combine(_dataDir, "profiles_export.sshconfig");

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandprofiles_export"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandprofiles_export_usage"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " profiles export ",
                Score = int.MaxValue
            });
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " profiles ", query.ActionKeyword + " profiles"));

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandprofiles_export"),
                SubTitle = string.Format(GetTranslation("plugin_quickssh_subtitle_commandprofiles_export"), exportPath),
                IcoPath = AppIconGreenPath,
                AutoCompleteText = query.ActionKeyword + " profiles export ",
                Action = _ =>
                {
                    try
                    {
                        Directory.CreateDirectory(_dataDir);
                        var profiles = _profileManager.UserData.Profiles
                            .ToDictionary(e => e.Key, e => e.Value);
                        var text = ProfileSerializer.Serialize(profiles);
                        File.WriteAllText(exportPath, text);
                        _pluginContext.API.ShowMsg("QuickSSH",
                            string.Format(GetTranslation("plugin_quickssh_export_success"),
                                profiles.Count, exportPath));
                    }
                    catch (Exception ex)
                    {
                        _pluginContext.API.ShowMsg("QuickSSH", "Error: " + ex.Message);
                    }
                    _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " profiles ", true);
                    return false;
                }
            });

            return results;
        }

        // ── profiles import ───────────────────────────────────────────────────────

        private List<Result> HandleProfilesImport(Query query, string rest)
        {
            var results = new List<Result>();

            string[] importFiles = Array.Empty<string>();
            try
            {
                if (Directory.Exists(_dataDir))
                {
                    // Accept both new .sshconfig and legacy .json files
                    var sshconfig = Directory.GetFiles(_dataDir, "*.sshconfig");
                    var json = Directory.GetFiles(_dataDir, "*.json");
                    importFiles = sshconfig.Concat(json).ToArray();
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandprofiles_import"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandprofiles_import_usage"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " profiles import ",
                Score = int.MaxValue
            });
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " profiles ", query.ActionKeyword + " profiles"));

            if (importFiles.Length == 0)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandprofiles_import"),
                    SubTitle = string.Format(GetTranslation("plugin_quickssh_import_nofiles"), _dataDir),
                    IcoPath = AppIconPath,
                    AutoCompleteText = query.ActionKeyword + " profiles import "
                });
                return results;
            }

            foreach (var file in importFiles)
            {
                var fileName = Path.GetFileName(file);
                if (!string.IsNullOrEmpty(rest) &&
                    !fileName.ToLowerInvariant().Contains(rest.ToLowerInvariant()))
                    continue;

                // Mark legacy .json files clearly in the result title so users understand
                // that .json is migration-only and the canonical format is .sshconfig.
                bool isLegacyJson = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                var displayTitle = GetTranslation("plugin_quickssh_title_commandprofiles_import") + ": " + fileName
                    + (isLegacyJson ? " " + GetTranslation("plugin_quickssh_import_legacy_label") : "");

                results.Add(new Result
                {
                    Title = displayTitle,
                    SubTitle = file,
                    IcoPath = AppIconGreenPath,
                    AutoCompleteText = query.ActionKeyword + " profiles import " + fileName,
                    Action = _ =>
                    {
                        try
                        {
                            ImportProfilesFromFile(file);
                        }
                        catch (Exception ex)
                        {
                            _pluginContext.API.ShowMsg("QuickSSH", "Error: " + ex.Message);
                        }
                        _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " profiles ", true);
                        return false;
                    }
                });
            }

            return results;
        }

        /// <summary>
        /// Imports profiles from a file into the structured profile store.
        /// </summary>
        /// <remarks>
        /// Canonical import format: <c>.sshconfig</c> (SSH-config-like text, written by "profiles export").
        /// <para/>
        /// Migration-only format: <c>.json</c> (v1 raw-command dictionary).
        /// JSON files are <b>never written</b> by this plugin and are accepted here solely for
        /// backward-compatibility migration.  They are clearly labelled "(legacy)" in the UI.
        /// </remarks>
        private void ImportProfilesFromFile(string filePath)
        {
            var text = File.ReadAllText(filePath);
            Dictionary<string, SshProfile> imported;

            if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                // MIGRATION-ONLY PATH: read v1 raw-command JSON (Dictionary<string, string>)
                // and parse each command into a structured SshProfile.
                // Unknown flags that cannot be parsed are stored in SshProfile.ExtraArgs
                // so no information is silently lost.
                // This path is NOT used for canonical import/export; use .sshconfig files instead.
                var legacy = JsonConvert.DeserializeObject<Dictionary<string, string>>(text);
                if (legacy == null || legacy.Count == 0)
                {
                    _pluginContext.API.ShowMsg("QuickSSH",
                        GetTranslation("plugin_quickssh_import_empty"));
                    return;
                }
                imported = new Dictionary<string, SshProfile>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in legacy)
                    imported[kvp.Key] = SshProfile.ParseFromLegacyCommand(kvp.Value);
            }
            else
            {
                imported = ProfileSerializer.Deserialize(text);
                if (imported.Count == 0)
                {
                    _pluginContext.API.ShowMsg("QuickSSH",
                        GetTranslation("plugin_quickssh_import_empty"));
                    return;
                }
            }

            int count = 0;
            _profileManager.UserData.Profiles.SetCallback(null);
            try
            {
                foreach (var kvp in imported)
                {
                    if (!_profileManager.UserData.Profiles.ContainsKey(kvp.Key))
                    {
                        _profileManager.UserData.Profiles[kvp.Key] = kvp.Value;
                        count++;
                    }
                }
            }
            finally
            {
                _profileManager.UserData.Profiles.SetCallback(_profileManager.SaveConfiguration);
            }

            if (count > 0)
                _profileManager.SaveConfiguration();

            _pluginContext.API.ShowMsg("QuickSSH",
                string.Format(GetTranslation("plugin_quickssh_import_success"), count));
        }

        private List<Result> HandleDirectConnect(Query query, string rest)
        {
            var results = new List<Result>();

            // Always show usage hint at the top.
            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commanddirect"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commanddirectconnect_usage"),
                IcoPath = AppIconPath,
                Score = int.MaxValue
            });

            if (string.IsNullOrEmpty(rest))
                return results;

            // Normalise the user input: strip accidental cmd-style /flags and
            // ensure the command starts with "ssh ".
            var sshCmd = NormalizeSshCommand(rest);
            if (string.IsNullOrEmpty(sshCmd))
                return results;

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_connect_label") + " " + rest,
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commanddirectconnect") + " " + sshCmd,
                IcoPath = AppIconGreenPath,
                Action = _ =>
                {
                    RunCommand(sshCmd);
                    return true;
                }
            });

            // Suggest registered SSH keys when the input contains "-i " with no key value yet,
            // or ends with "-i" (user is about to type a space then a key path).
            var trimmedInput = rest.TrimStart();
            bool suggestKeys = trimmedInput.Equals("-i", StringComparison.Ordinal) ||
                               trimmedInput.EndsWith(" -i", StringComparison.Ordinal) ||
                               trimmedInput.EndsWith(" -i ", StringComparison.Ordinal);
            // Also match "-i <partial>" where partial does not contain '@' (not a destination).
            if (!suggestKeys)
            {
                var dashI = trimmedInput.LastIndexOf("-i ", StringComparison.Ordinal);
                if (dashI >= 0)
                {
                    var afterI = trimmedInput.Substring(dashI + 3).TrimStart();
                    // If nothing follows -i or the text after -i has no space yet (still typing
                    // a key path/alias), suggest keys that match.
                    if (string.IsNullOrEmpty(afterI) || (!afterI.Contains('@') && !afterI.Contains(' ')))
                        suggestKeys = true;
                }
            }

            if (suggestKeys && _profileManager?.UserData?.SshKeys != null)
            {
                var keys = _profileManager.UserData.SshKeys;
                foreach (var entry in keys)
                {
                    var alias = entry.Key;
                    var keyPath = entry.Value?.Path ?? "";
                    var quotedPath = SshCommandBuilder.QuoteForDisplay(keyPath);

                    // Build the autocomplete text: replace "-i" / "-i <partial>" with "-i <full-path>"
                    var prefix = trimmedInput;
                    var dashIdx = prefix.LastIndexOf("-i", StringComparison.Ordinal);
                    if (dashIdx >= 0)
                        prefix = prefix.Substring(0, dashIdx).TrimEnd();

                    var newInput = string.IsNullOrEmpty(prefix)
                        ? "-i " + quotedPath + " "
                        : prefix + " -i " + quotedPath + " ";

                    var autoText = query.ActionKeyword + " " + newInput;
                    results.Add(new Result
                    {
                        Title = alias,
                        SubTitle = GetTranslation("plugin_quickssh_keys_identity") + " " + keyPath,
                        IcoPath = AppIconGreenPath,
                        AutoCompleteText = autoText,
                        Action = _ =>
                        {
                            _pluginContext?.API?.ChangeQuery(autoText, true);
                            return false;
                        }
                    });
                }
            }

            return results;
        }

        private List<Result> HandleShell(Query query, string rest)
        {
            var results = new List<Result>();
            var parts = rest.Split(new[] { ' ' }, 2);
            var subCmd = parts[0].ToLowerInvariant();
            var subRest = parts.Length > 1 ? parts[1].Trim() : "";

            switch (subCmd)
            {
                case "add":
                    // Always show usage hint at the top.
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_title_commandshell_add"),
                        SubTitle = GetTranslation("plugin_quickssh_subtitle_commandshell_add_usage"),
                        IcoPath = AppIconPath,
                        Score = int.MaxValue
                    });
                    results.Add(MakeBackNavResult(query, query.ActionKeyword + " shell ", query.ActionKeyword + " shell"));
                    if (!string.IsNullOrEmpty(subRest))
                    {
                        var (name, value) = ParseShellAddArgs(subRest);
                        results.Add(new Result
                        {
                            Title = GetTranslation("plugin_quickssh_addshell_label") + " " + name,
                            SubTitle = string.IsNullOrEmpty(value) ? name : value,
                            IcoPath = AppIconGreenPath,
                            Action = _ =>
                            {
                                // Suppress auto-save during mutations so we can set all
                                // fields (including SelectedCustomShell) before persisting once.
                                _profileManager.UserData.CustomShell.SetCallback(null);
                                _profileManager.UserData.CustomShell[name] = value ?? "";
                                if (_profileManager.UserData.CustomShell.Count == 1)
                                    _profileManager.UserData.SelectedCustomShell = name;
                                _profileManager.UserData.CustomShell.SetCallback(_profileManager.SaveConfiguration);
                                _profileManager.SaveConfiguration();
                                _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " shell ", true);
                                return false;
                            }
                        });
                    }
                    break;

                case "remove":
                    var shells = _profileManager.UserData.CustomShell;
                    // Always show usage hint at the top.
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_title_commandshell_remove"),
                        SubTitle = GetTranslation("plugin_quickssh_subtitle_commandshell_remove"),
                        IcoPath = AppIconPath,
                        AutoCompleteText = query.ActionKeyword + " shell remove ",
                        Score = int.MaxValue
                    });
                    results.Add(MakeBackNavResult(query, query.ActionKeyword + " shell ", query.ActionKeyword + " shell"));
                    if (shells.Count == 0)
                    {
                        results.Add(new Result
                        {
                            Title = GetTranslation("plugin_quickssh_title_commandshell_remove"),
                            SubTitle = GetTranslation("plugin_quickssh_noshells"),
                            IcoPath = AppIconPath
                        });
                    }
                    else
                    {
                        foreach (var shell in shells)
                        {
                            results.Add(new Result
                            {
                                Title = shell.Key,
                                SubTitle = string.IsNullOrEmpty(shell.Value) ? shell.Key : shell.Value,
                                IcoPath = AppIconRedPath,
                                AutoCompleteText = query.ActionKeyword + " shell remove " + shell.Key,
                                Action = _ =>
                                {
                                    // Suppress auto-save so we can update SelectedCustomShell
                                    // atomically before the single explicit save below.
                                    _profileManager.UserData.CustomShell.SetCallback(null);
                                    _profileManager.UserData.CustomShell.Remove(shell.Key);
                                    if (_profileManager.UserData.SelectedCustomShell == shell.Key)
                                    {
                                        // Auto-select the first remaining shell (if any).
                                        _profileManager.UserData.SelectedCustomShell =
                                            _profileManager.UserData.CustomShell.Keys.FirstOrDefault();
                                    }
                                    _profileManager.UserData.CustomShell.SetCallback(_profileManager.SaveConfiguration);
                                    _profileManager.SaveConfiguration();
                                    _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " shell ", true);
                                    return false;
                                }
                            });
                        }
                    }
                    break;

                default:
                    // Mirror the top-level matching pattern: when the partial input
                    // is a prefix of one or more sub-commands, delegate to the
                    // autocompleter so that "shell a" suggests "add" the same way
                    // "ssh p" suggests "profiles" at the top level.
                    if (!string.IsNullOrEmpty(subCmd) &&
                        ShellSubCommands.Any(s => s.StartsWith(subCmd)))
                    {
                        return new List<Result>(AutoCompleter.GetSuggestions(
                            query.ActionKeyword, "shell " + rest,
                            _profileManager?.UserData, AppIconPath,
                            _pluginContext?.API));
                    }

                    // Always show "Shell management" hint at the top.
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_title_commandshell"),
                        SubTitle = GetTranslation("plugin_quickssh_subtitle_commandshell_help"),
                        IcoPath = AppIconPath,
                        AutoCompleteText = query.ActionKeyword + " shell ",
                        Score = ScoreSubMenuManagement
                    });

                    // Back-navigation row — returns to top-level command list.
                    results.Add(MakeBackNavResult(query, query.ActionKeyword + " ", query.ActionKeyword));

                    // List shells in deterministic order:
                    //   1. management row     (ScoreSubMenuManagement = int.MaxValue)
                    //   2. back-nav row       (ScoreBackNavigation = int.MaxValue - 1)
                    //   3. action rows        (ScoreShellActionAdd = 1100, ScoreShellActionRemove = 1050)
                    //   4. selected shell     (ScoreShellSelected = 1000)
                    //   5. other shells       (decreasing from ScoreShellOtherStart = 500)
                    var allShells = _profileManager.UserData.CustomShell;
                    var selected = _profileManager.UserData.SelectedCustomShell;

                    // Sub-command action rows (add / remove) — always above saved shell entries.
                    var shellSubCmds = new[]
                    {
                        ("add",    GetTranslation("plugin_quickssh_title_commandshell_add"),    GetTranslation("plugin_quickssh_subtitle_commandshell_add_usage"),    ScoreShellActionAdd),
                        ("remove", GetTranslation("plugin_quickssh_title_commandshell_remove"), GetTranslation("plugin_quickssh_subtitle_commandshell_remove"),        ScoreShellActionRemove),
                    };
                    foreach (var (scName, scTitle, scSubTitle, scScore) in shellSubCmds)
                    {
                        if (string.IsNullOrEmpty(subCmd) || scName.StartsWith(subCmd))
                        {
                            var autoText = query.ActionKeyword + " shell " + scName + " ";
                            results.Add(new Result
                            {
                                Title = scTitle,
                                SubTitle = scSubTitle,
                                IcoPath = AppIconPath,
                                AutoCompleteText = autoText,
                                Score = scScore,
                                Action = _ =>
                                {
                                    _pluginContext?.API?.ChangeQuery(autoText, true);
                                    return false;
                                }
                            });
                        }
                    }

                    // Selected shell (if any) — pinned just below the action rows.
                    if (!string.IsNullOrEmpty(selected) && allShells.ContainsKey(selected))
                    {
                        var shellVal = allShells[selected];
                        results.Add(new Result
                        {
                            Title = selected + " " + GetTranslation("plugin_quickssh_shell_selected"),
                            SubTitle = string.IsNullOrEmpty(shellVal) ? selected : shellVal,
                            IcoPath = AppIconGreenPath,
                            AutoCompleteText = query.ActionKeyword + " shell " + selected,
                            Score = ScoreShellSelected,
                            Action = _ =>
                            {
                                _profileManager.UserData.SelectedCustomShell = selected;
                                _profileManager.SaveConfiguration();
                                _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " shell ", true);
                                return false;
                            }
                        });
                    }

                    // Remaining (non-selected) shell profiles.
                    int otherShellScore = ScoreShellOtherStart;
                    foreach (var shell in allShells)
                    {
                        if (shell.Key == selected)
                            continue;
                        results.Add(new Result
                        {
                            Title = shell.Key,
                            SubTitle = string.IsNullOrEmpty(shell.Value) ? shell.Key : shell.Value,
                            IcoPath = AppIconGreenPath,
                            AutoCompleteText = query.ActionKeyword + " shell " + shell.Key,
                            Score = otherShellScore--,
                            Action = _ =>
                            {
                                _profileManager.UserData.SelectedCustomShell = shell.Key;
                                _profileManager.SaveConfiguration();
                                _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " shell ", true);
                                return false;
                            }
                        });
                    }
                    break;
            }

            return results;
        }

        // ── keys (SSH key management) ─────────────────────────────────────────────

        private List<Result> HandleKeys(Query query, string rest)
        {
            var parts = rest.Split(new[] { ' ' }, 2);
            var subCmd = parts[0].ToLowerInvariant();
            var subRest = parts.Length > 1 ? parts[1].Trim() : "";

            switch (subCmd)
            {
                case KeysSubAdd:      return HandleKeysAdd(query, subRest);
                case KeysSubGenerate: return HandleKeysGenerate(query, subRest);
                case KeysSubInstall:  return HandleKeysInstall(query, subRest);
                case KeysSubRemove:   return HandleKeysRemove(query, subRest);
                case KeysSubRename:   return HandleKeysRename(query, subRest);
                case KeysSubCopyPath: return HandleKeysCopyPath(query, subRest);
                case KeysSubCopyPub:  return HandleKeysCopyPub(query, subRest);
                case KeysSubScan:     return HandleKeysScan(query);
                default:
                    // Partial sub-command matching (mirrors profiles/shell pattern).
                    if (!string.IsNullOrEmpty(subCmd) &&
                        KeysSubCommands.Any(s => s.StartsWith(subCmd)))
                    {
                        return new List<Result>(AutoCompleter.GetSuggestions(
                            query.ActionKeyword, "keys " + rest,
                            _profileManager?.UserData, AppIconPath,
                            _pluginContext?.API));
                    }
                    return HandleKeysList(query, rest);
            }
        }

        private List<Result> HandleKeysList(Query query, string search)
        {
            var results = new List<Result>();
            var keys = _profileManager.UserData.SshKeys;

            // 1. Management/usage hint — always pinned at the top.
            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandkeys"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandkeys"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " keys ",
                Score = ScoreSubMenuManagement
            });

            // 2. Back-navigation row — returns to top-level command list.
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " ", query.ActionKeyword));

            // 3. Action rows — only shown when no search text is active.
            if (string.IsNullOrEmpty(search))
            {
                var keysSubCmds = new[]
                {
                    ("install",   GetTranslation("plugin_quickssh_title_commandkeys_install"),   GetTranslation("plugin_quickssh_subtitle_commandkeys_install"),   ScoreKeysActionInstall),
                    ("add",       GetTranslation("plugin_quickssh_title_commandkeys_add"),       GetTranslation("plugin_quickssh_subtitle_commandkeys_add"),       ScoreKeysActionAdd),
                    ("generate",  GetTranslation("plugin_quickssh_title_commandkeys_generate"),  GetTranslation("plugin_quickssh_subtitle_commandkeys_generate"),  ScoreKeysActionGenerate),
                    ("remove",    GetTranslation("plugin_quickssh_title_commandkeys_remove"),    GetTranslation("plugin_quickssh_subtitle_commandkeys_remove"),    ScoreKeysActionRemove),
                    ("rename",    GetTranslation("plugin_quickssh_title_commandkeys_rename"),    GetTranslation("plugin_quickssh_subtitle_commandkeys_rename"),    ScoreKeysActionRename),
                    ("copy-path", GetTranslation("plugin_quickssh_title_commandkeys_copypath"),  GetTranslation("plugin_quickssh_subtitle_commandkeys_copypath"),  ScoreKeysActionCopyPath),
                    ("copy-pub",  GetTranslation("plugin_quickssh_title_commandkeys_copypub"),   GetTranslation("plugin_quickssh_subtitle_commandkeys_copypub"),   ScoreKeysActionCopyPub),
                    ("scan",      GetTranslation("plugin_quickssh_title_commandkeys_scan"),      GetTranslation("plugin_quickssh_subtitle_commandkeys_scan"),      ScoreKeysActionScan),
                };
                foreach (var (scName, scTitle, scSubTitle, scScore) in keysSubCmds)
                {
                    var autoText = query.ActionKeyword + " keys " + scName + " ";
                    results.Add(new Result
                    {
                        Title = scTitle,
                        SubTitle = scSubTitle,
                        IcoPath = AppIconPath,
                        AutoCompleteText = autoText,
                        Score = scScore,
                        Action = _ =>
                        {
                            _pluginContext?.API?.ChangeQuery(autoText, true);
                            return false;
                        }
                    });
                }
            }

            // 4. Saved keys.
            if (keys.Count == 0)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys"),
                    SubTitle = GetTranslation("plugin_quickssh_nokeys"),
                    IcoPath = AppIconPath,
                    Score = ScoreKeysSavedItem
                });
            }
            else
            {
                int keyScore = ScoreKeysSavedItem;
                foreach (var entry in keys)
                {
                    if (!string.IsNullOrEmpty(search) &&
                        !entry.Key.ToLowerInvariant().Contains(search.ToLowerInvariant()))
                        continue;

                    var alias = entry.Key;
                    var keyEntry = entry.Value;
                    var displayPath = keyEntry?.ToDisplayString() ?? "";
                    bool fileExists = !string.IsNullOrEmpty(keyEntry?.Path) && File.Exists(keyEntry.Path);

                    results.Add(new Result
                    {
                        Title = alias,
                        SubTitle = displayPath + (fileExists ? "" : " " + GetTranslation("plugin_quickssh_keys_file_missing")),
                        IcoPath = fileExists ? AppIconGreenPath : AppIconRedPath,
                        AutoCompleteText = query.ActionKeyword + " keys " + alias,
                        Score = keyScore--
                    });
                }
            }

            return results;
        }

        private List<Result> HandleKeysAdd(Query query, string rest)
        {
            var results = new List<Result>();

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandkeys_add"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandkeys_add"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " keys add ",
                Score = int.MaxValue
            });
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys ", query.ActionKeyword + " keys"));

            if (string.IsNullOrEmpty(rest))
                return results;

            var addParts = rest.Split(new[] { ' ' }, 2);
            var keyAlias = addParts[0];
            var keyPath = addParts.Length > 1 ? addParts[1].Trim() : "";

            // Strip surrounding quotes from the path.
            if (keyPath.Length >= 2 && keyPath.StartsWith("\"") && keyPath.EndsWith("\""))
                keyPath = keyPath.Substring(1, keyPath.Length - 2);

            if (!string.IsNullOrEmpty(keyPath))
            {
                // Expand ~ to user profile directory.
                var expandedPath = keyPath.Replace("~",
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                bool fileExists = File.Exists(expandedPath);

                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_save_label") + " " + keyAlias,
                    SubTitle = expandedPath + (fileExists ? "" : " " + GetTranslation("plugin_quickssh_keys_file_missing")),
                    IcoPath = fileExists ? AppIconGreenPath : AppIconRedPath,
                    Action = _ =>
                    {
                        _profileManager.UserData.SshKeys[keyAlias] = new SshKeyEntry
                        {
                            Path = expandedPath
                        };
                        _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " keys ", true);
                        return false;
                    }
                });
            }

            return results;
        }

        // ── keys install ──────────────────────────────────────────────────────────

        /// <summary>
        /// Row-driven flow for installing a public key on a remote Linux host.
        /// <list type="bullet">
        ///   <item><c>keys install</c> — list registered keys (select one)</item>
        ///   <item><c>keys install &lt;alias&gt;</c> — prompt for user@host</item>
        ///   <item><c>keys install &lt;alias&gt; &lt;user@host&gt;</c> — show 3 action rows</item>
        /// </list>
        /// </summary>
        private List<Result> HandleKeysInstall(Query query, string rest)
        {
            var results = new List<Result>();
            var keys = _profileManager.UserData.SshKeys;

            // Hint row — always pinned at the top.
            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandkeys_install"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandkeys_install"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " keys install ",
                Score = int.MaxValue
            });

            // Back row — always "← Back to ssh keys".
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys ", query.ActionKeyword + " keys"));

            if (string.IsNullOrEmpty(rest))
            {
                // Step 1: List registered keys for selection.
                if (keys.Count == 0)
                {
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_title_commandkeys_install"),
                        SubTitle = GetTranslation("plugin_quickssh_nokeys"),
                        IcoPath = AppIconPath
                    });
                    return results;
                }

                int keyScore = ScoreKeysSavedItem;
                foreach (var entry in keys)
                {
                    var alias = entry.Key;
                    var keyEntry = entry.Value;
                    var pubPath = keyEntry?.GetEffectivePublicKeyPath();
                    bool pubExists = !string.IsNullOrEmpty(pubPath) && File.Exists(pubPath);
                    var algoLabel = !string.IsNullOrEmpty(keyEntry?.Algorithm) ? keyEntry.Algorithm + " — " : "";

                    if (pubExists)
                    {
                        var targetQuery = query.ActionKeyword + " keys install " + alias + " ";
                        results.Add(new Result
                        {
                            Title = alias,
                            SubTitle = algoLabel + (keyEntry?.Path ?? ""),
                            IcoPath = AppIconGreenPath,
                            AutoCompleteText = targetQuery,
                            Score = keyScore--,
                            Action = _ =>
                            {
                                _pluginContext?.API?.ChangeQuery(targetQuery, true);
                                return false;
                            }
                        });
                    }
                    else
                    {
                        results.Add(new Result
                        {
                            Title = alias,
                            SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_install_pub_notfound"), pubPath ?? ""),
                            IcoPath = AppIconRedPath,
                            Score = keyScore--
                        });
                    }
                }

                return results;
            }

            // Split rest into <alias> and optional <user@host>.
            var installParts = rest.Split(new[] { ' ' }, 2);
            var installAlias = installParts[0];
            var userAtHost = installParts.Length > 1 ? installParts[1].Trim() : "";

            // Validate alias exists.
            if (!keys.ContainsKey(installAlias))
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_install"),
                    SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_install_alias_notfound"), installAlias),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            var installKeyEntry = keys[installAlias];
            var installPubPath = installKeyEntry?.GetEffectivePublicKeyPath();
            bool installPubExists = !string.IsNullOrEmpty(installPubPath) && File.Exists(installPubPath);

            if (!installPubExists)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_install"),
                    SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_install_pub_notfound"), installPubPath ?? ""),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (string.IsNullOrEmpty(userAtHost))
            {
                // Step 2: Prompt for user@host.
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_install"),
                    SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_install_type_userhost"),
                        query.ActionKeyword, installAlias),
                    IcoPath = AppIconPath
                });
                return results;
            }

            // Step 3: Validate destination and show action rows.
            if (!RemoteKeyInstallBuilder.IsValidUserAtHost(userAtHost))
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_install"),
                    SubTitle = GetTranslation("plugin_quickssh_keys_install_invalid_destination"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            // Read and validate the public key content.
            string pubContent;
            try
            {
                pubContent = File.ReadAllText(installPubPath).Trim();
            }
            catch (Exception)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_install"),
                    SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_install_pub_notfound"), installPubPath),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (!RemoteKeyInstallBuilder.ValidatePublicKeyLine(pubContent))
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_install"),
                    SubTitle = GetTranslation("plugin_quickssh_keys_install_pub_invalid"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            var bootstrap = RemoteKeyInstallBuilder.BuildBootstrapCommand(pubContent);
            var fullSshCmd = RemoteKeyInstallBuilder.BuildFullSshCommand(userAtHost, bootstrap);
            var runSshCmd = RemoteKeyInstallBuilder.BuildRunCommand(userAtHost, bootstrap);

            // Hint row subtitle updates for step 3.
            results[0] = new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandkeys_install"),
                SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_install_summary"),
                    installAlias, userAtHost),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " keys install ",
                Score = int.MaxValue
            };

            // Row 1: Run remote setup command (launches terminal)
            // Uses the wrapped run command that includes a local failure guard
            // for connection-level errors (refused, unreachable, auth failure).
            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_keys_install_run"),
                SubTitle = fullSshCmd,
                IcoPath = AppIconGreenPath,
                Action = _ =>
                {
                    RunCommand(runSshCmd);
                    return true;
                }
            });

            // Row 2: Copy remote setup command
            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_keys_install_copy_cmd"),
                SubTitle = GetTranslation("plugin_quickssh_keys_install_copy_cmd_subtitle"),
                IcoPath = AppIconPath,
                Action = _ =>
                {
                    _pluginContext?.API?.CopyToClipboard(fullSshCmd, false, false);
                    _pluginContext?.API?.ShowMsg("QuickSSH",
                        GetTranslation("plugin_quickssh_keys_install_copy_cmd_success"));
                    _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " keys install " + installAlias + " " + userAtHost, true);
                    return false;
                }
            });

            // Row 3: Copy public key
            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_keys_install_copy_pub"),
                SubTitle = GetTranslation("plugin_quickssh_keys_install_copy_pub_subtitle"),
                IcoPath = AppIconPath,
                Action = _ =>
                {
                    _pluginContext?.API?.CopyToClipboard(pubContent, false, false);
                    _pluginContext?.API?.ShowMsg("QuickSSH",
                        GetTranslation("plugin_quickssh_copy_pubkey_success"));
                    _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " keys install " + installAlias + " " + userAtHost, true);
                    return false;
                }
            });

            return results;
        }

        // ── keys generate ─────────────────────────────────────────────────────────

        /// <summary>
        /// Row-driven SSH key generation wizard.
        /// <list type="bullet">
        ///   <item><c>keys generate</c> — usage hint only</item>
        ///   <item><c>keys generate &lt;alias&gt;</c> — show actionable rows:
        ///     ed25519 (default), RSA 4096, and a custom-path hint row</item>
        ///   <item><c>keys generate &lt;alias&gt; &lt;custom-path&gt;</c> — show
        ///     ed25519 + RSA 4096 rows targeting the custom path</item>
        /// </list>
        /// Passphrase is intentionally NOT supported — keys are generated
        /// with <c>-N ""</c> (empty passphrase).
        /// </summary>
        private List<Result> HandleKeysGenerate(Query query, string rest)
        {
            var results = new List<Result>();
            var keys = _profileManager.UserData.SshKeys;

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandkeys_generate"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandkeys_generate"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " keys generate ",
                Score = int.MaxValue
            });
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys ", query.ActionKeyword + " keys"));

            if (string.IsNullOrEmpty(rest))
                return results;

            // Split rest into <alias> and optional <custom-path>.
            // Uses Char.IsWhiteSpace so non-breaking spaces and other
            // Unicode whitespace are handled correctly.
            var (alias, customPathRaw) = Utils.ParseGenerateArgs(rest);
            bool hasCustomPath = !string.IsNullOrEmpty(customPathRaw);

            // Sanitise alias for use as a file name (only used for the default-path branch)
            var safeFileName = Utils.SanitizeKeyFileName(alias);
            if (safeFileName == null)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_generate"),
                    SubTitle = GetTranslation("plugin_quickssh_keys_generate_invalid_alias"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            // Duplicate alias check
            if (keys.ContainsKey(alias))
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_generate") + ": " + alias,
                    SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_generate_duplicate"), alias),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (hasCustomPath)
            {
                // ── Custom path flow ──────────────────────────────────────────
                // Expand ~ to user profile directory.
                var expandedPath = customPathRaw.Replace("~",
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

                // Validate path characters
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(expandedPath);
                }
                catch (Exception ex)
                {
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_title_commandkeys_generate") + ": " + alias,
                        SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_generate_invalid_path"), ex.Message),
                        IcoPath = AppIconRedPath
                    });
                    return results;
                }

                // Target must not be an existing directory
                if (Directory.Exists(fullPath))
                {
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_title_commandkeys_generate") + ": " + alias,
                        SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_generate_path_is_directory"), fullPath),
                        IcoPath = AppIconRedPath
                    });
                    return results;
                }

                // Target must not be an existing file
                if (File.Exists(fullPath))
                {
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_title_commandkeys_generate") + ": " + alias,
                        SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_generate_file_exists"), fullPath),
                        IcoPath = AppIconRedPath
                    });
                    return results;
                }

                // Row 1: Generate ed25519 at custom path
                results.Add(new Result
                {
                    Title = string.Format(GetTranslation("plugin_quickssh_keys_generate_confirm"), alias),
                    SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_generate_subtitle"), "ed25519", fullPath),
                    IcoPath = AppIconGreenPath,
                    Action = _ => ExecuteKeyGeneration(alias, "ed25519", 0, fullPath, query.ActionKeyword)
                });

                // Row 2: Generate RSA 4096 at custom path
                results.Add(new Result
                {
                    Title = string.Format(GetTranslation("plugin_quickssh_keys_generate_confirm"), alias),
                    SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_generate_subtitle"), "RSA 4096", fullPath),
                    IcoPath = AppIconPath,
                    Action = _ => ExecuteKeyGeneration(alias, "rsa", 4096, fullPath, query.ActionKeyword)
                });
            }
            else
            {
                // ── Default path flow ─────────────────────────────────────────
                var sshDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
                var defaultKeyPath = Path.Combine(sshDir, safeFileName);

                // Check if target file already exists at the default path
                if (File.Exists(defaultKeyPath))
                {
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_title_commandkeys_generate") + ": " + alias,
                        SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_generate_file_exists"), defaultKeyPath),
                        IcoPath = AppIconRedPath
                    });
                    return results;
                }

                // Row 1: Generate ed25519 (recommended default)
                results.Add(new Result
                {
                    Title = string.Format(GetTranslation("plugin_quickssh_keys_generate_confirm"), alias),
                    SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_generate_subtitle"), "ed25519", defaultKeyPath),
                    IcoPath = AppIconGreenPath,
                    Action = _ => ExecuteKeyGeneration(alias, "ed25519", 0, defaultKeyPath, query.ActionKeyword)
                });

                // Row 2: Generate RSA 4096 (compatibility)
                results.Add(new Result
                {
                    Title = string.Format(GetTranslation("plugin_quickssh_keys_generate_confirm"), alias),
                    SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_generate_subtitle"), "RSA 4096", defaultKeyPath),
                    IcoPath = AppIconPath,
                    Action = _ => ExecuteKeyGeneration(alias, "rsa", 4096, defaultKeyPath, query.ActionKeyword)
                });

                // Row 3: Custom path hint — navigates the user to append a path
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_keys_generate_custom_path_title"),
                    SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_generate_custom_path_hint"),
                        query.ActionKeyword, alias),
                    IcoPath = AppIconPath,
                    AutoCompleteText = query.ActionKeyword + " keys generate " + alias + " "
                });
            }

            return results;
        }

        /// <summary>
        /// Runs ssh-keygen non-interactively to generate a keypair with an empty
        /// passphrase (<c>-N ""</c>), then auto-registers the key in the registry
        /// only if both the private key and <c>.pub</c> file exist on disk.
        /// On success, shows a confirmation message with paths and returns the
        /// user to the <c>keys</c> menu. On failure, closes Flow Launcher.
        /// </summary>
        private bool ExecuteKeyGeneration(string alias, string keyType, int keyBits, string keyPath, string actionKeyword)
        {
            // 1. Check ssh-keygen availability
            if (!Utils.IsSshKeygenInstalled())
            {
                _pluginContext?.API?.ShowMsg("QuickSSH",
                    GetTranslation("plugin_quickssh_keys_generate_no_keygen"));
                return true;
            }

            // 2. Ensure target directory exists
            var keyDir = Path.GetDirectoryName(keyPath);
            if (!string.IsNullOrEmpty(keyDir) && !Directory.Exists(keyDir))
            {
                try { Directory.CreateDirectory(keyDir); }
                catch (Exception ex)
                {
                    _pluginContext?.API?.ShowMsg("QuickSSH",
                        string.Format(GetTranslation("plugin_quickssh_keys_generate_invalid_path"), ex.Message));
                    return true;
                }
            }

            // 3. Final file-exists guard (race condition protection)
            if (File.Exists(keyPath))
            {
                _pluginContext?.API?.ShowMsg("QuickSSH",
                    string.Format(GetTranslation("plugin_quickssh_keys_generate_file_exists"), keyPath));
                return true;
            }

            // 4. Build ssh-keygen arguments.
            //    -N "" sets an empty passphrase — no interactive prompt needed.
            //    Passphrase support will be added in a follow-up PR.
            var keygenArgs = keyBits > 0
                ? $"-t {keyType} -b {keyBits} -f \"{keyPath}\" -C \"{alias}\" -N \"\""
                : $"-t {keyType} -f \"{keyPath}\" -C \"{alias}\" -N \"\"";

            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "ssh-keygen",
                    Arguments = keygenArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (process == null)
                {
                    _pluginContext?.API?.ShowMsg("QuickSSH",
                        GetTranslation("plugin_quickssh_keys_generate_failed"));
                    return true;
                }
                using (process)
                {
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                _pluginContext?.API?.ShowMsg("QuickSSH",
                    string.Format(GetTranslation("plugin_quickssh_keys_generate_failed_detail"), ex.Message));
                return true;
            }

            // 5. Verify generation succeeded — register only if BOTH private key and .pub exist.
            var pubKeyPath = keyPath + ".pub";
            if (File.Exists(keyPath) && File.Exists(pubKeyPath))
            {
                _profileManager.UserData.SshKeys[alias] = new SshKeyEntry
                {
                    Path = keyPath,
                    PublicKeyPath = pubKeyPath,
                    Algorithm = keyBits > 0 ? $"{keyType}-{keyBits}" : keyType,
                    Comment = alias,
                    Source = "generated",
                    CreatedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                };
                _pluginContext?.API?.ShowMsg("QuickSSH",
                    string.Format(GetTranslation("plugin_quickssh_keys_generate_success"), alias, keyPath, pubKeyPath));
                _pluginContext?.API?.ChangeQuery(actionKeyword + " keys ", true);
                return false;
            }
            else
            {
                _pluginContext?.API?.ShowMsg("QuickSSH",
                    GetTranslation("plugin_quickssh_keys_generate_failed"));
            }

            return true;
        }

        private List<Result> HandleKeysRemove(Query query, string rest)
        {
            var results = new List<Result>();
            var keys = _profileManager.UserData.SshKeys;

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandkeys_remove"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandkeys_remove"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " keys remove ",
                Score = int.MaxValue
            });
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys ", query.ActionKeyword + " keys"));

            if (keys.Count == 0)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_remove"),
                    SubTitle = GetTranslation("plugin_quickssh_nokeys"),
                    IcoPath = AppIconPath
                });
                return results;
            }

            foreach (var entry in keys)
            {
                if (!string.IsNullOrEmpty(rest) &&
                    !entry.Key.ToLowerInvariant().Contains(rest.ToLowerInvariant()))
                    continue;

                var alias = entry.Key;
                var entryValue = entry.Value;
                var keyPath = entryValue?.Path ?? "";
                var displayPath = entryValue?.ToDisplayString() ?? "";
                results.Add(new Result
                {
                    Title = alias,
                    SubTitle = displayPath,
                    IcoPath = AppIconRedPath,
                    AutoCompleteText = query.ActionKeyword + " keys remove " + alias,
                    Action = _ =>
                    {
                        _profileManager.UserData.SshKeys.Remove(alias);
                        _pluginContext?.API?.ShowMsg("QuickSSH",
                            string.Format(GetTranslation("plugin_quickssh_keys_remove_success"), alias, keyPath));
                        _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " keys ", true);
                        return false;
                    }
                });
            }

            return results;
        }

        // ── keys rename ───────────────────────────────────────────────────────────

        private List<Result> HandleKeysRename(Query query, string rest)
        {
            var results = new List<Result>();
            var keys = _profileManager.UserData.SshKeys;

            var parts = rest.Split(new[] { ' ' }, 2);
            var oldAlias = parts[0].Trim();
            var newAlias = parts.Length > 1 ? parts[1].Trim() : "";

            if (string.IsNullOrEmpty(oldAlias))
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_rename"),
                    SubTitle = GetTranslation("plugin_quickssh_subtitle_commandkeys_rename"),
                    IcoPath = AppIconPath,
                    AutoCompleteText = query.ActionKeyword + " keys rename ",
                    Score = int.MaxValue
                });
                results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys ", query.ActionKeyword + " keys"));

                if (keys.Count == 0)
                {
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_title_commandkeys_rename"),
                        SubTitle = GetTranslation("plugin_quickssh_nokeys"),
                        IcoPath = AppIconPath
                    });
                    return results;
                }

                foreach (var entry in keys)
                {
                    var alias = entry.Key;
                    var autoText = query.ActionKeyword + " keys rename " + alias + " ";
                    results.Add(new Result
                    {
                        Title = alias,
                        SubTitle = entry.Value?.ToDisplayString() ?? "",
                        IcoPath = AppIconPath,
                        AutoCompleteText = autoText,
                        Action = _ =>
                        {
                            _pluginContext?.API?.ChangeQuery(autoText, true);
                            return false;
                        }
                    });
                }
                return results;
            }

            if (!keys.ContainsKey(oldAlias))
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_rename"),
                    SubTitle = GetTranslation("plugin_quickssh_subtitle_commandkeys_rename"),
                    IcoPath = AppIconPath,
                    AutoCompleteText = query.ActionKeyword + " keys rename ",
                    Score = int.MaxValue
                });
                results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys ", query.ActionKeyword + " keys"));
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_rename") + ": " + oldAlias,
                    SubTitle = GetTranslation("plugin_quickssh_keys_rename_notfound"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandkeys_rename"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandkeys_rename"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " keys rename " + oldAlias + " ",
                Score = int.MaxValue
            });
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys ", query.ActionKeyword + " keys"));

            if (!string.IsNullOrEmpty(newAlias))
            {
                // Duplicate alias check
                if (keys.ContainsKey(newAlias))
                {
                    results.Add(new Result
                    {
                        Title = oldAlias + " → " + newAlias,
                        SubTitle = GetTranslation("plugin_quickssh_keys_rename_duplicate"),
                        IcoPath = AppIconRedPath
                    });
                }
                else
                {
                    var keyEntry = keys[oldAlias];
                    results.Add(new Result
                    {
                        Title = oldAlias + " → " + newAlias,
                        SubTitle = keyEntry?.ToDisplayString() ?? "",
                        IcoPath = AppIconGreenPath,
                        Action = _ =>
                        {
                            var value = keys[oldAlias];
                            keys.SetCallback(null);
                            try
                            {
                                keys.Remove(oldAlias);
                                keys[newAlias] = value;
                            }
                            finally
                            {
                                keys.SetCallback(_profileManager.SaveConfiguration);
                            }
                            _profileManager.SaveConfiguration();
                            _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " keys ", true);
                            return false;
                        }
                    });
                }
            }

            return results;
        }

        // ── keys copy-path ────────────────────────────────────────────────────────

        private List<Result> HandleKeysCopyPath(Query query, string search)
        {
            var results = new List<Result>();
            var keys = _profileManager.UserData.SshKeys;

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandkeys_copypath"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandkeys_copypath"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " keys copy-path ",
                Score = int.MaxValue
            });
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys ", query.ActionKeyword + " keys"));

            if (keys.Count == 0)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_copypath"),
                    SubTitle = GetTranslation("plugin_quickssh_nokeys"),
                    IcoPath = AppIconPath
                });
                return results;
            }

            foreach (var entry in keys)
            {
                if (!string.IsNullOrEmpty(search) &&
                    !entry.Key.ToLowerInvariant().Contains(search.ToLowerInvariant()))
                    continue;

                var alias = entry.Key;
                var keyPath = entry.Value?.Path ?? "";
                results.Add(new Result
                {
                    Title = alias,
                    SubTitle = GetTranslation("plugin_quickssh_keys_copypath_label") + " " + keyPath,
                    IcoPath = AppIconGreenPath,
                    AutoCompleteText = query.ActionKeyword + " keys copy-path " + alias,
                    Action = _ =>
                    {
                        _pluginContext?.API?.CopyToClipboard(keyPath, false, false);
                        _pluginContext?.API?.ShowMsg("QuickSSH",
                            GetTranslation("plugin_quickssh_copy_keypath_success"));
                        _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " keys copy-path ", true);
                        return false;
                    }
                });
            }

            return results;
        }

        // ── keys copy-pub ─────────────────────────────────────────────────────────

        private List<Result> HandleKeysCopyPub(Query query, string search)
        {
            var results = new List<Result>();
            var keys = _profileManager.UserData.SshKeys;

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandkeys_copypub"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandkeys_copypub"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " keys copy-pub ",
                Score = int.MaxValue
            });
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys ", query.ActionKeyword + " keys"));

            if (keys.Count == 0)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_copypub"),
                    SubTitle = GetTranslation("plugin_quickssh_nokeys"),
                    IcoPath = AppIconPath
                });
                return results;
            }

            foreach (var entry in keys)
            {
                if (!string.IsNullOrEmpty(search) &&
                    !entry.Key.ToLowerInvariant().Contains(search.ToLowerInvariant()))
                    continue;

                var alias = entry.Key;
                var pubPath = entry.Value?.GetEffectivePublicKeyPath();
                bool pubExists = !string.IsNullOrEmpty(pubPath) && File.Exists(pubPath);

                if (pubExists)
                {
                    results.Add(new Result
                    {
                        Title = alias,
                        SubTitle = GetTranslation("plugin_quickssh_keys_copypub_label") + " " + pubPath,
                        IcoPath = AppIconGreenPath,
                        AutoCompleteText = query.ActionKeyword + " keys copy-pub " + alias,
                        Action = _ =>
                        {
                            try
                            {
                                var content = File.ReadAllText(pubPath).Trim();
                                _pluginContext?.API?.CopyToClipboard(content, false, false);
                                _pluginContext?.API?.ShowMsg("QuickSSH",
                                    GetTranslation("plugin_quickssh_copy_pubkey_success"));
                            }
                            catch (Exception)
                            {
                                _pluginContext?.API?.ShowMsg("QuickSSH",
                                    GetTranslation("plugin_quickssh_copy_clipboard_error"));
                            }
                            _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " keys copy-pub ", true);
                            return false;
                        }
                    });
                }
                else
                {
                    results.Add(new Result
                    {
                        Title = alias,
                        SubTitle = GetTranslation("plugin_quickssh_keys_copypub_notfound") + " " + (pubPath ?? ""),
                        IcoPath = AppIconRedPath,
                        AutoCompleteText = query.ActionKeyword + " keys copy-pub " + alias
                    });
                }
            }

            return results;
        }

        // ── keys scan ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Scans the user's ~/.ssh/ directory for private key files and offers them
        /// as registration candidates. Files ending in .pub are filtered out.
        /// </summary>
        private List<Result> HandleKeysScan(Query query)
        {
            var results = new List<Result>();
            var keys = _profileManager.UserData.SshKeys;

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandkeys_scan"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandkeys_scan"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " keys scan ",
                Score = int.MaxValue
            });
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys ", query.ActionKeyword + " keys"));

            var sshDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

            if (!Directory.Exists(sshDir))
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_scan"),
                    SubTitle = GetTranslation("plugin_quickssh_keys_scan_nodir"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            var candidates = ScanSshDirectory(sshDir);

            if (candidates.Count == 0)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_scan"),
                    SubTitle = GetTranslation("plugin_quickssh_keys_scan_empty"),
                    IcoPath = AppIconPath
                });
                return results;
            }

            // Pre-compute registered paths for O(1) lookup during candidate matching.
            var registeredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in keys)
            {
                if (kv.Value?.Path != null)
                    registeredPaths.Add(kv.Value.Path);
            }

            foreach (var candidate in candidates)
            {
                var fileName = Path.GetFileName(candidate);
                bool alreadyRegistered = registeredPaths.Contains(candidate);

                if (alreadyRegistered)
                {
                    results.Add(new Result
                    {
                        Title = fileName + " " + GetTranslation("plugin_quickssh_keys_scan_registered"),
                        SubTitle = candidate,
                        IcoPath = AppIconPath
                    });
                }
                else
                {
                    results.Add(new Result
                    {
                        Title = fileName,
                        SubTitle = GetTranslation("plugin_quickssh_keys_scan_register") + " " + candidate,
                        IcoPath = AppIconGreenPath,
                        Action = _ =>
                        {
                            _profileManager.UserData.SshKeys[fileName] = new SshKeyEntry
                            {
                                Path = candidate
                            };
                            _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " keys ", true);
                            return false;
                        }
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Returns a list of candidate private key file paths from the given directory.
        /// Filters out:
        /// <list type="bullet">
        ///   <item>.pub files (public keys)</item>
        ///   <item>known_hosts, known_hosts.old</item>
        ///   <item>config</item>
        ///   <item>authorized_keys, authorized_keys2</item>
        ///   <item>environment, profiles.json</item>
        ///   <item>.log, .bak, .tmp, .old, .json extensions</item>
        /// </list>
        /// </summary>
        internal static List<string> ScanSshDirectory(string sshDir)
        {
            var candidates = new List<string>();

            var excludedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "known_hosts", "known_hosts.old", "config", "authorized_keys", "authorized_keys2",
                "environment", "profiles.json"
            };

            try
            {
                foreach (var file in Directory.GetFiles(sshDir))
                {
                    var name = Path.GetFileName(file);

                    // Skip .pub files
                    if (name.EndsWith(".pub", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Skip well-known non-key files
                    if (excludedNames.Contains(name))
                        continue;

                    // Skip hidden/system files starting with a dot (except key files)
                    // and files with common non-key extensions
                    var ext = Path.GetExtension(name).ToLowerInvariant();
                    if (ext == ".log" || ext == ".bak" || ext == ".tmp" || ext == ".old" || ext == ".json")
                        continue;

                    candidates.Add(file);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            return candidates;
        }

        private List<Result> HandleConfig(Query query, string rest)
        {
            // Both "config" and "config import" trigger the same import action.
            var results = new List<Result>();

            // 1. Management/usage hint — always pinned at the top.
            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandconfig"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandconfig_usage"),
                IcoPath = AppIconPath,
                AutoCompleteText = query.ActionKeyword + " config ",
                Score = ScoreSubMenuManagement
            });

            // 2. Back-navigation row — returns to top-level command list.
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " ", query.ActionKeyword));

            // 3. Config action row.
            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_title_commandconfig"),
                SubTitle = GetTranslation("plugin_quickssh_subtitle_commandconfig"),
                IcoPath = AppIconGreenPath,
                Action = _ =>
                {
                    try
                    {
                        var hosts = SshConfigParser.Parse();
                        if (hosts.Count == 0)
                        {
                            _pluginContext.API.ShowMsg("QuickSSH",
                                GetTranslation("plugin_quickssh_config_notfound"));
                            _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " config ", true);
                            return false;
                        }

                        int imported = 0;
                        foreach (var host in hosts)
                        {
                            if (!_profileManager.UserData.Profiles.ContainsKey(host.Key))
                            {
                                _profileManager.UserData.Profiles[host.Key] = host.Value;
                                imported++;
                            }
                        }

                        _pluginContext.API.ShowMsg("QuickSSH",
                            string.Format(GetTranslation("plugin_quickssh_config_imported"), imported));
                    }
                    catch (Exception ex)
                    {
                        _pluginContext.API.ShowMsg("QuickSSH", "Error: " + ex.Message);
                    }
                    _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " config ", true);
                    return false;
                }
            });

            return results;
        }

        private List<Result> HandleDocs(Query query)
        {
            return new List<Result>
            {
                // 1. Management/usage hint — always pinned at the top.
                new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandhelp"),
                    SubTitle = GetTranslation("plugin_quickssh_subtitle_commandhelp_usage"),
                    IcoPath = AppIconPath,
                    Score = ScoreSubMenuManagement
                },
                // 2. Back-navigation row — returns to top-level command list.
                MakeBackNavResult(query, query.ActionKeyword + " ", query.ActionKeyword),
                // 3. Help action row.
                new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandhelp"),
                    SubTitle = GetTranslation("plugin_quickssh_subtitle_commandhelp"),
                    IcoPath = AppIconGreenPath,
                    Action = _ =>
                    {
                        using var process = Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://github.com/Vaso73/Flow.Launcher.Plugin.QuickSSH",
                            UseShellExecute = true
                        });
                        _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " ", true);
                        return false;
                    }
                }
            };
        }

        #endregion

        #region Navigation Helpers

        /// <summary>
        /// Creates a back-navigation result that navigates the query up one command level.
        /// The result is scored at <see cref="ScoreBackNavigation"/> so it always appears
        /// immediately below the pinned usage-hint row.
        /// </summary>
        /// <param name="query">The current Flow Launcher query (for the action keyword).</param>
        /// <param name="parentQueryText">
        /// The full query text to restore, e.g. <c>"ssh profiles "</c>.
        /// Should end with a trailing space so the user can continue typing.
        /// </param>
        /// <param name="parentLabel">
        /// Human-readable name of the parent level shown in the result title,
        /// e.g. <c>"ssh"</c> or <c>"ssh profiles"</c>.
        /// </param>
        private Result MakeBackNavResult(Query query, string parentQueryText, string parentLabel)
        {
            return new Result
            {
                Title = string.Format(GetTranslation("plugin_quickssh_back_nav_title"), parentLabel),
                IcoPath = AppIconPath,
                Score = ScoreBackNavigation,
                AutoCompleteText = parentQueryText,
                Action = _ =>
                {
                    _pluginContext?.API?.ChangeQuery(parentQueryText, true);
                    return false;
                }
            };
        }

        #endregion

        #region SSH / SCP Execution

        /// <summary>
        /// Normalises a raw SSH or SCP command string so it is safe to pass to a terminal.
        /// <list type="bullet">
        ///   <item>Strips leading Windows cmd.exe-style /flags (e.g. "/c", "/k") that
        ///   users sometimes accidentally prepend.</item>
        ///   <item>Auto-prepends "ssh " when the user supplied only a destination
        ///   (e.g. "user@host" instead of "ssh user@host").</item>
        ///   <item>Removes /flags that appear immediately after the "ssh " prefix for
        ///   the same reason (e.g. "ssh /c user@host" → "ssh user@host").</item>
        /// </list>
        /// SCP commands ("scp ...") are returned unchanged after /flag stripping.
        /// Returns <see langword="null"/> if nothing valid remains after stripping.
        /// </summary>
        internal static string NormalizeSshCommand(string rawCommand)
        {
            if (string.IsNullOrWhiteSpace(rawCommand))
                return null;

            var cmd = rawCommand.Trim();

            // Strip any leading Windows cmd-style /flags.
            while (cmd.StartsWith("/", StringComparison.Ordinal))
            {
                var space = cmd.IndexOf(' ');
                if (space < 0)
                    return null; // nothing left after stripping
                cmd = cmd.Substring(space + 1).TrimStart();
            }

            if (string.IsNullOrEmpty(cmd))
                return null;

            // SCP commands are returned as-is (after /flag stripping above).
            if (cmd.StartsWith("scp ", StringComparison.OrdinalIgnoreCase)
                || cmd.Equals("scp", StringComparison.OrdinalIgnoreCase))
                return cmd;

            // Auto-prepend "ssh " when only a destination was given.
            if (!cmd.StartsWith("ssh ", StringComparison.OrdinalIgnoreCase)
                && !cmd.Equals("ssh", StringComparison.OrdinalIgnoreCase))
            {
                cmd = "ssh " + cmd;
            }

            // Remove any /flags that appear right after the "ssh " prefix
            // (e.g. a user stored "ssh /c user@host" by mistake).
            const string sshPrefix = "ssh ";
            if (cmd.Length > sshPrefix.Length)
            {
                var rest = cmd.Substring(sshPrefix.Length);
                bool changed = false;
                while (rest.StartsWith("/", StringComparison.Ordinal))
                {
                    var space = rest.IndexOf(' ');
                    if (space < 0) { rest = string.Empty; changed = true; break; }
                    rest = rest.Substring(space + 1).TrimStart();
                    changed = true;
                }
                if (changed)
                    cmd = string.IsNullOrEmpty(rest) ? null : "ssh " + rest;
            }

            return cmd;
        }

        /// <summary>
        /// Returns <see langword="true"/> when the input string looks like a direct SSH
        /// destination or option string rather than a plugin command name.
        /// Supports:
        /// <list type="bullet">
        ///   <item><c>user@host</c> — contains an at-sign</item>
        ///   <item><c>-p 22 user@host</c> — starts with a dash (SSH option flag)</item>
        ///   <item><c>10.100.100.110</c> or <c>myserver.example.com</c> — bare hostname / IP
        ///   (only hostname-safe characters, at least one dot)</item>
        /// </list>
        /// </summary>
        internal static bool IsImplicitSshInput(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            // SSH option flag (e.g. -p, -i, -o, -L, -R, -D, etc.)
            if (input[0] == '-')
                return true;

            // user@host format
            if (input.Contains('@'))
                return true;

            // Bare hostname or IP address: check only the first token so that
            // "10.0.0.1 -p 22" is still detected via the first token.
            var firstToken = input.Split(' ', 2)[0];
            return IsHostnameOrIp(firstToken);
        }

        /// <summary>
        /// Returns <see langword="true"/> when <paramref name="token"/> looks like a
        /// hostname or dotted IP address (only hostname-safe chars and at least one dot).
        /// </summary>
        private static bool IsHostnameOrIp(string token)
        {
            if (string.IsNullOrEmpty(token))
                return false;

            bool hasDot = false;
            foreach (var c in token)
            {
                if (c == '.') { hasDot = true; continue; }
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') continue;
                return false; // illegal char — not a hostname/IP
            }
            return hasDot; // must have at least one dot to be unambiguous
        }

        private void RunCommand(string command)
        {
            // Normalise: strip accidental Windows cmd-style /flags. For SSH commands,
            // also ensure the "ssh " prefix is present.
            command = NormalizeSshCommand(command);
            if (string.IsNullOrEmpty(command))
                return;

            var selectedShell = _profileManager.UserData.SelectedCustomShell;
            var customShells = _profileManager.UserData.CustomShell;

            string fileName;
            string arguments;
            string customShellName = null;

            if (!string.IsNullOrEmpty(selectedShell) && customShells.ContainsKey(selectedShell))
            {
                var shellValue = customShells[selectedShell];
                customShellName = selectedShell;

                if (string.IsNullOrEmpty(shellValue))
                {
                    // Shell name is the executable
                    fileName = Utils.ResolveExecutable(selectedShell);
                    arguments = command;
                }
                else
                {
                    // Parse the shell value into exe + args
                    var spaceIdx = shellValue.IndexOf(' ');
                    if (spaceIdx < 0)
                    {
                        fileName = Utils.ResolveExecutable(shellValue);
                        arguments = command;
                    }
                    else
                    {
                        fileName = Utils.ResolveExecutable(shellValue.Substring(0, spaceIdx));
                        arguments = shellValue.Substring(spaceIdx + 1) + " " + command;
                    }
                }
            }
            else
            {
                // Default: use cmd.exe with /k so the window stays open after SSH/SCP exits,
                // allowing the user to see any connection-error messages.
                fileName = GetCmdExePath();
                arguments = "/k " + command;
            }

            // Use the user's home directory as the working directory so SSH can always
            // find ~/.ssh keys and config, even when FlowLauncher itself is installed in
            // a path that contains non-ASCII characters or spaces.
            var workingDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true,
                    WorkingDirectory = workingDir
                });
            }
            catch (Exception ex)
            {
                if (customShellName != null)
                {
                    // The custom shell executable could not be started; fall back to
                    // cmd.exe so the connection still opens despite the broken shell.
                    try
                    {
                        using var fallback = Process.Start(new ProcessStartInfo
                        {
                            FileName = GetCmdExePath(),
                            Arguments = "/k " + command,
                            UseShellExecute = true,
                            WorkingDirectory = workingDir
                        });
                    }
                    catch (Exception ex2)
                    {
                        _pluginContext?.API?.ShowMsg("QuickSSH", "Error: " + ex2.Message);
                    }
                }
                else
                {
                    _pluginContext?.API?.ShowMsg("QuickSSH", "Error: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Returns the absolute path to cmd.exe (always in %SystemRoot%\System32).
        /// Falls back to the bare name as a last resort so PATH can resolve it.
        /// </summary>
        private static string GetCmdExePath()
        {
            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var cmdPath = Path.Combine(systemDir, "cmd.exe");
            return File.Exists(cmdPath) ? cmdPath : "cmd.exe";
        }

        #endregion

        #region Search Scoring

        internal static int ScoreProfile(string search, string name, string command)
        {
            var searchLower = RemoveDiacritics(search.ToLowerInvariant());
            var nameLower = RemoveDiacritics(name.ToLowerInvariant());
            var commandLower = RemoveDiacritics(command.ToLowerInvariant());

            bool nameExact = ContainsIgnoreAccents(nameLower, searchLower);
            bool cmdExact = ContainsIgnoreAccents(commandLower, searchLower);

            if (nameExact && cmdExact) return 0;
            if (nameExact) return 1;
            if (cmdExact) return 2;

            bool nameFuzzy = FuzzyContains(nameLower, searchLower);
            bool cmdFuzzy = FuzzyContains(commandLower, searchLower);

            if (nameFuzzy && cmdFuzzy) return 3;
            if (nameFuzzy) return 4;
            if (cmdFuzzy) return 5;

            return int.MaxValue;
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static bool ContainsIgnoreAccents(string source, string search)
        {
            return RemoveDiacritics(source).Contains(RemoveDiacritics(search));
        }

        private static bool FuzzyContains(string source, string search)
        {
            if (search.Length < 5)
                return source.Contains(search);

            int tolerance = search.Length / 5;

            for (int i = 0; i <= source.Length - search.Length + tolerance; i++)
            {
                int end = Math.Min(i + search.Length + tolerance, source.Length);
                var window = source.Substring(i, end - i);
                if (DamerauLevenshteinDistance(window, search) <= tolerance)
                    return true;
            }

            return false;
        }

        private static int DamerauLevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            var d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;

                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);

                    if (i > 1 && j > 1 && s[i - 1] == t[j - 2] && s[i - 2] == t[j - 1])
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + cost);
                }
            }

            return d[n, m];
        }

        #endregion

        #region Shell Argument Parsing

        private static (string name, string value) ParseShellAddArgs(string input)
        {
            if (string.IsNullOrEmpty(input))
                return ("", "");

            // Check for quoted strings
            if (input.StartsWith("\""))
            {
                int endQuote = input.IndexOf('"', 1);
                if (endQuote > 0)
                {
                    var name = input.Substring(1, endQuote - 1);
                    var value = input.Length > endQuote + 1
                        ? input.Substring(endQuote + 1).Trim()
                        : "";
                    return (name, value);
                }
            }

            var spaceIdx = input.IndexOf(' ');
            if (spaceIdx < 0)
                return (input, "");

            return (input.Substring(0, spaceIdx), input.Substring(spaceIdx + 1).Trim());
        }

        #endregion

        #region i18n

        public string GetTranslatedPluginTitle()
        {
            return GetTranslation("plugin_quickssh_plugin_name");
        }

        public string GetTranslatedPluginDescription()
        {
            return GetTranslation("plugin_quickssh_plugin_description");
        }

        public static string GetTranslation(string key)
        {
            try
            {
                return _pluginContext?.API?.GetTranslation(key) ?? key;
            }
            catch
            {
                return key;
            }
        }

        #endregion
    }
}
