# ADR 0003 — Minimal APIs en lugar de Controllers

**Status:** Aceptado

## Contexto

`CLAUDE.md` define Vertical Slice Architecture como estilo de organización del código
("un endpoint = un caso de uso"), pero no fijaba explícitamente el mecanismo de exposición
HTTP (Minimal APIs vs. Controllers de MVC).

## Decisión

Se usan **Minimal APIs**. El proyecto `SmartDoc.Api` se generó con la plantilla `dotnet new
web` (no `webapi`), sin scaffolding de controllers.

## Consecuencias

- Cada endpoint se define como un handler independiente, lo que encaja naturalmente con el
  principio de vertical slice ya documentado (un caso de uso, un archivo, sin controller
  compartido acumulando acciones no relacionadas).
- No se dispone de algunas convenciones automáticas de MVC (model binding avanzado, filtros
  de acción); donde haga falta, se implementan explícitamente vía endpoint filters de
  Minimal APIs.
- Swagger/OpenAPI se configura sobre Minimal APIs (soportado nativamente desde .NET 9+),
  pendiente de agregar cuando se expongan los primeros endpoints reales.
