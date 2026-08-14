# ADR 0001 — Versión de .NET SDK

**Status:** Aceptado

## Contexto

La VM de desarrollo tenía originalmente el SDK 8.0.419 instalado. `CLAUDE.md` indicaba
".NET 10.x" como versión objetivo, pendiente de confirmar la instalación real.

## Decisión

Se instaló el SDK **10.0.400** vía `winget install --id Microsoft.DotNet.SDK.10 --exact`.
Convive sin conflicto con el 8.0.419 preexistente (`dotnet --list-sdks` confirma ambos).
El proyecto fija **.NET 10** como target framework (`net10.0`) en todos los `.csproj`.

## Consecuencias

- Se recomienda agregar un `global.json` en la raíz de `backend-dotnet/` fijando el SDK
  10.0.400, para que `dotnet build` no tome accidentalmente el 8.0.419 si en el futuro se
  instalan más versiones en la misma VM.
- Pendiente: crear ese `global.json` (no se hizo todavía).
