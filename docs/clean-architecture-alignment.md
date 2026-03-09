# Clean Architecture Alignment

## Summary

The current codebase is best described as a layered, pipeline-oriented CLI application with some Clean Architecture traits.

- Current rating: `~7/10` for Clean Architecture
- Expected rating after a focused boundary refactor: `~9/10`
- This is not a DDD refactor
- This does not need to be a rewrite if scope stays tight

The main reason it is not "proper" Clean Architecture today is not the dependency graph. The dependency direction is mostly fine. The gap is that some orchestration and I/O concerns still live in the wrong layers.

## What Is Already Clean Enough

- `BlowingCandles.Domain` is the inner core and has no project dependencies.
- `BlowingCandles.Application` depends on `BlowingCandles.Domain`.
- `BlowingCandles.Infrastructure` depends on `BlowingCandles.Domain` and implements infrastructure-facing seams such as market data, calendar access, and state storage.
- `BlowingCandles.Cli` is the outer entry point and composition root.

This gives the application the overall shape of Clean Architecture already.

## Why The Current Design Is Only "Clean-ish"

### 1. Command orchestration lives in the CLI layer

`src/BlowingCandles.Cli/Handlers/RunCommandSupport.cs` does more than adapt CLI input. It assembles the core workflow by choosing concrete implementations and constructing:

- `EarningsGate`
- `TechnicalScorer`
- `TradeGovernor`
- `SignalPipeline`

That logic is application orchestration and would sit in application use cases in a stricter Clean Architecture setup.

### 2. File output sits in the Application project

`src/BlowingCandles.Application/OutputRenderer.cs` performs direct filesystem writes with `File.WriteAllLines` and `File.WriteAllText`.

In stricter Clean Architecture, the application layer would express output needs via interfaces or result models, and infrastructure would implement the actual file writing.

### 3. CLI handlers still know concrete infrastructure details

`RunRealtimeHandler` and `RunAsOfHandler` directly coordinate config loading, output writing, audit writing, and path shaping. That is workable, but it leaves the CLI layer thicker than necessary.

## Smallest Refactor To Reach About 9/10

The smallest useful refactor is to tighten boundaries, not redesign the system.

### A. Move run orchestration into Application use cases

Introduce one or more application-level use cases, for example:

- `RunSignalsUseCase`
- or `RunRealtimeUseCase`, `RunAsOfUseCase`, `RunRangeUseCase`

These use cases should own:

- pipeline assembly or pipeline invocation
- application workflow sequencing
- coordination of output/audit/state ports

The CLI layer should become thin: parse args, load config, call a use case, print summary text, return exit code.

### B. Move direct file-writing behind ports

Replace direct writes from `OutputRenderer` with application-facing interfaces such as:

- `ISignalOutputWriter`
- `IAuditWriter`

Infrastructure would then provide the concrete file-backed implementations.

The application layer would depend on the interfaces, not on file APIs.

### C. Keep `Program.cs` as the composition root

`Program.cs` should still be the place where concrete implementations are wired together. That part does not need a major change.

## What Should Not Change

To keep the refactor small and low-risk, the following should remain unchanged:

- domain services and their behavior
- Python parity rules
- output contracts
- state semantics
- CLI command names and arguments
- fail-safe WAIT defaults

This should be a boundary cleanup, not a business-logic rewrite.

## Python Behavior To Preserve

This refactor must preserve the existing Python-parity behavior already captured elsewhere in the repo. The important point is that this is an architectural cleanup, not a behavior change.

- Earnings gate behavior must remain unchanged.
- Technical scoring math must remain unchanged, including SMA-based RSI.
- Trade governor ordering and policy behavior must remain unchanged.
- All fail-safe error paths must continue to default to WAIT.
- Output file formats must remain unchanged.
- Live vs simulation file isolation must remain unchanged.

Concretely, these contracts must survive the refactor unchanged:

- `signals.txt` continues to use prefixed enum strings like `Action.BUY`
- `signals.json` continues to use plain enum strings like `BUY`
- audit JSONL continues to be append-only and use prefixed enum strings
- state file shape remains `day`, `buys_today`, `last_buy_at`
- simulation continues to use `sim_*` state/audit files and `asof_*.signals.*` output files

## Affected Files

### Files likely to change

- `src/BlowingCandles.Cli/Handlers/RunCommandSupport.cs`
- `src/BlowingCandles.Cli/Handlers/RunRealtimeHandler.cs`
- `src/BlowingCandles.Cli/Handlers/RunAsOfHandler.cs`
- `src/BlowingCandles.Cli/Handlers/RunRangeHandler.cs`
- `src/BlowingCandles.Cli/Program.cs`
- `src/BlowingCandles.Application/OutputRenderer.cs`
- `src/BlowingCandles.Application/SignalPipeline.cs`

### Files likely to be added

- one or more application use-case classes under `src/BlowingCandles.Application/`
- application-facing output/audit interfaces under `src/BlowingCandles.Application/` or `src/BlowingCandles.Domain/Interfaces/`
- infrastructure implementations for signal output and audit writing under `src/BlowingCandles.Infrastructure/`

### Tests likely to change or be added

- `tests/BlowingCandles.Application.Tests/`
- `tests/BlowingCandles.Infrastructure.Tests/`
- `tests/BlowingCandles.CrossValidation.Tests/`

## Expected Scope

If kept disciplined, this should be a moderate refactor, not a large one.

Likely characteristics:

- mostly moving orchestration responsibilities out of `Cli`
- introducing a small number of application use-case classes
- introducing a small number of output/audit interfaces
- moving file-writing implementations to `Infrastructure`
- keeping existing domain logic intact

This should not require:

- new architectural patterns like mediator/event bus
- a dependency injection framework overhaul
- a domain model redesign
- a DDD conversion

## Main Risks

- accidentally changing output file behavior while moving file-writing responsibilities
- accidentally changing live vs simulation path isolation
- leaking infrastructure details back into application use cases
- broadening the scope beyond boundary cleanup

## Concrete Step-By-Step Plan

### Phase 0. Freeze the contracts before moving anything

1. Re-read the Python parity constraints in `docs/python-reference.md`.
2. Re-read the existing orchestration contract in `docs/features/command-orchestration.md`.
3. Identify the exact current responsibilities of:
   - `RunCommandSupport`
   - `RunRealtimeHandler`
   - `RunAsOfHandler`
   - `RunRangeHandler`
   - `OutputRenderer`
   - `JsonlAuditWriter`
4. Confirm which current tests already protect:
   - output file contents
   - audit JSONL format
   - live vs simulation path behavior
   - run-range accumulation behavior
5. Add any missing characterization tests before moving code.

Phase 0 goal: make current behavior hard to accidentally change.

### Phase 1. Introduce application use cases without deleting current wiring

1. Add an application use-case class for the run workflow.
   Recommended starting point: a single `RunSignalsUseCase`.
2. Keep the first version narrow.
   It should accept already-resolved dependencies and execute the existing signal pipeline flow.
3. Do not move path-shaping logic yet.
4. Do not move file writing yet.
5. Update or add tests that verify the use case returns the same `FinalSignal` results as the current handler path.

Phase 1 goal: create an application orchestration seam without changing outputs.

### Phase 2. Move workflow orchestration out of `RunCommandSupport`

1. Move the signal-running workflow from `RunCommandSupport.RunSignalsCore` into the new application use case.
2. Keep dependency construction in the outer layer for now if needed.
3. Change `RunCommandSupport` to either:
   - become a thin adapter around the use case, or
   - disappear if the handlers can call the use case directly.
4. Ensure the CLI handlers stop owning workflow sequencing.
5. Keep console messages unchanged.

Phase 2 goal: CLI stops orchestrating the signal run itself.

### Phase 3. Extract output and audit ports

1. Define application-facing abstractions for writing outputs.
   Recommended minimum set:
   - `ISignalOutputWriter`
   - `IAuditWriter`
2. Decide where those interfaces belong.
   Recommendation: keep them in `Application` unless there is a strong reason to lift them into `Domain`.
3. Refactor application flow to depend on those interfaces instead of direct file APIs.
4. Keep payload shape identical to the current output contracts.
5. Add focused tests around the request/response shape passed into those ports.

Phase 3 goal: application no longer performs direct file I/O.

### Phase 4. Move concrete file-writing into Infrastructure

1. Move or replace `OutputRenderer` with a file-backed infrastructure implementation.
2. Keep `JsonlAuditWriter` in infrastructure and adapt it to the new `IAuditWriter` port if needed.
3. Ensure directory creation behavior remains unchanged.
4. Ensure newline and serialization behavior remain unchanged.
5. Re-run output-format tests after every move.

Phase 4 goal: all filesystem concerns live in infrastructure.

### Phase 5. Thin the CLI handlers

1. Make `RunRealtimeHandler` responsible only for:
   - loading config
   - creating clock
   - calling the use case
   - printing console summary
   - returning exit code
2. Make `RunAsOfHandler` responsible only for:
   - loading config
   - creating fixed clock
   - calling the use case
   - printing console summary
   - returning exit code
3. Make `RunRangeHandler` responsible only for:
   - loading config
   - iterating dates if that loop remains a CLI concern, or delegating if moved into application
   - printing console summary
   - returning exit code
4. Keep `Program.cs` as the composition root.
5. Avoid introducing mediator/event-bus/container complexity.

Phase 5 goal: CLI becomes a thin delivery mechanism.

### Phase 6. Clean up dead wiring

1. Remove any now-unused helper methods from `RunCommandSupport`.
2. Delete `RunCommandSupport` entirely if it no longer adds value.
3. Remove stale constructor overloads or test seams that are no longer necessary.
4. Keep diffs reviewable by deleting only after replacements are covered by tests.

Phase 6 goal: finish the cleanup after the new structure is stable.

### Phase 7. Validate parity and contracts

1. Run application tests.
2. Run infrastructure tests.
3. Run cross-validation tests against the fixture golden outputs.
4. Verify these invariants explicitly:
   - same signal decisions
   - same output file contents
   - same audit line contents
   - same simulation/live path isolation
   - same exit-code behavior
5. Review the final diff for accidental scope growth.

Phase 7 goal: prove the refactor changed structure, not behavior.

## Required Work

- introduce application use-case orchestration
- remove core workflow orchestration from `Cli`
- remove direct file-writing from `Application`
- keep file-backed output implementations in `Infrastructure`
- preserve all current behavior and contracts with tests

## Optional Improvements

These are explicitly out of scope for the first pass unless a real need appears:

- splitting into one dedicated use case per command if a single run use case is sufficient
- redesigning domain services
- adding mediator patterns
- adding a full DI-container abstraction layer
- changing config loading ownership
- renaming commands or changing CLI syntax
- redesigning output DTOs beyond what the ports need

## Why This Should Not Be A Big Refactor

This should remain moderate if the team follows two rules:

1. Move responsibilities, do not redesign behavior.
2. Add seams only where current boundaries are clearly wrong.

The signal logic already exists and is already tested. The likely work is mostly:

- relocating orchestration
- introducing a small number of interfaces
- swapping concrete file I/O for infrastructure implementations behind those interfaces
- deleting the old wiring once the new flow is proven

That is materially smaller than a domain rewrite or a DDD conversion.

## Review Checklist For The Refactor

- Does `Cli` only adapt input/output and call use cases?
- Does `Application` avoid direct file I/O?
- Does `Infrastructure` own concrete file writing and adapter implementations?
- Are output contracts unchanged?
- Are live/simulation path rules unchanged?
- Are fail-safe WAIT defaults unchanged?
- Is the diff mostly boundary movement rather than logic churn?

## Success Criteria

The refactor is successful if:

- `Cli` becomes thin and mostly adapter-only
- application orchestration no longer lives in `RunCommandSupport`
- direct filesystem writes no longer live in `BlowingCandles.Application`
- all current behavior and contracts stay unchanged
- the architecture is credibly closer to textbook Clean Architecture

## Bottom Line

This should not be a big refactor unless scope drifts.

If the work is limited to moving orchestration into application use cases and pushing file I/O behind infrastructure ports, the result should be materially cleaner without disturbing the core signal logic. That should move the architecture from roughly `7/10` to roughly `9/10` on a Clean Architecture scale.
