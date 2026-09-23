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

## Task 4: Strategy/account ledgers and execution idempotency

- Task 4 ruling: legacy lifecycle callbacks that omit `StrategyRunGeneration` are stamped with the runner's own generation at the serialized callback boundary; explicit mismatched generations remain rejected. This preserves older simulation callbacks without allowing them into the wrong run's ledger. Cost if wrong: an external producer that intended generation zero as a distinct owner cannot express that via the legacy callback shape and must adopt explicit run generation.
- Task 4: strategy and recovery smoke tests pass, including opposite-side A/B virtual positions remaining +1/-1 while aggregate is zero; duplicate execution charges commission once; manual external fills reconcile separately; mapper keeps route owner while resolving broker Open/Close from account net; generation-aware checkpoint restores position. Warnings remain from existing package compatibility, unavailable NuGet vulnerability feed, and existing nullable/unused-event diagnostics.

## Task 5: Shared managed-order lifecycle and checkpoint helper

- Task 5 ruling: `ManagedStrategyOrderBook` keeps terminal orders indexed rather than deleting them, so a late fill can still resolve to the exact client order after cancel acknowledgement. Cancelled→Filled late transitions are accepted; stale working acknowledgements after cancel and any transition after Filled/Rejected are rejected. Cost if wrong: memory grows with completed orders for the lifetime of a run; bounded compaction must wait until broker/recovery guarantees no late fills.
- Task 5: shared book, identity-scoped ID generation, exact lifecycle update, capture/restore and an explicit live-capability validator pass recovery tests; framework smoke suite passes. The host exposes an opt-in live capability guard; production wiring is deferred until all six strategies satisfy the contract (Tasks 6–7), then enabled centrally in Task 9.

## Task 6: Migrate MR, MODWT and Nikkei Alpha

- Task 6 ruling: keep legacy direct `OnExecution` test/simulation callbacks usable when they omit a cycle id by allocating the next local cycle only if this run has no current cycle; routed live callbacks retain their exact CycleId. Cost if wrong: direct callers must not replay an old unscoped execution after a later cycle has begun; managed live routing always supplies cycle and run identity.
- Task 6: all three strategies now implement managed order, exact-cancel observer and recovery participant contracts; order requests carry client/cycle/run identity; exit/cancel/reject lifecycle updates remain order-scoped; checkpoints capture indicators/windows, current position-protection/cooldown flags and working order book. Recovery, strategy-framework, and Nikkei smoke suites pass; serialized round-trip was tested after market samples populated state. Signal equations and parameter defaults are unchanged.

## Task 7: Migrate Mini Lead, OrderBook Trend and PQC

- Task 7: all three now implement managed order observation and versioned recovery checkpoints; PQC preserves order repricing `Generation` separately from `StrategyRunGeneration` and captures direction samples, both live legs, one-leg timer, cooldown and stop state. All three restore paused and require explicit Resume. Mini Lead, OrderBook, PQC and aggregate recovery smoke suites pass. Strategy parameter defaults and pricing logic remain unchanged.
