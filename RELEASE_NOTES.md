# OpenAPI Sentinel v1.3.1 release notes

## Highlights

- Compare HTML reports now use two primary sections: contract/result information with a concise summary,
  then one unified changes list.
- HTML generation time is deterministic Taipei UTC+8 in both Traditional-Chinese and English modes.
- Breaking, review-needed and general findings are sorted in one list and start collapsed.
- HTML-only semantic deduplication removes duplicate logical events without merging distinct endpoints,
  HTTP statuses, directions, parameters or properties. Concrete targets remain visible in every card.
- Snapshot status and version recommendations are no longer HTML presentation elements; snapshot safety and
  semantic-version recommendations remain available to their existing workflows and machine/human reports.

## Upgrade notes

No configuration migration is required. The default remains `--html-lang zh-TW` and `--html-lang en`
remains supported. This release changes HTML presentation only: comparison, policy evaluation, JSON,
Markdown, SARIF, JUnit, baselines, suppressions, finding fingerprints and snapshot semantics are unchanged.
