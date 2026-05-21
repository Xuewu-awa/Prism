# Unreal Engine Blueprint Ripper

ASP.NET-based WebUI for inspecting and editing Unreal Engine blueprint bytecode in `.uasset` files with UAssetAPI.

## Current capabilities

- Upload `.uasset`, optional `.uexp`, and optional `.usmap` files.
- Parse `StructExport.ScriptBytecode` into a node graph view.
- Extract semantic templates from `EX_Context` function calls into a local JSON node library.
- Edit visible node metadata, scalar literal values, node positions, and simple node connections.
- Apply graph changes back to the in-memory asset and save with a `.bak` backup.
- Preserve unsupported existing expressions where possible, while warning when newly added nodes cannot be compiled yet.

## Run

```powershell
dotnet run --project UEBPR
```

Then open the URL printed by ASP.NET, usually `http://localhost:5000` or the configured launch URL.

## Notes

This is an early editor surface over low-level Kismet bytecode. It is intentionally conservative: complex blueprint constructs are displayed and preserved first, then progressively upgraded into fully editable node templates as the node library learns more context.
