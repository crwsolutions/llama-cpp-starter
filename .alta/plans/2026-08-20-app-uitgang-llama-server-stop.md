# Plan: bij app-uitgang llama-server betrouwd stoppen

- Status: Implemented — klaar voor user-review (2026-08-20; **geen commit tot user-review**)

> **Deviation (2026-08-20, door bug-fix):** de oorspronkelijke `UnloadAsync(int? waitSeconds)` +
> `async void`-handler in `App.xaml.cs` werkte níet: tijdens sluiting gooide de
> `StateChanged`→`MainThread.BeginInvokeOnMainThread`-keten (via de `State = Stopping`-setter)
> `"Window was already deactivated"` en brak de sequence af vóór het kill → weestproces.
> Nieuwe aanpak: **`ShutdownServer()`** — synchroon/blokkerend op de UI-thread (POST /exit 2 s →
> 5 s wachten → `Kill(entireProcessTree)`), met `_shuttingDown` dat `StateChanged`/`LogReceived`
> onderdrukt (en `_state`/`_process` direct zet i.p.v. de events firende setters), en
> `MarkRunning()`-race-guard. `UnloadAsync()` (Overzicht, 30 s) is terug op de oorspronkelijke
> signatuur zonder parameters. `App.OnWindowDestroying` is synchroon (geen `async void`).
- Plan file: `.alta/plans/2026-08-20-app-uitgang-llama-server-stop.md`
- Created: 2026-08-20
- Task: Zorg dat de door de app gestarte `llama-server.exe` betrouwbaar stopt wanneer de app afsluit, zodat er geen weestproces in de achtergrond blijft draaien.
- Git: niet geïgnoreerd; plan-bestand hoort gecommit te worden met de implementatie, maar per gebruiker **geen commit tot review** (2026-08-20).

## Objective
- **Doel:** bij `window.Destroying` (app-sluiting) draait de draaiende `llama-server.exe` gegarandeerd af, en wel snel genoeg zodat de kill ook écht doorgaat.
- **Non-goals:** geen nieuwe unload/exit-knoppen, geen UI-wijzigingen, geen aanpassing van de Overzicht-unload (30 s, bestaand gedrag), geen multi-server-ondersteuning, geen aanpassing van de command-builder.

## Context and evidence
- `App.xaml.cs` (L18-42) haakt al op `window.Destroying` en roept `LlamaServerProcessService.UnloadAsync()` aan (`async void` met try/catch).
- `LlamaServerProcessService.UnloadAsync()` (`Services/LlamaServerProcessService.cs` L220-278):
  - best-effort `POST /exit` met HttpClient-timeout van 5 s (const `ExitPostTimeoutMs`, L32),
  - daarna max 30 s wachten op proceseindiging (const `UnloadWaitSeconds`, L31),
  - alleen bij die timeout `Kill(entireProcessTree: true)`.
- MAUI (.NET 10, `WindowsPackageType=None`) stopt de host nadat `Destroying` is afgehandeld; een `await` in een `async void`-handler die 35-40 s duurt (POST timeout + 30 s wachten) haalt dus het harde kill **niet**. Daardoor blijft `llama-server.exe` (eventueel met GPU-processen) draaien als de app wordt gesloten terwijl het model nog geladen is.
- `LlamaServerProcessService` is singleton (`MauiProgram.cs` L48) → `_process`/`_state` zijn app-breed beschikbaar; er kan op het moment van afsluiten maximaal één current server zijn.
- Bestaande unload (30 s) wordt ook geroboepen vanuit `OverviewViewModel.UnloadAsync()` (L424) — dat gedrag moet behouden blijven.

## Assumptions and open decisions
- **Aanname:** "app wordt gestopt" = gesloten via window close. Er zijn geen andere exit-paden (Taskkill/`--exit`-scenario's buiten scope).
- **Aanname:** bij afsluiting is een korte "groet" (POST /exit) + hard kill acceptabeler dan een nette 30 s-wacht; de server is een lokaal tool en data is niet persist, dus een abrupte kill is functioneel veilig.
- **Beslist (2026-08-20):** gebruiker keurde de korte exit-pad (5 s) goed.
- **Beslist (2026-08-20):** **niet committen** na implementatie — gebruiker wil de wijzigingen eerst reviewen.

## Design notes
- **Kiesbare aanpak:** toevoegen van een optioneel `waitSeconds`-argument aan `UnloadAsync` → `UnloadAsync(int? waitSeconds = null)`:
  - `null` (bestaand gedrag, Overzicht-scherm): POST /exit (5 s) → max 30 s wachten → bij timeout kill.
  - exit-pad (nieuwe aanroep vanuit `App.xaml.cs`): POST /exit met kortere client-timeout (2 s) → max 5 s wachten → daarna **altijd** `Kill(entireProcessTree: true)` (ook als het proces intussen al gestopt was, is Kill op een reeds-eindig proces een no-op; voor de zekerheid wrapped in try/catch zoals nu).
  - `RaiseLog`/state-updates (`Stopping` → `Idle`, `Session = null`) blijven dezelfde; log-regels tijdens afsluiting zijn goedkoop maar niet kritiek.
  - De POST /exit blijft eruit (niet weghalen): kost 0 s als de server snel antwoord, en geeft de server de kans om zelf af te sluiten voordat we killen.
- **Afgewezen alternatieven:**
  - `App.OnExit` in plaats van `window.Destroying`: `OnExit` kan in MAUI-afsluitflow later/onderbroken worden; `Destroying` is het vroegere en betrouwbare hook-punt.
  - `Process.Kill(entireProcessTree)` direct in `App.xaml.cs` zonder de service: omzeilt service-state, levert dubbele kill-logica, en mist de `Session = null`-cleanup.
  - 30 s-wacht behouden tijdens afsluiting: werkt niet, MAUI haalt de process-afsluiting niet op.
  - Environment-exit hook / `App.OnExit` + timer: complexer zonder voordeel.
- **Compatibiliteit/security:** geen DB-migratie, geen nieuwe dependency, geen API-klank in XAML; `UnloadAsync()` zonder argument blijft bit-voor-bit hetzelfde gedrag → bestaande tests/scenario's blijven groen.

## Risks and challenges
- **Race `Destroying` vs. draaiende `LoadAsync`/`UnloadAsync`:** het `SemaphoreSlim _operationLock` voorkomt dubbele kills; de exit-pad moet wel **geen** `WaitAsync(0)`-early-out gebruiken (die bestaat alleen in `LoadAsync`), maar mag wél op het lock wachten (max ~5-7 s, acceptabel).
- **App stopt tijdens POST/kill:** als de host tussentijds afbreekt, kan het proces blijven draaien — restant-risico, maar dan is de kill al onderweg; het 99% scenario (user kiest een draaiende server) wordt wel gedekt.
- **Kill tijdens GPU-vrijgeven:** `Kill(entireProcessTree)` is abrupt; `nvidia-smi`-processen van de server verdwijnen mee. Geen data-verlies verwacht (server is stateless t.o.v. de DB).
- **Log-regels na afsluiting:** `RaiseLog` in `async void`-context na window-close kan in theorie een `InvalidOperation` geven indien het UI-event al is losgekoppeld → daarom blijft de try/catch in `OnWindowDestroying` (bestaand) en blijft `RaiseLog` best-effort.

## Implementation checklist
- [x] `Services/LlamaServerProcessService.cs`: `UnloadAsync()` → `UnloadAsync(int? waitSeconds = null)`:
  - lokale const/variabelen: `var postTimeoutMs = waitSeconds is null ? ExitPostTimeoutMs : 2000;` en `var waitS = waitSeconds ?? UnloadWaitSeconds;`
  - HttpClient-timeout per call instellen via `CancellationTokenSource(postTimeoutMs)` rond de POST i.p.v. de globale `Timeout` (of globale timeout laag houden; kies simpelste variant die 0 warnings geeft),
  - na het wachten: als `waitSeconds is not null` → altijd `process.Kill(entireProcessTree: true)` + `WaitForExitAsync()` in try/catch; else bestaande timeout-only-kill,
  - `RaiseLog`-teksten aanpassen waar nodig (bijv. "Server gestopt (app-uitgang)." vs. "Server gestopt.").
- [x] `App.xaml.cs` `OnWindowDestroying`: `_processService.UnloadAsync(waitSeconds: 5)` aanroepen (exit-pad).
- [x] `AGENTS.md` "Procesbeheer"-bullets bijwerken: noem de exit-pad variant (`waitSeconds: 5`, altijd kill) naast de bestaande 30 s-unload.
- [ ] **NIET committen** (per gebruiker: eerst review); laat de worktree dirty met de wijzigingen + dit plan-bestand.

## Verification checklist
- [x] `dotnet build src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0` → 0 warnings / 0 errors.
- [ ] *(over aan gebruiker)* Handmatig (Windows): app starten, model laden (Overzicht → L), server `Running` (health 200), app sluiten via X → `tasklist | findstr llama-server` leeg, eventueel `nvidia-smi` zonder overgebleven `llama-server`-PID.
- [ ] *(over aan gebruiker)* Handmatig: app sluiten tijdens `Starting` (model nog aan het laden) → geen overgebleven proces.
- [ ] *(over aan gebruiker)* Handmatig: unload via Overzicht-scherm (bestaand 30 s-pad) → gedrag ongewijzigd (POST /exit → tot 30 s → kill).
- [x] `git diff --stat` toont alleen de 2 code-bestanden + AGENTS.md (+ ontracked plan-file).

## Handoff notes
- Geen test-project aanwezig (bewust, per gebruiker) → verificatie = build + handmatig procescheck.
- `LlamaServerProcessService` is pure process-beheer; hou de bestaande log-stijl (NL-teksten) en de `SemaphoreSlim`-semantiek aan.
- Let op: `Process.Kill` mag pas na `WaitForExitAsync`-attempt; double-kill van een al-eindig proces is safe maar mag niet uit een race met de wait-loop (L185-203) komen → het `finally`/`finally-release`-patroon van `UnloadAsync` aanhouden.
- **Geen commit:** gebruiker wil de wijzigingen eerst zelf reviewen. Laagst niveau van `git status`: alleen de verwachte bestanden (2 code-bestanden + AGENTS.md + plan-file), en meld dat.
