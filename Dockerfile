# syntax=docker/dockerfile:1
# ---------------------------------------------------------------------------
# HR Copilot (D6T1) — multi-stage build.
# The default provider is the deterministic offline `local` model, so the
# published image runs the full pipeline with no API key.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore separately for layer caching; locked-mode guarantees the committed
# packages.lock.json files are the only resolved graph.
COPY . .
RUN dotnet restore --locked-mode HR.sln

RUN dotnet publish HR.API/HR.API.csproj \
        --configuration Release \
        --no-restore \
        --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# curl is only for the container healthcheck.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data && chown -R app /data
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Storage__ConnectionString="Data Source=/data/hr.db" \
    Ingestion__SeedCorpusOnStartup=true \
    Llm__FallbackOrder=local

EXPOSE 8080
VOLUME ["/data"]

HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
    CMD curl -fsS http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "HR.API.dll"]
