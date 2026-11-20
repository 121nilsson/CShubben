# Cshubben

Systerprojekt till My-hub, byggt med **.NET 11 / C# 15** och **Microsoft.UI.Reactor** (deklarativt, React-liknande UI i ren C# för WinUI 3).

> Status: grunduppsättning. Reactor är experimentellt (preview) och bara för Windows.

## Bygga lokalt (Windows)

```
dotnet build -p:Platform=x64
dotnet run --project src/Cshubben -p:Platform=x64
```

Bygg alltid med explicit `-p:Platform=x64` (eller `ARM64`).

## CI

`.github/workflows/ci.yml` bygger och publicerar x64 och ARM64 på `windows-latest` och sparar resultatet som artefakter.
