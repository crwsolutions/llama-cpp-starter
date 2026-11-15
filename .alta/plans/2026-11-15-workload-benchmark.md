# Plan: Workload Benchmark voor llama.cpp Starter

## Doel

Voeg een Benchmark-pagina toe aan de bestaande .NET MAUI-app. De benchmark vergelijkt llama.cpp-modellen, quantisaties, MTP-instellingen en launchprofielen op realistische coding-agent-workloads. Dit is een inference-performancebenchmark, geen codekwaliteitsbenchmark.

Primaire metingen zijn promptverwerkingssnelheid (PP t/s) en tokengeneratiesnelheid (TG t/s). Daarnaast worden toolcalls en mislukte toolcalls geteld. Er wordt geen semantische kwaliteit van modeluitvoer beoordeeld.

## Context en bestaande architectuur

- De Windows-gerichte MAUI-app gebruikt MVVM met CommunityToolkit.MVVM en een Repository/Services-laag.
- Services en repositories worden singleton geregistreerd in `MauiProgram.cs`; views en viewmodels in `Views/AddViewsExtension.cs`. `AppShell.xaml` bevat de bestaande navigatiepagina's.
- `LlamaServerProcessService` beheert precies één server en stelt `LoadedSession` beschikbaar met runtime, model, geladen profiel-ID, parameters, poort en PID. De benchmark gebruikt uitsluitend deze al geladen server; start of stopt geen server.
- De actieve serverconfiguratie (`LoadedSession`) is leidend. De selectie in het Overview-scherm kan verschillen van de geladen sessie en mag daarom niet worden gebruikt als benchmarkidentiteit.
- SQLite gebruikt `PRAGMA user_version`; migraties staan in `Repositories/Database.cs`, huidige schema-versie is 2. Repositories volgen Dapper en openen een eigen SQLite-verbinding per call.
- `RuntimeMetricPollerService` pollt reeds `/slots` en eventueel `/metrics` om de twee seconden. Er is nog geen completion-client of benchmarkrunner.
- Er is bewust geen testproject en er worden geen nieuwe dependencies of externe benchmarkframeworks toegevoegd.

## Aannames en beslissingen

- Een run kan alleen starten wanneer een draaiende `LoadedSession` bestaat en de bijbehorende lokale endpoint bereikbaar is.
- Benchmarkscenario's worden na elkaar uitgevoerd tegen de bestaande endpoint; nooit wordt een tweede server gestart.
- Benchmarkruns leggen model, profiel en runtime vast als snapshots, zodat historie leesbaar blijft als de gekoppelde records later veranderen of verwijderd worden.
- De eerste dataset wordt beperkt en representatief gehouden; scenario's zijn data, geen code in de uitvoeringsengine.
- Warm-ups worden apart gemarkeerd en tellen niet mee in standaardmedianen.
- Cachetokens en cachegerelateerde informatie worden expliciet geregistreerd. Cache- en niet-cache-metingen worden niet zonder onderscheid vergeleken.

## 1. Architectuur en verantwoordelijkheden

1. **Workload-dataset** — versieerbare scenario's met stabiele IDs, onafhankelijk van uitvoeringslogica.
2. **Benchmark-runner-service** — valideert de geladen sessie, voert scenario's en herhalingen sequentieel uit, doet HTTP-requests naar de bestaande server, slaat metingen tussentijds op en publiceert voortgang. De service start of stopt geen server.
3. **Tool-simulatie** — valideert vooraf gedefinieerde toolnamen en argumenten en geeft deterministische, gesimuleerde antwoorden terug. Er worden geen filesystemwijzigingen of externe acties uitgevoerd.
4. **Benchmark-repository** — beheert configuraties, runmetadata en ruwe meetregels via Dapper/SQLite.
5. **Benchmark-viewmodel en pagina** — beheren configuraties, tonen de actieve sessie, start/stop, voortgang, samenvattingen en historische vergelijkingen.
6. **Registratie en navigatie** — voeg repository/service toe in `MauiProgram.cs`, viewmodel toe in `AddViewsExtension.cs` en `ShellContent` toe in `AppShell.xaml`.

De runner blijft onafhankelijk van de UI. Voortgang kan via een event of expliciete callback aan het viewmodel worden geleverd; de UI-marshalling volgt de bestaande `MainThread.BeginInvokeOnMainThread`-patronen.

## 2. Database-entiteiten en migratie

Voeg een voorwaartse migratie **2 → 3** toe in `Database.cs` met de volgende tabellen:

- **BenchmarkConfigurations**: ID, naam, JSON-instellingen (scenario-IDs en datasetversie, aantal herhalingen, warm-ups, maximum gegenereerde tokens en samplinginstellingen) en aangemaakt/bijgewerkt-tijdstip.
- **BenchmarkRuns**: ID, configuratie-ID/snapshot, begin- en eindtijd, status (voltooid/geannuleerd/mislukt), plus snapshots van model, quantisatie, profiel, runtime/build-identiteit (naam en waar beschikbaar versie/commit), server-PID en relevante launchparameters.
- **BenchmarkMeasurements**: één rij per scenario-uitvoering/repetitie, inclusief warm-upmarkering, scenario-ID/versie/categorie, prompt- en gegenereerde tokenaantallen, cachetokens indien beschikbaar, PP/TG, prompt-/generatie-/totale duur, toolcall- en foutaantallen en eventuele request-/meetfout.

Bewaar iedere meting direct na voltooiing zodat geannuleerde of onderbroken runs gedeeltelijke data behouden. Bewaar ruwe individuele meetregels; medianen en categoriebreakdowns worden uit meetregels berekend. Gebruik geen cascadeverwijdering van model- of profielrecords die historische runs zou wissen. Behandel opgeslagen configuratie- en scenario-JSON defensief bij ontbrekende of ongeldige velden.

## 3. Uitvoering en meetstrategie

1. Valideer vóór start dat `LlamaServerProcessService.Session` aanwezig is en de server `Running` is. Gebruik de poort uit de geladen sessie.
2. Leg de `LoadedSession` voor de run vast. Controleer tussen requests dat dezelfde sessie nog actief is. Een stop, onverwachte herstart of sessiewissel beëindigt de run gecontroleerd.
3. Voer scenario's, warm-ups, meetrepetities en toolrondes sequentieel uit met een `CancellationToken` en eindige request-timeouts. Annuleren stopt uitsluitend de runner, niet de server.
4. Gebruik waar beschikbaar llama.cpp-timing uit de completion-respons: onder andere `prompt_n`, `prompt_ms`, `predicted_n` en `predicted_ms`. Bewaar de ruwe aantallen en tijden. PP t/s en TG t/s worden berekend uit de bijbehorende servergemeten tokenaantallen en -tijden.
5. Leg client-wandkloktijd apart vast van servergemeten prompt- en generatietijd. Totale duur omvat ook HTTP-overhead en toolrondes en vervangt geen ontbrekende servertiming.
6. Sla cache-informatie afzonderlijk op. Markeer cached prompt tokens en de beschikbare cachetoestand; meng cached en uncached resultaten niet in één vergelijkingswaarde alsof zij gelijkwaardig zijn.
7. Warm-ups zijn herkenbaar in de opslag en uitgesloten van standaardaggregaties. Gebruik medianen over meetrepetities; toon ontbrekende timing als ontbrekend/onvergelijkbaar in plaats van een schijnwaarde te berekenen.
8. Bij serverdisconnect, ongeldige respons of timeout wordt de run expliciet als mislukt of gedeeltelijk opgeslagen, met foutinformatie; daarna stopt de benchmarkrunner.

### Toolcalls

Scenario's kunnen functiedefinities bevatten. Parse en valideer modeluitvoer strikt. Een onbekende toolnaam, ongeldige JSON, ontbrekend verplicht argument of niet-uitvoerbare gesimuleerde actie telt als mislukte toolcall. Geef een deterministische toolfout terug aan het model waar de protocolstroom dat toelaat. Begrens het aantal toolrondes en het aantal requests per scenario om lussen te voorkomen.

Een scenario-uitvoering telt alle requests uit de toolloop op voor gegenereerde tokens en serverduur, maar bewaart ook toolcallaantallen en client-totale duur.

## 4. Datasetformaat

Gebruik een apart JSON-datasetbestand als app-asset; dit houdt workloads onafhankelijk van de engine en laat scenario's toevoegen zonder runnerlogica te wijzigen. Per scenario:

- `id`, `version`, `category` en titel;
- realistische prompt en optionele codecontext;
- optionele functiedefinities met namen, beschrijvingen en JSON-schema's;
- verwachte outputvorm/kenmerken als documentatie of meetcontext, niet als kwaliteitsscore;
- scenario-specifieke requestinstellingen en limieten, zoals maximum toolrondes.

De eerste set moet realistische C#/.NET-, .NET MAUI-, JavaScript/TypeScript-, SQL-, HTML-/Markdown-, codeanalyse-, codewijzigings- en generatie-workloads bevatten. Toolscenario's modelleren coding-agent-acties met gesimuleerde functies (bijvoorbeeld code zoeken, tests uitvoeren of een wijzigingsvoorstel opvragen) en vaste antwoorden; ze doen geen echte I/O.

Benchmarkconfiguraties verwijzen naar scenario-IDs en datasetversie. De run slaat de effectieve configuratie en scenario-identiteit/versie op, zodat resultaten later te interpreteren zijn.

## 5. UI en navigatie

Voeg een compacte Benchmark-pagina toe aan Shell-navigatie met:

- bovenaan een prominente **Start benchmark**-knop (tijdens uitvoering vervangen/aangevuld met **Stop**); stop annuleert alleen de benchmark;
- benchmarkconfiguratie selecteren, aanmaken, bewerken en bewaren;
- selectie van workloadscenario's, herhalingen, warm-ups, maximum gegenereerde tokens en relevante samplinginstellingen;
- read-only model-, profiel- en runtimegegevens uit de **geladen** sessie;
- tijdens uitvoering live voortgang, huidige scenario, categorie, repetitie/warm-up en status;
- zodra een run klaar is, direct daaronder het resultaat tonen met mediane PP t/s, mediane TG t/s, mislukte toolcalls en totale duur;
- daaronder een tabel met alle historische runs, nieuwste eerst, met minimaal de kolommen **Date | Model | Profile | Build | TG t/s | PP t/s | Failed**. `Build` identificeert de gebruikte llama.cpp-runtime/build (bij voorkeur naam en versie/commit indien beschikbaar); TG en PP zijn de medianen van de meetrepetities. Toon ontbrekende of cache-onvergelijkbare snelheden expliciet als zodanig, niet als nul. Een rij kan worden geselecteerd voor detail en categorie-breakdown;
- vergelijkingen tussen modellen/profielen, met categoriebreakdowns voor coderings- en toolscenario's.

Hetzelfde workload kan tegen een andere model-/profielcombinatie worden uitgevoerd door die combinatie eerst normaal via de bestaande serverbediening te laden en vervolgens dezelfde benchmarkconfiguratie opnieuw te starten.

## 6. llama.cpp-beperkingen en meetvalkuilen

- Controleer de beschikbare OpenAI-compatibele `/v1/chat/completions`-vorm, toolcall-responsen en timingvelden tegen de beoogde lokale llama.cpp-builds voordat implementatie op die velden wordt vastgezet.
- Timing-/cachevelden kunnen per llama.cpp-versie, endpoint en chat template verschillen of ontbreken. Geen stille fallback naar een andere tijdbron voor PP/TG; rapporteer meetkwaliteit en onvergelijkbare data expliciet.
- Promptcache kan PP sterk beïnvloeden. Als de endpoint/build geen betrouwbare cachecontrole biedt, sla de waargenomen cached tokens en relevante omstandigheden op en vergelijk alleen compatibele metingen.
- De bestaande metrics-poller blijft ondertussen `/slots` en mogelijk `/metrics` benaderen. Dit is geen tweede server, maar veroorzaakt wel wat achtergrondverkeer; benchmarkrequests blijven onderling sequentieel.
- Toolcallinggedrag hangt mede af van het chat template en model. Het tellen van mislukte toolcalls is een protocol-/uitvoeringsmeting, geen beoordeling van codekwaliteit of semantische juistheid.
- Configureer limieten en timeouts om lange generaties, herhaalde toolloops en disconnects beheersbaar te houden.

## 7. Implementatiestappen

1. **API-onderzoek:** verifieer op de ondersteunde llama.cpp-builds de completion-response, timing- en cached-tokenvelden, toolcallformaten en relevante samplingparameters.
2. **Datasetspecificatie:** definieer het JSON-formaat en maak een kleine, representatieve eerste set realistische C#/.NET-, MAUI-, TypeScript-, SQL-, markup- en toolscenario's.
3. **Domein en opslag:** voeg benchmarkmodellen, migratie 2 → 3 en repositorymethoden voor configs, runs en metingen toe.
4. **Runner:** implementeer sessievalidatie, sequentiële uitvoering, annulering, timeout/disconnect-afhandeling, timingextractie en onmiddellijke persistente opslag.
5. **Toolsimulatie:** implementeer schema-/argumentvalidatie, deterministische responses, fouttelling en begrensde toolrondes.
6. **Integratie:** registreer repository en services; voeg viewmodel, Benchmark-pagina en Shell-navigatie toe.
7. **Resultaten:** implementeer medianen, cachebewuste vergelijkbaarheid en categorieaggregaties/historie.
8. **Documentatie en verificatie:** werk README/architectuurdocumentatie bij; bouw Windows-target en valideer tegen een bestaande lokale server dat de runner de geladen sessie gebruikt, tussentijds meetregels opslaat en bij annulering of disconnect stopt zonder serverbeheer te beïnvloeden.

## Verificatiechecklist

- [ ] Migratie 2 → 3 is voorwaarts en behoudt bestaande gegevens.
- [ ] Benchmark start alleen tegen een draaiende, geladen sessie.
- [ ] Er wordt geen tweede serverproces gestart en stop/annulering stopt de bestaande server niet.
- [ ] Scenario's en repetities lopen sequentieel; annulering en disconnect slaan de juiste status en reeds verzamelde metingen op.
- [ ] Ruwe tokenaantallen, serverduur, wandkloktijd, cache-informatie en toolcallresultaten blijven per meting beschikbaar.
- [ ] Warm-ups tellen niet mee in standaardmedianen; ontbrekende timing en verschillende cachecondities worden niet als equivalente snelheden gepresenteerd.
- [ ] Vergelijkingen en categoriebreakdowns gebruiken dezelfde scenario-/datasetversie en tonen het geladen model en profiel.
- [ ] Windows-build slaagt en XAML-bindings voor de nieuwe pagina compileren.
