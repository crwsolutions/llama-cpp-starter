# Plan: Modellen — GGUF-metadata, capabilities, deterministische IDs, Default-profielen, hernoemen "Laadprofielen"

- Status: Executed (2026-08-18; implementatie compleet in werkboom, build 0/0, console-checks all passed; NIET gecommit per gebruikerwens)
- Plan file: `.alta/plans/2026-08-18-model-metadata-capabilities.md`
- Created: 2026-08-18
- Task: Gescande modellen krijgen GGUF-metadata (JSON-blob) + capability-summary (11 velden, DB-caché); Modelbestanden zijn single-selectbaar → Laadprofielen (hernoemd uit "Opgeslagen modelvarianten") + metadata-tekenreeks tonen, Default-profiel altijd geselecteerd; Default is niet te hernoemen/verwijderen; vision-launch-settings alleen bij `LikelyVision`.
- Git: `.alta/plans/` is niet geïignoreerd → commit dit planfile mét de implementatie (geen commit in Plan-mode; Default-agent commit, per gebruiker expliciet verzoek "commit niets" geldig tot goedkeuring). `docs/prototype-schermen.txt` blijft onaangetast.

## Objective
1. **GGUF-metadata-lezer** (binair, alleen-lezen, geen dependency) + metadata per model als JSON-blob in de DB.
2. **Scannen & registreren**: deterministic Id (safe-path-prefix + SHA256), companion-uitsluiting (projector/draft/MTP), metadata-JSON met exact de gespecificeerde velden, Default-profiel seeden met app-globale defaults.
3. **Capability-detectie** (11 velden) per selectie, gecachet als JSON-blob bij het model in de DB.
4. **UI**: Modelbestanden single-select (max 1); bij selectie: Laadprofielen + metadata-chips tonen, Default-profiel geselecteerd. Hernoem "Opgeslagen modelvarianten" → "Laadprofielen".
5. **Gevolgswerk**: vision-gating in settings-paneel, rope/prompt-cache velden, `--spec-draft-model` auto-resolutie (embedded MTP inbegrepen).

**Non-goals** (bewust uitgesloten): per-model `model.json`-import, nieuwe dependencies, live metrics, Instellingen-content, test-project, `MmprojPath`-tabelveld vervangen door de nieuwe projector-search (auto-mmproj-koppeling bij scan blijft).

## Context and evidence
- Huidige scanner (`Services/ModelScannerService.cs`): `Directory.EnumerateFiles(dir, "*.gguf", AllDirectories)` zonder reparse-point-skip; alleen `mmproj`-bestanden uitgesloten; `Model.Id = int` (DB PK); quant-regex `\b(IQ\d+_[A-Z0-9_]+|...)\b` (niet de gespecificeerde vorm); upsert op `Path`; verdwenen bestanden verwijderen binnen gescande map.
- Huidige `Models`-tabel (`Repositories/Database.cs`, user_version 1): `Id INTEGER PK, Path TEXT UNIQUE, Name, Quant, SizeBytes, MmprojPath, ScannedAt`. Migratie via `PRAGMA user_version` (alleen voorwaarts).
- `ModelsViewModel.cs`: modeltabel heeft **geen** ItemSelectionMode → selectie werkt niet; `LoadProfilesAsync` seedt Default alleen met `new ProfileParameters()` (leeg, `Jinja=true`); Default IS al bij voorkeur geselecteerd (`profiles.FirstOrDefault(p => p.IsDefault)`), maar alleen nadat `OnSelectedModelChanged` fired is — nu nergens getriggerd.
- `ModelsPage.xaml`: panel "Opgeslagen modelvarianten" (rij 116-183, linkerkolom); rechterkolom = "Startinstellingen"-paneel met `SelectedProfile.Name`-Entry als eerste regel (rij 192); Vision-sectie (rij 354-378) altijd zichtbaar.
- `OverviewPage.xaml` (rij 172-196): dezelfde titel "Opgeslagen modelvarianten" + "Add" → `PendingNewProfileModelId` → Modellen-scherm.
- `LlamaServerCommandBuilder.cs`: pure static; referentie-opdracht; `--mmproj` via `p.GetEffectiveMmproj(model)` (profiel-override → anders `model.MmprojPath`). `--spec-draft-model` ontbreekt.
- `ProfileParameters.cs`: nullable velden; `Jinja` default `true`; JSON-blob pattern `ToJson/FromJson/TryParse`. RoPE/prompt-cache velden ontbreken.
- Referentieproject `E:\repos\llama-cpp-windows-manager\src\LocalLlmConsole.App\Services\Models\`:
  - `GgufMetadataReader.cs` — werkende lezer (magic "GGUF", version 1-3, type-tags 0-12, arrays als samenvatting, limieten 1 MiB string / 100.000 array-elementen / 64 MiB blok / 512 keys, case-insensitief, elke fout → leeg dictionary).
  - `ModelCatalogService.cs` — `FindModelFiles` (`EnumerationOptions { RecurseSubdirectories, IgnoreInaccessible, AttributesToSkip = System|ReparsePoint }`), `IsModelGguf` (uitsluit projector/draft-namen + standalone-spec architectuur via GGUF-lezing), `ModelIdForPath` (relatief pad → `SafeId` → prefix 86 + `-{8-hex SHA256(lowercase fullpath)}`), `FriendlyName` (`_`→`-`, per deel PascalCase), `InferQuant` (regex `(?:^|[-_.])(iq\d_[a-z0-9]+|q\d(?:_[a-z-9]+)+|f16|bf16|f32)(?:[-_.]|$)` → uppercase), `MergeGgufManifest` (exakt de 5 `gguf*`-velden + context-only-als->0).
  - `ModelCatalogService.Companions.cs` — `FindVisionProjectors` (zelfde map, name-markers, `LooksCompatibleWithMainModel` family-versie + parametergrootte, `f16` eerst), `FindDraftModels` + `ClassifySpeculativeCompanion` (DSpark/DFlash/Eagle3/Mtp/DraftModel), `HasEmbeddedDraftMtp` (`*.nextn_predict_layers > 0` in hoofdmiddel), `MatchesSpeculativeType` + prioriteit Mtp<DSpark<DFlash<Eagle3<DraftModel, `ResolveDraftModelPath` (configured pad wint; draft-mtp + embedded → null).
  - `ModelCapabilityService.cs` — `ModelCapabilitySummary` (11 velden), `Inspect`, `SummaryText` (chips, gescheiden door `"  |  "`), `HuggingFaceHintsFromMetadata` (CapabilityHints/pipelineTag/Tags/HasVisionProjector + ruwe-tekstscan), `ContextLength`, `TryInt`.
  - `ModelCapabilityCacheService.cs` — in-geheugen caché op fingerprint (path|size|lastwrite|projector…). → In deze app vervangen door **DB-blob** (per gebruiker expliciet: "opslaan in de database bij het model in een json-blob").
- App-globale defaults bestaan nog niet: `SettingsViewModel` is placeholder, `AppSettings` heeft alleen `ModelsDirectory`/`RuntimeDirectory`.

## Assumptions and open decisions
1. **Waarde van de app-globale defaults** (OPGELOST 2026-08-18, gebruiker): de **referentie-opdracht** uit het core-plan als `ProfileParameters`-JSON, **incl. spec-type + vision-waarden**: `CtxSize=192144`, `SplitMode="layer"`, `Ngl="999"`, `BatchSize=256`, `UbatchSize=256`, `Threads=8`, `Temperature=1.0`, `TopP=0.95`, `TopK=20`, `MinP=0.00`, `FlashAttn="on"`, `TensorSplit="24,8"`, `CacheTypeK="q8_0"`, `CacheTypeV="q8_0"`, `Parallel=1`, `PresencePenalty=0.0`, `RepeatPenalty=1.0`, `Jinja=true`, `Keep=1024`, `CtxCheckpoints=128`, `SpecType="draft-mtp"`, `SpecDraftNMax=4`, `ImageMinTokens=1024`. Deze JSON wordt opgeslagen als `AppSettings`-rij `GlobalLaunchDefaults` en is de exacte seed voor elk nieuw Default-profiel.
2. **Opslagplek defaults** (aanneming, laag risico): één `AppSettings`-rij `GlobalLaunchDefaults` (JSON-blob van `ProfileParameters`), ge-seeded bij migratie 1→2 met `INSERT OR IGNORE`; Default-profielen worden gevuld vanuit deze rij. Nieuwe, niet-Default profielen blijven leeg (bestaand gedrag).
3. **`Model.Id` wordt string-PK** (aanneming): deterministic id per spec (safe-prefix 86 + `-{8hex}`); DB-migratie voegt `ModelId TEXT UNIQUE NOT NULL` toe (backfill uit `Path`, oude int `Id` blijft als interne surrogate-PK). `Profile.ModelId` is al int → geen cascade-pains. Alternatief (alleen nieuwe kolom, int-PK behouden voor bindingen) is minder spec-compatibel; ik kies voor `ModelId`-kolom + alle query's/bindings schakelen op `ModelId` waar determinisme telt (profiel-seeding, capability-blob).
4. **"Modelbestanden selecteren (max 1)"** (OPEN — review-vraag): geïnterpreteerd als `ItemSelectionMode="Single"` op de modellijst (ModelsPage én OverviewPage). Alternatief: `SelectionMode="Multiple"` met guard "max 1" — vermelden indien dat de bedoeling is.
5. **Metadata-JSON-blob** bevat exact de 9 gespecificeerde velden; de ruwe GGUF-kv's zitten NIET in de blob (worden per selectie herlezen uit het bestand + capability-blob). HF-hint-velden (CapabilityHints etc.) ontbreken in de blob van de starter (geen downloads) → de hint-parser blijft er wél (defensief, afwezig = geen hints).
6. **Rope/prompt-cache**: veldenset = `RopeScaling` (picker none/linear/yarn), `RopeScale (int?)`, `RopeFreqBase (int?)`, `RopeFreqScale (int?)`, `CachePrompt (bool?)` — in `ProfileParameters` + command builder + "Prestaties & Geheugen"-sectie. (Deze velden staan in `docs/llama-server-help.txt` maar niet in het originele referentie-opdracht-ensemble; toevoegen omdat de instruction ze noemt.)
7. **Draft-resolutie in de command**: `--spec-draft-model` wordt op het moment van **Laden** (Overzicht `LoadAsync`) geresolvieerd via `ModelCompanionService.ResolveDraftModelPath(model, p.SpecType, configured: p.SpecDraftPath)`; embedded MTP (`draft-mtp` + `nextn_predict_layers > 0` in hoofdmiddel) → geen `--spec-draft-model`. Nieuw veld `SpecDraftPath (string?)` (override, null = auto).
8. **Vision-gating**: Vision-sectie in het Startinstellingen-paneel is zichtbaar ⇔ `SelectedModelCapability?.LikelyVision == true` (niet geselecteerd model → sectie verborgen).
9. **Naam**: `FriendlyName` (PascalCase, spatie-gescheiden) vervangt de ruwe bestandsnaam in `Model.Name` bij (re-)scan; bestaande rijen worden bij de volgende scan bijgewerkt (upsert).
10. **HasMtp toegevoegd (2026-08-20, na afloop; gebruiker)**: `ModelCapabilitySummary` is 11 → 12 velden (HasMtp, nextn-detectie in `Inspect` + "MTP"-chip in `SummaryText`). Geen cache-format-migratie: gebruiker recreate de database. AGENTS.md + code-comment zijn aangepast (12 velden).
11. **MTP-conditionele Default-seeding (2026-08-20; gebruiker)**: scanner seedt modellen **zonder MTP** met `GlobalLaunchDefaults` minus `SpecType`/`SpecDraftNMax` (`Clone()` + null in de seed; geen mutatie van de gedeelde defaults-instance). De `GlobalLaunchDefaults`-rij zelf blijft de exacte referentie-opdracht (MTP-modellen houden `draft-mtp`/`4`). Detectie = `ModelCapabilityService.HasMtp(metadata, nameText)` (public static, zelfde regels als de chip).

## Design notes

### Nieuwe services/models
```
Services/GgufMetadataReader.cs      — port van het referentieproject (static TryRead(path) →
                                      case-insensitief Dictionary<string,object?>; elke fout → leeg).
Services/ModelCompanionService.cs   — pure static (port Companions.cs):
                                      LooksLikeVisionProjectorName / LooksLikeDraftOrMtpHeadName /
                                      HasStandaloneSpeculativeArchitecture / FindVisionProjectors /
                                      FindDraftModels / ClassifySpeculativeCompanion /
                                      ResolveVisionProjectorPath / ResolveDraftModelPath /
                                      HasEmbeddedDraftMtp / FamilyVersion / ParameterSize.
Services/ModelCapabilityService.cs  — ModelCapabilitySummary (record, 11 velden, exacte volgorde per
                                      spec), Inspect(Model) (GGUF-lezen + HF-hints uit blob +
                                      companion-scan), SummaryText (chips per spec, "  |  "-scheiding),
                                      CacheKey/Fingerprint (path + lastwrite + size + projector-pad
                                      + projector-lastwrite), TryReadCached(Model) (blob-decode +
                                      fingerprint-check).
Models/Model.cs                     — + string ModelId (deterministisch), + MetadataJson,
                                       + CapabilitiesJson (null = nog nooit geïnspecteerd).
```
`ModelCapabilityService.Inspect` leest het GGUF-bestand zelf (spec: "per selectie, niet bij scan"); de scan slaat alleen metadata-JSON op.

### DB (migratie 1→2 in `Database.cs`)
- `ALTER TABLE Models ADD COLUMN ModelId TEXT NOT NULL DEFAULT ''` + backfill (per rij: deterministisch uit `Path`) + `CREATE UNIQUE INDEX IF NOT EXISTS IX_Models_ModelId ON Models(ModelId)`.
- `ALTER TABLE Models ADD COLUMN MetadataJson TEXT`; `ALTER TABLE Models ADD COLUMN CapabilitiesJson TEXT`.
- Seed `AppSettings.GlobalLaunchDefaults` (`INSERT OR IGNORE`, JSON van `ProfileParameters` met de globale defaults — exacte waarden zie opgeloste decision 1).
- `CurrentUserVersion = 2`. (Nieuwe DB's gaan via `CreateDatabase` → `CreateCoreTables` + beide seeds; `CreateCoreTables` krijgt de drie nieuwe kolommen in de CREATE.)

### Scanner (`ModelScannerService.cs`)
- `EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = System | ReparsePoint }`; `IsModelGguf` = `.gguf` + géén projector-naam + géén draft/MTP-naam + géén standalone-spec-architectuur (GGUF-lezen per bestand, `GgufMetadataReader.TryRead`).
- Per bestand: `ModelId` deterministisch (prefix uit **relatief pad t.o.v. models-root**, extensie eraf, `[^a-z0-9._-]`→`-`, max 86 + `-{8hex SHA256(lowercase full path)}`); `Name` = `FriendlyName`; `Quant` = `InferQuant` (fallback `general.file_type`, anders "unknown"); `MetadataJson` = exact 9 velden (`sourceFolder`, `modelFile`, `quant`, `registeredAt` (unix s), `ggufMetadataAvailable`, `ggufArchitecture` (of "unknown"), `ggufQuantization` (of "unknown"), `ggufContextLength` (alleen > 0), `ggufHasChatTemplate`); `MmprojPath` = bestaande auto-koppeling (eerste `*mmproj*` in dezelfde map) — onveranderd.
- Upsert: `ON CONFLICT (Path)` update incl. `MetadataJson`, `Quant`, `Name`, `ModelId` (ModelId verandert alleen als path verandert → conflict-free). Verdwenen-bestanden-cleanup: onveranderd (prefix op gescande map).
- **Default-profile-seeding centraliseren**: scanner (of VM? → scanner, na upsert) vult voor elk model zonder `IsDefault`-profiel een Default aan met `ParamsJson` = `GlobalLaunchDefaults`-JSON uit `AppSettings` (fallback leeg profiel). De dubbele seeding in `ModelsViewModel.LoadProfilesAsync`/`ScanModelsAsync` vervalt.

### Command builder
- Nieuwe vlaggen (naast referentie-volgorde; nieuwe vlaggen achteraan hun sectie):
  - `--rope-scaling {none,linear,yarn}`, `--rope-scale N`, `--rope-freq-base N`, `--rope-freq-scale N` (na `--cache-type-v`).
  - `--cache-prompt`/`--no-cache-prompt` (na `--ctx-checkpoints`).
  - `--spec-draft-model FNAME` (na `--spec-draft-n-max`; alleen indien pad niet-witruimte).
- `LlamaServerProcessService.LoadAsync` krijgt `Model` al mee → daar `ModelCompanionService.ResolveDraftModelPath` roepen en resultaat als extra argument doorgeven (signature: `BuildArgs(runtime, model, parameters, port, draftModelPath)`) — `ModelsViewModel.UpdateCommandPreview` roept dezelfde resolutie (pure static) voor de preview.

### UI — ModelsPage
- Modelbestanden-CollectionView: `ItemSelectionMode="Single"` + `SelectedItem="{Binding SelectedModel}"` (maakt de huidige "niet-selecteerbaar"-bug weg; `OnSelectedModelChanged` doet dan wat het nu al moet: `LoadProfilesAsync`).
- `OnSelectedModelChanged`: profielen laden (Default-geselecteerd bij afwezigheid van vorige selectie) + `LoadCapabilityAsync(model)`:
  - capability-blob lezen → fingerprint-check → bij miss/stale: `Task.Run(() => Inspect(model))` → `SummaryText` bouwen → blob + summary opslaan (`ModelRepository.UpdateCapabilityAsync(modelId, capabilitiesJson, summaryText)` — `SelectedModelCapabilitySummaryText` + `SelectedModelHasVision` observable's in VM; blob-update via repo).
- Rechterkolom (Startinstellingen), boven de `SelectedProfile.Name`-regel: read-only Label `Text="{Binding SelectedModelCapabilitySummaryText}"` + caption "Metadata" (`SectionTitle`-stijl, fontsize 13); `IsVisible` op `SelectedModelCapabilitySummaryText`.
- Vision-sectie: `IsVisible="{Binding SelectedModelHasVision, Converter=...BoolToVisibilityConverter}"`.
- Default-bescherming: `SaveProfileAsync` blokkeert hernoemen (naam wijzigen van `IsDefault`-profiel → StatusText "Het Default-profiel kan niet worden gehernoemd."), `DeleteProfileAsync` blijft geblokkeerd; XAML: naam-Entry `IsEnabled="{Binding SelectedProfile.IsDefault, Converter={StaticResource InvertedBoolConverter}}"` + "Verwijderen"-knop blijft disabled op Default.
- Hernoem panel-titel "Opgeslagen modelvarianten" → "Laadprofielen" (ook de comment-regel + subtitle bijstellen: "Laadbare varianten van startinstellingen." — NL, klein).

### UI — OverviewPage
- Titels "Opgeslagen modelvarianten" → "Laadprofielen" (+ comment).
- Modelbestanden-lijst: zelfde single-select toevoegen (consistent; laag risico).
- `LoadAsync`: `draftModelPath` resolutie toevoegen (zie command-builder).

### Registraties (`MauiProgram.cs`)
- `ModelScannerService` blijft singleton; geen nieuwe singleton-services nodig (`GgufMetadataReader`/`ModelCompanionService`/`ModelCapabilityService` zijn pure static).

## Risks and challenges
- **Uitsluiting kan echte modellen verbergen**: een model genaamd bv. `myspec-model.gguf` verdwijnt uit de catalogus (per spec). Acceptabel per instruction; vermelden in StatusText na scan ("X bestanden overgeslagen (companion)").
- **Scan-tijd**: standalone-spec-architectuur-check leest elk .gguf-bestand (alleen metadata-kop, ~ms); OK.
- **String-vs-int ModelId**: alle bestaande SQL-queries en `ModelRepository`-calls moeten meebewegen (backfill + uniek index; Dapper-mapping `ModelId` string). `Profile.ModelId` int blijft → geen cascade-risk.
- **Capability-blob stale**: fingerprint op lastwrite/size + projector; file-system edge-cases (netwerkdrive, time-sync) → stale summary mogeel; mitigatie: selectie herleidt bij mismatch en "Modellenmap scannen" verversen.
- **Geen test-project**: GGUF-lezer/companion-detectie zijn pure static → handmatige verificatie via tijdelijk console-project (patroon uit core-plan); daarna verwijderen.
- **`--spec-draft-model` in preview**: preview moet dezelfde resolutie tonen als de echte load (zelfde pure static call).
- **Bestaande DB (user_version 1)**: `ALTER TABLE` + backfill moeten idempotent en niet-destructief zijn; `INSERT OR IGNORE` voor seeds.

## Implementation checklist
> Volgorde = laag → hoog; elke stap compleet + compile-clean.

### Fase 1 — fundament (lezer + companions + capability-service)
- [x] `Services/GgufMetadataReader.cs`: port van het referentieproject (namespace `LlamaCppStarterApp.Services`); zelfde limieten; `TryRead(string path)` → `IReadOnlyDictionary<string, object?>` (case-insensitief), elke fout → leeg dictionary.
- [x] `Services/ModelCompanionService.cs`: pure static port (Companions.cs): name-markers, `FamilyVersion`, `ParameterSize`, `FindVisionProjectors`, `FindDraftModels`, `ClassifySpeculativeCompanion`, `MatchesSpeculativeType`, `ResolveVisionProjectorPath`, `ResolveDraftModelPath`, `HasEmbeddedDraftMtp`, `HasStandaloneSpeculativeArchitecture`, `CandidateCompanions` (+ `ModelIdForPath`/`FriendlyName`/`InferQuant`/`SafeId`/`ShortHash`).
- [x] `Services/ModelCapabilityService.cs`: `ModelCapabilitySummary` record (11 velden, exacte naam/volgorde per spec), `Inspect(Model)`, `SummaryText` (chips per spec; scheiding `"  |  "`), `HuggingFaceHintsFromMetadata` (defensief), `ContextLength`, `TryInt`, `Fingerprint(Model)` (path|size|lastwrite|projector-pad|projector-size|projector-lastwrite), `TryReadCached(Model)` + `BuildCacheJson` (blob: fingerprint + summary + summaryText).

### Fase 2 — model & database
- [x] `Models/Model.cs`: + `string ModelId`, + `string MetadataJson`, + `string? CapabilitiesJson`.
- [x] `Repositories/Database.cs`: migratie 1→2 (`ModelId TEXT NOT NULL DEFAULT ''` + backfill + uniek index; `MetadataJson TEXT`; `CapabilitiesJson TEXT`; seed `AppSettings.GlobalLaunchDefaults`); `CurrentUserVersion = 2`; `CreateCoreTables` aangevuld voor nieuwe DB's. AFWIJKING (bugfix): pre-v1 DB's gaan 0→2 in één stap (CreateCoreTables maakt direct het v2-schema), anders zou `ALTER TABLE` dubbel draaien op een v2-schema.
- [x] `Repositories/IModelRepository.cs` + `ModelRepository.cs`: queries `ModelId`/`MetadataJson`/`CapabilitiesJson` selecteren; `UpsertManyAsync` upsert op `Path` met nieuwe kolommen; + `UpdateCapabilityAsync(string modelId, string? capabilitiesJson)`. `GetByModelIdAsync` niet toegevoegd (geen gebruik; smallest change).
- [x] `Services/ModelScannerService.cs`: `EnumerationOptions` (reparse/skip), `IsModelGguf` (3 uitsluitingen, GGUF-lezen voor standalone-spec), `ModelIdForPath` (relatief t.o.v. root, `SafeId`, prefix 86 + 8hex SHA256), `FriendlyName`, `InferQuant` (nieuwe regex + `general.file_type`-fallback), `BuildMetadataJson` (exact 9 velden), Default-profile-seeding vanuit `AppSettings.GlobalLaunchDefaults` (na upsert; dubbele seeding in VM's verwijderen). AFWIJKINGEN (bugfix): seeding draait op de DB-rijen na `GetAllAsync` (lokale upsert-list heeft nog geen autoincrement Id) en alleen binnen de gescande map; lege/ontbrekende GlobalLaunchDefaults-rij → fallback op `ProfileParameters.GlobalLaunchDefaults` (niet leeg profiel).

### Fase 3 — command + parameters
- [x] `Models/ProfileParameters.cs`: + `RopeScaling (string?)`, `RopeScale (int?)`, `RopeFreqBase (int?)`, `RopeFreqScale (int?)`, `CachePrompt (bool?)`, `SpecDraftPath (string?)`; optielijsten `RopeScalingValues`/`RopeScalingOptions`; + statische `GlobalLaunchDefaults`/`GlobalLaunchDefaultsJson()` (exakte referentie-opdracht, decision 1).
- [x] `Services/LlamaServerCommandBuilder.cs`: `BuildArgs(runtime, model, parameters, port, draftModelPath)`: `--rope-*` (na `--cache-type-v`), `--cache-prompt`/`--no-cache-prompt` (na `--ctx-checkpoints`), `--spec-draft-model` (na `--spec-draft-n-max`; gequote).
- [x] `Services/LlamaServerProcessService.cs` (`LoadAsync`) + `ViewModels/ModelsViewModel.cs` (`UpdateCommandPreview`): `ModelCompanionService.ResolveDraftModelPath(model.Path, p.SpecType, p.SpecDraftPath)` resolutie doorgeven (zelfde pure static call → preview = echte load).
- [x] `ViewModels/ModelsViewModel.cs`: Default-bescherming hernoemen (`SaveProfileAsync` + originele-naam-tracking), `SelectedModelCapabilitySummaryText` + `SelectedModelHasVision` observable's, `LoadCapabilityAsync` (blob → fingerprint → miss? `Task.Run(Inspect)` → opslaan via `UpdateCapabilityAsync`), `ScanModelsAsync` vereenvoudigen (seeding nu in scanner; StatusText noemt aantal overgeslagen companions).

### Fase 4 — UI
- [x] `Views/ModelsPage.xaml`: Modelbestanden-CollectionView `SelectionMode="Single"` (MAUI 10 heeft géén `ItemSelectionMode`) + `SelectedItem="{Binding SelectedModel}"`; metadata-label (caption "Metadata" + summary-tekst, `CharacterWrap`, `StringToVisibilityConverter`) boven de naam-regel in rechterkolom; Vision-sectie `IsVisible` op `SelectedModelHasVision`; naam-Entry disabled op Default; titel → "Laadprofielen" (+ comment/subtitle "Laadbare varianten van startinstellingen.").
- [x] `Views/OverviewPage.xaml`: titel → "Laadprofielen" (+ subtitle); modellijst single-select.
- [x] `MauiProgram.cs`: registraties gecheckt; geen nieuwe singletons (alle drie services pure static). `Converters/Converters.xaml` + `VisibilityConverters.cs`: + `StringToVisibilityConverter`.
- [x] `AGENTS.md`: kernmechanismen-blok uitbreiden (metadata-lezer, capability-caché in DB, companion-detectie, `GlobalLaunchDefaults`-seed); `Models`/`Services`/`Repositories`-lijst bijwerken.

### Fase 5 — afwerking
- [x] `README.md` kort bijwerken (wat de app nu doet: metadata + capabilities + Laadprofielen).
- [x] Self-review: `git diff` alleen verwachte bestanden; `docs/prototype-schermen.txt` ongewijzigd. Bugs gevonden/verholpen in self-review: (1) pre-v1 migratie zou 0→1 én 1→2 draaien (nu 0→2 één stap), (2) scanner-seeding op Id=0-rijen (nu DB-rijen), (3) lege GlobalLaunchDefaults → leeg profiel in plaats van fallback (nu fallback).

## Verification checklist
- [x] `dotnet build src/LlamaCppStarterApp -f net10.0-windows10.0.19041.0` → 0 warnings / 0 errors (2026-08-18; na alle fasen).
- [x] Tijdelijk console-project (bronnen gelinkt, daarna verwijderen; `.tmp/gguf-check`, **verwijderd**): ALL CHECKS PASSED (~60 checks):
  - [x] Synthetische GGUF (magic+version 1-3, enkele kv's incl. array + string) → lezer levert juiste dict; corrupt/te-groot/buffer-kort → leeg dict, geen exception.
  - [x] `InferQuant`: `Q4_K_M`, `IQ2_XXS`, `F16`, `BF16`, `f32`, en negatieve controles (bv. `random-quantized` → "").
  - [x] `ModelIdForPath`: deterministic, ≤ 86+9 tekens, alleen `[a-z0-9._-]`.
  - [x] `FriendlyName`: `my-model_q4_k_m` → `My Model Q4 K M`.
  - [x] Companion-classificatie: projector/draft/MTP-naam + family/size-match (positief + negatief), embedded MTP (`nextn_predict_layers > 0`), `ResolveDraftModelPath` per spec-type + embedded.
  - [x] `SummaryText`-chips per spec (alle velden aan/uit, incl. "GGUF metadata: unavailable" en "Vision: likely, projector not found").
  - [x] Command-builder: referentie-opdracht blijft exact (MATCH-check) + nieuwe vlaggen op de juiste plek; `--spec-draft-model` alleen indien pad (gequote); embedded MTP → geen pad.
  - [x] Metadata-JSON-blob: exact 9 velden; `ggufContextLength` afwezig bij 0; corrupte GGUF → `ggufMetadataAvailable=false` + "unknown"-velden.
  - [x] Capability-cache-blob: roundtrip + stale na bestandswijziging + lege/corrupte blob → miss (geen exception).
  - [x] `GlobalLaunchDefaults`: referentie-waarden exact (incl. SpecType=draft-mtp, SpecDraftNMax=4, ImageMinTokens=1024).
- [ ] Handmatig (Windows, gebruiker):
  - [ ] Modellen → scan: modellen verschijnen met PascalCase-naam, quant uit bestandsnaam; projector/draft/MTP-bestanden AFWEZIG van de lijst (StatusText noemt aantal overgeslagen).
  - [ ] Model selecteren → Laadprofielen-paneel + metadata-chips verschijnen; Default-profiel geselecteerd; naam-Entry disabled; Default niet te hernoemen/verwijderen; ander profiel wél.
  - [ ] Vision-model (met mmproj in map) → Vision-sectie zichtbaar; non-vision-model → sectie verborgen.
  - [ ] Model met embedded MTP + profiel `SpecType=draft-mtp` → command-preview toont GEEN `--spec-draft-model`; met extern `*-mtp-*.gguf` in map → pad wél.
  - [ ] Na app-herstart: capabilities uit DB-blob (geen herlezen) totdat bestand wijzigt (lastwrite) of her-scan.
  - [ ] Overzicht: "Laadprofielen"-titel; modellijst selecteerbaar; Laden met spec-type werkt (llama-server log).
- [x] `git diff --stat`: alleen verwachte bestanden + dit planfile; `docs/prototype-schermen.txt` ongewijzigd (niet committen).

## Handoff notes
- **Niet committen** totdat de gebruiker het plan heeft goedgekeurd (explicit verzoek "Commit niets"). Na goedkeuring: één commit (of per fase, bespreekbaar) incl. dit planfile; `docs/prototype-schermen.txt` blijft buiten de commit.
- `GgufMetadataReader`/`ModelCompanionService`/`ModelCapabilityService` zijn **pure static** — geen DI-registratie nodig; wel handmatig verifiëren (geen test-project).
- `ModelCapabilityService.Inspect` leest het GGUF-bestand zelf; de scan slaat alleen metadata-JSON op (spec: "per selectie, niet bij scan").
- `ProfileParameters` blijft editor-model + opslagmodel; nieuwe velden (rope/prompt-cache/SpecDraftPath) gaan in de JSON-blob (voorwaarts-compatibel, geen migratie nodig).
- `--spec-draft-model`-resolutie: `ModelCompanionService.ResolveDraftModelPath(modelPath, specType, configuredPath)`; embedded MTP (`draft-mtp` + `nextn_predict_layers > 0`) → `null` (geen flag). Configured-pad (`p.SpecDraftPath`) wint altijd.
- Bestaand patroon: repos eigen `SqliteConnection` per call; VM's `[ObservableProperty]`/`[RelayCommand]`; NL-teksten in UI; `MainThread.BeginInvokeOnMainThread` voor asynchroon → UI.
- `docs/prototype-schermen.txt` bevat user-wijzigingen: NIET touchen/committen.
