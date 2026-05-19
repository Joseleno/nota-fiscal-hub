# ============================================================
# Stage 1 — build
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY VisuFiscalHub.slnx ./
COPY src/VisuFiscalHub.Domain/VisuFiscalHub.Domain.csproj ./src/VisuFiscalHub.Domain/
COPY src/VisuFiscalHub.Application/VisuFiscalHub.Application.csproj ./src/VisuFiscalHub.Application/
COPY src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj ./src/VisuFiscalHub.Infrastructure/
COPY src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj ./src/VisuFiscalHub.Api/

RUN dotnet restore VisuFiscalHub.slnx

COPY src/ ./src/

RUN dotnet publish ./src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ============================================================
# Stage 2 — runtime
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# ca-certificates necessário para validação TLS dos webservices SEFAZ (cadeia ICP-Brasil)
# curl necessário para o healthcheck no docker-compose (CMD curl -f http://localhost:8080/health/live)
RUN apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates curl && \
    rm -rf /var/lib/apt/lists/*

RUN adduser --disabled-password --gecos "" --no-create-home appuser
COPY --chown=appuser:appuser --from=build /app/publish .
USER appuser

EXPOSE 8080
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "VisuFiscalHub.Api.dll"]
