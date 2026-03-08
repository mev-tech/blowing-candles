# Technical Scoring — Fix Specification

## Summary of Issues

The production code is correct and requires no changes. The test suite is missing 4 test scenarios explicitly defined in the feature specification (scenarios #3, #4, #13, #15).

## Required Changes

No changes to production code. Add 4 missing test methods to `TechnicalScorerTests.cs`.

### 1. Score=0 (all conditions missed)

Construct prices where close < SMA50, close < SMA200, SMA50 < SMA200, and RSI > 50. Verify Action=SELL, Score=0. Example: 150 prices at 200, then 50 prices descending so close is well below both SMAs, with RSI > 50 from a recent uptick that still leaves the close below both averages.

### 2. Score=80 via RSI oversold without golden cross

Construct prices where close > SMA50 (+20), close > SMA200 (+20), SMA50 < SMA200 (no golden cross, +0), RSI <= 40 (+40) = 80. Verify Action=BUY, Score=80. Example: early prices high enough to keep SMA200 above SMA50, with the last 15 prices producing RSI=40 while close remains above both SMAs.

### 3. RSI just above neutral boundary (50.01)

Construct 15 prices producing RSI slightly above 50. Verify RSI component adds 0 points. This confirms the `<=` boundary at 50 excludes values above it.

### 4. All losses (RSI=0)

Construct 15 prices that are strictly decreasing. Verify RSI=0 and no divide-by-zero. Verify the totalGain==0 path returns RSI=0.

## Affected Modules or Files

| File | Change |
|------|--------|
| `tests/BlowingCandles.Domain.Tests/TechnicalScorerTests.cs` | Add 4 test methods |

## Edge Cases to Address

- Score=0 confirms SELL is emitted when every scoring condition fails.
- Score=80 without golden cross confirms BUY can be reached through the RSI oversold path alone (combined with SMA conditions).
- RSI=50.01 confirms the neutral boundary is exclusive above 50.
- RSI=0 (all losses) confirms no arithmetic error when totalGain is zero and totalLoss is positive.

## Test Updates Required

Add to `TechnicalScorerTests.cs`:

1. `Score_AllConditionsMissed_ReturnsSellWithScoreZero` — 200 prices where close < SMA50, close < SMA200, SMA50 < SMA200, RSI > 50. Assert Action=SELL, Score=0, empty or no reason codes.
2. `Score_BuyViaRsiOversoldWithoutGoldenCross_ReturnsBuyWithScore80` — 200 prices where close > SMA50, close > SMA200, SMA50 < SMA200, RSI <= 40. Assert Action=BUY, Score=80, Reason contains `ABOVE_SMA50,ABOVE_SMA200,RSI_OVERSOLD` but not `GOLDEN_CROSS`.
3. `Score_RsiJustAboveNeutralBoundary_AddsZeroRsiPoints` — 200 prices producing RSI slightly above 50. Assert RSI > 50 and RSI component contributes 0 points to score.
4. `Score_AllLosses_ReturnsRsiZero` — 200 prices with the last 15 strictly decreasing. Assert Rsi14=0, no exception thrown.
