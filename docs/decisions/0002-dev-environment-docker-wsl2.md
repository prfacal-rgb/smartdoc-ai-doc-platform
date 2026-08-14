# ADR 0002 — Entorno de desarrollo: Docker Desktop sobre WSL2

**Status:** Aceptado

## Contexto

El plan original (`CLAUDE.md`) asumía Docker Compose como orquestador de PostgreSQL+pgvector
tanto en desarrollo como referencia de despliegue. La VM de desarrollo (Windows 11 sobre
VMware Workstation) inicialmente no tenía WSL2 habilitado, y al intentar instalarlo se
encontraron dos bloqueos sucesivos:

1. WSL no instalado (`wsl --install` resuelto tras reinicio).
2. `HCS_E_HYPERV_NOT_INSTALLED` — la VM no exponía virtualización anidada, necesaria porque
   WSL2/Hyper-V corre dentro de una VM que a su vez corre sobre VMware Workstation.

Se evaluaron tres alternativas si Docker no llegaba a funcionar: PostgreSQL nativo en
Windows, PostgreSQL dentro de WSL2 sin Docker, o Postgres gestionado en la nube (Supabase).

## Decisión

Se habilitó virtualización anidada en VMware Workstation (Settings → Processors →
"Virtualize Intel VT-x/EPT or AMD-V/RVI") con la VM apagada, confirmando previamente
VT-x/AMD-V en el host físico. Con eso, Docker Desktop funciona normalmente usando WSL2 como
backend. **Se mantiene el plan original: Docker Compose** como mecanismo de orquestación,
tanto en desarrollo como en la definición de referencia del proyecto.

Las alternativas sin Docker (Postgres nativo, Postgres dentro de WSL2 standalone, Supabase)
quedan descartadas — no se llegaron a implementar.

## Consecuencias

- No hay divergencia entre el entorno de desarrollo local y el mecanismo de orquestación
  documentado como decisión de arquitectura del proyecto — no requiere nota aclaratoria en
  el README.
- WSL2 queda como dependencia de infraestructura (requerida por Docker Desktop en Windows),
  no como pieza que el desarrollador gestione directamente.
- Activar virtualización anidada puede introducir overhead menor de performance en la VM;
  no se ha observado impacto relevante hasta el momento.
