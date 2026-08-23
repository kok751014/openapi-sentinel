# OpenAPI Sentinel — offline-capable contract analysis in a container.
# Build:  docker build -t openapi-sentinel .
# Run:    docker run --rm -v "$PWD:/workspace" openapi-sentinel \
#           compare /workspace/old.yaml /workspace/new.yaml --fail-on high
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY OpenApiSentinel.slnx ./
COPY src/OpenApiSentinel.Core src/OpenApiSentinel.Core
COPY src/OpenApiSentinel.OpenApi src/OpenApiSentinel.OpenApi
COPY src/OpenApiSentinel.Rules src/OpenApiSentinel.Rules
COPY src/OpenApiSentinel.Reporting src/OpenApiSentinel.Reporting
COPY src/OpenApiSentinel.Cli src/OpenApiSentinel.Cli

RUN dotnet restore && \
    dotnet build -c Release --no-restore && \
    dotnet publish src/OpenApiSentinel.Cli -c Release --no-build \
      -r linux-x64 --self-contained false -o /app/publish && \
    mv /app/publish/OpenApiSentinel.Cli /app/publish/openapi-sentinel

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
WORKDIR /workspace

# Run as a non-root user; reports are written to /workspace (mount point).
RUN useradd --create-home --uid 1000 sentinel
COPY --from=build /app/publish /opt/sentinel
ENV PATH="/opt/sentinel:${PATH}" \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

USER sentinel
ENTRYPOINT ["openapi-sentinel"]
CMD ["--help"]
