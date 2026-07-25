# QuickSSH — Flow Launcher Plugin

Enhanced SSH/SCP connection plugin for [Flow Launcher](https://www.flowlauncher.com/) with query autocomplete, structured profile management, SSH key registry, SSH config import, human-readable profile export/import, custom shell support, and fuzzy search.

Inspired by [Melv1no/Flow.Launcher.Plugin.easyssh](https://github.com/Melv1no/Flow.Launcher.Plugin.easyssh).

## Command Structure

| Command | Description |
|---------|-------------|
| `ssh profiles [filter]` | Browse saved profiles and connect |
| `ssh profiles add <name> <ssh-command>` | Save a new SSH or SCP profile |
| `ssh profiles remove [filter]` | Delete a saved profile |
| `ssh profiles rename <oldname> <newname>` | Rename an existing profile |
| `ssh profiles copy [filter]` | Copy an SSH/SCP command to the clipboard |
| `ssh profiles export` | Export all profiles to a human-readable `.sshconfig` file |
| `ssh profiles import [filter]` | Import profiles from a `.sshconfig` or legacy `.json` file |
| `ssh keys` | Manage registered SSH key aliases (install / add / generate / remove / rename / copy-path / copy-pub / scan) |
| `ssh keys install <alias> <user@host>` | Install public key on remote Linux host |
| `ssh keys add <alias> <path>` | Register an SSH key alias pointing to a local key file |
| `ssh keys generate <alias> [path]` | Generate a new SSH keypair and auto-register it (default: `~/.ssh/`, or custom path) |
| `ssh keys remove [filter]` | Remove a registered SSH key alias |
| `ssh keys rename <old> <new>` | Rename an existing key alias |
| `ssh keys copy-path [filter]` | Copy the private key file path to clipboard |
| `ssh keys copy-pub [filter]` | Copy the public key (.pub) content to clipboard |
| `ssh keys scan` | Scan `~/.ssh/` for key files and offer registration |
| `ssh shell` | Manage custom terminal shells (add / remove / select) |
| `ssh config` | Import hosts from `~/.ssh/config` |
| `ssh help` | Open plugin documentation |
| `ssh -i <key> <destination>` | Direct connect with key autocomplete from registered keys |
| `ssh <destination>` | **Implicit direct connect** — type a destination or SSH options directly |

> **Suggestion order:** Typing bare `ssh` (with no arguments) shows top-level suggestions in this order: **profiles**, **keys**, **shell**, **config**, **help**.

> **Partial subcommand matching:** Under `ssh profiles`, subcommand matching reacts from the first matching character, consistently with top-level command matching.
> Examples: `ssh profiles a` → **add**; `ssh profiles r` → **remove**, **rename**; `ssh profiles rem` → **remove**; `ssh profiles ren` → **rename**.
> Single-letter prefixes that match only one subcommand show just that suggestion (e.g. `a` → **add**, `e` → **export**, `i` → **import**, `c` → **copy**).

> **Shell subcommand matching:** Under `ssh shell`, partial subcommand matching works the same way.
> Examples: `ssh shell a` → **add**; `ssh shell r` → **remove**; `ssh shell rem` → **remove**.

> **Keys subcommand matching:** Under `ssh keys`, partial subcommand matching works the same way.
> Examples: `ssh keys a` → **add**; `ssh keys g` → **generate**; `ssh keys i` → **install**; `ssh keys r` → **remove**, **rename**; `ssh keys c` → **copy-path**, **copy-pub**; `ssh keys s` → **scan**.

> **Note for v1 users:** The top-level `add` command (v1: `ssh add <name> <cmd>`) has been moved to `ssh profiles add <name> <cmd>`.
> Typing `ssh add ...` shows an explicit redirect hint in the UI — it will not silently do something unexpected.

### Capabilities

- **Structured profile model** — profiles are stored as typed, structured objects (not raw strings); supports SSH, RemoteCommand, port-forwards, SCP, ProxyJump, and more
- **Human-readable export/import** — profiles are exported and imported in an SSH-config-like text format (`.sshconfig` files)
- **Legacy migration** — v1 raw-command profiles (JSON) are automatically migrated to the structured format on first load
- **Query autocomplete** — type partial commands or profile names to see matching suggestions; select a result to expand the query
- **SSH key registry** — register local SSH keys by alias; registered keys are offered in autocomplete when typing `ssh -i`
- **SSH key generation** — generate new SSH keypairs (ed25519 or RSA 4096) locally via row-driven wizard; supports custom output path or default `~/.ssh/` location; generated keys are auto-registered after verifying both private and public key files
- **SSH key installation** — deploy a registered public key to a remote Linux host's `~/.ssh/authorized_keys` via an idempotent bootstrap command; supports run, copy command, and copy public key actions
- **Implicit direct SSH input** — type a destination (`user@host`, bare IP/hostname) or SSH options (`-p 22 user@host`, `-i key user@host`) directly without any command prefix
- **SSH config import** — parse and import hosts from `~/.ssh/config`
- **SCP support** — save SCP upload/download profiles with all SCP options
- **Tunnel support** — save SSH tunnel profiles with LocalForward, RemoteForward, DynamicForward
- **RemoteCommand support** — run arbitrary remote commands (e.g. `reboot`, `systemctl restart nginx`)
- **Fuzzy search** — accent-insensitive search with Damerau-Levenshtein distance
- **Custom shells** — use cmd.exe, PowerShell, WSL, Git Bash, Windows Terminal, or any terminal
- **Multi-language support** — English, Slovak, French, German, Russian, Polish, and Spanish
- **Atomic saves** — profile data is written atomically to prevent corruption

---

## Usage Examples

### Browse and connect to saved profiles

```
ssh profiles           → profile management view: action rows + saved profiles
ssh profiles prod      → filter saved profiles containing "prod"
```

Press Enter on a profile row to launch the connection.

> **Stay-open behaviour:** All non-launch actions (add, remove, rename, copy, export, import, generate, scan, config import, help) keep Flow Launcher open and navigate back to the parent menu. Only actions that actually launch an SSH/SCP connection close the plugin.

> **Display order** — `ssh profiles` always shows results in a fixed, stable order regardless of fuzzy-match scoring:
> 1. **Profile management** (usage hint, always pinned at the top)
> 2. **← Back to ssh** (back-navigation row — press Enter to return to the top-level command list)
> 3. **Action rows** as one continuous block: Add profile → Remove profile → Rename profile → Copy SSH command → Export profiles → Import profiles
> 4. **Saved profiles** (filtered / sorted by relevance when a search term is given)

Sub-command views (e.g. `ssh profiles add`, `ssh shell remove`) and single-action views (`ssh config`, `ssh help`) also show a back-navigation row immediately below their usage hint so you can press Enter to return to the parent level.

### Add a profile

Profiles are added using standard SSH or SCP command syntax.
The plugin parses the command into a structured profile automatically.

**SSH — basic login:**
```
ssh profiles add myserver ssh root@10.0.0.150
ssh profiles add dev-box ssh -p 2222 dev@10.0.0.50
```

**SSH — with identity file:**
```
ssh profiles add production ssh -i "C:\Users\me\.ssh\id_rsa" -o IdentitiesOnly=yes admin@prod.example.com
```

**SSH — run a remote command:**
```
ssh profiles add reboot-proxmox ssh -t -t root@10.0.0.150 reboot
```

**SSH — local port forward (tunnel):**
```
ssh profiles add pangolin-tunnel ssh -L 8443:127.0.0.1:443 -L 8080:127.0.0.1:80 root@10.100.100.242
```

**SSH — SOCKS proxy:**
```
ssh profiles add socks-proxy ssh -D 1080 root@jump.example.com
```

**SSH — ProxyJump:**
```
ssh profiles add internal-host ssh -J bastion.example.com root@10.0.0.10
```

**SCP — upload a file:**
```
ssh profiles add upload-index scp -i "~/.ssh/key" "C:\web\index.html" root@10.0.0.1:/var/www/html/index.html
```

You can also omit the `ssh ` prefix — the plugin adds it automatically:
```
ssh profiles add bastion admin@bastion.example.com -p 22222
```

### Remove a profile

```
ssh profiles remove             → list all profiles for removal
ssh profiles remove prod        → filter profiles by "prod", then click to delete
```

### Rename a profile

```
ssh profiles rename                      → list all profiles to select for renaming
ssh profiles rename myserver             → pick "myserver" as the source
ssh profiles rename myserver new-name    → rename "myserver" to "new-name"
```

### Copy an SSH command to the clipboard

```
ssh profiles copy               → list all profiles for copying
ssh profiles copy myserver      → filter by "myserver", then click to copy
```

> **Copied command format:** The clipboard receives a user-friendly, paste-ready command with single backslashes for Windows paths. Arguments are quoted only when needed (e.g. paths containing spaces). Examples:
> - `ssh -i C:\Users\info\.ssh\key root@10.0.0.150` — no quotes (path without spaces)
> - `ssh -i "C:\Users\info\My Keys\key" root@10.0.0.150` — quoted (path with spaces)

### SSH key management

Register SSH keys by alias so you can quickly reference them in direct connect or profile creation:

```
ssh keys                                     → key management view: action rows + registered keys
ssh keys add prod ~/.ssh/id_ed25519          → register key alias "prod"
ssh keys add dev "C:\Users\me\.ssh\dev_key"  → register key alias "dev" (quoted path)
ssh keys remove prod                         → remove key alias "prod" (registry only — files on disk are kept)
```

> **Display order** — `ssh keys` always shows action rows in a fixed, stable order: **install** → **add** → **generate** → **remove** → **rename** → **copy-path** → **copy-pub** → **scan**, followed by registered key entries.

> **Security note:** QuickSSH stores only the alias and the file path — **never** the private key content. The key file is accessed by SSH at connection time, not by the plugin.

> **Key file validation:** When browsing registered keys, QuickSSH checks whether the key file exists on disk and shows a warning icon if it is missing.

> **Post-action feedback:** All management actions (add, remove, rename, copy, export, import, generate, install, scan, config import) keep Flow Launcher open and return to the parent menu so you can see the updated state and continue working. Clipboard actions (profiles copy, keys copy-path, keys copy-pub) stay inside their submenu. The `keys install` "Run remote setup command" action opens a terminal and closes Flow Launcher. The `keys remove` command only removes the alias from the registry — key files on disk are never deleted.

### Generate an SSH keypair

Generate a new SSH keypair locally and auto-register it in the key registry:

```
ssh keys generate                            → usage hint
ssh keys generate mykey                      → shows actionable rows:
                                                ● Generate ed25519 (recommended default)
                                                ● Generate RSA 4096
                                                ● Custom path… (hint row)
ssh keys generate mykey C:\keys\mykey        → custom path flow:
                                                ● Generate ed25519 → C:\keys\mykey
                                                ● Generate RSA 4096 → C:\keys\mykey
```

**Row-driven UX:** After typing the alias, you choose the algorithm by clicking a row — no need to type `ed25519` or `rsa` as arguments. To use a custom output path, append it after the alias.

**Default behaviour:**
- **Algorithm:** ed25519 (recommended). RSA 4096 is available as an alternative row.
- **Output path:** `%USERPROFILE%\.ssh\<alias>` — the file name is derived from the alias with unsafe characters removed.
- **Custom path:** Append a path after the alias to generate the keypair at a custom location (e.g. `ssh keys generate mykey D:\keys\mykey`). Quoted paths with spaces are supported (e.g. `ssh keys generate mykey "C:\My Keys\mykey"`). The alias and path are separated using Unicode-aware whitespace parsing, so the custom path is never mangled through alias sanitisation.
- **Passphrase:** Not supported in this version — keys are generated with an empty passphrase (`-N ""`). Interactive passphrase support will be added in a future release.

**What happens on click:**
1. `ssh-keygen` runs non-interactively in the background (no terminal window).
2. QuickSSH verifies that **both** the private key and `.pub` file were created.
3. If both files exist → the key is auto-registered with metadata (alias, path, algorithm, source, timestamp).
4. If either file is missing (failed) → nothing is registered.
5. On success, a confirmation message shows the alias, private key path, and public key path. Flow Launcher stays open and returns to the `ssh keys` menu.

**Validations:**
- Empty alias → usage hint shown
- Duplicate alias → error: alias already exists
- Target key file already exists → error: file already exists
- Custom path is an existing directory → error: path is a directory
- Custom path contains invalid characters → error: invalid path
- ssh-keygen not found → error: install OpenSSH
- Generation failed → no registration

> **Storage:** Only harmless metadata is stored in the key registry: alias, path, public key path, algorithm, source (`"generated"`), and creation timestamp. Private key content and passphrases are **never** stored.

> **Passphrase flow:** Intentionally deferred. Launching an interactive terminal from a Flow Launcher plugin and waiting for completion has not been runtime-verified. This will be addressed in a follow-up PR.

### Install public key on a remote host

Deploy a registered public key to a remote Linux host's `~/.ssh/authorized_keys`:

```
ssh keys install                             → list registered keys (select one)
ssh keys install mykey                       → prompt for user@host
ssh keys install mykey admin@10.0.0.1        → shows 3 action rows:
                                                ● Run remote setup command (opens terminal)
                                                ● Copy remote setup command (clipboard)
                                                ● Copy public key (clipboard)
```

**Available actions:**
- **Run remote setup command** — opens a terminal and runs `ssh user@host '<bootstrap>'`. The user enters their password in the terminal.
- **Copy remote setup command** — copies the full `ssh user@host '...'` command to the clipboard.
- **Copy public key** — copies the `.pub` file content to the clipboard.

**Remote bootstrap command:** The plugin builds an idempotent one-liner that:
1. Sets `umask 077` for secure permissions
2. Creates `~/.ssh` and `authorized_keys` if missing
3. Fixes permissions (`chmod 700`/`600`)
4. Checks whether the key is already present (`grep -qxF`)
5. Appends the key only if not already present (`printf` — no `echo`)

**Public key validation:** The `.pub` file content must start with a known key type (`ssh-ed25519`, `ssh-rsa`, `ecdsa-sha2-*`, etc.) followed by base64 data. Everything after the second token is treated as an optional comment. Single quotes, newlines, and null bytes are rejected to prevent shell injection.

**Security note:** Only the public key is handled. No private keys are transmitted. No `sshd_config` changes are made on the remote host. The user enters their password directly in the SSH terminal.

### Rename a key alias

```
ssh keys rename                              → list all key aliases for renaming
ssh keys rename prod                         → pick "prod" as the source
ssh keys rename prod production              → rename "prod" to "production"
```

Duplicate alias names are validated — renaming to an existing alias shows an error.

### Copy key path to clipboard

```
ssh keys copy-path                           → list all keys for path copying
ssh keys copy-path prod                      → copy the private key file path for "prod"
```

### Copy public key content to clipboard

```
ssh keys copy-pub                            → list all keys for public key copying
ssh keys copy-pub prod                       → copy the content of prod's .pub file
```

If the `.pub` file does not exist (e.g. the key was generated without a public counterpart), an error row is shown instead of the copy action.

The public key path is derived as `<private-key-path>.pub` by default, or from the explicit `PublicKeyPath` field if set.

### Scan for key files

```
ssh keys scan                                → scan ~/.ssh/ for key files
```

Scans `%USERPROFILE%\.ssh\` for private key files. Filters out:
- `.pub` files (public keys)
- `known_hosts`, `known_hosts.old`, `config`, `authorized_keys`, `environment`
- Files with `.log`, `.bak`, `.tmp`, `.old` extensions

Each discovered key file is shown as a candidate — click to register it with the file name as the alias. Already-registered keys are marked as "(already registered)".

#### Identity file autocomplete (`-i`)

When typing a direct SSH command with `-i`, registered keys are offered as autocomplete suggestions:

```
ssh -i                    → shows all registered key aliases
ssh -i ~/.ssh/pr          → shows matching key aliases (e.g. "prod")
```

Select a key alias to fill in the full path automatically, then continue typing the destination. The inserted path uses normal Windows backslashes (e.g. `ssh -i "C:\Users\me\.ssh\key"`) — paths containing spaces are quoted automatically.

### Quick one-time connection (without saving)

Type a destination or SSH options directly — the plugin detects these automatically:

```
ssh root@10.0.0.1
ssh -p 2222 deploy@staging.example.com
ssh -i "C:\Users\me\.ssh\private_key" -o IdentitiesOnly=yes root@10.100.100.110
ssh 10.100.100.110
```

**Implicit detection rules** — input is treated as a direct connect when it:
- contains `@` (e.g. `user@host`, `root@10.0.0.1`)
- starts with `-` (e.g. `-p 22 user@host`, `-i key user@host`)
- is a bare hostname or IP with at least one dot (e.g. `10.0.0.1`, `myserver.example.com`)

### Import hosts from `~/.ssh/config`

```
ssh config             → import all Host entries from ~/.ssh/config
```

Only new hosts are imported — existing profiles are not overwritten.

The parser captures: `HostName`, `User`, `Port`, `IdentityFile`, `IdentitiesOnly`,
`LocalForward`, `RemoteForward`, `DynamicForward`, `ProxyJump`, `ProxyCommand`.

Example `~/.ssh/config` that is fully supported:

```
Host proxmox
    HostName 10.0.0.150
    User root
    Port 22
    IdentityFile ~/.ssh/id_ed25519

Host production
    HostName prod.example.com
    User deploy
    Port 2222
    IdentityFile ~/.ssh/id_ed25519
    IdentitiesOnly yes

Host bastion
    HostName bastion.corp.internal
    User ec2-user
    IdentityFile "C:\Users\me\.ssh\corp_key"

Host internal
    HostName 10.0.0.10
    ProxyJump bastion
```

Wildcard entries (`Host *`) are skipped automatically.

### Export and import profiles

**Export** — saves all current profiles to a human-readable `.sshconfig` file:

```
ssh profiles export
```

The file is written to:
```
%APPDATA%\FlowLauncher\Plugins\QuickSSH\data\profiles_export.sshconfig
```

**Import** — loads profiles from any `.sshconfig` file (or legacy `.json` file) placed in the `data\` folder:

```
ssh profiles import                    → list all importable files
ssh profiles import mybackup           → filter files containing "mybackup"
```

Only profiles that do not already exist are added (no overwriting).

### Saved profile format (`.sshconfig`)

Profiles are exported in a human-readable SSH-config-like format.
This format is intentionally similar to OpenSSH `ssh_config(5)` but is **not** a strict clone —
it adds QuickSSH-specific fields like `Type`, `RemoteCommand`, `RequestTTY`, `Source`, `Target`, etc.

**SSH profile — normal login:**
```
Host Proxmox-Host
    Type ssh
    HostName 10.0.0.150
    User root
    Port 22
    IdentityFile ~/.ssh/private_key
    IdentitiesOnly yes
```

**SSH profile — remote command with TTY:**
```
Host RustDesk-REBOOT
    Type ssh
    HostName 10.100.100.110
    User root
    Port 22
    IdentityFile "C:\Users\info\.ssh\private_key"
    IdentitiesOnly yes
    RemoteCommand reboot
    RequestTTY force
```

**SSH profile — local port forwards (tunnel):**
```
Host Pangolin-Tunnel
    Type ssh
    HostName 10.100.100.242
    User root
    Port 22
    IdentityFile ~/.ssh/private_key
    IdentitiesOnly yes
    LocalForward 8443 127.0.0.1:443
    LocalForward 8080 127.0.0.1:80
```

**SCP profile — file upload:**
```
Host Homepage-Upload
    Type scp
    HostName 10.100.100.241
    User root
    Port 22
    IdentityFile "C:\Users\info\.ssh\private_key"
    IdentitiesOnly yes
    Source "C:\web\index.html"
    Target "/var/www/html/index.html"
```

### Supported profile fields

**Common fields (SSH and SCP):**

| Field | Description |
|-------|-------------|
| `Type` | `ssh` (default) or `scp` |
| `HostName` | Hostname or IP address |
| `User` | Remote user name |
| `Port` | Port number (omitted from command when 22) |
| `IdentityFile` | Path to private key file |
| `IdentitiesOnly` | `yes` adds `-o IdentitiesOnly=yes` |
| `ExtraArgs` | Raw extra arguments (fallback for unparsed flags) |

**SSH-specific fields:**

| Field | Description |
|-------|-------------|
| `RemoteCommand` | Command to execute on the remote host |
| `RequestTTY` | TTY allocation: `force` (-t -t), `yes` (-t), `no` (-T) |
| `LocalForward` | Local port forward spec, e.g. `8443 127.0.0.1:443` (repeatable) |
| `RemoteForward` | Remote port forward spec (repeatable) |
| `DynamicForward` | SOCKS5 proxy port, e.g. `1080` |
| `ProxyJump` | Jump host(s) for `-J` |
| `ProxyCommand` | Proxy command string |

**SCP-specific fields:**

| Field | Description |
|-------|-------------|
| `Source` | **Bare source path** — local path for upload, remote path for download |
| `Target` | **Bare target path** — remote path for upload, local path for download |
| `Recursive` | `yes` adds `-r` |
| `PreserveTimes` | `yes` adds `-p` |
| `Compression` | `yes` adds `-C` |

**SCP normalization rule:**  
`Source` and `Target` always store **bare paths** — no `user@host:` prefix.
`HostName` and `User` are always in the common structured fields.  
The command builder determines transfer direction by inspecting the paths:

- **Upload** — `Source` is a Windows local path (e.g. `C:\...`): builds `scp source user@host:target`
- **Download** — `Target` is a Windows local path: builds `scp user@host:source target`
- **Ambiguous** (both are Unix-style paths): upload is assumed, Source is treated as local

This means on-disk profiles are always portable and can be re-parsed without data loss.
Legacy SCP commands with `user@host:path` positionals are automatically normalised on import.

### Custom shell management

```
ssh shell                                             → shell management view: action rows + saved shells
ssh shell add PowerShell                              → add PowerShell (found via PATH)
ssh shell add GitBash "C:\Program Files\Git\bin\bash.exe" --login -i -c
ssh shell add WSL wsl.exe --
ssh shell add WindowsTerminal wt.exe ssh
ssh shell remove PowerShell                           → remove a shell entry
```

**Shell value format** — `ssh shell add <name> [<executable> [extra-args]]`:

| Example | Effect |
|---------|--------|
| `ssh shell add PowerShell` | Name = value = `PowerShell`; resolved via PATH |
| `ssh shell add PS "C:\...\pwsh.exe"` | Name `PS`, explicit exe path, no extra args |
| `ssh shell add GitBash "C:\...\bash.exe" -c` | Name `GitBash`, exe + `-c` flag prepended to command |

Click any shell in the list to **select** it. All SSH connections will then launch through that shell. Click it again to deselect (returns to default `cmd.exe`).

---

## Data Storage

QuickSSH stores its main database in Flow Launcher's per-plugin settings directory.
In portable Flow Launcher builds this stays inside the portable `UserData` tree:

```
<FlowLauncherPortable>\UserData\Settings\Plugins\Flow.Launcher.Plugin.QuickSSH\profiles.json
```

On first startup after upgrading, an existing legacy `~/.ssh/profiles.json` file is copied
to the new location only when the new database does not already exist. The legacy file is
left untouched.

| File | Purpose |
|------|---------|
| `<Flow Launcher plugin settings>\profiles.json` | Main profile, shell, and key database (v2 structured JSON) |
| `~/.ssh/profiles.json` | Legacy database path, copied once during upgrade when needed |
| `%APPDATA%\FlowLauncher\Plugins\QuickSSH\data\*.sshconfig` | Human-readable export/import files |
| `%APPDATA%\FlowLauncher\Plugins\QuickSSH\data\*.json` | Legacy import files (v1, still readable) |

### profiles.json schema (v2)

```json
{
  "PluginVersion": "2.0",
  "ProfilesLists": {
    "<profile-name>": {
      "Type": "ssh",
      "HostName": "10.0.0.150",
      "User": "root",
      "Port": "22",
      "IdentityFile": "~/.ssh/private_key",
      "IdentitiesOnly": true,
      "RemoteCommand": "reboot",
      "RequestTTY": "force"
    }
  },
  "CustomShellLists": {
    "<shell-name>": "<executable path + optional args>"
  },
  "SelectedCustomShell": "<shell-name or null>",
  "SshKeysLists": {
    "<alias>": {
      "Path": "C:\\Users\\me\\.ssh\\id_ed25519",
      "PublicKeyPath": "C:\\Users\\me\\.ssh\\id_ed25519.pub",
      "Fingerprint": "SHA256:...",
      "Comment": "user@host",
      "Description": "optional description",
      "Algorithm": "ed25519",
      "Source": "generated",
      "CreatedAt": "2025-01-15T10:30:00.0000000Z"
    }
  }
}
```

### Migration from v1

**v1 profiles.json** stored profiles as raw SSH command strings:
```json
{
  "PluginVersion": "1.0",
  "EntriesLists": {
    "myserver": "ssh user@192.168.1.100",
    "production": "ssh -i \"C:\\...\\id_rsa\" admin@prod -p 2222"
  }
}
```

**On first load**, QuickSSH automatically:
1. Parses each raw command string into a structured `SshProfile`
2. Stores them in the new `ProfilesLists` format
3. Clears the legacy `EntriesLists` field from memory
4. **Immediately persists the v2 format** so the disk file is canonical after the first run

Migration handles:
- Simple `ssh user@host` → structured with `User`, `HostName`
- `ssh -p 22 user@host` → structured with `Port`, `User`, `HostName`
- `ssh -i key -o IdentitiesOnly=yes user@host` → structured with all fields
- Remote commands after the destination → `RemoteCommand` field
- Unknown/unsupported flags (e.g. `-X`, `-A`) → stored verbatim in `ExtraArgs`
- SCP upload `scp C:\file.txt user@host:/path` → `Source` = local bare path, `Target` = remote bare path, `User`/`HostName` extracted safely (Windows drive paths never misidentified as remote specs)
- SCP download `scp user@host:/remote/file C:\local\file` → `Source` = remote bare path, `Target` = local bare path

**Unparseable flag fallback (ExtraArgs):** SSH has many options. Flags that this plugin does not map to a named structured field are preserved verbatim in the `ExtraArgs` field and appended to the generated command. This means:
- **No data is silently lost** during migration.
- The `ExtraArgs` field is round-trip stable: it is included when exporting to `.sshconfig` and is read back unchanged on import.
- The generated SSH command still includes the flag, so the connection behaviour is preserved.

> **Note:** "profiles import" still accepts legacy `.json` files for backward-compatible migration.
> JSON is **never written** by this plugin — `.sshconfig` is the canonical export format.
> Legacy `.json` files are clearly labelled "(legacy)" in the import UI.

---

## Installation

### From Flow Launcher (after plugin is published)

1. Open Flow Launcher
2. Type `pm install QuickSSH`
3. Restart Flow Launcher

### Manual Installation

1. Download `QuickSSH.zip` from [Releases](https://github.com/Vaso73/Flow.Launcher.Plugin.QuickSSH/releases)
2. Extract the zip into a new folder:
   ```
   %APPDATA%\FlowLauncher\Plugins\QuickSSH\
   ```
3. Restart Flow Launcher
4. Type `ssh` to verify the plugin loaded

## Building from Source

Requires Windows with .NET 9.0 SDK installed.

```powershell
# Clone the repository
git clone https://github.com/Vaso73/Flow.Launcher.Plugin.QuickSSH.git
cd Flow.Launcher.Plugin.QuickSSH

# Build
dotnet publish -c Release -r win-x64 --no-self-contained

# Output: bin\Release\win-x64\publish\
# Copy that folder's contents to:
# %APPDATA%\FlowLauncher\Plugins\QuickSSH\
```

## Requirements

- Windows 10 version 1809+ or Windows Server 2019+ (built-in OpenSSH)  
  — or any Windows with `ssh.exe` available in PATH
- [Flow Launcher](https://www.flowlauncher.com/) v1.19+
- .NET 9.0 Runtime (bundled with Flow Launcher v1.19+)

## Languages

| Code | Language |
|------|----------|
| `en` | English |
| `sk` | Slovak (Slovenčina) |
| `fr` | French (Français) |
| `de` | German (Deutsch) |
| `ru` | Russian (Русский) |
| `pl` | Polish (Polski) |
| `es` | Spanish (Español) |

Flow Launcher automatically selects the language that matches your system locale.

## Publishing to Flow Launcher Plugin Store

To make QuickSSH available via `pm install QuickSSH` in Flow Launcher:

1. **Create a GitHub Release** — open a Pull Request in this repository, update `plugin.json` with the desired version, add one label (`release:patch`, `release:minor`, or `release:major`), and merge it into `main`. GitHub Actions will automatically build `QuickSSH.zip`, create a tag, and publish the release.

2. **Fork the Plugin Manifest** — fork [Flow-Launcher/Flow.Launcher.PluginsManifest](https://github.com/Flow-Launcher/Flow.Launcher.PluginsManifest).

3. **Add a manifest entry** — in your fork create the file:

   ```
   plugins/QuickSSH-86AC23FE48BC45E5B7E0A94F5847FA83.json
   ```

   with the following content (update `UrlDownload` to point to the latest release zip):

   ```json
   {
     "ID": "86AC23FE48BC45E5B7E0A94F5847FA83",
     "Name": "QuickSSH",
     "Description": "Enhanced SSH/SCP connection plugin with query autocomplete, structured profiles, SSH config support, and custom shell handling",
     "Author": "Vaso73",
     "Version": "3.4.0",
     "Language": "csharp",
     "MinFlowLauncherVersion": "1.19.0",
     "Website": "https://github.com/Vaso73/Flow.Launcher.Plugin.QuickSSH",
     "UrlSourceCode": "https://github.com/Vaso73/Flow.Launcher.Plugin.QuickSSH",
     "UrlDownload": "https://github.com/Vaso73/Flow.Launcher.Plugin.QuickSSH/releases/latest/download/QuickSSH.zip",
     "IcoPath": "https://raw.githubusercontent.com/Vaso73/Flow.Launcher.Plugin.QuickSSH/main/Images/app.png"
   }
   ```

4. **Open a Pull Request** — submit the PR to `Flow-Launcher/Flow.Launcher.PluginsManifest`. Once merged the plugin becomes available in the Flow Launcher store.

## Contributing

Contributions are welcome! Here is the typical workflow:

1. Fork the repository and create a feature branch.
2. Make your changes.
3. Run a local build to verify nothing is broken:
   ```powershell
   dotnet publish -c Release -r win-x64 --no-self-contained
   ```
4. If the Pull Request should create a release, add one label: `release:patch`, `release:minor`, or `release:major`.
5. Open a Pull Request describing your changes.

### Versioning

`plugin.json` is the **single source of truth** for the plugin version. There is no separate version in the project file or any other location. GitHub tags and release names always match the version committed in `plugin.json`.

### Releasing a new version

1. Open a Pull Request with your changes.
2. **Update `plugin.json`** — set `Version` to the desired new version (e.g. `"3.0.1"` for a patch, `"3.1.0"` for a minor, `"4.0.0"` for a major). This must be done in the PR itself before merge — the workflow reads whatever version is already in `plugin.json` on `main` after the merge.
3. Add **exactly one** release label: `release:patch`, `release:minor`, or `release:major` (or `skip-release` to skip the release entirely).
4. Merge the Pull Request into `main`.
5. GitHub Actions automatically:
   - Validates that exactly one release label is present
   - Reads the current version from `plugin.json` on `main`
   - Builds `QuickSSH.zip`
   - Creates a matching git tag and GitHub Release
6. Update the Plugin Manifest entry in `Flow-Launcher/Flow.Launcher.PluginsManifest` if needed.

`plugin.json` is the **single source of truth** for the plugin version. The version must already be correct in the PR — the workflow does **not** modify `main` after merge. This is required because `main` is a protected branch and post-merge pushes from CI would be rejected.

> **Note:** `skip-release` prevents the entire release job from running. It is different from a missing label: a PR without any label at all that is merged to `main` will cause the release job to **fail** with a clear error. Always include either a release label or `skip-release`.

## License

[MIT](LICENSE)
