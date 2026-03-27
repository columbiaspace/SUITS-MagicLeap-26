# AIA LTV Procedure Usage (Short)

## Goal
Use TSS LTV errors to select the correct procedure, guide the astronaut step-by-step, and only move on when the current error flow is actually resolved.

## Where Data Comes From
1. Live error/telemetry state: `TssUnityApiService` (`GetLtv()`, `GetLtvErrors()`).
2. Procedure content: `Assets/TSS-API/Resources/ltv_procedures.json`.
3. Runtime workflow logic: `Assets/LTV/LtvInstructionService.cs`.

## How AIA Should Call It
1. Get a reference to `LtvInstructionService`.
2. Subscribe to `InstructionUpdated` for UI refresh/voice updates.
3. Read current instruction using `GetCurrentInstruction()`.
4. For manual steps only, call `MarkCurrentStepDone()`.
5. For criteria-gated steps (`next_step_has_criteria == true`), do not call mark-done; wait for telemetry to satisfy criteria.

## API/Endpoint Equivalents
1. `GET /api/v1/ltv/errors`
2. `GET /api/v1/procedures/ltv/{procedure_id}`
3. `GET /api/v1/procedures/ltv/{procedure_id}/status` (available, but LTV app flow is driven by `LtvInstructionService`)

## Strict Workflow (Implemented)
1. Fetch `ltv.errors` first.
2. Pick highest-priority active error rule.
3. Lock to that error/procedure (`strictSingleErrorFlow`) so no mid-flow bouncing.
4. Show the current `next_instruction`.
5. Manual step: astronaut presses `Mark Done`, service advances.
6. Criteria step: service advances only when telemetry matches criteria (commonly `ltv.errors.<error> == false`).
7. When procedure is complete, service moves to next active error by priority.

## ERM Delegation Rule
1. If `ERM` is active and its current checkpoint is blocked by an active `ltv.errors.<x>`, the service temporarily delegates to that specific procedure.
2. It returns to ERM only after that error flag becomes `false`.

## Fields AIA Should Render
1. `error_key`
2. `procedure_id`
3. `next_step_id`
4. `next_step_has_criteria`
5. `next_instruction`
6. `hint`
7. `voice_short`
8. `procedure_complete`

## Minimal Call Pattern
```csharp
void OnEnable()
{
    ltvService.InstructionUpdated += OnInstructionUpdated;
    ltvService.RefreshNow();
}

void OnInstructionUpdated(Dictionary<string, object> data)
{
    // Render next_instruction, hint, and step/procedure metadata in AIA.
}

public void OnMarkDonePressed()
{
    // Only for manual steps (next_step_has_criteria == false)
    ltvService.MarkCurrentStepDone();
}
```
