# ADR 0005 — Estructura de la solución .NET

**Status:** Aceptado

## Contexto

`CLAUDE.md` ya definía los nombres de proyectos (`SmartDoc.Api`, `.Application`, `.Domain`,
`.Infrastructure`, `.Worker`) pero no la dirección explícita de las referencias entre ellos.

## Decisión

Se sigue la dirección de dependencias estándar de Clean Architecture:

```
SmartDoc.Domain          (sin dependencias propias del proyecto)
      ↑
SmartDoc.Application      (depende de Domain)
      ↑
SmartDoc.Infrastructure   (depende de Application)
      ↑
SmartDoc.Api / SmartDoc.Worker   (dependen de Application e Infrastructure)
```

`SmartDoc.UnitTests` referencia `Domain` y `Application` (sin infraestructura real, para
tests rápidos y aislados). `SmartDoc.IntegrationTests` referencia `SmartDoc.Api` completo
(para tests end-to-end contra el pipeline real).

## Consecuencias

- `Domain` permanece libre de dependencias de infraestructura (EF Core, HTTP, etc.), lo que
  facilita testear reglas de negocio sin mocks pesados.
- `Api` y `Worker` son los únicos puntos de composición (Dependency Injection root); ambos
  pueden compartir la misma configuración de servicios de `Infrastructure` sin duplicar
  lógica.
- Verificado con `dotnet build` exitoso sobre las 7 proyectos de la solución.
