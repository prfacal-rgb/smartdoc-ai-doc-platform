# ADR 0010 — Object storage real (MinIO) y upload de archivo

**Status:** Aceptado

## Contexto

El upload de archivo real (`Store original file`, segundo paso del flujo documentado en
`PROJECT.md`) se difirió dos veces (Fase 1 → Fase 2 → ahora) porque no había object storage
provisionado y nadie necesitaba leer el archivo todavía. Con Fase 3 (AI pipeline) arrancando,
el Worker va a necesitar el archivo real para poder parsearlo — este es el punto en el que
conectarlo deja de ser prematuro.

## Decisiones

**MinIO** como object storage de desarrollo (contenedor más en `docker-compose.yml`,
mismo patrón que Postgres — healthcheck, volumen persistente, credenciales vía `.env`).

**`IFileStorage`** como puerto en `SmartDoc.Application/Storage/` (primer código real de esa
capa, antes vacía — ver ADR 0007, que explícitamente decía "se usa el día que aparezca una
razón concreta"; esta es esa razón: un proveedor externo swappeable, tal como pide
`PROJECT.md` §"Storage de archivos"). Implementación `MinioFileStorage` en
`SmartDoc.Infrastructure`, usando `AWSSDK.S3` (cliente estándar S3, en vez del SDK propio de
MinIO) para que el día de mañana un swap a AWS S3 real sea solo cambiar la implementación
registrada en DI, no reescribir el cliente.

**Bucket creado de forma idempotente al arrancar la Api** (`MinioBucketInitializer`), mismo
espíritu que `SmartDocDbContextSeeder`.

**`POST /api/documents` pasa de JSON a `multipart/form-data`.** Recibe `IFormFile` +
`userId`, valida `ContentType == "application/pdf"` explícitamente (PROJECT.md §8: "PDF
únicamente" — antes era un supuesto no verificado, ahora es una regla de validación real),
sube a MinIO vía `IFileStorage.SaveAsync`, y persiste el `StoragePath` devuelto (una key con
prefijo `Guid` para evitar colisiones entre archivos con el mismo nombre).

**`DELETE /api/documents/{id}` ahora también borra el objeto de MinIO** (antes solo borraba
la fila de la base, dejando el archivo huérfano — encontrado al escribir los tests de esta
ronda, no reportado por nadie). Es best-effort: si el delete de storage falla, no se revierte
el delete de la fila (la base es la fuente de verdad de "existe o no"); un objeto huérfano en
MinIO es un problema de limpieza barato, no de correctness.

## Problema encontrado y resuelto

Los endpoints Minimal API que bindean `IFormFile` reciben automáticamente metadata de
anti-forgery desde .NET 8, y explotan en runtime (`InvalidOperationException`) si no hay
middleware de anti-forgery registrado. Se resuelve con `.DisableAntiforgery()` en el POST —
correcto para este caso: es un backend de API consumido por HTTP directo (o eventualmente un
SPA con JWT), no un formulario autenticado por cookies, que es el escenario que la protección
CSRF/anti-forgery está pensada para cubrir.

## Consecuencias

- `.env.example` y `CLAUDE.md` actualizados con `MINIO_ROOT_USER`/`MINIO_ROOT_PASSWORD`/
  `MINIO_BUCKET_NAME`.
- Tests de integración actualizados para mandar `multipart/form-data` real (no JSON), y
  nuevos tests que verifican el contenido efectivamente guardado en MinIO y su borrado
  simétrico al eliminar el `Document`.
- Verificado con `dotnet run` real de la Api (no solo tests): el bucket se crea solo al
  arrancar, confirmado inspeccionando el volumen de MinIO directamente.
- Pendiente explícito para cuando se construya `DocumentChunks` (próximo paso de Fase 3):
  cada chunk guarda qué modelo de embeddings lo generó (`EmbeddingModel`, no un valor global),
  y la dimensión del vector (`vector(768)` con `nomic-embed-text`) queda documentada como
  parte fija del schema — cambiar de modelo a otra dimensión no es un swap de config, requiere
  migration de columna y reembeder todo lo existente.
