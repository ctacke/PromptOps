# syntax=docker/dockerfile:1

# Builds the PromptOps daemon (ADR-0009): Host + Infrastructure + all in-tree plugins,
# packaged as a single image started once per developer machine.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first so `dotnet restore` is cached independently of source changes.
COPY PromptOps.slnx .
COPY src/PromptOps.Domain/PromptOps.Domain.csproj src/PromptOps.Domain/
COPY src/PromptOps.Application/PromptOps.Application.csproj src/PromptOps.Application/
COPY src/PromptOps.Infrastructure/PromptOps.Infrastructure.csproj src/PromptOps.Infrastructure/
COPY src/PromptOps.Host/PromptOps.Host.csproj src/PromptOps.Host/
COPY plugins/PromptOps.Plugin.Sdk/PromptOps.Plugin.Sdk.csproj plugins/PromptOps.Plugin.Sdk/
RUN dotnet restore src/PromptOps.Host/PromptOps.Host.csproj

COPY src/ src/
COPY plugins/ plugins/
RUN dotnet publish src/PromptOps.Host/PromptOps.Host.csproj \
    -c Release \
    -o /app \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system promptops && useradd --system --gid promptops --home-dir /app promptops \
    && mkdir -p /data \
    && chown -R promptops:promptops /app /data

COPY --from=build --chown=promptops:promptops /app .

USER promptops
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__PromptOps="Data Source=/data/promptops.db"
EXPOSE 8080
VOLUME /data

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "PromptOps.Host.dll"]
