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
COPY plugins/PromptOps.Plugins.Sonar/PromptOps.Plugins.Sonar.csproj plugins/PromptOps.Plugins.Sonar/
COPY plugins/PromptOps.Plugins.BuildResult/PromptOps.Plugins.BuildResult.csproj plugins/PromptOps.Plugins.BuildResult/
RUN dotnet restore src/PromptOps.Host/PromptOps.Host.csproj \
    && dotnet restore plugins/PromptOps.Plugins.Sonar/PromptOps.Plugins.Sonar.csproj \
    && dotnet restore plugins/PromptOps.Plugins.BuildResult/PromptOps.Plugins.BuildResult.csproj

COPY src/ src/
COPY plugins/ plugins/
RUN dotnet publish src/PromptOps.Host/PromptOps.Host.csproj \
    -c Release \
    -o /app \
    --no-restore

# Daemon-side provider plugins (ADR-0004) — each published into its own subdirectory under
# /app/plugins/<ProjectName>, the convention PluginLoader expects (docs/plugin-authoring.md).
RUN dotnet publish plugins/PromptOps.Plugins.Sonar/PromptOps.Plugins.Sonar.csproj \
    -c Release \
    -o /app/plugins/PromptOps.Plugins.Sonar \
    --no-restore \
    && dotnet publish plugins/PromptOps.Plugins.BuildResult/PromptOps.Plugins.BuildResult.csproj \
    -c Release \
    -o /app/plugins/PromptOps.Plugins.BuildResult \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Node.js + the Claude Code CLI back the optional "claude-cli" AIExecutionProvider
# (docs/ai-evaluation.md): the daemon shells out to `claude -p` for real AI-judge calls instead
# of the "manual"/echo test stub. Off by default (AIExecution__Provider unset) — installed
# unconditionally here so switching it on later doesn't require an image rebuild.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl gnupg ca-certificates \
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && npm install -g @anthropic-ai/claude-code \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system promptops && useradd --system --gid promptops --home-dir /app promptops \
    && mkdir -p /data /app/.claude \
    && chown -R promptops:promptops /app /data

COPY --from=build --chown=promptops:promptops /app .

USER promptops
ENV HOME=/app \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__PromptOps="Data Source=/data/promptops.db"
EXPOSE 8080
VOLUME /data

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "PromptOps.Host.dll"]
