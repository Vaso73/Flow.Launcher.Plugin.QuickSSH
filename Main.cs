using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
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
        private const string CommandActions = "actions";
        private const string CommandTools = "tools";
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
            CommandProfiles, CommandActions, CommandTools, CommandCustomShell, CommandKeys, CommandConfig, CommandHelp, "add"
        };

        // Sub-commands of "profiles"
        private const string ProfilesSubAdd    = "add";
        private const string ProfilesSubRemove = "remove";
        private const string ProfilesSubRename = "rename";
        private const string ProfilesSubCopy   = "copy";
        private const string ProfilesSubExport = "export";
        private const string ProfilesSubImport = "import";
        private const string ProfilesSubManage = "manage";

        private static readonly string[] ProfilesSubCommands = new[]
        {
            ProfilesSubAdd, ProfilesSubRemove, ProfilesSubRename,
            ProfilesSubCopy, ProfilesSubExport, ProfilesSubImport, ProfilesSubManage
        };

        // Sub-commands of "actions"
        private const string ActionsSubRun    = "run";       // profile-first compatibility route
        private const string ActionsSubUse    = "use";       // action-first guided route
        private const string ActionsSubAdd    = "add";
        private const string ActionsSubManage = "manage";
        private const string ActionsSubRemove = "remove";
        private const string ActionsSubRename = "rename";

        private static readonly string[] ActionsSubCommands = new[]
        {
            ActionsSubRun, ActionsSubAdd, ActionsSubManage, ActionsSubRemove, ActionsSubRename
        };

        // Sub-commands of "shell"
        private static readonly string[] ShellSubCommands = new[]
        {
            "add", "remove", "manage"
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
        private const string KeysSubManage   = "manage";

        private static readonly string[] KeysSubCommands = new[]
        {
            KeysSubInstall, KeysSubAdd, KeysSubGenerate, KeysSubRename, KeysSubRemove, KeysSubCopyPath, KeysSubCopyPub, KeysSubScan, KeysSubManage
        };

        private const string AppIconPath = "Images\\app.png";
        private const string AppIconGreenPath = "Images\\app-green.png";
        private const string AppIconOrangePath = "Images\\app-orange.png";
        private const string AppIconRedPath = "Images\\app-red.png";

        /// <summary>
        /// Returns the icon that represents an operation consistently across every menu,
        /// submenu, autocomplete result, selection step, and confirmation screen.
        /// </summary>
        internal static string GetSemanticIconPath(string operation)
        {
            switch ((operation ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "add":
                case "create":
                case "generate":
                case "import":
                case "export":
                case "install":
                case "scan":
                case "run":
                case "use":
                case "save":
                case "saved":
                case "connect":
                case "execute":
                    return AppIconGreenPath;

                case "rename":
                case "edit":
                case "update":
                    return AppIconOrangePath;

                case "remove":
                case "delete":
                    return AppIconRedPath;

                default:
                    return AppIconPath;
            }
        }

        // ── Submenu ordering scores (Flow Launcher sorts higher score first) ──────
        // Consistent submenu layout:
        //   1. back navigation
        //   2. saved items / primary actions
        //   3. manage row
        // Operation-specific usage hints remain directly below Back.
        internal const int ScoreBackNavigation    = int.MaxValue;
        internal const int ScoreSubMenuManagement = int.MaxValue - 1;

        // "profiles" submenu — saved profiles are primary; all mutations live under Manage.
        internal const int ScoreProfilesSavedItem    = 900_000; // decremented per additional profile
        internal const int ScoreProfilesActionManage = 100_000;
        internal const int ScoreProfilesManageAdd    = 9000;
        internal const int ScoreProfilesManageRename = 8000;
        internal const int ScoreProfilesManageCopy   = 7000;
        internal const int ScoreProfilesManageExport = 6000;
        internal const int ScoreProfilesManageImport = 5000;
        internal const int ScoreProfilesManageRemove = 1000;

        // Profile creation steps. Port selection is shown before authentication;
        // saved private keys stay together above default-auth and advanced choices.
        internal const int ScoreProfilesWizardDefaultPort   = 900_000;
        internal const int ScoreProfilesWizardCustomPort    = 800_000;
        internal const int ScoreProfilesWizardSavedKeyStart = 900_000;
        internal const int ScoreProfilesWizardManageKeys    = 300_000;
        internal const int ScoreProfilesWizardDefaultAuth   = 200_000;
        internal const int ScoreProfilesWizardAdvanced      = 100_000;

        // "shell" submenu — saved shells are primary; add/remove live under Manage.
        internal const int ScoreShellSelected     = 900_000;
        internal const int ScoreShellOtherStart   = 899_000; // decremented per additional shell
        internal const int ScoreShellActionManage = 100_000;
        internal const int ScoreShellManageAdd    = 9000;
        internal const int ScoreShellManageRemove = 8000;

        // ── Top-level command ordering (root "ssh" menu) ────────────────────────
        // Gaps of 100 000 ensure Flow Launcher's internal usage-history / fuzzy-match
        // bonus (which can add thousands of points for frequently-selected items)
        // cannot reorder the root menu.
        internal const int ScoreTopLevelProfiles = 400_000;
        internal const int ScoreTopLevelActions  = 300_000;
        internal const int ScoreTopLevelTools    = 200_000;
        internal const int ScoreTopLevelHelp     = 100_000;

        // "tools" submenu — direct navigation to less frequent setup operations.
        internal const int ScoreToolsKeys   = 900_000;
        internal const int ScoreToolsShell  = 800_000;
        internal const int ScoreToolsConfig = 700_000;

        // "actions" submenu — saved actions are primary; all mutations live under Manage.
        internal const int ScoreActionsSavedItem       = 900_000;
        internal const int ScoreActionsActionManage    = 100_000;
        internal const int ScoreActionsManageAdd       = 9000;
        internal const int ScoreActionsManageRename    = 8000;
        internal const int ScoreActionsManageRemove    = 7000;

        // Final confirmation follows the global rule: Back is always first.
        internal const int ScoreActionsConfirmBack     = int.MaxValue;
        internal const int ScoreActionsConfirmRun      = int.MaxValue - 1;
        internal const int ScoreActionsConfirmProfile  = 8000;
        internal const int ScoreActionsConfirmAction   = 7000;
        internal const int ScoreActionsConfirmCommand  = 6000;

        // "keys" submenu — install and saved keys are primary; lower-level operations are grouped.
        internal const int ScoreKeysSavedItem      = 900_000; // decremented per additional key
        internal const int ScoreKeysActionInstall  = 200_000;
        internal const int ScoreKeysActionManage   = 100_000;
        internal const int ScoreKeysManageAdd      = 8000;
        internal const int ScoreKeysManageGenerate = 7000;
        internal const int ScoreKeysManageScan     = 6000;
        internal const int ScoreKeysManageRename   = 5000;
        internal const int ScoreKeysManageCopyPath = 4000;
        internal const int ScoreKeysManageCopyPub  = 3000;
        internal const int ScoreKeysManageRemove   = 1000;

        private string _databasePath;
        private string _dataDir;
        private bool _isSshInstalled = true;
        private bool _isDatabaseCreated = true;

        /// <inheritdoc />
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

        /// <inheritdoc />
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
                case CommandActions:
                    results.AddRange(HandleActions(query, rest));
                    break;
                case CommandTools:
                    results.AddRange(HandleTools(query, rest));
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
                case ProfilesSubManage: return HandleProfilesManage(query);
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
            var results = new List<Result>
            {
                MakeBackNavResult(query, query.ActionKeyword + " ", query.ActionKeyword)
            };
            var profiles = _profileManager.UserData.Profiles;

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
                        SubTitle = BuildProfileListSubtitle(profile),
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

            if (string.IsNullOrEmpty(search))
            {
                var manageText = query.ActionKeyword + " profiles manage ";
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandprofiles_manage"),
                    SubTitle = GetTranslation("plugin_quickssh_subtitle_commandprofiles_manage"),
                    IcoPath = AppIconPath,
                    AutoCompleteText = manageText,
                    Score = ScoreProfilesActionManage,
                    Action = _ =>
                    {
                        _pluginContext?.API?.ChangeQuery(manageText, true);
                        return false;
                    }
                });
            }

            return results;
        }

        private List<Result> HandleProfilesManage(Query query)
        {
            var results = new List<Result>
            {
                MakeBackNavResult(query, query.ActionKeyword + " profiles ", query.ActionKeyword + " profiles")
            };

            var profileActions = new[]
            {
                ("add",    GetTranslation("plugin_quickssh_title_commandprofiles_add"),    GetTranslation("plugin_quickssh_subtitle_commandprofiles_add"),          AppIconGreenPath,  ScoreProfilesManageAdd),
                ("rename", GetTranslation("plugin_quickssh_title_commandprofiles_rename"), GetTranslation("plugin_quickssh_subtitle_commandprofiles_rename"),       AppIconOrangePath, ScoreProfilesManageRename),
                ("copy",   GetTranslation("plugin_quickssh_title_commandprofiles_copy"),   GetTranslation("plugin_quickssh_subtitle_commandprofiles_copy_usage"),   AppIconPath,       ScoreProfilesManageCopy),
                ("export", GetTranslation("plugin_quickssh_title_commandprofiles_export"), GetTranslation("plugin_quickssh_subtitle_commandprofiles_export_usage"), AppIconGreenPath,  ScoreProfilesManageExport),
                ("import", GetTranslation("plugin_quickssh_title_commandprofiles_import"), GetTranslation("plugin_quickssh_subtitle_commandprofiles_import_usage"), AppIconGreenPath,  ScoreProfilesManageImport),
                ("remove", GetTranslation("plugin_quickssh_title_commandprofiles_remove"), GetTranslation("plugin_quickssh_subtitle_commandprofiles_remove"),       AppIconRedPath,    ScoreProfilesManageRemove),
            };

            foreach (var (scName, scTitle, scSubTitle, iconPath, scScore) in profileActions)
            {
                var autoText = query.ActionKeyword + " profiles " + scName + " ";
                results.Add(new Result
                {
                    Title = scTitle,
                    SubTitle = scSubTitle,
                    IcoPath = iconPath,
                    AutoCompleteText = autoText,
                    Score = scScore,
                    Action = _ =>
                    {
                        _pluginContext?.API?.ChangeQuery(autoText, true);
                        return false;
                    }
                });
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
                    IcoPath = GetSemanticIconPath("add"),
                    AutoCompleteText = redirectTarget,
                    Score = ScoreSubMenuManagement,
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
            var profiles = _profileManager.UserData.Profiles;

            results.Add(MakeBackNavResult(
                query,
                query.ActionKeyword + " profiles manage ",
                query.ActionKeyword + " profiles manage"));

            rest = CommandInputGuard.NormalizeNestedCommandInput(
                rest, query.ActionKeyword, "profiles add");
            if (string.IsNullOrWhiteSpace(rest))
            {
                var exampleName = ProfileWizard.BuildAvailableName("server", profiles.Keys);
                var exampleText = query.ActionKeyword + " profiles add " + exampleName;
                results.Add(MakeWizardExampleResultFromKeys(
                    "plugin_quickssh_wizard_profiles_add_name_title",
                    "plugin_quickssh_wizard_profiles_add_name_subtitle",
                    exampleText,
                    exampleName));
                return results;
            }

            var addParts = rest.Split(new[] { ' ' }, 2);
            var profileName = addParts[0].Trim();
            var profileInput = addParts.Length > 1 ? addParts[1].Trim() : "";

            if (!CommandInputGuard.IsValidSavedName(profileName))
            {
                results.Add(new Result
                {
                    Title = profileName,
                    SubTitle = GetTranslation("plugin_quickssh_name_invalid"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (CommandInputGuard.IsReservedSavedName(profileName))
            {
                results.Add(new Result
                {
                    Title = profileName,
                    SubTitle = GetTranslation("plugin_quickssh_name_reserved"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (CommandInputGuard.FindExistingName(profiles, profileName) != null)
            {
                results.Add(new Result
                {
                    Title = profileName,
                    SubTitle = GetTranslation("plugin_quickssh_name_exists"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (string.IsNullOrWhiteSpace(profileInput))
            {
                var exampleText = query.ActionKeyword + " profiles add " + profileName + " user@host";
                results.Add(MakeWizardExampleResultFromKeys(
                    "plugin_quickssh_wizard_profiles_add_target_title",
                    "plugin_quickssh_wizard_profiles_add_target_subtitle",
                    exampleText));
                return results;
            }

            SshProfile profile;
            if (ProfileWizard.IsAdvancedCommand(profileInput))
            {
                var advancedCommand = NormalizeSshCommand(profileInput) ?? "";
                if (string.IsNullOrEmpty(advancedCommand))
                {
                    results.Add(new Result
                    {
                        Title = profileInput,
                        SubTitle = GetTranslation("plugin_quickssh_command_invalid"),
                        IcoPath = AppIconRedPath
                    });
                    return results;
                }

                profile = SshProfile.ParseFromLegacyCommand(advancedCommand);
                if (string.IsNullOrWhiteSpace(profile.HostName) &&
                    !string.Equals(profile.Type, "scp", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new Result
                    {
                        Title = profileInput,
                        SubTitle = GetTranslation("plugin_quickssh_profiles_destination_invalid"),
                        IcoPath = AppIconRedPath
                    });
                    return results;
                }
            }
            else
            {
                var inputTokens = SshProfile.TokenizeShellLine(profileInput);
                var hasPortOption = inputTokens.Any(token =>
                    token.Equals(ProfileWizard.PortOption, StringComparison.OrdinalIgnoreCase));
                var looksLikeBasicInput = inputTokens.Count == 1 ||
                    inputTokens.Any(token =>
                        token.Equals(ProfileWizard.SavedKeyOption, StringComparison.OrdinalIgnoreCase) ||
                        token.Equals(ProfileWizard.DefaultAuthOption, StringComparison.OrdinalIgnoreCase) ||
                        token.Equals(ProfileWizard.PortOption, StringComparison.OrdinalIgnoreCase));

                if (!ProfileWizard.TryParseBasicInput(
                    profileInput,
                    out var destination,
                    out var keyAlias,
                    out var useDefaultAuthentication,
                    out var port))
                {
                    if (looksLikeBasicInput)
                    {
                        results.Add(new Result
                        {
                            Title = hasPortOption
                                ? GetTranslation("plugin_quickssh_profiles_port_invalid_title")
                                : profileInput,
                            SubTitle = hasPortOption
                                ? GetTranslation("plugin_quickssh_profiles_port_invalid_subtitle")
                                : GetTranslation("plugin_quickssh_profiles_destination_invalid"),
                            IcoPath = AppIconRedPath
                        });
                        return results;
                    }

                    // Backward-compatible advanced input without an explicit "ssh" prefix.
                    var legacyCommand = NormalizeSshCommand(profileInput) ?? "";
                    profile = SshProfile.ParseFromLegacyCommand(legacyCommand);
                    if (string.IsNullOrWhiteSpace(profile.HostName))
                    {
                        results.Add(new Result
                        {
                            Title = profileInput,
                            SubTitle = GetTranslation("plugin_quickssh_profiles_destination_invalid"),
                            IcoPath = AppIconRedPath
                        });
                        return results;
                    }
                }
                else if (keyAlias == null && !useDefaultAuthentication && port == null)
                {
                    var portPrefix = query.ActionKeyword + " profiles add " + profileName + " " + destination;
                    var defaultPortText = portPrefix + " " + ProfileWizard.PortOption + " 22";
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_profiles_port_default_title"),
                        SubTitle = GetTranslation("plugin_quickssh_profiles_port_default_subtitle"),
                        IcoPath = AppIconPath,
                        Score = ScoreProfilesWizardDefaultPort,
                        AutoCompleteText = defaultPortText,
                        Action = _ =>
                        {
                            _pluginContext?.API?.ChangeQuery(defaultPortText, true);
                            return false;
                        }
                    });

                    var customPortText = portPrefix + " " + ProfileWizard.PortOption + " ";
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_profiles_port_custom_title"),
                        SubTitle = GetTranslation("plugin_quickssh_profiles_port_custom_subtitle"),
                        IcoPath = AppIconPath,
                        Score = ScoreProfilesWizardCustomPort,
                        AutoCompleteText = customPortText,
                        Action = _ =>
                        {
                            _pluginContext?.API?.ChangeQuery(customPortText, true);
                            return false;
                        }
                    });
                    return results;
                }
                else if (keyAlias == null && !useDefaultAuthentication)
                {
                    var authPrefix = query.ActionKeyword + " profiles add " + profileName + " " +
                        destination + " " + ProfileWizard.PortOption + " " + port;
                    var usableKeyCount = 0;
                    var keyScore = ScoreProfilesWizardSavedKeyStart;
                    foreach (var entry in _profileManager.UserData.SshKeys)
                    {
                        var rowScore = keyScore--;
                        var selectedAlias = entry.Key;
                        var selectedEntry = entry.Value;
                        var selectedPath = selectedEntry?.Path ?? "";
                        if (!ProfileWizard.IsUsablePrivateKey(selectedEntry))
                        {
                            results.Add(new Result
                            {
                                Title = selectedAlias,
                                SubTitle = GetProfileKeyUnavailableSubtitle(selectedEntry),
                                IcoPath = AppIconRedPath,
                                Score = rowScore
                            });
                            continue;
                        }

                        usableKeyCount++;
                        var keyText = authPrefix + " " +
                            ProfileWizard.SavedKeyOption + " " + selectedAlias;
                        results.Add(new Result
                        {
                            Title = selectedAlias,
                            SubTitle = GetTranslation("plugin_quickssh_keys_private_path_label") + " " +
                                ProfileWizard.ExpandLocalPath(selectedPath),
                            IcoPath = AppIconGreenPath,
                            Score = rowScore,
                            AutoCompleteText = keyText,
                            Action = _ =>
                            {
                                _pluginContext?.API?.ChangeQuery(keyText, true);
                                return false;
                            }
                        });
                    }

                    if (usableKeyCount == 0)
                    {
                        var manageKeysText = query.ActionKeyword + " keys manage ";
                        results.Add(new Result
                        {
                            Title = GetTranslation("plugin_quickssh_profiles_no_private_keys_title"),
                            SubTitle = GetTranslation("plugin_quickssh_profiles_no_private_keys_subtitle"),
                            IcoPath = AppIconPath,
                            Score = ScoreProfilesWizardManageKeys,
                            AutoCompleteText = manageKeysText,
                            Action = _ =>
                            {
                                _pluginContext?.API?.ChangeQuery(manageKeysText, true);
                                return false;
                            }
                        });
                    }

                    var defaultText = authPrefix + " " + ProfileWizard.DefaultAuthOption;
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_profiles_auth_default_title"),
                        SubTitle = GetTranslation("plugin_quickssh_profiles_auth_default_subtitle"),
                        IcoPath = AppIconPath,
                        Score = ScoreProfilesWizardDefaultAuth,
                        AutoCompleteText = defaultText,
                        Action = _ =>
                        {
                            _pluginContext?.API?.ChangeQuery(defaultText, true);
                            return false;
                        }
                    });

                    var advancedText = query.ActionKeyword + " profiles add " + profileName + " ssh ";
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_profiles_auth_advanced_title"),
                        SubTitle = GetTranslation("plugin_quickssh_profiles_auth_advanced_subtitle"),
                        IcoPath = AppIconPath,
                        Score = ScoreProfilesWizardAdvanced,
                        AutoCompleteText = advancedText,
                        Action = _ =>
                        {
                            _pluginContext?.API?.ChangeQuery(advancedText, true);
                            return false;
                        }
                    });
                    return results;
                }
                else
                {
                    string? identityFile = null;
                    if (keyAlias != null)
                    {
                        var storedAlias = CommandInputGuard.FindExistingName(
                            _profileManager.UserData.SshKeys, keyAlias);
                        if (storedAlias == null)
                        {
                            results.Add(new Result
                            {
                                Title = keyAlias,
                                SubTitle = GetTranslation("plugin_quickssh_profiles_key_notfound"),
                                IcoPath = AppIconRedPath
                            });
                            return results;
                        }

                        var keyEntry = _profileManager.UserData.SshKeys[storedAlias];
                        if (!ProfileWizard.IsUsablePrivateKey(keyEntry))
                        {
                            results.Add(new Result
                            {
                                Title = storedAlias,
                                SubTitle = GetProfileKeyUnavailableSubtitle(keyEntry),
                                IcoPath = AppIconRedPath
                            });
                            return results;
                        }

                        identityFile = ProfileWizard.ExpandLocalPath(keyEntry.Path);
                    }

                    if (!ProfileWizard.TryCreateBasicProfile(
                        destination, identityFile, port, out profile))
                    {
                        results.Add(new Result
                        {
                            Title = destination,
                            SubTitle = GetTranslation("plugin_quickssh_profiles_destination_invalid"),
                            IcoPath = AppIconRedPath
                        });
                        return results;
                    }
                }
            }

            results.Add(new Result
            {
                Title = string.Format(
                    GetTranslation("plugin_quickssh_profiles_save_title"), profileName),
                SubTitle = BuildProfileListSubtitle(profile),
                IcoPath = AppIconGreenPath,
                Action = _ =>
                {
                    profiles[profileName] = profile;
                    _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " profiles ", true);
                    return false;
                }
            });

            return results;
        }


        // ── profiles remove ───────────────────────────────────────────────────────

        private List<Result> HandleProfilesRemove(Query query, string rest)
        {
            var profiles = _profileManager.UserData.Profiles;
            var exactName = CommandInputGuard.FindExistingName(profiles, rest);
            var results = new List<Result>
            {
                exactName == null
                    ? MakeBackNavResult(
                        query,
                        query.ActionKeyword + " profiles manage ",
                        query.ActionKeyword + " profiles manage")
                    : MakeBackNavResult(
                        query,
                        query.ActionKeyword + " profiles remove ",
                        "profiles remove selection")
            };

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

            if (exactName != null)
            {
                var profile = profiles[exactName];
                results.Add(new Result
                {
                    Title = string.Format(
                        GetTranslation("plugin_quickssh_profiles_remove_confirm"), exactName),
                    SubTitle = BuildProfileListSubtitle(profile),
                    IcoPath = AppIconRedPath,
                    Score = ScoreActionsConfirmRun,
                    Action = _ =>
                    {
                        profiles.Remove(exactName);
                        _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " profiles ", true);
                        return false;
                    }
                });
                return results;
            }

            foreach (var entry in profiles)
            {
                if (!string.IsNullOrEmpty(rest) &&
                    !entry.Key.ToLowerInvariant().Contains(rest.ToLowerInvariant()))
                    continue;

                var autoText = query.ActionKeyword + " profiles remove " + entry.Key;
                results.Add(new Result
                {
                    Title = entry.Key,
                    SubTitle = BuildProfileListSubtitle(entry.Value),
                    IcoPath = AppIconRedPath,
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

        // ── profiles rename ───────────────────────────────────────────────────────

        private List<Result> HandleProfilesRename(Query query, string rest)
        {
            var results = new List<Result>();
            var profiles = _profileManager.UserData.Profiles;

            rest = CommandInputGuard.NormalizeNestedCommandInput(
                rest, query.ActionKeyword, "profiles rename");
            var parts = rest.Split(new[] { ' ' }, 2);
            var requestedOldName = parts[0].Trim();
            var newName = parts.Length > 1 ? parts[1].Trim() : "";

            if (string.IsNullOrEmpty(requestedOldName))
            {
                results.Add(MakeBackNavResult(query, query.ActionKeyword + " profiles manage ", query.ActionKeyword + " profiles manage"));

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
                    var autoText = ProfileWizard.BuildPrefilledRenameQuery(
                        query.ActionKeyword, "profiles rename", name);
                    results.Add(new Result
                    {
                        Title = name,
                        SubTitle = BuildProfileListSubtitle(entry.Value),
                        IcoPath = GetSemanticIconPath("rename"),
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

            var oldName = CommandInputGuard.FindExistingName(profiles, requestedOldName);
            results.Add(MakeBackNavResult(query, query.ActionKeyword + " profiles manage ", query.ActionKeyword + " profiles manage"));

            if (oldName == null)
            {
                results.Add(new Result
                {
                    Title = requestedOldName,
                    SubTitle = GetTranslation("plugin_quickssh_rename_notfound"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                var suggestedName = ProfileWizard.BuildSuggestedName(oldName, profiles.Keys);
                var exampleText = ProfileWizard.BuildRenameQuery(
                    query.ActionKeyword, "profiles rename", oldName, suggestedName);
                results.Add(MakeWizardExampleResult(
                    string.Format(GetTranslation("plugin_quickssh_wizard_profiles_rename_title"), suggestedName),
                    string.Format(GetTranslation("plugin_quickssh_wizard_profiles_rename_subtitle"), oldName),
                    exampleText));
                return results;
            }

            if (!CommandInputGuard.IsValidSavedName(newName))
            {
                results.Add(new Result
                {
                    Title = newName,
                    SubTitle = GetTranslation("plugin_quickssh_name_invalid"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (CommandInputGuard.IsReservedSavedName(newName))
            {
                results.Add(new Result
                {
                    Title = newName,
                    SubTitle = GetTranslation("plugin_quickssh_name_reserved"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (string.Equals(oldName, newName, StringComparison.Ordinal))
            {
                var suggestedName = ProfileWizard.BuildSuggestedName(oldName, profiles.Keys);
                var exampleText = ProfileWizard.BuildRenameQuery(
                    query.ActionKeyword, "profiles rename", oldName, suggestedName);
                results.Add(MakeWizardExampleResult(
                    string.Format(GetTranslation("plugin_quickssh_wizard_profiles_rename_title"), suggestedName),
                    string.Format(GetTranslation("plugin_quickssh_wizard_profiles_rename_prefilled_subtitle"), oldName),
                    exampleText));
                return results;
            }

            var conflictingName = CommandInputGuard.FindExistingName(profiles, newName);
            if (conflictingName != null &&
                !string.Equals(conflictingName, oldName, StringComparison.Ordinal))
            {
                results.Add(new Result
                {
                    Title = newName,
                    SubTitle = GetTranslation("plugin_quickssh_name_exists"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            results.Add(new Result
            {
                Title = string.Format(
                    GetTranslation("plugin_quickssh_profiles_rename_confirm_title"),
                    oldName, newName),
                SubTitle = GetTranslation("plugin_quickssh_profiles_rename_confirm_subtitle"),
                IcoPath = GetSemanticIconPath("rename"),
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

            return results;
        }

        // ── profiles copy ─────────────────────────────────────────────────────────

        private List<Result> HandleProfilesCopy(Query query, string search)
        {
            var results = new List<Result>();
            var profiles = _profileManager.UserData.Profiles;

            results.Add(MakeBackNavResult(query, query.ActionKeyword + " profiles manage ", query.ActionKeyword + " profiles manage"));

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
                    SubTitle = GetTranslation("plugin_quickssh_subtitle_commandprofiles_copy") + " " + BuildProfileListSubtitle(entry.Value),
                    IcoPath = GetSemanticIconPath("copy"),
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

            results.Add(MakeBackNavResult(query, query.ActionKeyword + " profiles manage ", query.ActionKeyword + " profiles manage"));

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

            results.Add(MakeBackNavResult(query, query.ActionKeyword + " profiles manage ", query.ActionKeyword + " profiles manage"));

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

            var result = ProfileImportService.Import(_profileManager, imported);

            _pluginContext.API.ShowMsg("QuickSSH",
                string.Format(
                    GetTranslation("plugin_quickssh_import_success"),
                    result.ImportedCount,
                    result.SkippedCount));
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
                Score = ScoreSubMenuManagement
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


        // ── actions (production CRUD and confirmed remote execution) ─────────────

        private List<Result> HandleActions(Query query, string rest)
        {
            var parts = rest.Split(new[] { ' ' }, 2);
            var subCmd = parts[0].ToLowerInvariant();
            var subRest = parts.Length > 1 ? parts[1].Trim() : "";

            switch (subCmd)
            {
                case ActionsSubRun:    return HandleActionsRun(query, subRest);
                case ActionsSubUse:    return HandleActionsUse(query, subRest);
                case ActionsSubAdd:    return HandleActionsAdd(query, subRest);
                case ActionsSubManage: return HandleActionsManage(query);
                case ActionsSubRemove: return HandleActionsRemove(query, subRest);
                case ActionsSubRename: return HandleActionsRename(query, subRest);
                default:
                    if (!string.IsNullOrEmpty(subCmd) &&
                        ActionsSubCommands.Any(s => s.StartsWith(subCmd)))
                    {
                        return new List<Result>(AutoCompleter.GetSuggestions(
                            query.ActionKeyword, "actions " + rest,
                            _profileManager?.UserData, AppIconPath,
                            _pluginContext?.API));
                    }
                    return HandleActionsList(query, rest);
            }
        }

        private List<Result> HandleActionsList(Query query, string search)
        {
            var results = new List<Result>
            {
                MakeBackNavResult(query, query.ActionKeyword + " ", query.ActionKeyword)
            };
            var actions = _profileManager.UserData.CommandProfiles;

            if (actions.Count == 0)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_actions_empty_title"),
                    SubTitle = GetTranslation("plugin_quickssh_actions_empty_subtitle"),
                    IcoPath = AppIconPath,
                    Score = ScoreActionsSavedItem
                });
            }
            else
            {
                int itemScore = ScoreActionsSavedItem;
                foreach (var entry in actions)
                {
                    var display = entry.Value?.ToDisplayString() ?? "";
                    if (!string.IsNullOrEmpty(search) &&
                        !SearchMatcher.ContainsIgnoreAccents(entry.Key, search) &&
                        !SearchMatcher.ContainsIgnoreAccents(display, search))
                        continue;

                    var autoText = query.ActionKeyword + " actions use " + entry.Key + " ";
                    results.Add(new Result
                    {
                        Title = entry.Key,
                        SubTitle = display,
                        IcoPath = AppIconGreenPath,
                        AutoCompleteText = autoText,
                        Score = itemScore--,
                        Action = _ =>
                        {
                            _pluginContext?.API?.ChangeQuery(autoText, true);
                            return false;
                        }
                    });
                }
            }

            if (string.IsNullOrEmpty(search))
            {
                var manageText = query.ActionKeyword + " actions manage ";
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandactions_manage"),
                    SubTitle = GetTranslation("plugin_quickssh_subtitle_commandactions_manage"),
                    IcoPath = AppIconPath,
                    AutoCompleteText = manageText,
                    Score = ScoreActionsActionManage,
                    Action = _ =>
                    {
                        _pluginContext?.API?.ChangeQuery(manageText, true);
                        return false;
                    }
                });
            }

            return results;
        }

        private List<Result> HandleActionsManage(Query query)
        {
            var results = new List<Result>
            {
                MakeBackNavResult(query, query.ActionKeyword + " actions ", query.ActionKeyword + " actions")
            };

            var actionRows = new[]
            {
                ("add",    GetTranslation("plugin_quickssh_title_commandactions_add"),    GetTranslation("plugin_quickssh_subtitle_commandactions_add"),    AppIconGreenPath,  ScoreActionsManageAdd),
                ("rename", GetTranslation("plugin_quickssh_title_commandactions_rename"), GetTranslation("plugin_quickssh_subtitle_commandactions_rename"), AppIconOrangePath, ScoreActionsManageRename),
                ("remove", GetTranslation("plugin_quickssh_title_commandactions_remove"), GetTranslation("plugin_quickssh_subtitle_commandactions_remove"), AppIconRedPath,    ScoreActionsManageRemove),
            };

            foreach (var (name, title, subtitle, icon, score) in actionRows)
            {
                var autoText = query.ActionKeyword + " actions " + name + " ";
                results.Add(new Result
                {
                    Title = title,
                    SubTitle = subtitle,
                    IcoPath = icon,
                    AutoCompleteText = autoText,
                    Score = score,
                    Action = _ =>
                    {
                        _pluginContext?.API?.ChangeQuery(autoText, true);
                        return false;
                    }
                });
            }

            return results;
        }

        private List<Result> HandleActionsUse(Query query, string rest)
        {
            var actions = _profileManager.UserData.CommandProfiles;
            var profiles = _profileManager.UserData.Profiles;
            var parts = rest.Split(new[] { ' ' }, 2);
            var requestedActionName = parts[0].Trim();
            var requestedProfileName = parts.Length > 1 ? parts[1].Trim() : "";
            var actionName = CommandInputGuard.FindExistingName(actions, requestedActionName);
            var results = new List<Result>
            {
                MakeBackNavResult(query, query.ActionKeyword + " actions ", query.ActionKeyword + " actions")
            };

            if (actionName == null)
            {
                results.Add(new Result
                {
                    Title = requestedActionName,
                    SubTitle = GetTranslation("plugin_quickssh_actions_notfound"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            var selectedAction = actions[actionName];

            if (profiles.Count == 0)
            {
                var addProfileText = query.ActionKeyword + " profiles add ";
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_actions_add_profile"),
                    SubTitle = GetTranslation("plugin_quickssh_actions_no_profiles"),
                    IcoPath = AppIconGreenPath,
                    AutoCompleteText = addProfileText,
                    Action = _ =>
                    {
                        _pluginContext?.API?.ChangeQuery(addProfileText, true);
                        return false;
                    }
                });
                return results;
            }

            var profileName = CommandInputGuard.FindExistingName(profiles, requestedProfileName);
            if (profileName == null)
            {
                foreach (var entry in profiles)
                {
                    if (string.Equals(entry.Value?.Type, "scp", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(requestedProfileName) &&
                        !SearchMatcher.ContainsIgnoreAccents(entry.Key, requestedProfileName) &&
                        !SearchMatcher.ContainsIgnoreAccents(entry.Value?.ToDisplayString() ?? "", requestedProfileName))
                        continue;

                    var autoText = query.ActionKeyword + " actions use " + actionName + " " + entry.Key;
                    results.Add(new Result
                    {
                        Title = entry.Key,
                        SubTitle = BuildProfileListSubtitle(entry.Value),
                        IcoPath = AppIconGreenPath,
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

            var selectedProfile = profiles[profileName];
            if (string.Equals(selectedProfile?.Type, "scp", StringComparison.OrdinalIgnoreCase))
            {
                results.Clear();
                results.Add(MakeBackNavResult(
                    query,
                    query.ActionKeyword + " actions use " + actionName + " ",
                    "actions profile selection"));
                results.Add(new Result
                {
                    Title = profileName,
                    SubTitle = GetTranslation("plugin_quickssh_actions_profile_notfound"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            return BuildActionConfirmationResults(
                query,
                query.ActionKeyword + " actions use " + actionName + " ",
                "actions profile selection",
                profileName,
                selectedProfile,
                actionName,
                selectedAction);
        }

        private List<Result> BuildActionConfirmationResults(
            Query query,
            string backQuery,
            string backTarget,
            string profileName,
            SshProfile selectedProfile,
            string actionName,
            CommandProfile selectedAction)
        {
            var results = new List<Result>
            {
                MakeBackNavResult(query, backQuery, backTarget)
            };

            if (!ActionCommandBuilder.TryBuild(selectedProfile, selectedAction, out var command) ||
                !ActionCommandBuilder.TryBuildDisplay(selectedProfile, selectedAction, out var displayCommand))
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_actions_cannot_run"),
                    SubTitle = GetTranslation("plugin_quickssh_actions_unsupported"),
                    IcoPath = AppIconRedPath,
                    Score = ScoreActionsConfirmRun
                });
                return results;
            }

            results.Add(new Result
            {
                Title = string.Format(
                    GetTranslation("plugin_quickssh_actions_execute_named_title"), actionName),
                SubTitle = string.Format(
                    GetTranslation("plugin_quickssh_actions_execute_summary"),
                    profileName,
                    selectedAction?.ToDisplayString() ?? ""),
                IcoPath = AppIconGreenPath,
                Score = ScoreActionsConfirmRun,
                Action = _ =>
                {
                    RunCommand(command);
                    return true;
                }
            });

            results.Add(new Result
            {
                Title = GetTranslation("plugin_quickssh_actions_copy_command_title"),
                SubTitle = displayCommand,
                IcoPath = AppIconPath,
                Score = ScoreActionsConfirmCommand,
                Action = _ =>
                {
                    _pluginContext?.API?.CopyToClipboard(displayCommand, false, false);
                    _pluginContext?.API?.ShowMsg(
                        "QuickSSH",
                        GetTranslation("plugin_quickssh_copy_command_success"));
                    return false;
                }
            });

            return results;
        }

        private List<Result> HandleActionsAdd(Query query, string rest)
        {
            var results = new List<Result>
            {
                MakeBackNavResult(query, query.ActionKeyword + " actions manage ", query.ActionKeyword + " actions manage")
            };

            var actions = _profileManager.UserData.CommandProfiles;
            rest = CommandInputGuard.NormalizeNestedCommandInput(
                rest, query.ActionKeyword, "actions add");
            if (string.IsNullOrWhiteSpace(rest))
            {
                var exampleName = ProfileWizard.BuildAvailableName("check", actions.Keys);
                var exampleText = query.ActionKeyword + " actions add " + exampleName;
                results.Add(MakeWizardExampleResultFromKeys(
                    "plugin_quickssh_wizard_actions_add_name_title",
                    "plugin_quickssh_wizard_actions_add_name_subtitle",
                    exampleText,
                    exampleName));
                return results;
            }

            var parts = rest.Split(new[] { ' ' }, 2);
            var name = parts[0].Trim();
            var command = parts.Length > 1 ? parts[1].Trim() : "";

            if (!CommandInputGuard.IsValidSavedName(name))
            {
                results.Add(new Result
                {
                    Title = name,
                    SubTitle = GetTranslation("plugin_quickssh_name_invalid"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (CommandInputGuard.IsReservedSavedName(name))
            {
                results.Add(new Result
                {
                    Title = name,
                    SubTitle = GetTranslation("plugin_quickssh_name_reserved"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (CommandInputGuard.FindExistingName(actions, name) != null)
            {
                results.Add(new Result
                {
                    Title = name,
                    SubTitle = GetTranslation("plugin_quickssh_name_exists"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                var exampleText = query.ActionKeyword + " actions add " + name + " hostname";
                results.Add(MakeWizardExampleResultFromKeys(
                    "plugin_quickssh_wizard_actions_add_command_title",
                    "plugin_quickssh_wizard_actions_add_command_subtitle",
                    exampleText));
                return results;
            }

            if (!CommandProfile.IsSafeToStore(command))
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandactions_add"),
                    SubTitle = GetTranslation("plugin_quickssh_actions_rejected"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            results.Add(new Result
            {
                Title = string.Format(
                    GetTranslation("plugin_quickssh_actions_save_title"), name),
                SubTitle = command,
                IcoPath = AppIconGreenPath,
                Action = _ =>
                {
                    actions[name] = new CommandProfile
                    {
                        Command = command
                    };
                    _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " actions ", true);
                    return false;
                }
            });

            return results;
        }

        private List<Result> HandleActionsRemove(Query query, string search)
        {
            var actions = _profileManager.UserData.CommandProfiles;
            var exactName = CommandInputGuard.FindExistingName(actions, search);
            var results = new List<Result>
            {
                exactName == null
                    ? MakeBackNavResult(
                        query,
                        query.ActionKeyword + " actions manage ",
                        query.ActionKeyword + " actions manage")
                    : MakeBackNavResult(
                        query,
                        query.ActionKeyword + " actions remove ",
                        "actions remove selection")
            };

            if (actions.Count == 0)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_actions_empty_title"),
                    SubTitle = GetTranslation("plugin_quickssh_actions_empty_subtitle"),
                    IcoPath = AppIconPath
                });
                return results;
            }

            if (exactName != null)
            {
                var action = actions[exactName];
                results.Add(new Result
                {
                    Title = string.Format(
                        GetTranslation("plugin_quickssh_actions_remove_confirm"), exactName),
                    SubTitle = action?.ToDisplayString() ?? "",
                    IcoPath = AppIconRedPath,
                    Score = ScoreActionsConfirmRun,
                    Action = _ =>
                    {
                        actions.Remove(exactName);
                        _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " actions ", true);
                        return false;
                    }
                });
                return results;
            }

            foreach (var entry in actions)
            {
                if (!string.IsNullOrEmpty(search) && !SearchMatcher.ContainsIgnoreAccents(entry.Key, search))
                    continue;

                var autoText = query.ActionKeyword + " actions remove " + entry.Key;
                results.Add(new Result
                {
                    Title = entry.Key,
                    SubTitle = entry.Value?.ToDisplayString() ?? "",
                    IcoPath = AppIconRedPath,
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

        private List<Result> HandleActionsRename(Query query, string rest)
        {
            var results = new List<Result>();
            var actions = _profileManager.UserData.CommandProfiles;

            rest = CommandInputGuard.NormalizeNestedCommandInput(
                rest, query.ActionKeyword, "actions rename");
            var parts = rest.Split(new[] { ' ' }, 2);
            var requestedOldName = parts[0].Trim();
            var newName = parts.Length > 1 ? parts[1].Trim() : "";

            results.Add(MakeBackNavResult(query, query.ActionKeyword + " actions manage ", query.ActionKeyword + " actions manage"));

            if (actions.Count == 0)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_actions_empty_title"),
                    SubTitle = GetTranslation("plugin_quickssh_actions_empty_subtitle"),
                    IcoPath = AppIconPath
                });
                return results;
            }

            if (string.IsNullOrEmpty(requestedOldName))
            {
                foreach (var entry in actions)
                {
                    var autoText = ProfileWizard.BuildPrefilledRenameQuery(
                        query.ActionKeyword, "actions rename", entry.Key);
                    results.Add(new Result
                    {
                        Title = entry.Key,
                        SubTitle = entry.Value?.ToDisplayString() ?? "",
                        IcoPath = AppIconOrangePath,
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

            var oldName = CommandInputGuard.FindExistingName(actions, requestedOldName);
            if (oldName == null)
            {
                results.Add(new Result
                {
                    Title = requestedOldName,
                    SubTitle = GetTranslation("plugin_quickssh_actions_notfound"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                var suggestedName = ProfileWizard.BuildSuggestedName(oldName, actions.Keys);
                var exampleText = ProfileWizard.BuildRenameQuery(
                    query.ActionKeyword, "actions rename", oldName, suggestedName);
                results.Add(MakeWizardExampleResult(
                    string.Format(GetTranslation("plugin_quickssh_wizard_actions_rename_title"), suggestedName),
                    string.Format(GetTranslation("plugin_quickssh_wizard_actions_rename_subtitle"), oldName),
                    exampleText));
                return results;
            }

            if (!CommandInputGuard.IsValidSavedName(newName))
            {
                results.Add(new Result
                {
                    Title = newName,
                    SubTitle = GetTranslation("plugin_quickssh_name_invalid"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (CommandInputGuard.IsReservedSavedName(newName))
            {
                results.Add(new Result
                {
                    Title = newName,
                    SubTitle = GetTranslation("plugin_quickssh_name_reserved"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (string.Equals(oldName, newName, StringComparison.Ordinal))
            {
                var suggestedName = ProfileWizard.BuildSuggestedName(oldName, actions.Keys);
                var exampleText = ProfileWizard.BuildRenameQuery(
                    query.ActionKeyword, "actions rename", oldName, suggestedName);
                results.Add(MakeWizardExampleResult(
                    string.Format(GetTranslation("plugin_quickssh_wizard_actions_rename_title"), suggestedName),
                    string.Format(GetTranslation("plugin_quickssh_wizard_actions_rename_prefilled_subtitle"), oldName),
                    exampleText));
                return results;
            }

            var conflictingName = CommandInputGuard.FindExistingName(actions, newName);
            if (conflictingName != null &&
                !string.Equals(conflictingName, oldName, StringComparison.Ordinal))
            {
                results.Add(new Result
                {
                    Title = newName,
                    SubTitle = GetTranslation("plugin_quickssh_name_exists"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            results.Add(new Result
            {
                Title = string.Format(
                    GetTranslation("plugin_quickssh_actions_rename_confirm_title"),
                    oldName, newName),
                SubTitle = GetTranslation("plugin_quickssh_actions_rename_confirm_subtitle"),
                IcoPath = AppIconOrangePath,
                Action = _ =>
                {
                    var captured = actions[oldName];
                    actions.SetCallback(null);
                    try
                    {
                        actions.Remove(oldName);
                        actions[newName] = captured;
                    }
                    finally
                    {
                        actions.SetCallback(_profileManager.SaveConfiguration);
                    }
                    _profileManager.SaveConfiguration();
                    _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " actions ", true);
                    return false;
                }
            });

            return results;
        }

        private List<Result> HandleActionsRun(Query query, string rest)
        {
            var profiles = _profileManager.UserData.Profiles;
            var actions = _profileManager.UserData.CommandProfiles;
            var parts = rest.Split(new[] { ' ' }, 2);
            var requestedProfileName = parts[0].Trim();
            var requestedActionName = parts.Length > 1 ? parts[1].Trim() : "";
            var profileName = profiles.Count == 0
                ? null
                : CommandInputGuard.FindExistingName(profiles, requestedProfileName);
            var actionName = profileName == null || actions.Count == 0
                ? null
                : CommandInputGuard.FindExistingName(actions, requestedActionName);

            if (actions.Count == 0)
            {
                var results = new List<Result>
                {
                    MakeBackNavResult(query, query.ActionKeyword + " actions ", query.ActionKeyword + " actions")
                };
                var addText = query.ActionKeyword + " actions add ";
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_actions_add_first"),
                    SubTitle = GetTranslation("plugin_quickssh_actions_empty_subtitle"),
                    IcoPath = AppIconGreenPath,
                    AutoCompleteText = addText,
                    Action = _ =>
                    {
                        _pluginContext?.API?.ChangeQuery(addText, true);
                        return false;
                    }
                });
                return results;
            }

            if (profiles.Count == 0)
            {
                var results = new List<Result>
                {
                    MakeBackNavResult(query, query.ActionKeyword + " actions ", query.ActionKeyword + " actions")
                };
                var addProfileText = query.ActionKeyword + " profiles add ";
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_actions_add_profile"),
                    SubTitle = GetTranslation("plugin_quickssh_actions_no_profiles"),
                    IcoPath = AppIconGreenPath,
                    AutoCompleteText = addProfileText,
                    Action = _ =>
                    {
                        _pluginContext?.API?.ChangeQuery(addProfileText, true);
                        return false;
                    }
                });
                return results;
            }

            if (profileName == null)
            {
                var results = new List<Result>
                {
                    MakeBackNavResult(query, query.ActionKeyword + " actions ", query.ActionKeyword + " actions")
                };
                foreach (var entry in profiles)
                {
                    if (string.Equals(entry.Value?.Type, "scp", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(requestedProfileName) &&
                        !SearchMatcher.ContainsIgnoreAccents(entry.Key, requestedProfileName) &&
                        !SearchMatcher.ContainsIgnoreAccents(entry.Value?.ToDisplayString() ?? "", requestedProfileName))
                        continue;

                    var autoText = query.ActionKeyword + " actions run " + entry.Key + " ";
                    results.Add(new Result
                    {
                        Title = entry.Key,
                        SubTitle = BuildProfileListSubtitle(entry.Value),
                        IcoPath = AppIconGreenPath,
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

            var selectedProfile = profiles[profileName];
            if (string.Equals(selectedProfile?.Type, "scp", StringComparison.OrdinalIgnoreCase))
            {
                return new List<Result>
                {
                    MakeBackNavResult(
                        query,
                        query.ActionKeyword + " actions run ",
                        "actions profile selection"),
                    new Result
                    {
                        Title = profileName,
                        SubTitle = GetTranslation("plugin_quickssh_actions_profile_notfound"),
                        IcoPath = AppIconRedPath
                    }
                };
            }

            if (actionName == null)
            {
                var results = new List<Result>
                {
                    MakeBackNavResult(
                        query,
                        query.ActionKeyword + " actions run ",
                        "actions profile selection")
                };
                foreach (var entry in actions)
                {
                    if (!string.IsNullOrEmpty(requestedActionName) &&
                        !SearchMatcher.ContainsIgnoreAccents(entry.Key, requestedActionName) &&
                        !SearchMatcher.ContainsIgnoreAccents(entry.Value?.ToDisplayString() ?? "", requestedActionName))
                        continue;

                    var autoText = query.ActionKeyword + " actions run " + profileName + " " + entry.Key;
                    results.Add(new Result
                    {
                        Title = entry.Key,
                        SubTitle = entry.Value?.ToDisplayString() ?? "",
                        IcoPath = AppIconGreenPath,
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

            var selectedAction = actions[actionName];
            return BuildActionConfirmationResults(
                query,
                query.ActionKeyword + " actions run " + profileName + " ",
                "actions action selection",
                profileName,
                selectedProfile,
                actionName,
                selectedAction);
        }


        private List<Result> HandleTools(Query query, string search)
        {
            var results = new List<Result>
            {
                MakeBackNavResult(query, query.ActionKeyword + " ", query.ActionKeyword)
            };

            var toolItems = new[]
            {
                (CommandKeys, GetTranslation("plugin_quickssh_title_commandkeys"), GetTranslation("plugin_quickssh_subtitle_tools_keys"), ScoreToolsKeys),
                (CommandCustomShell, GetTranslation("plugin_quickssh_title_commandshell"), GetTranslation("plugin_quickssh_subtitle_tools_shell"), ScoreToolsShell),
                (CommandConfig, GetTranslation("plugin_quickssh_title_commandconfig"), GetTranslation("plugin_quickssh_subtitle_tools_config"), ScoreToolsConfig),
            };

            foreach (var (command, title, subtitle, score) in toolItems)
            {
                if (!string.IsNullOrEmpty(search) &&
                    !command.StartsWith(search, StringComparison.OrdinalIgnoreCase) &&
                    !title.StartsWith(search, StringComparison.CurrentCultureIgnoreCase))
                    continue;

                var target = query.ActionKeyword + " " + command + " ";
                results.Add(new Result
                {
                    Title = title,
                    SubTitle = subtitle,
                    IcoPath = AppIconPath,
                    Score = score,
                    AutoCompleteText = target,
                    Action = _ =>
                    {
                        _pluginContext?.API?.ChangeQuery(target, true);
                        return false;
                    }
                });
            }

            return results;
        }


        private List<Result> HandleShell(Query query, string rest)
        {
            var parts = rest.Split(new[] { ' ' }, 2);
            var subCmd = parts[0].ToLowerInvariant();
            var subRest = parts.Length > 1 ? parts[1].Trim() : "";

            switch (subCmd)
            {
                case "add":
                {
                    var results = new List<Result>
                    {
                        MakeBackNavResult(
                            query,
                            query.ActionKeyword + " shell manage ",
                            query.ActionKeyword + " shell manage")
                    };

                    subRest = CommandInputGuard.NormalizeNestedCommandInput(
                        subRest, query.ActionKeyword, "shell add");
                    if (string.IsNullOrWhiteSpace(subRest))
                    {
                        var exampleName = ProfileWizard.BuildAvailableName(
                            "PowerShell", _profileManager.UserData.CustomShell.Keys);
                        var exampleText = query.ActionKeyword + " shell add " + exampleName;
                        results.Add(MakeWizardExampleResultFromKeys(
                            "plugin_quickssh_wizard_shell_add_name_title",
                            "plugin_quickssh_wizard_shell_add_name_subtitle",
                            exampleText,
                            exampleName));
                        return results;
                    }

                    var (name, value) = ParseShellAddArgs(subRest);
                    name = name.Trim();

                    if (!CommandInputGuard.IsValidSavedName(name))
                    {
                        results.Add(new Result
                        {
                            Title = name,
                            SubTitle = GetTranslation("plugin_quickssh_name_invalid"),
                            IcoPath = AppIconRedPath
                        });
                        return results;
                    }

                    if (CommandInputGuard.IsReservedSavedName(name))
                    {
                        results.Add(new Result
                        {
                            Title = name,
                            SubTitle = GetTranslation("plugin_quickssh_name_reserved"),
                            IcoPath = AppIconRedPath
                        });
                        return results;
                    }

                    if (_profileManager.UserData.CustomShell.Keys.Any(shell =>
                        string.Equals(shell, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        results.Add(new Result
                        {
                            Title = name,
                            SubTitle = GetTranslation("plugin_quickssh_name_exists"),
                            IcoPath = AppIconRedPath
                        });
                        return results;
                    }

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        var exampleText = query.ActionKeyword + " shell add " + name + " pwsh.exe -NoLogo";
                        results.Add(MakeWizardExampleResultFromKeys(
                            "plugin_quickssh_wizard_shell_add_command_title",
                            "plugin_quickssh_wizard_shell_add_command_subtitle",
                            exampleText));
                        results.Add(new Result
                        {
                            Title = string.Format(
                                GetTranslation("plugin_quickssh_shell_save_title"), name),
                            SubTitle = string.Format(
                                GetTranslation("plugin_quickssh_wizard_shell_use_name_subtitle"), name),
                            IcoPath = AppIconGreenPath,
                            Action = _ =>
                            {
                                _profileManager.UserData.CustomShell.SetCallback(null);
                                _profileManager.UserData.CustomShell[name] = "";
                                if (_profileManager.UserData.CustomShell.Count == 1)
                                    _profileManager.UserData.SelectedCustomShell = name;
                                _profileManager.UserData.CustomShell.SetCallback(_profileManager.SaveConfiguration);
                                _profileManager.SaveConfiguration();
                                _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " shell ", true);
                                return false;
                            }
                        });
                        return results;
                    }

                    results.Add(new Result
                    {
                        Title = string.Format(
                            GetTranslation("plugin_quickssh_shell_save_title"), name),
                        SubTitle = value,
                        IcoPath = AppIconGreenPath,
                        Action = _ =>
                        {
                            _profileManager.UserData.CustomShell.SetCallback(null);
                            _profileManager.UserData.CustomShell[name] = value;
                            if (_profileManager.UserData.CustomShell.Count == 1)
                                _profileManager.UserData.SelectedCustomShell = name;
                            _profileManager.UserData.CustomShell.SetCallback(_profileManager.SaveConfiguration);
                            _profileManager.SaveConfiguration();
                            _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " shell ", true);
                            return false;
                        }
                    });
                    return results;
                }

                case "remove":
                {
                    var results = new List<Result>
                    {
                        MakeBackNavResult(
                            query,
                            query.ActionKeyword + " shell manage ",
                            query.ActionKeyword + " shell manage")
                    };
                    var shells = _profileManager.UserData.CustomShell;
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
                                    _profileManager.UserData.CustomShell.SetCallback(null);
                                    _profileManager.UserData.CustomShell.Remove(shell.Key);
                                    if (_profileManager.UserData.SelectedCustomShell == shell.Key)
                                    {
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
                    return results;
                }

                case "manage":
                    return HandleShellManage(query);

                default:
                    if (!string.IsNullOrEmpty(subCmd) &&
                        ShellSubCommands.Any(s => s.StartsWith(subCmd)))
                    {
                        return new List<Result>(AutoCompleter.GetSuggestions(
                            query.ActionKeyword, "shell " + rest,
                            _profileManager?.UserData, AppIconPath,
                            _pluginContext?.API));
                    }

                    var defaultResults = new List<Result>
                    {
                        MakeBackNavResult(query, query.ActionKeyword + " ", query.ActionKeyword)
                    };
                    var allShells = _profileManager.UserData.CustomShell;
                    var selected = _profileManager.UserData.SelectedCustomShell;

                    if (allShells.Count > 0)
                    {
                        if (!string.IsNullOrEmpty(selected) && allShells.ContainsKey(selected))
                        {
                            var shellVal = allShells[selected];
                            defaultResults.Add(new Result
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

                        int otherShellScore = ScoreShellOtherStart;
                        foreach (var shell in allShells)
                        {
                            if (shell.Key == selected)
                                continue;
                            defaultResults.Add(new Result
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
                    }

                    var manageText = query.ActionKeyword + " shell manage ";
                    defaultResults.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_title_commandshell_manage"),
                        SubTitle = GetTranslation("plugin_quickssh_subtitle_commandshell_help"),
                        IcoPath = AppIconPath,
                        AutoCompleteText = manageText,
                        Score = ScoreShellActionManage,
                        Action = _ =>
                        {
                            _pluginContext?.API?.ChangeQuery(manageText, true);
                            return false;
                        }
                    });
                    return defaultResults;
            }
        }

        private List<Result> HandleShellManage(Query query)
        {
            var results = new List<Result>
            {
                MakeBackNavResult(query, query.ActionKeyword + " shell ", query.ActionKeyword + " shell")
            };

            var shellActions = new[]
            {
                ("add",    GetTranslation("plugin_quickssh_title_commandshell_add"),    GetTranslation("plugin_quickssh_subtitle_commandshell_add_usage"), AppIconGreenPath, ScoreShellManageAdd),
                ("remove", GetTranslation("plugin_quickssh_title_commandshell_remove"), GetTranslation("plugin_quickssh_subtitle_commandshell_remove"),    AppIconRedPath,   ScoreShellManageRemove),
            };

            foreach (var (name, title, subtitle, icon, score) in shellActions)
            {
                var autoText = query.ActionKeyword + " shell " + name + " ";
                results.Add(new Result
                {
                    Title = title,
                    SubTitle = subtitle,
                    IcoPath = icon,
                    AutoCompleteText = autoText,
                    Score = score,
                    Action = _ =>
                    {
                        _pluginContext?.API?.ChangeQuery(autoText, true);
                        return false;
                    }
                });
            }

            return results;
        }

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
                case KeysSubManage:   return HandleKeysManage(query);
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
            var results = new List<Result>
            {
                MakeBackNavResult(query, query.ActionKeyword + " ", query.ActionKeyword)
            };
            var keys = _profileManager.UserData.SshKeys;

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
                    bool savedPathIsPublic = !string.IsNullOrEmpty(keyEntry?.Path) &&
                        keyEntry.Path.EndsWith(".pub", StringComparison.OrdinalIgnoreCase);
                    var keyTypeLabel = savedPathIsPublic
                        ? GetTranslation("plugin_quickssh_keys_public_path_label")
                        : GetTranslation("plugin_quickssh_keys_private_path_label");
                    var subtitle = keyTypeLabel + " " + displayPath +
                        (fileExists ? "" : " " + GetTranslation("plugin_quickssh_keys_file_missing"));
                    var installText = query.ActionKeyword + " keys install " + alias + " ";

                    results.Add(new Result
                    {
                        Title = alias,
                        SubTitle = subtitle,
                        IcoPath = fileExists ? AppIconGreenPath : AppIconRedPath,
                        AutoCompleteText = installText,
                        Score = keyScore--,
                        Action = _ =>
                        {
                            _pluginContext?.API?.ChangeQuery(installText, true);
                            return false;
                        }
                    });
                }
            }

            if (string.IsNullOrEmpty(search))
            {
                var installText = query.ActionKeyword + " keys install ";
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_install"),
                    SubTitle = GetTranslation("plugin_quickssh_subtitle_commandkeys_install"),
                    IcoPath = AppIconGreenPath,
                    AutoCompleteText = installText,
                    Score = ScoreKeysActionInstall,
                    Action = _ =>
                    {
                        _pluginContext?.API?.ChangeQuery(installText, true);
                        return false;
                    }
                });

                var manageText = query.ActionKeyword + " keys manage ";
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_manage"),
                    SubTitle = GetTranslation("plugin_quickssh_subtitle_commandkeys_manage"),
                    IcoPath = AppIconPath,
                    AutoCompleteText = manageText,
                    Score = ScoreKeysActionManage,
                    Action = _ =>
                    {
                        _pluginContext?.API?.ChangeQuery(manageText, true);
                        return false;
                    }
                });
            }

            return results;
        }

        private List<Result> HandleKeysManage(Query query)
        {
            var results = new List<Result>
            {
                MakeBackNavResult(query, query.ActionKeyword + " keys ", query.ActionKeyword + " keys")
            };

            var keyActions = new[]
            {
                ("add",       GetTranslation("plugin_quickssh_title_commandkeys_add"),      GetTranslation("plugin_quickssh_subtitle_commandkeys_add"),      AppIconGreenPath,  ScoreKeysManageAdd),
                ("generate",  GetTranslation("plugin_quickssh_title_commandkeys_generate"), GetTranslation("plugin_quickssh_subtitle_commandkeys_generate"), AppIconGreenPath,  ScoreKeysManageGenerate),
                ("scan",      GetTranslation("plugin_quickssh_title_commandkeys_scan"),     GetTranslation("plugin_quickssh_subtitle_commandkeys_scan"),     AppIconGreenPath,  ScoreKeysManageScan),
                ("rename",    GetTranslation("plugin_quickssh_title_commandkeys_rename"),   GetTranslation("plugin_quickssh_subtitle_commandkeys_rename"),   AppIconOrangePath, ScoreKeysManageRename),
                ("copy-path", GetTranslation("plugin_quickssh_title_commandkeys_copypath"), GetTranslation("plugin_quickssh_subtitle_commandkeys_copypath"), AppIconPath,       ScoreKeysManageCopyPath),
                ("copy-pub",  GetTranslation("plugin_quickssh_title_commandkeys_copypub"),  GetTranslation("plugin_quickssh_subtitle_commandkeys_copypub"),  AppIconPath,       ScoreKeysManageCopyPub),
                ("remove",    GetTranslation("plugin_quickssh_title_commandkeys_remove"),   GetTranslation("plugin_quickssh_subtitle_commandkeys_remove"),   AppIconRedPath,    ScoreKeysManageRemove),
            };

            foreach (var (scName, scTitle, scSubTitle, iconPath, scScore) in keyActions)
            {
                var autoText = query.ActionKeyword + " keys " + scName + " ";
                results.Add(new Result
                {
                    Title = scTitle,
                    SubTitle = scSubTitle,
                    IcoPath = iconPath,
                    AutoCompleteText = autoText,
                    Score = scScore,
                    Action = _ =>
                    {
                        _pluginContext?.API?.ChangeQuery(autoText, true);
                        return false;
                    }
                });
            }

            return results;
        }

        private List<Result> HandleKeysAdd(Query query, string rest)
        {
            var results = new List<Result>
            {
                MakeBackNavResult(query, query.ActionKeyword + " keys manage ", query.ActionKeyword + " keys manage")
            };
            var keys = _profileManager.UserData.SshKeys;

            rest = CommandInputGuard.NormalizeNestedCommandInput(
                rest, query.ActionKeyword, "keys add");
            if (string.IsNullOrWhiteSpace(rest))
            {
                var exampleName = ProfileWizard.BuildAvailableName("server-key", keys.Keys);
                var exampleText = query.ActionKeyword + " keys add " + exampleName;
                results.Add(MakeWizardExampleResultFromKeys(
                    "plugin_quickssh_wizard_keys_add_name_title",
                    "plugin_quickssh_wizard_keys_add_name_subtitle",
                    exampleText,
                    exampleName));
                return results;
            }

            var addParts = rest.Split(new[] { ' ' }, 2);
            var keyAlias = addParts[0].Trim();
            var keyPath = addParts.Length > 1 ? addParts[1].Trim() : "";

            if (!CommandInputGuard.IsValidSavedName(keyAlias))
            {
                results.Add(new Result
                {
                    Title = keyAlias,
                    SubTitle = GetTranslation("plugin_quickssh_name_invalid"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (CommandInputGuard.IsReservedSavedName(keyAlias))
            {
                results.Add(new Result
                {
                    Title = keyAlias,
                    SubTitle = GetTranslation("plugin_quickssh_name_reserved"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (keys.Keys.Any(key => string.Equals(
                key, keyAlias, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new Result
                {
                    Title = keyAlias,
                    SubTitle = GetTranslation("plugin_quickssh_name_exists"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (string.IsNullOrWhiteSpace(keyPath))
            {
                var exampleText = query.ActionKeyword + " keys add " + keyAlias + " ~/.ssh/private_key";
                results.Add(MakeWizardExampleResultFromKeys(
                    "plugin_quickssh_wizard_keys_add_path_title",
                    "plugin_quickssh_wizard_keys_add_path_subtitle",
                    exampleText));
                return results;
            }

            if (keyPath.Length >= 2 && keyPath.StartsWith("\"") && keyPath.EndsWith("\""))
                keyPath = keyPath.Substring(1, keyPath.Length - 2);

            var expandedPath = keyPath.Replace("~",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            if (!File.Exists(expandedPath))
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_keys_path_missing_title"),
                    SubTitle = expandedPath,
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            results.Add(new Result
            {
                Title = string.Format(
                    GetTranslation("plugin_quickssh_keys_save_title"), keyAlias),
                SubTitle = expandedPath,
                IcoPath = AppIconGreenPath,
                Action = _ =>
                {
                    keys[keyAlias] = new SshKeyEntry
                    {
                        Path = expandedPath
                    };
                    _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " keys ", true);
                    return false;
                }
            });

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
            var results = new List<Result>
            {
                MakeBackNavResult(query, query.ActionKeyword + " keys ", query.ActionKeyword + " keys")
            };
            var keys = _profileManager.UserData.SshKeys;
            var profiles = _profileManager.UserData.Profiles;

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
            var requestedDestination = installParts.Length > 1 ? installParts[1].Trim() : "";

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

            if (string.IsNullOrEmpty(requestedDestination))
            {
                // Step 2: Select a saved SSH profile when available; otherwise prompt for user@host.
                var hasUsableProfiles = false;
                foreach (var entry in profiles)
                {
                    if (!TryGetInstallDestinationFromProfile(entry.Value, out _, out _))
                        continue;

                    hasUsableProfiles = true;
                    var profileName = entry.Key;
                    var profile = entry.Value;
                    var autoText = query.ActionKeyword + " keys install " + installAlias + " " + profileName;
                    results.Add(new Result
                    {
                        Title = profileName,
                        SubTitle = profile?.ToDisplayString() ?? "",
                        IcoPath = AppIconGreenPath,
                        AutoCompleteText = autoText,
                        Action = _ =>
                        {
                            _pluginContext?.API?.ChangeQuery(autoText, true);
                            return false;
                        }
                    });
                }

                if (hasUsableProfiles)
                {

                    var manualText = query.ActionKeyword + " keys install " + installAlias + " ";
                    results.Add(new Result
                    {
                        Title = GetTranslation("plugin_quickssh_keys_install_manual_destination"),
                        SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_install_type_userhost"),
                            query.ActionKeyword, installAlias),
                        IcoPath = AppIconPath,
                        AutoCompleteText = manualText,
                        Action = _ =>
                        {
                            _pluginContext?.API?.ChangeQuery(manualText, true);
                            return false;
                        }
                    });
                    return results;
                }

                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_install"),
                    SubTitle = string.Format(GetTranslation("plugin_quickssh_keys_install_type_userhost"),
                        query.ActionKeyword, installAlias),
                    IcoPath = AppIconPath
                });
                return results;
            }

            // Step 3: Resolve a selected saved profile, or treat the input as manual user@host.
            SshProfile selectedInstallProfile = null;
            var selectedProfileName = CommandInputGuard.FindExistingName(profiles, requestedDestination);
            var userAtHost = requestedDestination;
            if (selectedProfileName != null)
            {
                selectedInstallProfile = profiles[selectedProfileName];
                if (!TryGetInstallDestinationFromProfile(selectedInstallProfile, out userAtHost, out _))
                {
                    results.Add(new Result
                    {
                        Title = selectedProfileName,
                        SubTitle = GetTranslation("plugin_quickssh_keys_install_profile_unsupported"),
                        IcoPath = AppIconRedPath
                    });
                    return results;
                }
            }

            // Validate destination and show action rows.
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
            var fullSshCmd = selectedInstallProfile == null
                ? RemoteKeyInstallBuilder.BuildFullSshCommand(userAtHost, bootstrap)
                : BuildProfileKeyInstallCommand(selectedInstallProfile, userAtHost, bootstrap);
            var runSshCmd = fullSshCmd + " || echo " + RemoteKeyInstallBuilder.FailureMessage;

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
                    _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " keys install " + installAlias + " " + requestedDestination, true);
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
                    _pluginContext?.API?.ChangeQuery(query.ActionKeyword + " keys install " + installAlias + " " + requestedDestination, true);
                    return false;
                }
            });

            return results;
        }

        private static bool TryGetInstallDestinationFromProfile(
            SshProfile profile,
            out string userAtHost,
            out string error)
        {
            userAtHost = "";
            error = "";

            if (profile == null ||
                string.Equals(profile.Type, "scp", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(profile.User) ||
                string.IsNullOrWhiteSpace(profile.HostName))
            {
                error = "unsupported";
                return false;
            }

            userAtHost = profile.User.Trim() + "@" + profile.HostName.Trim();
            if (!RemoteKeyInstallBuilder.IsValidUserAtHost(userAtHost))
            {
                error = "destination";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(profile.Port) && profile.Port.Trim() != "22")
            {
                if (!int.TryParse(profile.Port.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
                    port < 1 || port > 65535)
                {
                    error = "port";
                    return false;
                }
            }

            return true;
        }

        private static string BuildProfileKeyInstallCommand(
            SshProfile profile,
            string userAtHost,
            string bootstrapCommand)
        {
            var sb = new StringBuilder("ssh");

            if (!string.IsNullOrWhiteSpace(profile?.IdentityFile))
                sb.Append(" -i ").Append(SshCommandBuilder.QuoteArgument(profile.IdentityFile.Trim()));

            if (profile?.IdentitiesOnly == true)
                sb.Append(" -o IdentitiesOnly=yes");

            if (!string.IsNullOrWhiteSpace(profile?.Port) && profile.Port.Trim() != "22")
                sb.Append(" -p ").Append(profile.Port.Trim());

            sb.Append(" ").Append(userAtHost).Append(" \"").Append(bootstrapCommand).Append("\"");
            return sb.ToString();
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

            results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys manage ", query.ActionKeyword + " keys manage"));

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
                    IcoPath = GetSemanticIconPath("generate"),
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
                    IcoPath = GetSemanticIconPath("generate"),
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

            results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys manage ", query.ActionKeyword + " keys manage"));

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

            rest = CommandInputGuard.NormalizeNestedCommandInput(
                rest, query.ActionKeyword, "keys rename");
            var parts = rest.Split(new[] { ' ' }, 2);
            var requestedOldAlias = parts[0].Trim();
            var newAlias = parts.Length > 1 ? parts[1].Trim() : "";

            results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys manage ", query.ActionKeyword + " keys manage"));

            if (string.IsNullOrEmpty(requestedOldAlias))
            {
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
                    var autoText = ProfileWizard.BuildPrefilledRenameQuery(
                        query.ActionKeyword, "keys rename", alias);
                    results.Add(new Result
                    {
                        Title = alias,
                        SubTitle = entry.Value?.ToDisplayString() ?? "",
                        IcoPath = GetSemanticIconPath("rename"),
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

            var oldAlias = keys.Keys.FirstOrDefault(key => string.Equals(
                key, requestedOldAlias, StringComparison.OrdinalIgnoreCase));
            if (oldAlias == null)
            {
                results.Add(new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandkeys_rename") + ": " + requestedOldAlias,
                    SubTitle = GetTranslation("plugin_quickssh_keys_rename_notfound"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (string.IsNullOrWhiteSpace(newAlias))
            {
                var suggestedName = ProfileWizard.BuildSuggestedName(oldAlias, keys.Keys);
                var exampleText = ProfileWizard.BuildRenameQuery(
                    query.ActionKeyword, "keys rename", oldAlias, suggestedName);
                results.Add(MakeWizardExampleResult(
                    string.Format(GetTranslation("plugin_quickssh_wizard_keys_rename_title"), suggestedName),
                    string.Format(GetTranslation("plugin_quickssh_wizard_keys_rename_subtitle"), oldAlias),
                    exampleText));
                return results;
            }

            if (!CommandInputGuard.IsValidSavedName(newAlias))
            {
                results.Add(new Result
                {
                    Title = newAlias,
                    SubTitle = GetTranslation("plugin_quickssh_name_invalid"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (CommandInputGuard.IsReservedSavedName(newAlias))
            {
                results.Add(new Result
                {
                    Title = newAlias,
                    SubTitle = GetTranslation("plugin_quickssh_name_reserved"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            if (string.Equals(oldAlias, newAlias, StringComparison.Ordinal))
            {
                var suggestedName = ProfileWizard.BuildSuggestedName(oldAlias, keys.Keys);
                var exampleText = ProfileWizard.BuildRenameQuery(
                    query.ActionKeyword, "keys rename", oldAlias, suggestedName);
                results.Add(MakeWizardExampleResult(
                    string.Format(GetTranslation("plugin_quickssh_wizard_keys_rename_title"), suggestedName),
                    string.Format(GetTranslation("plugin_quickssh_wizard_keys_rename_prefilled_subtitle"), oldAlias),
                    exampleText));
                return results;
            }

            var conflictingAlias = keys.Keys.FirstOrDefault(key => string.Equals(
                key, newAlias, StringComparison.OrdinalIgnoreCase));
            if (conflictingAlias != null &&
                !string.Equals(conflictingAlias, oldAlias, StringComparison.Ordinal))
            {
                results.Add(new Result
                {
                    Title = newAlias,
                    SubTitle = GetTranslation("plugin_quickssh_keys_rename_duplicate"),
                    IcoPath = AppIconRedPath
                });
                return results;
            }

            results.Add(new Result
            {
                Title = string.Format(
                    GetTranslation("plugin_quickssh_keys_rename_confirm_title"),
                    oldAlias, newAlias),
                SubTitle = GetTranslation("plugin_quickssh_keys_rename_confirm_subtitle"),
                IcoPath = GetSemanticIconPath("rename"),
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

            return results;
        }

        // ── keys copy-path ────────────────────────────────────────────────────────

        private List<Result> HandleKeysCopyPath(Query query, string search)
        {
            var results = new List<Result>();
            var keys = _profileManager.UserData.SshKeys;

            results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys manage ", query.ActionKeyword + " keys manage"));

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
                    IcoPath = GetSemanticIconPath("copy"),
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

            results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys manage ", query.ActionKeyword + " keys manage"));

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
                        IcoPath = GetSemanticIconPath("copy"),
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

            results.Add(MakeBackNavResult(query, query.ActionKeyword + " keys manage ", query.ActionKeyword + " keys manage"));

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
            var results = new List<Result>
            {
                MakeBackNavResult(query, query.ActionKeyword + " ", query.ActionKeyword)
            };

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
                MakeBackNavResult(query, query.ActionKeyword + " ", query.ActionKeyword),
                new Result
                {
                    Title = GetTranslation("plugin_quickssh_title_commandhelp"),
                    SubTitle = GetTranslation("plugin_quickssh_subtitle_commandhelp"),
                    IcoPath = AppIconPath,
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
        /// The result is scored at <see cref="ScoreBackNavigation"/> so it is always
        /// the first row in every submenu, selection view, and confirmation view.
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
            var displayLabel = GetBackNavigationLabel(query, parentLabel);
            return new Result
            {
                Title = string.Format(GetTranslation("plugin_quickssh_back_nav_title"), displayLabel),
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

        private Result MakeWizardExampleResultFromKeys(
            string titleKey,
            string subtitleKey,
            string exampleQuery,
            params object[] titleArguments)
        {
            var title = GetTranslation(titleKey);
            if (titleArguments != null && titleArguments.Length > 0)
                title = string.Format(title, titleArguments);

            return MakeWizardExampleResult(
                title,
                GetTranslation(subtitleKey),
                exampleQuery);
        }

        private Result MakeWizardExampleResult(
            string title,
            string subtitle,
            string exampleQuery)
        {
            return new Result
            {
                Title = title,
                SubTitle = subtitle,
                IcoPath = AppIconPath,
                Score = ScoreSubMenuManagement,
                AutoCompleteText = exampleQuery,
                Action = _ =>
                {
                    _pluginContext?.API?.ChangeQuery(exampleQuery, true);
                    return false;
                }
            };
        }

        private static string GetBackNavigationLabel(Query query, string parentLabel)
        {
            var label = (parentLabel ?? string.Empty).Trim();
            var keyword = (query?.ActionKeyword ?? string.Empty).Trim();

            if (!string.IsNullOrEmpty(keyword) &&
                label.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
                label = label.Substring(keyword.Length).Trim();

            var normalized = label.ToLowerInvariant();
            if (string.IsNullOrEmpty(normalized))
                return GetTranslation("plugin_quickssh_back_root_label");
            if (normalized == "profiles manage")
                return GetTranslation("plugin_quickssh_back_profiles_manage_label");
            if (normalized == "profiles remove selection")
                return GetTranslation("plugin_quickssh_back_profiles_selection_label");
            if (normalized == "actions manage")
                return GetTranslation("plugin_quickssh_back_actions_manage_label");
            if (normalized == "actions profile selection")
                return GetTranslation("plugin_quickssh_back_actions_profile_selection_label");
            if (normalized == "actions action selection" ||
                normalized == "actions remove selection")
                return GetTranslation("plugin_quickssh_back_actions_action_selection_label");
            if (normalized == "keys manage")
                return GetTranslation("plugin_quickssh_back_keys_manage_label");
            if (normalized == "shell manage")
                return GetTranslation("plugin_quickssh_back_shell_manage_label");
            if (normalized.StartsWith("profiles", StringComparison.Ordinal))
                return GetTranslation("plugin_quickssh_back_profiles_label");
            if (normalized.StartsWith("actions", StringComparison.Ordinal))
                return GetTranslation("plugin_quickssh_back_actions_label");
            if (normalized.StartsWith("tools", StringComparison.Ordinal))
                return GetTranslation("plugin_quickssh_back_tools_label");
            if (normalized.StartsWith("shell", StringComparison.Ordinal))
                return GetTranslation("plugin_quickssh_back_shell_label");
            if (normalized.StartsWith("keys", StringComparison.Ordinal))
                return GetTranslation("plugin_quickssh_back_keys_label");
            if (normalized.StartsWith("config", StringComparison.Ordinal))
                return GetTranslation("plugin_quickssh_back_tools_label");
            if (normalized.StartsWith("help", StringComparison.Ordinal))
                return GetTranslation("plugin_quickssh_back_root_label");
            return label;
        }

        private string GetProfileKeyUnavailableSubtitle(SshKeyEntry? entry)
        {
            var path = ProfileWizard.ExpandLocalPath(entry?.Path);
            switch (ProfileWizard.GetKeyFileKind(entry))
            {
                case ProfileWizard.SshKeyFileKind.Public:
                    return string.Format(
                        GetTranslation("plugin_quickssh_profiles_key_public_subtitle"), path);
                case ProfileWizard.SshKeyFileKind.Unknown:
                    return string.Format(
                        GetTranslation("plugin_quickssh_profiles_key_invalid_subtitle"), path);
                default:
                    return string.Format(
                        GetTranslation("plugin_quickssh_profiles_key_missing_subtitle"), path);
            }
        }

        internal static string BuildProfileListSubtitle(SshProfile profile)
        {
            if (profile == null)
                return string.Empty;

            if (string.Equals(profile.Type, "scp", StringComparison.OrdinalIgnoreCase))
                return profile.ToDisplayString();

            var host = profile.HostName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(host))
                return profile.ToDisplayString();

            var destination = string.IsNullOrWhiteSpace(profile.User)
                ? host
                : profile.User + "@" + host;

            if (!string.IsNullOrWhiteSpace(profile.Port) && profile.Port != "22")
                destination += ":" + profile.Port;

            if (string.IsNullOrWhiteSpace(profile.IdentityFile))
                return destination;

            var normalizedPath = profile.IdentityFile.Trim('"').Replace('\\', '/');
            var separator = normalizedPath.LastIndexOf('/');
            var keyName = separator >= 0 ? normalizedPath.Substring(separator + 1) : normalizedPath;
            return string.IsNullOrWhiteSpace(keyName)
                ? destination
                : destination + " • " + keyName;
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

            string? ResolveSelectedExecutable(string executable) =>
                Utils.TryResolveExecutable(executable, out var resolvedPath)
                    ? resolvedPath
                    : null;

            if (!ShellLaunchPlan.TryCreate(
                    command,
                    selectedShell,
                    customShells,
                    ResolveSelectedExecutable,
                    GetCmdExePath(),
                    out var launchPlan,
                    out var planError))
            {
                _pluginContext?.API?.ShowMsg(
                    "QuickSSH",
                    GetShellLaunchPlanErrorMessage(planError, selectedShell));
                return;
            }

            // Use the user's home directory as the working directory so SSH can always
            // find ~/.ssh keys and config, even when FlowLauncher itself is installed in
            // a path that contains non-ASCII characters or spaces.
            var workingDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!ShellCommandLauncher.TryStart(
                    launchPlan,
                    workingDir,
                    Process.Start,
                    out var launchError))
            {
                var shellName = launchPlan.UsesDefaultShell
                    ? "cmd.exe"
                    : launchPlan.ShellName;
                _pluginContext?.API?.ShowMsg(
                    "QuickSSH",
                    string.Format(
                        GetTranslation("plugin_quickssh_shell_start_failed"),
                        shellName,
                        launchError?.Message ?? string.Empty));
            }
        }

        private static string GetShellLaunchPlanErrorMessage(
            ShellLaunchPlanError error,
            string? selectedShell)
        {
            var shellName = string.IsNullOrWhiteSpace(selectedShell)
                ? "cmd.exe"
                : selectedShell;

            switch (error)
            {
                case ShellLaunchPlanError.SelectedShellMissing:
                    return string.Format(
                        GetTranslation("plugin_quickssh_shell_selected_missing"),
                        shellName);
                case ShellLaunchPlanError.InvalidShellDefinition:
                    return string.Format(
                        GetTranslation("plugin_quickssh_shell_definition_invalid"),
                        shellName);
                case ShellLaunchPlanError.ExecutableNotFound:
                    return string.Format(
                        GetTranslation("plugin_quickssh_shell_executable_missing"),
                        shellName);
                default:
                    return string.Format(
                        GetTranslation("plugin_quickssh_shell_start_failed"),
                        shellName,
                        string.Empty);
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
            return SearchMatcher.ScoreProfile(search, name, command);
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

        /// <inheritdoc />
        public string GetTranslatedPluginTitle()
        {
            return GetTranslation("plugin_quickssh_plugin_name");
        }

        /// <inheritdoc />
        public string GetTranslatedPluginDescription()
        {
            return GetTranslation("plugin_quickssh_plugin_description");
        }

        /// <summary>Returns a localized resource value, or the key itself when translation lookup fails.</summary>
        /// <param name="key">Resource key to resolve.</param>
        /// <returns>The localized text or the original key as a safe fallback.</returns>
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
