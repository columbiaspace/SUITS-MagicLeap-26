# LTV Backend Script Progress

## Overview
Priority-queue-based error orchestrator for LTV system diagnostics. Fetches `error_procedures` from TSS, triages by NASA's 4-digit error code priority, and walks the astronaut through repairs one error at a time with TSS verification.

## Priority Scoring (NASA Format)
```
Error code: [Criticality 0-4][Subsystem 0-9][ID][ID]
Priority = criticality * 10 + subsystem
```
- Higher criticality + higher subsystem = fix first
- Error identifiers (last 2 digits) are NOT priority-relevant
- Equal criticality: higher subsystem wins (2700 before 2600)

## Files
| File | Purpose |
|------|---------|
| `Assets/LTV/LtvError.cs` | Data model: code, description, priority, IComparable |
| `Assets/LTV/MaxHeap.cs` | Generic max-heap for priority queue |
| `Assets/LTV/LtvErrorQueueService.cs` | MonoBehaviour orchestrator: queue, pop, step, verify, retry |
| `Assets/TSS-API/TssUnityApiService.cs` | Added `GetLtvErrorProcedures()` to parse TSS response |

## Error Code to TSS Key Mapping
| Code | TSS Error Key | Description |
|------|---------------|-------------|
| 0000 | recovery_mode | Recovery Mode |
| 4155 | power_distribution | Main Power Bus Error |
| 4761 | dust_sensor | Dust Sensor Error |
| 2129 | fuse | Backup Fuse Error |
| 2130 | nav_system | Nav System Error |
| 2131 | lidar_system | Lidar System Error |
| 2132 | comms | Comms System Error |
| 3700 | electronic_heater | Heat Generating Reaction Error |
| 2900 | power_subsystem_bus | Power Subsystem Bus Error |

> **Note**: Codes 3700 and 2900 are placeholders. Update in `MapCodeToErrorKey()` once NASA confirms actual codes.

## Phases

### Phase 1: Data Model
- [x] `LtvError` class with code, description, needs_resolved, procedures, priority score
- [x] Priority parsing from 4-digit error code (criticality * 10 + subsystem)

### Phase 2: Max-Heap
- [x] Generic max-heap implementation
- [x] Insert, ExtractMax, Peek, Count, Clear

### Phase 3: LtvErrorQueueService
- [x] MonoBehaviour orchestrator
- [x] `StartDiagnosis(List<Dict>)` — parse raw JSON, build heap
- [x] `StartDiagnosisFromTss()` — fetch from TssUnityApiService
- [x] `PopNextError()` — extract max priority, set as current
- [x] `AdvanceStep()` — move through instructions one by one
- [x] TSS verification gate after all steps complete (coroutine polling)
- [x] Retry logic: if TSS says error still active, ResolutionFailed event, reset step to 0
- [x] Max retry cap (default 3) with MaxRetriesExceeded event, skips to next error
- [x] Events: ErrorChanged, StepChanged, ResolutionFailed, MaxRetriesExceeded, AllErrorsResolved
- [x] `GetCurrentSnapshot()` — dictionary for UI consumption
- [x] `StopDiagnosis()` — cleanup
- [x] Error code to TSS error key mapping (`MapCodeToErrorKey`)

### Phase 4: TSS Integration
- [x] `GetLtvErrorProcedures()` added to TssUnityApiService
- [x] Parses `error_procedures` array from LTV TSS response

## Changelog

### 2026-03-27 — Initial Implementation
- Created LtvError, MaxHeap, LtvErrorQueueService
- Added GetLtvErrorProcedures() to TssUnityApiService

### 2026-03-27 — Critical Safety Fixes (code review)
- **IsErrorStillActive fail-safe**: Now returns `true` (assume active) when TSS unavailable, error key unmapped, or data missing. Prevents falsely declaring errors resolved during connectivity loss.
- **StartVerification race condition**: Fixed sequence where `verifying` flag was immediately reset to false by `StopVerification()` call. Now stops old coroutine without touching the flag.
- **Missing error key mappings**: Added `electronic_heater` (3700) and `power_subsystem_bus` (2900).
- **Max retry cap**: Added `maxRetries = 3` (configurable in Inspector). After exceeding, fires `MaxRetriesExceeded` event and skips to next error.
- **Null guard**: `HandleResolutionFailed` now checks `currentError != null`.
- **Coroutine capture**: `VerifyResolution` captures local ref to `currentError` to avoid field mutation during yield.
- **Verification timing**: `yield return wait` now happens before incrementing `elapsed` so timeout matches wall time.

## Status
- **Started**: 2026-03-27
- **Current Phase**: All phases complete, safety fixes applied
- **Last Updated**: 2026-03-27
- **Next**: AIA integration (on hold), frontend UI hookup
