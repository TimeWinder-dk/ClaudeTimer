# ClaudeTimer

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

- **ClaudeTimer-Setup-*.exe** — dobbeltklik-installer. Installerer per bruger i
  `%LOCALAPPDATA%\Programs\ClaudeTimer` (ingen administrator), tilbyder genveje på
  skrivebord, i Start-menuen og på proceslinjen, og kan af-installeres via
  Tilføj/Fjern programmer.
- **ClaudeTimer-*-win-x64.exe** — enkelt selv-indeholdt fil, kan køres direkte.
- **ClaudeTimer-*-win-x64.zip** — samme app som mappe.

Ingen af filerne er kodesignerede endnu, så Windows SmartScreen kan advare første
gang (vælg "Flere oplysninger" → "Kør alligevel").

Installeren bygges fra `installer\ClaudeTimer.iss` med
[Inno Setup](https://jrsoftware.org/isinfo.php):

```powershell
.\installer\build-installer.ps1
```

Scriptet rydder automatisk gamle `ClaudeTimer-Setup-*.exe` filer, bygger en ny
installer og genererer `SHA256SUMS.txt` samt en `.sha256`-fil ved siden af
installeren i `artifacts\installer`.

## Krav

- Windows 10 1809 eller nyere (Windows 11 anbefales)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) til udvikling
- Claude Code logget ind med OAuth, eller et Claude Code OAuth access token

## Kør lokalt

```powershell
dotnet restore
dotnet run --project .\src\ClaudeTimer\ClaudeTimer.csproj
```

ClaudeTimer prøver først et manuelt gemt token og derefter Claude Codes
standardfil `%USERPROFILE%\.claude\.credentials.json`. Et manuelt token
krypteres med Windows DPAPI (`CurrentUser`) og gemmes under
`%LOCALAPPDATA%\ClaudeTimer\token.dat`. Tokenet logges aldrig.

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
