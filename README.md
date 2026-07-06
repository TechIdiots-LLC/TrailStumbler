# TrailStumbler

A .NET MAUI app for importing and navigating GIS trail and point-of-interest data. Import GPS Trail Masters KMZ packages, OpenStreetMap GeoJSON exports, GPX tracks, or any KML/KMZ/GeoJSON file and view them on an interactive MapLibre map with offline hybrid routing.

## Features

- **Import GIS files** — GeoJSON, KML, KMZ (including GPS Trail Masters packages), and GPX
- **Styled trail rendering** — trail-class colours from KML `<Style>` definitions, symbol icons for POI categories (parking, fuel, food, lodging, camping, scenic, restroom, atv club)
- **Tap-to-inspect** — tap any feature to see its name, category, and description in a popup
- **Zoom-to-layer** — navigate the map to any imported layer's bounding box
- **GPS track recording** — live yellow track line, crash-recoverable, exports as GPX or KML
- **Offline hybrid routing** — routes on imported trail data using A★, bridges gaps with offline MVT road graph (no server required); supports ATV/motorcycle, bicycle, and walking profiles
- **Alternative routes** — request 1–3 route options; shortest selected by default, others shown as selectable cards

## Platforms

| Platform | Status |
|---|---|
| Android | Supported (API 26+) |
| Windows | Supported (10.0.19041+, unpackaged) |
| iOS / macOS | Planned |

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) with the MAUI workload
- Android SDK (for Android target) or Windows 10/11 (for Windows target)

```
dotnet workload install maui
```

### Building locally

1. Clone the repo and navigate to it:

```
git clone https://github.com/TechIdiots-LLC/TrailStumbler.git
cd TrailStumbler
```

2. Add the TechIdiots GitHub Packages feed (required for dev prerelease dependencies):

```
dotnet nuget update source techidiots-github \
  --username <your-github-username> \
  --password <PAT-with-read:packages-scope> \
  --store-password-in-clear-text
```

3. Restore and build:

```
# Windows
dotnet restore TrailStumbler/TrailStumbler.csproj
dotnet build TrailStumbler/TrailStumbler.csproj -f net10.0-windows10.0.19041.0

# Android
dotnet restore TrailStumbler/TrailStumbler.csproj
dotnet build TrailStumbler/TrailStumbler.csproj -f net10.0-android36.0
```

4. Run on Windows:

```
dotnet run --project TrailStumbler/TrailStumbler.csproj -f net10.0-windows10.0.19041.0
```

### NuGet dependencies

TrailStumbler depends on two packages that are not yet published to NuGet.org:

| Package | Source |
|---|---|
| `MapLibreNative.Maui.Handlers` | [maplibre-maui-ac](https://github.com/TechIdiots-LLC/maplibre-maui-ac) |
| `MaplibreNative.Routing` | [MaplibreNativeRouting](https://github.com/TechIdiots-LLC/MaplibreNativeRouting) |

These are hosted on the [TechIdiots-LLC GitHub Packages NuGet feed](https://nuget.pkg.github.com/TechIdiots-LLC/index.json). GitHub Actions CI authenticates automatically via `GITHUB_TOKEN`; local development requires a [personal access token](https://github.com/settings/tokens) with `read:packages` scope.

## CI / Release workflow

Three GitHub Actions workflows are included:

| Workflow | Trigger | Purpose |
|---|---|---|
| **CI** | Push to `main`, PRs | Unit tests + Windows and Android build verification |
| **Create bump version PR** | Manual (`workflow_dispatch`) | Bumps `ApplicationDisplayVersion`, updates `CHANGELOG.md`, opens a PR |
| **Build and Release** | Push to `main` or manual | Publishes a GitHub Release with a Windows zip and Android APK |

### Creating a release

1. Go to **Actions → Create bump version PR** and dispatch with the desired bump type (`patch`, `minor`, `major`, or a `pre*` variant).
2. Review and merge the generated PR (check the `CHANGELOG.md` entries).
3. The push to `main` triggers **Build and Release** automatically, or trigger it manually.

> **Note:** The release workflow requires a GitHub environment named `release`. Create it at  
> `Settings → Environments → New environment → release`.

## Project structure

```
TrailStumbler/               .NET MAUI head project (Android + Windows)
TrailStumbler.Core/          Platform-agnostic parsing, models, and services
TrailStumbler.Core.Tests/    xUnit tests for parsers and core logic
```

## License

MIT
