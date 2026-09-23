# SDD ledger — plan: docs/superpowers/plans/2026-09-23-multi-strategy-routing-recovery.md

Execution mode: Native (inline), selected by user. No subagents used.

Setup:
- Workspace: current feature branch `codex/periodic-queue-cycle`; not main/master.
- Plan: `docs/superpowers/plans/2026-09-23-multi-strategy-routing-recovery.md`.
- Spec: `docs/superpowers/specs/2026-09-23-multi-strategy-live-routing-recovery-design.md`.
- Skills read: executing-plans, test-driven-development, workspace kgi-strategy-development, framework-contract, integration-checklist.
- `task-start`/`sdd-workspace` helper scripts referenced by executing-plans are unavailable in this environment; this ledger and per-task briefs will be maintained directly here. Risk: no automatic extraction guard, mitigated by task-by-task plan review and explicit test outputs.
- Pre-flight shared interfaces: Task 1 → Tasks 2, 5, 6, 7, 8, 9: checkpoint/StrategyRunGeneration consumed by journal, common order lifecycle, strategy adapters and restore. Reviewed: plan names `StrategyRunGeneration`; strategy skill calls it run generation; spec names the same concept `StrategyGeneration`.
- Pre-flight shared interfaces: Task 2 → Tasks 3, 8: owner key, aliases, fill dedupe, replay snapshot consumed by router and recovery. No signature conflict found.
- Pre-flight shared interfaces: Task 3 → Tasks 4, 8, 9: routed executions feed ledger; router and facade feed service/recovery/UI. No signature conflict found.
- Pre-flight shared interfaces: Task 4 → Tasks 8, 9: account reconciliation snapshots consumed by recovery/UI. No signature conflict found.
- Pre-flight shared interfaces: Task 5 → Tasks 6, 7, 9: common lifecycle/checkpoint helper consumed by six strategies and capability gate. No signature conflict found.
- Pre-flight ruling: use `StrategyRunGeneration` as the concrete C# name for the generation concept. This matches the workspace Skill wording and avoids collision with existing order `Generation`; cost if wrong: adapter or serialized field renaming if external consumers require the spec's shorter spelling.
- Task 1 ruling: default runner generation is `1` for deterministic simulation/replay; live execution must inject a persisted monotonic generation in Task 8. A process-global counter broke the existing deterministic lifecycle replay test. Cost if wrong: simulations reusing the same instance name across separate hosts share the default value, which has no broker ownership effect; live callers must never rely on this default.

## Task 1: Strategy run recovery identity

- Task 1: Ruling: default runner generation is `1` for deterministic simulation/replay; live execution must inject a persisted monotonic generation in Task 8. A process-global counter broke the existing deterministic lifecycle replay test. Cost if wrong: simulations reusing the same instance name across separate hosts share the default value, which has no broker ownership effect; live callers must never rely on this default.
- Task 1: complete (commits c99ed44..ba92e84, tests: `dotnet run --project tests/LiveStrategyRecoverySmokeTests/LiveStrategyRecoverySmokeTests.csproj -c Release --no-restore` → PASS; `dotnet run --project tests/StrategyFrameworkSmokeTests/StrategyFrameworkSmokeTests.csproj -c Release --no-restore` → PASS).

## Task 2: Immutable ownership journal

- Task 2: complete (commits ba92e84..2a4fcb4, tests: `dotnet run --project tests/LiveStrategyRecoverySmokeTests/LiveStrategyRecoverySmokeTests.csproj -c Release --no-restore` → PASS; new route isolation, alias uniqueness, ExecId/watermark dedupe, submission-unknown restart, truncated-tail repair, and complete-record corruption quarantine assertions passed).

## Task 3: Central broker event router and strategy facade

- Task 3 ruling: keep the per-run execution facade as a nested `RoutedGateway` owned by `LiveStrategyOrderRouter`, rather than adding another standalone public wrapper. The router owns subscription, route registration, event delivery and facade disposal as one lifecycle. Cost if wrong: future facade reuse outside the router will require extraction.
- Task 3: implementation and focused tests complete; recovery/router, order-management, and strategy-framework smoke suites passed. Test outputs used isolated `BaseOutputPath` directories because unrelated live test processes held default build outputs open. Commit follows.
