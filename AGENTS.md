# AGENTS.md

## Project purpose
This repository contains a C# rewrite of a Python trading signal generator. The Python implementation under `signals-bot/` serves as the behavioral reference. See `docs/python-system-analysis.md` for the full analysis and `docs/python-reference.md` for the behavioral summary.

## Current phase
Current phase: C# rewrite, implementing features in the order defined in `docs/implementation-order.md`.

## Source of truth
When instructions conflict, prefer:
1. `docs/python-reference.md` for behavioral requirements
2. `docs/python-system-analysis.md` for detailed analysis
3. tests describing intended behavior
4. documented contracts (config, output files, exit codes)

## Core documentation
- `docs/system-overview.md` -- what the system does
- `docs/python-reference.md` -- behaviors to preserve from Python
- `docs/python-system-analysis.md` -- full Python analysis
- `docs/implementation-order.md` -- build sequence for the rewrite
- `docs/features/_feature-template.md` -- template for feature specs

## General rules
- Inspect relevant code before proposing changes.
- Prefer small, reviewable diffs.
- Do not broaden scope without stating it.
- Do not introduce dependencies unless justified.
- Preserve behavioral parity with the Python reference unless explicitly changing it.
- Flag ambiguity instead of inventing product logic.
- All error conditions default to WAIT (fail-safe invariant).

## Planner role
For non-trivial tasks:
1. Summarize the relevant Python behavior from the reference docs
2. Identify affected files
3. Produce a numbered plan
4. Identify risks
5. Separate required work from optional improvements

## Executor role
- Implement only the current approved feature
- Keep changes minimal
- Run relevant checks
- Summarize what changed and why

## Reviewer role
Check for:
- Correctness against Python reference behavior
- Regressions
- Edge cases
- Contract drift (output formats, exit codes, file formats)
- Unnecessary abstraction
- Oversized diffs
- Numeric precision differences (especially RSI/SMA)
- DateTime/timezone mismatches
- Serialization differences (enum string formats)

## Validation
For each substantial task, report:
1. Plan
2. Files changed
3. Python behavior matched
4. Checks run
5. Known risks/gaps

## Working style
- Prefer reversible changes
- Prefer explicit tradeoffs
- Prefer behavior preservation over premature optimization
