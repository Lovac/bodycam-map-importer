# Security: how the app uses your disk

This page lists every place the app writes, what it reads, and what it never does.
Every point names the code, as `file:line` from the root of this repository.
The line numbers were checked on the 16 September 2026 source; later edits may have moved some by a few lines.

In the code the game folder is called `<Bodycam>`. For a normal Steam install it is
`C:\Program Files (x86)\Steam\steamapps\common\Bodycam`.

## Short version

- It writes only inside `<Bodycam>\Bodycam\Content\Paks` and in `%LOCALAPPDATA%\BodycamMapInstaller`.
- It never deletes a file. Replaced and removed files are moved to `Paks\_backup\`.
- It never touches the game's own `pakchunk*-Windows.pak` files.
- It does not use the internet, does not write the registry, and does not ask for administrator rights at start.

## 1. Writes in the game folder

All of these are inside `<Bodycam>\Bodycam\Content\Paks`. The folder names are set in `Core/Game.cs:23-27`.
The app accepts a game folder only if it holds `Bodycam\Binaries\Win64\Bodycam-Win64-Shipping.exe` and `Bodycam\Content\Paks` (`Core/Game.cs:107-114`).

| What | Where | Code |
|---|---|---|
| Map `.pak` files from a map package. Copied as `<name>.pak.part`, read back and checked, then renamed into place. | `Paks\` | `Core/Installer.cs:534-567`, `Core/Installer.cs:612-618` |
| Card files (card, photo, menu data). Written to a staging folder first, then moved. | `Paks\_cards\.staging\<Id>` then `Paks\_cards\<Id>` | `Core/Installer.cs:530`, `Core/Installer.cs:569-584`, `Core/Installer.cs:619` |
| A single map `.pak` you drop. Copied as `.part`, read back, then renamed. Names starting with `pakchunk` are refused. | `Paks\` | `Core/Installer.cs:717`, `Core/Installer.cs:782-810` |
| The shared menu file. Written as `.part`, every entry read back and compared, then moved into place. | `Paks\zzMapCards_19_P.pak` | `Core/MenuPak.cs:95`, `Core/Installer.cs:1135-1161`, `Core/Installer.cs:1197-1202` |
| The app's own state (menu file hash, card order, hidden or not). Written to a temp file, then swapped in. | `Paks\_cards\_installer.json` | `Core/Backup.cs:156-165`, `Core/Backup.cs:180`, `Core/Backup.cs:208-214` |
| "Hide custom maps": custom map files are moved aside and renamed `.pak.off`, so the game does not load them. | `Paks\_paused\` | `Core/Installer.cs:942-972` |
| "Show custom maps again": the `.pak.off` files move back. A file with a game pak name is not put back. | `Paks\` | `Core/Installer.cs:975-1004`, `Core/Installer.cs:992` |
| Backups (see section 4). | `Paks\_backup\<date>_<time>\` | `Core/Backup.cs:34-43` |

## 2. Writes in your user folder

| What | Where | Code |
|---|---|---|
| Window size and the game folder you picked | `%LOCALAPPDATA%\BodycamMapInstaller\settings.json` | `App/Ui/Settings.cs:19-29`, `App/Ui/Settings.cs:64-81` |
| Activity log. When it passes 1 MB it is renamed with a date, not deleted. | `%LOCALAPPDATA%\BodycamMapInstaller\installer.log` | `App/Ui/Settings.cs:84-123` |

The log stays on your PC. It can contain folder paths. The app never sends it anywhere.
Start the app with `--no-settings` to write neither file (`App/Program.cs:69`, `App/Ui/MainForm.cs:98-99`).
One exception: if the app crashes, it still adds the error to `installer.log` (`App/Program.cs:122-124`).
Command-line jobs print to the console and do not use these files.
The environment variable `BCMI_SETTINGS_DIR` moves both files to another folder (`App/Ui/Settings.cs:23`).

## 3. Writes only when you ask on the command line

| Option | What it writes | Code |
|---|---|---|
| `--report <file>` | A new report file. Refuses if the file exists. | `Core/CommandLine.cs:62-66` |
| `--screenshot <png>` | A new PNG. Refuses a real game folder: it only accepts a fake one under `%TEMP%`. | `App/Ui/MainForm.Shots.cs:46-48`, `App/Program.cs:81-89` |
| `--ui-selftest <file>` | A new report file and one empty folder under `%TEMP%`. | `App/Ui/UiSelfTest.cs:17`, `App/Ui/UiSelfTest.cs:67-68` |
| `--selftest <folder>` | Only a new folder under `%TEMP%`, never inside a Steam library. It may start `py.exe` or `UnrealPak.exe`, if they exist, to read back its own test files. It needs private test files, so from this repository it stops before writing anything. | `Core/SelfTest.cs:113-142`, `Core/SelfTest.cs:1302-1392` |

## 4. Backups and safety rules

- **No deleting.** There is no `File.Delete` or `Directory.Delete` in `App/` or `Core/`.
  A file to be replaced or removed is moved into the backup folder (`Core/Backup.cs:55-85`).
- **Backups do not load in the game.** A `.pak` in the backup folder is renamed `.pak.bak` (`Core/Backup.cs:66`, `Core/Backup.cs:76-82`).
- **Every backup folder explains itself.** It holds `manifest.json` and `RESTORE.txt`, which say what came from where and why (`Core/Backup.cs:124-151`).
- **Game paks are never moved.** A move of `pakchunk<N>-Windows.pak` is refused (`Core/Backup.cs:63-64`, name pattern in `Core/Util.cs:114`).
- **The backup folder is never backed up into itself** (`Core/Backup.cs:62`).
- **No accidental overwrite.** New files are opened with `FileMode.CreateNew`, which fails if the file exists
  (`Core/Util.cs:169-178`, `Core/Installer.cs:539`, `Core/Installer.cs:787`).
  The only files replaced in place are the app's own JSON files (`Core/Backup.cs:163`, `App/Ui/Settings.cs:77`).
- **Undo on failure.** If a step fails, the files moved so far are put back (`Core/Backup.cs:89-117`, `Core/Installer.cs:669-681`).
- **Not while the game runs.** Bodycam is detected by its process name (`Core/Game.cs:150-156`).
  Install, remove, hide and show stop if it is running (for example `Core/Installer.cs:592`, `Core/Installer.cs:801`, `Core/Installer.cs:886-891`, `Core/Installer.cs:944`, `Core/Installer.cs:977`, `Core/Installer.cs:1190-1196`).
- **Safe zip entries.** Names with `..`, `\`, `:`, a leading `/` or non-ASCII letters are refused (`Core/PackagePaths.cs:24-33`, used in `Core/CardPackage.cs:116`).
- **Map packages cannot replace game files.** A `.bcmap` holding a path that Bodycam already has is refused (`Core/CardPackage.cs:492`).

## 5. When it writes without a click

Opening the window can write in two cases:

1. A step from an earlier run did not finish (a `*.part` file in `Paks`, or `Paks\_cards\.staging`).
   The leftovers are moved to the backup folder. Nothing else changes. This is skipped while Bodycam runs.
   (`App/Ui/MainForm.cs:509`, `Core/Installer.cs:1211-1239`). The command-line jobs do the same (`Core/CommandLine.cs:115`).
2. Maps with cards are installed but the shared menu file is missing. The app builds it again (`App/Ui/MainForm.cs:585-598`).

## 6. What it reads

- Where Steam is installed, from the registry, read-only (`Core/Game.cs:56-64`).
- Steam's `libraryfolders.vdf` and `appmanifest_2406770.acf`, to find Bodycam and its build number (`Core/Game.cs:133`, `Core/Game.cs:163-199`).
- The file index of every `pakchunk*-Windows.pak`, read-only (`Core/Game.cs:268-321`).
  It compares 8 entries with the tables inside the exe (`Core/BaseTables.cs:111-141`).
- The map files you drop on the window or pass on the command line.

## 7. What it never does

- **No internet.** No `HttpClient`, `WebClient`, `WebRequest`, sockets or `System.Net` in `App/` or `Core/`. No update check. No telemetry.
- **No registry writes.** The registry is only opened read-only (`Core/Game.cs:56`, `Core/Game.cs:62-63`).
- **No administrator rights at start.** `App/app.manifest:8` requests `asInvoker`.
  Only if Windows refuses a write in the game folder does the app show a "Run as administrator" button.
  It restarts itself as administrator only if you click it (`App/Ui/MainForm.cs:824-843`).
- **Other programs it starts:** Windows Explorer, when you click "Mods folder" or "Backups" (`App/Ui/MainForm.cs:147-148`, `App/Ui/MainForm.cs:1010-1016`),
  and itself as administrator, as above. `--selftest` may start `py.exe` or `UnrealPak.exe` (section 3).
- A private developer build can also start Git Bash. That code is not in this repository and not in the published exe.
  Run `BodycamMapInstaller.exe --ui-selftest report.txt` and look for `PASS flavour.player_build`.

## Check it yourself

In Windows PowerShell, in the repository folder:

```powershell
# No deleting, no network code. Prints nothing.
Select-String -Path App\*.cs, App\Ui\*.cs, Core\*.cs -Pattern 'File\.Delete', 'Directory\.Delete', 'HttpClient', 'WebClient', 'WebRequest', 'Socket', 'System\.Net'

# No registry writes. Prints nothing.
Select-String -Path App\*.cs, App\Ui\*.cs, Core\*.cs -Pattern 'SetValue', 'CreateSubKey', 'DeleteValue', 'DeleteSubKey'

# Every program start. Prints 3 lines: MainForm.cs 838 and 1014, SelfTest.cs 1323.
Select-String -Path App\*.cs, App\Ui\*.cs, Core\*.cs -Pattern 'Process\.Start', 'new Process\('
```

## Reporting a problem

If you find a security problem, please tell the author privately first, through the app's Nexus Mods page.
