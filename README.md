# ClaudeTimer

[![Latest release](https://img.shields.io/github/v/release/TimeWinder-dk/ClaudeTimer?display_name=tag)](https://github.com/TimeWinder-dk/ClaudeTimer/releases/latest)

En lille Windows 11-inspireret WPF-app, der viser Claude Code-forbrug for alle
aktive grænser: det aktuelle 5-timers vindue, den ugentlige grænse og eventuelle
model-afgrænsede uger (fx Fable). Kortene bygges dynamisk ud fra API'ets
`limits`-liste, så nye grænser dukker op automatisk uden kodeændringer.

Appen viser for hvert vindue:

- aktuel udnyttelse i procent
- lokal nedtælling med sekundpræcision
- det præcise lokale nulstillingstidspunkt

Data hentes fra Claudes OAuth usage-endpoint. API-data opdateres hvert femte
minut; nedtællingerne opdateres lokalt hvert sekund. Ved et passeret
`resets_at` forsøger appen at hente det nye vindue efter få sekunder.

## Installér

Hent den nyeste [release](https://github.com/TimeWinder-dk/ClaudeTimer/releases)
og vælg én af:

- Direkte download af installer: [ClaudeTimer-Setup-1.5.1.exe](https://github.com/TimeWinder-dk/ClaudeTimer/releases/download/v1.5.1/ClaudeTimer-Setup-1.5.1.exe)
- SHA256 (installer): [ClaudeTimer-Setup-1.5.1.exe.sha256](https://github.com/TimeWinder-dk/ClaudeTimer/releases/download/v1.5.1/ClaudeTimer-Setup-1.5.1.exe.sha256)
- Alle checksums: [SHA256SUMS.txt](https://github.com/TimeWinder-dk/ClaudeTimer/releases/download/v1.5.1/SHA256SUMS.txt)

- **ClaudeTimer-Setup-*.exe** — dobbeltklik-installer. Installerer per bruger i
  `%LOCALAPPDATA%\Programs\ClaudeTimer` (ingen administrator), tilbyder genveje på
  skrivebord, i Start-menuen og på proceslinjen, og kan af-installeres via
  Tilføj/Fjern programmer.
- **ClaudeTimer-*-win-x64.msi** — per-bruger MSI (ingen administrator), ofte
  nemmere i enterprise-miljøer med AppLocker/Intune-politikker.
- **ClaudeTimer-*-win-x64-allusers.msi** — all-users MSI (kræver elevation/admin),
  installerer til `C:\Program Files\ClaudeTimer`.
- **ClaudeTimer-*-win-x64.exe** — enkelt selv-indeholdt fil, kan køres direkte.
- **ClaudeTimer-*-win-x64.zip** — samme app som mappe.

Alle varianter er selv-indeholdte (.NET er pakket med), så de kan installeres
offline — fx ved at kopiere filen til en maskine uden internet.

Kontrollér en download mod checksummen:

```powershell
(Get-FileHash .\ClaudeTimer-Setup-1.5.1.exe -Algorithm SHA256).Hash
# sammenlign med linjen i SHA256SUMS.txt
```

Ingen af filerne er kodesignerede endnu, så Windows SmartScreen kan advare første
gang (vælg "Flere oplysninger" → "Kør alligevel").

Alle release-filer inkl. `.sha256`-filer og en samlet `SHA256SUMS.txt` bygges med:

```powershell
.\installer\build-release.ps1
```

Resultatet lægges i `artifacts\release-v<version>`.

Installeren bygges fra `installer\ClaudeTimer.iss` med
[Inno Setup](https://jrsoftware.org/isinfo.php):

```powershell
.\installer\build-installer.ps1
```

Scriptet rydder automatisk gamle `ClaudeTimer-Setup-*.exe` filer, bygger en ny
installer og genererer `SHA256SUMS.txt` samt en `.sha256`-fil ved siden af
installeren i `artifacts\installer`.

MSI bygges med WiX via:

```powershell
.\installer\build-msi.ps1
```

MSI'en installerer per bruger til `%LOCALAPPDATA%\Programs\ClaudeTimer` og kan
installeres uden admin med:

```powershell
msiexec /i .\artifacts\installer\ClaudeTimer-1.5.1-win-x64.msi
```

All-users MSI bygges via:

```powershell
.\installer\build-msi-allusers.ps1
```

Og installeres med:

```powershell
msiexec /i .\artifacts\installer\ClaudeTimer-1.5.1-win-x64-allusers.msi
```

Bemærk: all-users MSI opretter ikke genveje i denne version.

## Automatiske opdateringer

ClaudeTimer spørger GitHub efter den seneste release ved opstart og derefter
hver 6. time. Findes der en nyere version, hentes pakken, der passer til
installationstypen (Setup-exe, per-bruger MSI, all-users MSI eller enkelt-fil
exe), og dens SHA256 kontrolleres mod release'ens `SHA256SUMS.txt`. Derefter
lukker appen, installerer stille og starter igen i systembakken. All-users MSI
beder om administrator-godkendelse. Zip-udgaven kan ikke opdatere sig selv,
men giver besked og et link til GitHub.

Begge dele kan slås fra under *Opdateringer* i indstillingerne, hvor man også kan
søge manuelt.

Der kører kun én ClaudeTimer ad gangen; åbnes den igen, vises det kørende vindue.

## Krav

- Windows 10 1809 eller nyere (Windows 11 anbefales)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) til udvikling
- Claude Code eller Claude Desktop logget ind, eller et Claude OAuth access token

## Kør lokalt

```powershell
dotnet restore
dotnet run --project .\src\ClaudeTimer\ClaudeTimer.csproj
```

## Indstillinger

Tandhjulet øverst til højre åbner de samlede indstillinger, som gemmes i
`%LOCALAPPDATA%\ClaudeTimer\settings.json`.

**Token-kilde**

- *Automatisk* (standard) — bruger alle fundne logins. Hvert token slås op på
  Claudes profil-endpoint; tokens for samme konto og organisation slås sammen,
  mens forskellige konti vises i hver sin fane. Fanerne skjules, når der kun er én.
- *Claude Code* — læser `%USERPROFILE%\.claude\.credentials.json` (eller
  `%CLAUDE_CONFIG_DIR%\.credentials.json`). Filen deles af Claude Code CLI og
  VS Code-udvidelsen, så ét login i en af dem dækker begge. Udløbne tokens
  springes over.
- *Claude Desktop* — læser Claude Desktop-appens token-cache (`config.json` i
  `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude` for
  Store/MSIX-installationen, ellers `%APPDATA%\Claude`). Cachen er krypteret med
  Electrons safeStorage (AES-GCM med en DPAPI-beskyttet nøgle) og kan kun læses
  af din egen Windows-bruger. Kun et gyldigt token med `user:profile`-scope bruges.
  **Fra som standard:** dekrypteringen ligner det, infostealere gør, så Microsoft
  Defender m.fl. kan melde "Suspicious DPAPI Activity". Slå den til under
  *Medtag Claude Desktop-appens login* (eller vælg *Kun Claude Desktop*), helst
  kun på egne maskiner.
- *Kun manuelt* — kun det indsatte token. Det krypteres med Windows DPAPI
  (`CurrentUser`) og gemmes under `%LOCALAPPDATA%\ClaudeTimer\token.dat`.

Tokens læses igen ved hver opdatering (hvert 5. minut), så fornyelser og
konto-skift i Claude Code/Desktop slår igennem. Tokens logges aldrig.

Bakke-ikonets tooltip viser ved én konto en linje pr. grænse
(`5 timer · 42 % · 01:12:33`) og ved flere konti én kompakt linje pr. konto
(`thha: 5t 42% (1t12m) · Uge 18% (3d4t) · Fable 5%`), inden for Windows' grænse
på 127 tegn.

**Konti** — giv hver konto et eget navn (fx "Arbejde"), som bruges i faner,
tooltip og notifikationer. E-mail og organisation vises stadig som undertitel.

**Udseende**

- *Tema* — følg Windows (skifter med det samme, når Windows skifter), lys eller mørk.
- *Forbrug i bakke-ikonet* — ikonet tegner den mest pressede grænse som en ring på lys baggrund (ens på lys og mørk proceslinje)
  med procenttal: grøn under 50 %, gul under 80 %, rød derover. Kortenes procent
  og bjælke farves efter samme niveauer.
- Hvert kort viser en *prognose* ud fra tempoet i vinduet: "Prognose ved
  nulstilling: 40 %" eller, hvis grænsen nås før, "Med dette tempo: 100 % kl. 14:20".

**Notifikationer** — besked fra bakken, når en grænse krydser de valgte
procenter (standard 80 og 95), og når en grænse nulstilles. Første indlæsning
efter start giver ingen besked.

**Fejllog** — hændelser og fejl (token-kilder, API-fejl, opdateringer,
installation) skrives til `%LOCALAPPDATA%\ClaudeTimer\log.txt` (roteres ved
512 KB). Tokens og e-mails logges aldrig. Knappen *Åbn log* åbner filen.

**Opstart**

- *Start når Windows starter* — registreres under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` med `--autostart`.
- *Start skjult i systembakken* — ved autostart vises kun bakke-ikonet
  (vinduet vises alligevel, hvis der mangler et token).

**Administrator**

- *Kør altid som administrator* — appen genstarter sig selv via UAC ved opstart.
  Afvises prompten, kører den videre uden rettigheder.
- Kombineret med autostart oprettes i stedet en planlagt opgave (`ClaudeTimer`)
  med "højeste rettigheder", fordi Windows ikke starter elevated apps fra
  Run-nøglen. Opgaven udløses kun ved din egen logon og spørger ikke om UAC.
  Oprettelse og sletning af opgaven kræver én UAC-godkendelse.

## Test

```powershell
dotnet test .\ClaudeTimer.slnx
```

## Publicér uden Microsoft Store

Self-contained mappe:

```powershell
dotnet publish .\src\ClaudeTimer\ClaudeTimer.csproj `
  -p:PublishProfile=Folder
```

Self-contained single-file `.exe`:

```powershell
dotnet publish .\src\ClaudeTimer\ClaudeTimer.csproj `
  -p:PublishProfile=SingleFile
```

Resultaterne placeres i henholdsvis `artifacts\folder` og
`artifacts\single-file`. De kan kopieres direkte til en anden Windows x64-pc;
.NET behøver ikke være installeret. Kodesignering anbefales før distribution,
så Windows SmartScreen kan opbygge tillid til udgiveren.

MSIX kan tilføjes senere som et separat packaging-projekt uden ændringer i
appens kerne eller krav om Microsoft Store.

## Arkitektur

- `ViewModels` — præsentationslogik, kommandoer og sekundnedtælling
- `Services` — typed `HttpClient`, credential-læsning og DPAPI-tokenlager
- `Models` — API- og domænemodeller deserialiseret med `System.Text.Json`
- `Themes` — Fluent-inspirerede WPF styles

Endpointet er et OAuth beta-endpoint og er ikke en del af Anthropics offentligt
dokumenterede API-kontrakt. Appen håndterer manglende vinduer, ukendt JSON,
401/403, 429 og netværksfejl, men et fremtidigt API-skift kan kræve en opdatering.
