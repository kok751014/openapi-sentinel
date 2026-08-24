# OpenAPI Sentinel — offline-capable contract analysis in a container.
#
# Build:
#   docker build -t openapi-sentinel .
#
# Run:
#   docker run --rm -v "$PWD:/workspace" openapi-sentinel \
#     compare /workspace/old.yaml /workspace/new.yaml --fail-on high

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG TARGETARCH

WORKDIR /src

COPY Directory.Build.props ./
COPY OpenApiSentinel.slnx ./
COPY src/OpenApiSentinel.Core src/OpenApiSentinel.Core
COPY src/OpenApiSentinel.OpenApi src/OpenApiSentinel.OpenApi
COPY src/OpenApiSentinel.Rules src/OpenApiSentinel.Rules
COPY src/OpenApiSentinel.Reporting src/OpenApiSentinel.Reporting
COPY src/OpenApiSentinel.Cli src/OpenApiSentinel.Cli

RUN case "$TARGETARCH" in \
      amd64) RID="linux-x64" ;; \
      arm64) RID="linux-arm64" ;; \
      *) echo "Unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac && \
    dotnet publish src/OpenApiSentinel.Cli/OpenApiSentinel.Cli.csproj \
      -c Release \
      -r "$RID" \
      --self-contained true \
      -o /app/publish && \
    mv /app/publish/OpenApiSentinel.Cli /app/publish/openapi-sentinel

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime

WORKDIR /workspace

COPY --from=build /app/publish /opt/sentinel

# Official .NET 10 Linux images already provide a non-root app user.
# Avoid creating a fixed UID such as 1000 because it can collide with an
# existing user in the base image.
RUN chown "$APP_UID:$APP_UID" /workspace

ENV PATH="/opt/sentinel:${PATH}" \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

USER $APP_UID

ENTRYPOINT ["/opt/sentinel/openapi-sentinel"]
CMD ["--help"]
