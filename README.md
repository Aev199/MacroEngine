# MacroEngine

MacroEngine is a tray-resident Windows automation utility for text expansion, application-aware shortcuts, leader key sequences, and reusable multi-step macros.

## Features

- text triggers with application context filters;
- direct global shortcuts and leader chords;
- dynamic tokens such as `{date}`, `{time}`, `{clipboard}`, `{input:...}`, `{choice:...}` and `{cursor}`;
- text, rich text, LISP, script, open, launch and named macro actions;
- serialized execution so keyboard, mouse and focus operations cannot interleave;
- emergency cancellation from the tray menu;
- automatic config reload and last-known-good backups.

## Requirements

- Windows 10 or Windows 11 x64;
- .NET is not required for the self-contained release artifact.

## Build

```powershell
dotnet restore MacroEngine.Tests/MacroEngine.Tests.csproj
dotnet test MacroEngine.Tests/MacroEngine.Tests.csproj -c Release
dotnet publish MacroEngine/MacroEngine.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o artifacts/win-x64
```

GitHub Actions runs the same test, build and publish sequence on `windows-latest`.

## User data

By default mutable files are stored outside the application directory:

```text
%LocalAppData%\MacroEngine\
  config\
    triggers.json
    triggers.json.bak
    macros.json
    macros.json.bak
  logs\
    macroengine.log
    macroengine.log.1
  state\
```

On the first launch after upgrading from an older portable build, existing `config\triggers.json` and `config\macros.json` beside the executable are copied to the new location if no user config exists there yet.

To keep all data beside the executable, create an empty file named `.portable` next to `MacroEngine.exe` before the first launch.

## Safe persistence and recovery

Configuration is serialized and validated before replacing the active file. Writes use a temporary file and retain one `.bak` last-known-good copy. If the main JSON file becomes invalid, MacroEngine attempts to restore the backup automatically.

A failed save is shown in the settings window. The window remains open and retains its unsaved state.

## Privacy

Normal logging does **not** record typed characters, trigger replacement values, clipboard contents, commands, or macro scripts.

Detailed keyboard diagnostics are disabled by default. They can be explicitly enabled from the tray menu under **Диагностическое логирование**. This mode can include virtual-key information, keyboard layout and active-window context, so it should only be used temporarily while troubleshooting.

Legacy `macroengine.log` files beside the executable, created by versions that logged every key, are removed during migration to the hardened storage layout.

## Macro execution

All actions are placed in a bounded queue and executed one at a time on a dedicated STA thread. Use **Остановить текущее действие** in the tray menu to cancel the running macro or script and discard pending actions.

Macro steps:

```text
type text to enter
key Ctrl+S
sleep 500
click 120,300
dclick 120,300
rclick 120,300
run "C:\Program Files\Tool\tool.exe" --argument
```

Blank lines and lines beginning with `#` are ignored.

## Development status

The project is suitable for personal beta use. Automated tests cover atomic persistence, backup recovery, command parsing and virtual-key mapping. Interactive Windows smoke testing is still recommended for keyboard hooks, tray behavior, prompts, rich-text paste and application-specific automation.
