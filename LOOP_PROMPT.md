Read `progress.md` and `MASTER_PROMPT.md` completely before doing any work.

Inspect the actual current repository, Git state, latest commits, files, build status, and tests before making changes.

`progress.md` is an append-only historical progress log and may contain older stale snapshots.

When information conflicts, use this priority order:

1. actual current repository state
2. latest entries near the end of progress.md
3. current Git history/status
4. older progress.md entries
5. stale AGENTS.md information

Do not restart, re-scaffold, or redo verified completed work.

Do not downgrade, replace, or rewrite already-working architecture merely to produce activity.

The in-repository MASTER_PROMPT.md Definition of Done may already be substantially or fully complete. Verify the current state before assuming unfinished implementation remains.

Continue the highest-value unfinished OpenAPI Sentinel work.

First fix any concrete:

* build failure
* test failure
* runtime failure
* packaging failure
* CI failure
* correctness bug
* OpenAPI semantic error
* false positive
* false negative
* security issue
* cross-platform issue
* documentation inconsistency
* release-quality problem

before starting unrelated work.

For every meaningful change:

1. implement working code
2. add or update appropriate regression tests
3. run relevant tests
4. run the full build/test suite when appropriate
5. verify representative CLI behavior and generated outputs
6. fix failures before moving on

Keep:

```bash
dotnet build -c Release
dotnet test -c Release
```

green.

Do not weaken or delete valid tests merely to make the suite pass.

Do not invent fake results.

Do not expand the product into unrelated new major features beyond MASTER_PROMPT.md.

If all original in-repository MASTER_PROMPT.md requirements are already complete, focus on high-value commercial hardening instead:

* find concrete semantic compatibility mistakes
* find false positives and false negatives
* test difficult OpenAPI 2.0 / 3.0 / 3.1 / 3.2 edge cases
* test real-world specifications
* improve security robustness
* improve deterministic behavior
* improve cross-platform behavior
* improve CLI usability
* improve report quality
* improve CI/release reliability
* add regression tests for every concrete defect found
* verify documentation against actual behavior

Do not make speculative rewrites without a demonstrated defect or measurable product-quality benefit.

Do not repeatedly attempt external actions that require unavailable credentials or services.

If NuGet credentials, Docker credentials/daemon, marketplace access, payment services, DNS, or another external dependency is unavailable:

* record the blocker accurately in progress.md
* keep the existing abstraction/configuration ready
* continue all other unblocked work
* do not repeatedly retry the same unavailable external operation

Do not expose, print, commit, or store secrets.

At meaningful milestones, update `progress.md` truthfully.

Append a new clearly dated latest-state section rather than rewriting historical evidence unless cleanup is specifically necessary.

The latest progress entry should include:

* completed work
* files changed
* actual current build result
* actual current test count/result
* bugs found and fixed
* unfinished work
* external blockers
* Git status
* exact next recommended action

Before starting another task, check whether it creates real value for a commercially releasable OpenAPI Sentinel product.

If there is no concrete in-repository defect, missing MASTER_PROMPT.md requirement, useful regression test, security issue, interoperability problem, or measurable commercial-quality improvement remaining, do not invent unnecessary features or churn working code.

Instead:

* re-run the most important quality gates if appropriate
* update progress.md with the verified current state
* leave the repository healthy for the next iteration


## Long-running loop safety

This project may run unattended for long periods through OpenCode / Ox Alpha.

### Provider rate limits and transient failures

If OpenRouter, Ox Alpha, or another model/provider returns a rate limit, temporary capacity error, timeout, connection reset, or similar transient provider failure:

* do not enter an aggressive retry loop
* do not repeatedly resend the same expensive operation
* do not treat provider throttling as a product defect
* preserve the current repository state
* if meaningful uncommitted progress exists, update `progress.md`
* leave the current work in a safe resumable state
* allow the outer OpenCode loop/runtime to retry later
* continue only when the execution environment is healthy again

If the same provider/external failure repeats without new information, record it once accurately and avoid wasting tokens on repeated retries.

### Context exhaustion protection

Do not rely on temporary model context, session memory, or the OpenCode Todo panel as the only source of project state.

If the current model context is becoming very large, near its practical limit, or likely to be reset soon:

* do not start another large unrelated task
* finish or stabilize the current small unit of work when safe
* run the most relevant practical verification
* update `progress.md` before context/session termination
* record the exact current implementation state
* record changed files
* record actual build/test results
* record blockers and unfinished work
* record Git status and latest relevant commit SHA
* record the exact next recommended action and useful resume commands

The actual repository, Git history, and `progress.md` must be sufficient for a completely fresh session to resume safely without depending on old Todo items or hidden session state.

### Git milestone backup policy

After a meaningful milestone is fully implemented and verified:

1. run `git status`
2. review the intended diff
3. ensure the relevant build/tests are green
4. ensure unrelated user changes are not accidentally included
5. ensure no secrets, credentials, tokens, private keys, or sensitive local configuration are included
6. create a concise milestone commit
7. push the verified commit to the configured remote when the repository's current branch/workflow allows it
8. if the repository's existing workflow uses a pull request, create or update the appropriate PR instead of bypassing that workflow
9. update `progress.md` with the commit SHA, milestone summary, and verification results

Do not create commits for trivial intermediate edits.

Do not commit or push broken, partially verified, or knowingly failing work.

Do not include unrelated user changes merely to make the working tree clean.

Never force-push.

Never rewrite Git history.

Never delete, reset, clean, checkout away, or otherwise discard unrelated user work.

Preserve the repository's existing branch and pull-request workflow.

If direct push is not appropriate for the current branch or repository workflow, preserve the verified work safely and document the exact Git state in `progress.md` rather than forcing a workflow change.

### Loop ownership and recursion safety

Do not create, schedule, invoke, configure, or start another OpenCode loop, nested loop, recurring loop, automation, background agent, or self-restarting process from inside this task.

The outer user-configured OpenCode loop owns iteration scheduling.

Do not modify loop cadence, loop runtime, loop persistence, or loop configuration merely to keep yourself running.

Do not run `/loop`, `/loop-doctor`, or equivalent loop-management commands from inside the agent task unless the user explicitly asks for loop-management work.

If the current iteration finishes, persist progress normally and allow the existing outer loop to decide when the next iteration starts.

### Safe unattended-operation behavior

When running unattended:

* prefer verified incremental progress over large risky rewrites
* keep the repository buildable/testable at meaningful checkpoints
* do not leave migrations, generated files, or release artifacts half-written when they can be stabilized
* do not disable tests, validation, security checks, or CI safeguards to keep the loop moving
* do not endlessly churn already-working code merely to consume tokens
* prioritize concrete correctness, interoperability, security, reporting, packaging, CLI, CI/release, and commercial-quality improvements

If there is no concrete useful work remaining, verify the repository, update `progress.md`, and leave it healthy rather than manufacturing artificial tasks.

## Final iteration handoff requirement

Before any run ends for any reason, including provider failure, context exhaustion, user interruption, environment shutdown, or completion:

* update `progress.md` if meaningful state changed
* make the latest state unambiguous
* include completed work
* include files changed
* include actual build/test results
* include unfinished work
* include external blockers
* include Git status
* include latest relevant commit/PR information when available
* include the exact next recommended action

A fresh OpenCode/Ox Alpha session must be able to continue safely from repository state + Git history + `progress.md` alone.

Continue making concrete, verified progress toward the most reliable and commercially presentable OpenAPI Sentinel release possible.
