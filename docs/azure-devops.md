# Azure DevOps

A complete pipeline is available at [samples/azure-devops/pipeline.yml](../samples/azure-devops/pipeline.yml).

## Minimal usage

```yaml
steps:
  - task: UseDotNet@2
    inputs:
      version: "10.0.x"

  - script: dotnet tool install --global OpenApiSentinel || true
    displayName: Install OpenAPI Sentinel

  - script: |
      export PATH="$HOME/.dotnet/tools:$PATH"
      set +e
      openapi-sentinel compare \
        "git:origin/main:contracts/openapi.yaml" \
        contracts/openapi.yaml \
        --fail-on high \
        --json  $(Build.ArtifactStagingDirectory)/sentinel/api-diff.json \
        --html  $(Build.ArtifactStagingDirectory)/sentinel/api-diff.html \
        --no-color
      exit $?
    displayName: Compare API contracts

  - task: PublishBuildArtifacts@1
    condition: always()
    inputs:
      pathToPublish: $(Build.ArtifactStagingDirectory)/sentinel
      artifactName: sentinel-reports
```

## Notes

- The failing step blocks the pipeline via exit code 1 (policy violated).
- URL authentication uses environment variables (`headerEnv` config) mapped from secret
  variables — never inline secrets:
  `SENTINEL_SWAGGER_TOKEN: $(SwaggerToken)`.
- Parse the JSON artifact to publish counts as build metadata or gate checks:
  `summary.breaking`, `recommendation.display`.
