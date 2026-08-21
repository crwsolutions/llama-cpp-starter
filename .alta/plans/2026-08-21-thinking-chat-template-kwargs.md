# Thinking-prefielveld → `--chat-template-kwargs`

- Status: Implemented (2026-08-21; approved: "Ja" / "Switch to Default and execute"; build 0 warn/err, 14/14 builder-checks PASSED; 2 interactieve verif-punten bewust niet uitgevoerd, zie Verification)
- Plan file: `.alta/plans/2026-08-21-thinking-chat-template-kwargs.md`
- Created: 2026-08-21
- Task: Nieuwe profilexclusieve Thinking-picker op ModelsPage (labels: `(default)`, `off`, `low`, `medium`, `xhigh`) die `--chat-template-kwargs` met de juiste JSON-waarde naar de llama-server-opstartopdracht vertaalt.
- Git: not ignored — commit dit planbestand samen met de implementatie.

## Objective
- Voeg een **Thinking**-sectie toe aan de parameter-sections (Startinstellingen-paneel) in `Views/ModelsPage.xaml`, met een label "Thinking" en een Picker met waarden `<default>`, `off`, `low`, `medium`, `xhigh`.
- Vertaling naar `llama-server`-argumenten:
  - `(default)` (null) → géén `--chat-template-kwargs` vlag.
  - `off` → `--chat-template-kwargs "{\"enable_thinking\": false}"`
  - `low` → `--chat-template-kwargs "{\"reasoning_effort\": \"low\"}"`
  - `medium` → `--chat-template-kwargs "{\"reasoning_effort\": \"medium\"}"`
  - `xhigh` → `--chat-template-kwargs "{\"reasoning_effort\": \"xhigh\"}"`
- Neeit/goals: géén DB-migratie (nieuwe nullable JSON-key kost geen migratie, bestaande profielen = default/géén vlag), géén verandering aan runtime-scanner/Overview, géén vrij-editbaar kwargs-veld.

## Context and evidence
- `Models/ProfileParameters.cs`: nullable string-velden + statische option-lijsten (bijv. `RopeScalingValues`/`RopeScalingOptions`), `(default)`-placeholder via `DefaultPlaceholder`; `Clone()` kopieert alle velden per veld; `IsEmpty()` controleert alle velden. Nieuwe velden zijn automatisch voorwaarts-compatibel in de JSON-blob (`WhenWritingNull`).
- `Views/ModelsPage.xaml` (lijnen ~212-479): ScrollView met Grid `RowDefinitions="Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"`; secties per Grid-row: 0 Header, 1 Basisstart, 2 Prestaties & Geheugen, 3 Speculatie/MTP, 4 Vision (conditionally visible), 5 Standaardwaarden generatie, 6 Runtime Command (leeg Grid). Pickers gebruiken `ItemsSource="{x:Static models:ProfileParameters.XxxOptions}"` + `SelectedItem="{Binding CurrentParameters.Xxx, Converter={StaticResource PickerDefaultConverter}}"` + `Style="{StaticResource FieldPicker}"`.
- `Converters/PickerDefaultConverter.cs`: null ↔ `"(default)"` — werkt ongewijzigd voor de nieuwe optielijst.
- `Services/LlamaServerCommandBuilder.cs` (`BuildArgs`, pure static): vlaggen-lijst in referentie-volgorde; `--metrics` is de huidige laatste vlag (na `--image-min-tokens`).
- `Services/LlamaServerProcessService.cs` (`LoadAsync` lijn ~151-163): gebruikt `ProcessStartInfo.ArgumentList` (geen shell) → een JSON-waarde met spaties/quotes is veilig als één `ArgumentList`-element. De logregel ("Opstarten: …") gebruikt `BuildCommandLine` (ruimtejoin) dus de quote rond de JSON-waarde is nodig voor leesbaarheid in preview + log (bestaand `Quote()`-patroon).
- `ModelsViewModel.UpdateCommandPreview` (lijn ~237-239) roept dezelfde `BuildArgs` → preview toont automatisch de nieuwe vlag.
- `docs/llama-server-help.txt` lijn 554-556: `--chat-template-kwargs STRING` = "must be a valid json object string".
- Worktree clean (geen oncommitted changes); `.alta/plans/` versiebeheerd (per AGENTS.md: plans meecommitpen).

## Assumptions and open decisions
- Aangenomen: `<default>` in de Picker = de bestaande placeholder `"(default)"` (`ProfileParameters.DefaultPlaceholder`), zodat `PickerDefaultConverter` direct bruikbaar is.
- Aangenomen: sectie "Thinking" komt na Vision en vóór "Standaardwaarden generatie" (row 5; Generation e.d. verschuiven naar row 6/7).
- Aangenomen: UI-label = "Thinking" (NL-teksten elders zijn vrij vertaald; "Thinking" is de gebruikelijke term voor reasoning-modellen).
- Aangenomen: de JSON-waarde is één `ArgumentList`-element (exacte string zonder extra quoting), en `Quote()` toegepast op die waarde zodat preview/log-leesbaarheid klopt.
- Geen open decisions.

## Design notes
- Nieuw veld `string? ThinkingLevel` op `ProfileParameters` (naam: waarde "off"/"low"/"medium"/"xhigh"; null = default). Static readonly `ThinkingValues = ["off","low","medium","xhigh"]` + `ThinkingOptions = [(default) + ThinkingValues]` in het bestaand patroon.
- JSON-mapping als `private static string?` helper in `LlamaServerCommandBuilder` (pure static; bouwt de exacte JSON-object-string per waarde; null voor default/unknown). Mapping hardcoded in de builder = één plek waar de CLI-contract leeft (conform AGENTS.md: command-constructie = pure static, handmatig verifiëren).
- Alternatief afgekeurd: mapping in `ProfileParameters` (model zou dan CLI-kennis krijgen) en vrij-text-kwargs Entry (scope van de vraag = vastkeuzepicker).
- Compatibiliteit: oude `Params`-blobs zonder key → `null` → géén vlag (zelfde gedrag als vandaag). `GlobalLaunchDefaults`/`Clone()`/`IsEmpty()` moeten het veld wel meenemen, anders verliest seeding/cloning het veld.

## Risks and challenges
- Quote/readability: JSON bevat spaties + dubbele quotes. **Afwijking t.o.v. de originele aanname (gecorrigeerd tijdens executie):** géén `Quote()` om de JSON-waarde — de process-argv moet exact de JSON-objectstring bevatten (llama.cpp parst het als JSON), en .NET quote't de `ArgumentList`-elementen zelf via CreateProcess. `Quote()` had letterlijke quote-tekens in de argv gestopt → corrupte JSON → server-startfout. De preview/log tonen de JSON zonder omhullende quotes (niet-eenduidig leesbaar, maar exact wat de server krijgt — acceptabel).
- Gecompileerde bindings: nieuwe XAML-binding `CurrentParameters.ThinkingLevel` moet na source-gen matchen (build controleert MAUIX-fouten).
- Sectie-runschuiving: alle `Grid.Row` waarden in de ScrollView-Grid (rows 5-7) én `RowDefinitions` moeten aangepast worden; over het hoofd gezien = overlapende secties (visueel direct zichtbaar bij run).

## Implementation checklist
- [x] `Models/ProfileParameters.cs`: `ThinkingValues` + `ThinkingOptions` static readonly (na `RopeScalingOptions`); `[ObservableProperty] string? ThinkingLevel` in een nieuwe `// --- Thinking ---` sectie (na Prompt cache / vóór Vision, volgorde = XAML); `ThinkingLevel = ThinkingLevel` toevoegen aan `Clone()`; `&& ThinkingLevel is null` toevoegen aan `IsEmpty()`.
- [x] `Services/LlamaServerCommandBuilder.cs`: in `BuildArgs`, na `--metrics` (laatste vlag vóór return): `if (p.ThinkingLevel is not null)` → `--chat-template-kwargs` + JSON-waarde (zie afwijking onder Risks). Nieuwe `private static string? ChatTemplateKwargsFor(string thinkingLevel)`: `off` → `{ "enable_thinking": false }`; `low`/`medium`/`xhigh` → `{ "reasoning_effort": "<value>" }`; default → null (vlag dan niet toegeven). Compacte JSON-string exact als in de opdracht.
- [x] `Views/ModelsPage.xaml`: ScrollView-Grid `RowDefinitions` → `"Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"`; nieuwe sectie "Thinking" als Grid row 5 (2 rijen: title + label/Picker, bestaand sectie-patroon: `SectionTitle`/`FieldLabel`/`FieldPicker`, `ItemsSource={x:Static models:ProfileParameters.ThinkingOptions}`, `SelectedItem={Binding CurrentParameters.ThinkingLevel, Converter={StaticResource PickerDefaultConverter}}`); huidige "Standaardwaarden generatie" row 5→6 en "Runtime Command" (leeg Grid) row 6→7.
- [x] Build + handmatige verificatie (zie Verification); zelfde review-loop als bij eerdere parameter-toevoegingen.
- [x] Commit plan + implementatie samen (`.alta/plans/` is niet geïgnoreerd).

## Verification checklist
- [x] `dotnet build src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0` → 0 warnings / 0 errors.
- [x] Handmatig `LlamaServerCommandBuilder` verifiëren (tijdelijk console-project `src/TempCommandCheck` dat de bron-bestanden linkt, daarna verwijderd — AGENTS.md-patroon): 14/14 checks PASSED — ThinkingLevel = null → géén `--chat-template-kwargs` + ongewijzigde staart `... --image-min-tokens 1024 --metrics`; `off` → exact `{"enable_thinking": false}` als laatste argument (na `--metrics`); `low`/`medium`/`xhigh` → exact `{"reasoning_effort": "<v>"}`; unknown → géén vlag; ThinkingOptions-lijst, `Clone()`, `IsEmpty()`, JSON round-trip én legacy-blob zonder key → null allemaal correct.
- [ ] `dotnet run`: ModelsPage → Thinking-picker toont `(default)/off/low/medium/xhigh`; bij selectie verschijnt de juiste vlag in de Runtime Command-preview; Opslaan → opnieuw openen profiel → selectie blijft (JSON-blob); oude profiel (zonder key) → picker op `(default)`, géén vlag. *(niet uitgevoerd: interactieve GUI-test; binding volgt exact het bestaande Picker-patroon en de build met compiled bindings is groen. Overlaat aan gebruiker.)*
- [ ] App start/stop met geladen server met `off` en `medium` → serverlog-regel toont de JSON-waarde correct quoted (proces start, `/health` running). *(niet uitgevoerd: vereist een werkende runtime + model. Residu-risico: laag — het JSON-argument gaat als één element via `ArgumentList`, dus de server ontvangt exact de JSON-string.)*

## Handoff notes
- Volgorde: eerst `ProfileParameters` (source-gen property), dan builder, dan XAML (compiled binding).
- Picker-optielijsten + `PickerDefaultConverter` + `FieldPicker`-stijl zijn bestaand; kopiëren exact het bestaand sectie-patroon (zie "Speculatie / MTP" in ModelsPage.xaml als compact voorbeeld).
- Géén migratie, géén nieuwe dependency, géén test-project.
- Na afloop: planbestand en code in één commit meenemen (conventie uit AGENTS.md).
