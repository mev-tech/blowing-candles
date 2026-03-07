# Implementation Order

This document describes the order in which major system capabilities should be implemented in the C# rewrite. Each phase builds on the previous one and adds testable functionality.

## Guiding Principles

- Start with capabilities that are deterministic and do not require external network access.
- Build shared infrastructure (config, state, audit) early so later phases can reuse it.
- Defer Yahoo Finance integration until the internal logic is solid and testable.
- Each phase should produce a working, testable capability.

## Phase 1: Project Scaffold and Calendar Validator ✅

**Status: COMPLETED**

**Capabilities:** Check Calendar command

**What was built:**
- .NET solution structure (Domain, Application, Infrastructure, CLI projects)
- YAML config loader
- Earnings calendar reader
- Clock abstraction (for deterministic testing of date comparisons)
- Check Calendar CLI command with stdout and exit-code parity

**Validation:** Golden tests comparing stdout and exit codes against Python reference output.

## Phase 2: Audit Analysis

**Capabilities:** Stats Periods command

**What to build:**
- JSONL audit log reader
- Holding period calculation logic
- Stats Periods CLI command

**Why second:** Read-only, deterministic, validates the audit file contract that later phases will write to.

**Validation:** Compare output against known audit fixtures.

## Phase 3: Shared Infrastructure

**Capabilities:** Config loading, state persistence, audit writing, output rendering

**What to build:**
- Consolidated config loader (extending Phase 1 bootstrap)
- File-backed state store (JSON read/write with day-reset logic)
- JSONL audit writer with correct enum string formatting
- Output renderers for signals.txt and signals.json

**Why third:** Every remaining capability depends on config, state, and output infrastructure. Building it here avoids duplication in later phases.

**Validation:** Unit tests for state reset behavior, enum serialization, and output formatting.

## Phase 4: Trade Governor

**Capabilities:** Signal merging, policy enforcement

**What to build:**
- Trade Governor domain logic (news gating, market action pass-through, buy limits, cooldown)
- State integration for buy tracking

**Why fourth:** Pure domain logic that can be tested with constructed inputs. Does not require Yahoo Finance or real market data.

**Validation:** Focused tests for each gate (missing news, stale news, NO_TRADE, WAIT, TRADE_OK pass-through) and policy constraint (max buys, cooldown).

## Phase 5: Earnings Gate

**Capabilities:** News sentinel / earnings proximity checking

**What to build:**
- Local calendar parsing and earnings proximity logic
- Yahoo Finance earnings date fallback (behind an interface)
- Reuse calendar reader from Phase 1

**Why fifth:** Depends on config infrastructure from Phase 3. Local calendar path is deterministic and testable; Yahoo fallback is network-dependent and should be behind an interface.

**Validation:** Unit tests with fixture calendars. Integration tests for Yahoo fallback are optional and should use captured responses.

## Phase 6: Technical Scoring

**Capabilities:** Market analyst / SMA and RSI scoring

**What to build:**
- Daily price retrieval behind an interface
- SMA50, SMA200, RSI14 calculations (using simple moving averages, not EMA)
- Scoring and signal generation logic

**Why sixth:** Most complex numerical logic. Requires careful validation against Python output. Yahoo Finance adapter is network-dependent.

**Validation:** Unit tests with captured price fixtures. Verify SMA and RSI values match Python output to sufficient precision. Test boundary conditions (score exactly 80, RSI exactly 40/50).

## Phase 7: Command Orchestration

**Capabilities:** Run Realtime, Run As-Of, Run Range commands

**What to build:**
- Run As-Of command (deterministic with fixed date, implement first)
- Run Realtime command (wall-clock dependent)
- Run Range command (batch loop, in-process rather than subprocess)
- Full pipeline wiring: config -> earnings gate -> technical scoring -> trade governor -> output

**Why seventh:** Ties everything together. All components must be working before orchestration is meaningful.

**Validation:** End-to-end tests comparing full output (signals.txt, signals.json, audit JSONL, state.json) against Python reference output for the same inputs.

## Phase 8: Finalization

**Capabilities:** Docker packaging, documentation, operational readiness

**What to build:**
- Dockerfile for the C# application
- Operational documentation
- Final validation against Python reference across multiple scenarios

**Validation:** Run both Python and C# implementations against the same inputs and compare all outputs.

## Decision Points

The following decisions should be made before or during the indicated phase:

| Decision | Phase | Description |
|----------|-------|-------------|
| .NET target version | 1 ✅ | .NET 8 LTS |
| Test framework | 1 ✅ | xUnit |
| YAML library | 1 ✅ | YamlDotNet |
| JSON serializer | 3 | System.Text.Json or Newtonsoft.Json |
| State reset behavior | 4 | Preserve real-clock reset or fix to use as_of |
| Run Range architecture | 7 | In-process loop or subprocess per day |
