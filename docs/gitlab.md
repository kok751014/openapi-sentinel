# GitLab CI

A complete configuration is available at [samples/gitlab/gitlab-ci.yml](../samples/gitlab/gitlab-ci.yml).

## Minimal job

```yaml
openapi-sentinel:
  stage: verify
  image: mcr.microsoft.com/dotnet/sdk:10.0
  rules:
    - if: $CI_PIPELINE_SOURCE == "merge_request_event"
  variables:
    GIT_DEPTH: 0   # full clone so `git:` sources can reach origin/main
  before_script:
    - dotnet tool install --global OpenApiSentinel || true
    - export PATH="$HOME/.dotnet/tools:$PATH"
  script:
    - set +e
    - openapi-sentinel compare
        "git:origin/${CI_DEFAULT_BRANCH}:contracts/openapi.yaml"
        contracts/openapi.yaml
        --fail-on high
        --json sentinel/api-diff.json
        --html sentinel/api-diff.html
        --no-color
    - EXIT=$?
    - exit $EXIT
  artifacts:
    when: always
    paths: [sentinel/]
```

## Notes

- Merge-request pipelines only (`rules:` block) keep verdicts attached to the code change.
- `GIT_DEPTH: 0` matters when comparing against `origin/<default branch>` via the
  `git:` input source.
- Artifacts are published even on failure so reviewers can open the HTML report.
