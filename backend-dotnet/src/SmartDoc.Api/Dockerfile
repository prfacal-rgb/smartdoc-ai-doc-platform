# Build context is backend-dotnet/ (not this directory) - SmartDoc.Api references
# SmartDoc.Application/Domain/Infrastructure by project, so the whole src/ tree is needed to
# restore/build. See docker-compose.yml's `build.context` for the api service.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json .
COPY src/ src/

# A single `dotnet publish` (implicit restore), not a separate `restore` layer followed by
# `--no-restore` - tried the split-layer version first for Docker layer caching, but hit
# NETSDK1064 ("AWSSDK.S3 ... was not found") on publish despite restore reporting success for
# the exact same project. Simpler and more robust; costs some rebuild speed, not correctness.
RUN dotnet publish src/SmartDoc.Api/SmartDoc.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl for the HEALTHCHECK below - not present in the base runtime image by default.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# Mount point for the Serilog file sink (ADR 0020/0024) - created here too so the container
# still starts cleanly if run without docker-compose's bind mount over this path.
RUN mkdir -p logs

# The official aspnet image already sets ASPNETCORE_HTTP_PORTS=8080 by default.
EXPOSE 8080

HEALTHCHECK --interval=5s --timeout=5s --retries=10 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "SmartDoc.Api.dll"]
