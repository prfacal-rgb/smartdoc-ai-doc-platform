# Build context is backend-dotnet/ (not this directory) - same reasoning as
# SmartDoc.Api/Dockerfile: project references require the whole src/ tree.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json .
COPY src/ src/

# See SmartDoc.Api/Dockerfile for why this is one `publish` step, not restore+ --no-restore.
RUN dotnet publish src/SmartDoc.Worker/SmartDoc.Worker.csproj -c Release -o /app

# `runtime`, not `aspnet` - SmartDoc.Worker is a plain Generic Host (no Kestrel/ASP.NET Core),
# so the smaller base image is enough.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# libgssapi-krb5-2: Npgsql probes for it during connection negotiation even though this
# project uses plain password auth, not Kerberos - present by default in the (larger) aspnet
# base image SmartDoc.Api uses, missing from this leaner runtime-only one. Without it, Npgsql
# still connects (falls back past the failed GSS attempt) but logs a scary-looking
# "Cannot load library" error on every connection, worth silencing properly.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# Mount point for the Serilog file sink (ADR 0020/0024).
RUN mkdir -p logs

# No EXPOSE/HEALTHCHECK - the Worker has no listening socket to probe. Startup ordering
# relative to the Api's own migrations is handled by docker-compose's depends_on instead
# (see ADR 0024).
ENTRYPOINT ["dotnet", "SmartDoc.Worker.dll"]
