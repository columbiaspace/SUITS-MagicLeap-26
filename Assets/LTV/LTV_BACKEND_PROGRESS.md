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

## Files
| File | Purpose |
|------|---------|
| `Assets/LTV/LtvError.cs` | Data model: code, description, priority, IComparable |
| `Assets/LTV/MaxHeap.cs` | Generic max-heap for priority queue |
| `Assets/LTV/LtvErrorQueueService.cs` | MonoBehaviour orchestrator: queue, pop, step, verify, retry |
| `Assets/TSS-API/TssUnityApiService.cs` | Added `GetLtvErrorProcedures()` to parse TSS response |

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
- [x] Retry logic: if TSS says error still active → ResolutionFailed event, reset step to 0
- [x] Events: ErrorChanged, StepChanged, ResolutionFailed, AllErrorsResolved
- [x] `GetCurrentSnapshot()` — dictionary for UI consumption
- [x] `StopDiagnosis()` — cleanup
- [x] Error code → TSS error key mapping (`MapCodeToErrorKey`)

### Phase 4: TSS Integration
- [x] `GetLtvErrorProcedures()` added to TssUnityApiService
- [x] Parses `error_procedures` array from LTV TSS response

## API Reference

### LtvErrorQueueService

**Properties:**
- `CurrentError` — the LtvError currently being worked on
- `CurrentStepIndex` — index into current error's procedures list
- `IsDiagnosisActive` — true while diagnosis session is running
- `IsVerifying` — true while waiting for TSS to confirm resolution
- `RemainingErrors` — count of errors still in the heap
- `RetryCount` — how many times current error failed verification

**Methods:**
- `StartDiagnosis(List<Dict> errorProceduresRaw)` — parse and queue errors, begin
- `StartDiagnosisFromTss()` — fetch from TSS, then StartDiagnosis
- `AdvanceStep()` — move to next instruction; triggers verification when last step reached
- `StopDiagnosis()` — abort and clear state
- `GetCurrentSnapshot()` — dictionary snapshot for UI

**Events:**
- `ErrorChanged(LtvError)` — fired when popping a new error from heap
- `StepChanged(LtvError, int stepIndex)` — fired on each step advance
- `ResolutionFailed(LtvError)` — fired when TSS says error NOT resolved after all steps
- `AllErrorsResolved()` — fired when heap is empty and last error is resolved

### Flow
```
StartDiagnosis → PopNextError → StepChanged(0)
  → AdvanceStep → StepChanged(1)
  → AdvanceStep → StepChanged(2)
  → ...
  → AdvanceStep (last step) → VerifyResolution (poll TSS)
    → Resolved? → PopNextError (or AllErrorsResolved)
    → NOT Resolved? → ResolutionFailed, reset to step 0, re-show all
```

## Status
- **Started**: 2026-03-27
- **Current Phase**: All phases complete (initial implementation)
- **Last Updated**: 2026-03-27
- **Next**: AIA integration (on hold per user), UI hookup
