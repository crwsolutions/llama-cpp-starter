# Ctrl+C unload voor llama-server (in plaats van POST /exit)

- Status: Completed (2026-08-23; geapprove door gebruiker "ja graag" / "Switch to Default and execute"; implementatie klaar, nog niet ge-commit — gebruiker test zelf eerst)
- Plan file: `.alta/plans/2026-08-23-ctrlc-unload-llama-server.md`
- Created: 2026-08-23
- Task: stop de llama-server met een echte Ctrl+C (console `CTRL_C_EVENT`) in plaats van `POST /exit`, zowel bij Unload (Overzicht) als bij app-uitgang.
- Git: `.alta/plans/` is niet geïgnoreerd → plan hoort mee ge-commit te worden met de implementatie; gebruiker wil eerst zelf testen en **nu niet** committen.

## Objective
- Unload én app-uitgang stoppen de server met Ctrl+C; `POST /exit` verdwijnt volledig (ook géén fallback hiernaar).
- Graceful shutdown: llama.cpp registreert een eigen console-CtrlC-handler (interrupt → nette stop); de bestaande wait + `Kill(entireProcessTree)`-fallback blijven.
- Non-goals: geen nieuwe dependencies, geen UI-wijzigingen, geen wijziging van health/metrics-polling, géén commit/stage in deze iteratie.

## Context and evidence
- Alle 5 lokale `llama-server.exe`-builds (`E:\llama.cpp\…`) zijn **console-subsystem (PE subsystem 3)** — geverifieerd via PE-header-parse (kalibratie: powershell=3, notepad=2, cmd=3). Consequentie: met `CreateNoWindow = true` (standaard `CREATE_NO_WINDOW`, géén consolevenster) krijgt het kindproces wél nog zijn eigen **verborgen console** → `AttachConsole` + `GenerateConsoleCtrlEvent` komt er. `AllocConsole` is **niet nodig** (en bestaat bovendien niet op `ProcessStartInfo` in .NET 10; buildfout CS0117 bevestigde dit).
- Empirische verificatie (tijdelijk `.ctrlc-test`-project, verwijderd erna): GUI-parent (WinExe, geen eigen console) start `cmd /c ping` met `CreateNoWindow=true` + stdout-redirect → `AttachConsole` OK → `GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)` = true → kind stopt in 6 ms met exit code `-1073741510` (= 0xC000013A, `STATUS_CONTROL_C_EXIT`). Mechanisme werkt dus exact zoals geïmplementeerd.
- Oud unload-pad: `UnloadAsync()`: `POST /exit` 5 s → wacht max 30 s → `Kill(entireProcessTree)`. Oud app-uitgang-pad: `ShutdownServer()`: `POST /exit` 2 s → `WaitForExit(5 s)` → kill; aangelopen vanuit `App.xaml.cs` `OnWindowDestroying`.
- `_hostBind` + `_httpClient` + constanten `ExitPostTimeoutMs`/`ExitPostFastTimeoutMs` werden uitsluitend voor de `POST /exit` gebruikt → verwijderd.
- SO 283128 (door gebruiker meegegeven): voor een GUI-app zonder eigen console = `AttachConsole(pid)` → `SetConsoleCtrlHandler(null, true)` → `GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)` → **wachten tot het doelproces gestopt is** → `SetConsoleCtrlHandler(null, false)` + `FreeConsole()`. Zonder te wachten blijft het Ctrl+C-event in de queue en zou onze eigen app afsluiten bij herstel.
- Alternatieven uit dezelfde thread ("`\x3`" naar stdin, `StandardInput.Close()`) werken hier **niet**: de graceful stop van llama-server komt uit de console-CtrlC-handler, niet uit stdin-lezen.
- Precedent `#if WINDOWS` in `MauiProgram.cs`; csproj targett ook ios/maccatalyst → P/Invoke + console-code onder `#if WINDOWS`, met niet-Windows fallback (kill-pad; `TrySendCtrlC`/`DetachConsole` hebben daar no-op-stubs zodat de ongeconditionele call-sites compileren).
- Working tree had ongecommitte changes (KV-cache-iteratie; o.a. `OverviewViewModel.cs`, `AGENTS.md`) → daarin alleen minimaal meebewerkt (één doc-comment resp. docs), géén stage/commit.

## Assumptions and open decisions (eindstand)
- `CreateNoWindow = true` blijft ongewijzigd (standaard, alle TF's) — geen `AllocConsole`, geen hide-window-logica: het consolevenster bestaat gewoon niet, en de console wél (subsystem 3).
- Als `AttachConsole` bij stopslag mislukt: géén `POST /exit` — direct het bestaande wait + kill-pad (NL-logregel dat Ctrl+C niet kon worden gestuurd).
- Wacht-tijden ongewijzigd: 30 s (unload), 5 s (app-uitgang); exit-code wordt gewoon in het logboek gemeld.
- Open voor de gebruiker: of *llama-server zelf* een nette Ctrl-C-handler registreert (dan graceful; anders stopt het via de kill-fallback na 30/5 s). Dat is precies wat de handmatige test zal uitwijzen.
- Geen commit door de Default-agent (gebruiker test zelf eerst).

## Design notes (eindstand)
- Nieuw `#if WINDOWS` P/Invoke-blok in `LlamaServerProcessService` (+ delegate): `AttachConsole`, `FreeConsole`, `SetConsoleCtrlHandler`, `GenerateConsoleCtrlEvent`; `CtrlCEVENT = 0`. (GetConsoleWindow/ShowWindow niet nodig — geen venster om te verbergen.)
- Helpers: `TrySendCtrlC(Process)` (attach → `SetConsoleCtrlHandler(null, true)` → `GenerateConsoleCtrlEvent(0,0)` → true/false; `_consoleAttached`-flag + statische `_consoleLock` want attach is process-wide state) en `DetachConsole()` (handler-reset + `FreeConsole`, alleen indien attached).
- `UnloadAsync`: `POST /exit`-blok vervangen door `TrySendCtrlC` (NL-logregels "Ctrl+C gestuurd (PID {id})." / "[stderr] Kon geen Ctrl+C sturen …"); wait 30 s + kill ongewijzigd; `finally` = `DetachConsole()` vóór lock-release (SO-vereiste: wachten vóór detach, anders raakt het Ctrl+C-event onze eigen app).
- `ShutdownServer` (synchroon, géén async): zelfde attach+Ctrl+C; `WaitForExit(5 s)` + kill ongewijzigd; lock-timeout-tak (concurrente unload/load): best-effort `TrySendCtrlC` + 2 s-wacht vóór kill; `finally`: `DetachConsole()`.
- Conventies: NL-teksten in logmeldingen; code-comments in English.

## Risks and challenges
- `AttachConsole` kan falen (server al gestopt, console verdwenen, andere proces attached) → fallback = bestaand kill-pad; gedrag blijft robuust.
- Het Ctrl+C-event raakt ook onze eigen app zolang die attached is → gedekt door `SetConsoleCtrlHandler(null, true)` vóór generate én `DetachConsole()` pas ná de wait (SO-waarschuwing over het event in de queue).
- App-uitgang: `ShutdownServer` blokkeert de UI-thread tot ~5 s wanneer de server niet stopt — bestaand gedrag, ongewijzigd.
- Ongecommitte working-tree changes (KV-cache-iteratie) in `OverviewViewModel.cs`/`AGENTS.md` → edits minimaal gehouden om conflicten te voorkomen.

## Implementation checklist
- [x] `LlamaServerProcessService.cs`: `#if WINDOWS` P/Invoke-blok + delegate toevoegen (`AttachConsole`, `FreeConsole`, `SetConsoleCtrlHandler`, `GenerateConsoleCtrlEvent`, `CtrlCEVENT = 0`) + niet-Windows no-op-stubs.
- [x] `LlamaServerProcessService.cs`: helpers `TrySendCtrlC(Process)` / `DetachConsole()` (attached-flag + statische `_consoleLock`).
- [x] `LoadAsync`: `CreateNoWindow = true` ongewijzigd gebleken (subsystem-3-check + test); uitleg-comment toegevoegd waarom het niet mag wijzigen.
- [x] `UnloadAsync`: `POST /exit`-blok vervangen door `TrySendCtrlC` + NL-logregels; wait 30 s + kill ongewijzigd; `finally`: `DetachConsole()` vóór `_operationLock.Release()`.
- [x] `ShutdownServer`: `POST /exit`-blok vervangen door synchroon `TrySendCtrlC`; wait 5 s + kill ongewijzigd; lock-timeout-tak: best-effort Ctrl+C + 2 s-wacht vóór kill; `finally`: `DetachConsole()`.
- [x] `LlamaServerProcessService.cs`: verwijderen `_httpClient`, `_hostBind`, `ExitPostTimeoutMs`, `ExitPostFastTimeoutMs`; class-doc + XML-docs (`UnloadAsync`/`ShutdownServer`) bijgewerkt.
- [x] `OverviewViewModel.cs`: doc-comment "POST /exit" → "Ctrl+C" (één regel; geen andere changes).
- [x] `AGENTS.md` (architectuurblok + Procesbeheer) en `README.md` (Overzicht): beschrijving `POST /exit` → Ctrl-C-flow (Nederlands, beknopt).
- [x] Géén stage/commit (gebruiker test eerst).

## Verification checklist
- [x] `dotnet build src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0` → 0 warnings / 0 errors.
- [x] Mechanisme-empirisch geverifieerd (tijdelijk WinExe-testproject): GUI-parent → `CreateNoWindow`-child → AttachConsole OK → CTRL_C_EVENT → kind stopt, exit code 0xC000013A (STATUS_CONTROL_C_EXIT). Testproject daarna verwijderd.
- [ ] Handmatig (gebruiker): model laden → **Unload** → log toont "Ctrl+C gestuurd…", server stopt net (exit code in log, "Server gestopt."), géén `/exit`-verkeer.
- [ ] Handmatig (gebruiker): app sluiten met draaiende server → server stopt binnen ~5 s; `tasklist | findstr llama-server` → géén weestproces.
- [ ] Handmatig (gebruiker): géén consolevenster zichtbaar na laden (en niet na unload/uitgang).
- [x] `git status` zelfcheck: alleen de bedoelde bestanden; géén staged/committed changes.

## Handoff notes
- Gebruikersinstructie: "gewoon uitvoeren, niet eigenwijs; ik test het eerst, niet committen" → Default-agent implementeerde en bouwde, stopte daarna; **géén commit/stage**.
- Belangrijkste afwijking ten opzichte van het oorspronkelijke plan: `AllocConsole` is niet nodig (en bestaat niet) — PE-subsystem-check van de 5 builden (alle 3/console) + empirische test tonen aan dat `CreateNoWindow = true` al een attachable verborgen console oplevert. Geen hide-window-logica nodig.
- De SO-vereiste "wachten vóór `FreeConsole`" is verwerkt: `DetachConsole()` staat in `finally` ná de wait-logic.
- Bestaande ongecommitte working-tree changes (KV-cache-iteratie) niet aangetast.
- Planfile zelf hoort gecommit te worden met de implementatie (door de gebruiker, na testen).
