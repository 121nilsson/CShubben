# Cshubben

A sibling project to My-hub, built with **.NET 11 / C# 15** and **Microsoft.UI.Reactor** (declarative, React-style UI in plain C# for WinUI 3).

> Status: initial setup. Reactor is experimental (preview) and Windows-only.

## Build locally (Windows)

```
dotnet build src/Cshubben/Cshubben.csproj -p:Platform=x64
dotnet run --project src/Cshubben -p:Platform=x64
```

Always build with an explicit `-p:Platform=x64` (or `ARM64`).

## CI

`.github/workflows/ci.yml` builds and publishes x64 and ARM64 on `windows-latest` and uploads the output as artifacts.
