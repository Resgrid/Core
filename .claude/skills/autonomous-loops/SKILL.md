---
name: autonomous-loops
description: >
  Autonomous iteration loops for .NET development: build-fix, test-fix, refactor,
  and scaffold loops. Each loop has bounded iterations, progress detection, and
  fail-safe guards that prevent infinite retries and wasted tokens. Load this skill
  when Claude needs to fix build errors, fix failing tests, perform multi-step
  refactoring, scaffold a new feature, or when the user says "fix the build",
  "make the tests pass", "refactor this", "scaffold", "generate and verify",
  "keep going until it works", "autonomous", or "loop".
---

# Autonomous Loops

## Core Principles

1. **Bounded iteration, always** — Every loop has a maximum iteration count. Default is 5, hard cap is 10. No loop runs forever. If 5 iterations cannot solve a build error, the problem needs human judgment, not a 6th attempt at the same approach.

2. **Progress tracking or exit** — Each iteration must make measurable progress: fewer errors, fewer failing tests, fewer warnings. If an iteration produces the same error count as the previous one, the loop exits with a STUCK status. Retrying without progress is token waste.

3. **Fail-safe guards are non-negotiable** — Loops exit on: max iterations reached, no progress detected, critical error encountered, more errors introduced than fixed, or user interruption. These guards exist to prevent the most common failure mode: Claude stubbornly retrying the same broken approach 20 times.

4. **Transparency at every iteration** — Report what changed and why after each iteration. The user should be able to follow the loop's reasoning without reading every file. "Iteration 3: fixed CS0246 by adding `using System.Text.Json`, 2 errors remain" is transparent. Silently modifying files is not.

5. **Atomicity per iteration** — Each iteration's changes should leave the codebase in a valid state (or at least no worse than before). Never make partial changes that depend on a future iteration succeeding. If iteration 3 fails, the code should still be in the state that iteration 2 left it in.

## Patterns

### Build-Fix Loop

The most common loop. Fix compilation errors iteratively until `dotnet build` succeeds.

```
BUILD-FIX LOOP:
  max_iterations = 5
  previous_errors = []

  for iteration in 1..max_iterations:
    result = dotnet build [project/solution]

    if result.exit_code == 0:
      report "BUILD PASS after {iteration} iteration(s)"
      return PASS

    errors = parse_errors(result.output)

    if errors == previous_errors:
      report "STUCK — same {len(errors)} error(s) after fix attempt"
      report "Errors: {errors}"
      return STUCK

    if len(errors) > len(previous_errors) and iteration > 1:
      report "REGRESSING — {len(errors)} errors, up from {len(previous_errors)}"
      report "Last fix introduced new errors. Reverting iteration {iteration}."
      revert_last_changes()
      return REGRESSION

    report "Iteration {iteration}: {len(errors)} error(s) found"
    for error in errors:
      category = categorize(error)
      fix = determine_fix(error, category)
      apply(fix)
      report "  Fixed {error.code}: {error.message} → {fix.description}"

    previous_errors = errors

  report "MAX ITERATIONS reached with {len(errors)} error(s) remaining"
  return FAIL
```

**Error Categories and Fix Strategies:**

```
CATEGORY               EXAMPLE CODE    FIX STRATEGY
Missing using          CS0246          Add the correct using directive
Missing reference      CS0246          Add NuGet package or project reference
Type mismatch          CS0029          Check expected type, cast or convert
API change             CS0117          Check new API signature, update call
Nullable warning       CS8600-CS8604   Add null check, use ?. or ?? operator
Ambiguous reference    CS0104          Add full namespace qualifier
Missing member         CS1061          Check spelling, verify type has the member
Obsolete API           CS0618          Replace with recommended alternative
Missing implementation CS0535          Implement missing interface members
Syntax error           CS1002-CS1003   Fix syntax based on error context
```

### Test-Fix Loop

Fix failing tests iteratively. Critically, this loop must determine whether the bug is in the test or in the production code.

```
TEST-FIX LOOP:
  max_iterations = 5
  previous_failures = []

  for iteration in 1..max_iterations:
    result = dotnet test [project/solution] --no-build

    if result.all_passed:
      report "TESTS PASS after {iteration} iteration(s)"
      return PASS

    failures = parse_failures(result.output)

    if failures == previous_failures:
      report "STUCK — same {len(failures)} failure(s) after fix attempt"
      return STUCK

    report "Iteration {iteration}: {len(failures)} failure(s)"
    for failure in failures:
      # CRITICAL: diagnose before fixing
      diagnosis = diagnose(failure)
      report "  {failure.test_name}: {failure.message}"
      report "  Diagnosis: {diagnosis.root_cause}"
      report "  Fix target: {diagnosis.fix_in}"  # "test" or "production"

      if diagnosis.fix_in == "test":
        fix = fix_test(failure, diagnosis)
      else:
        fix = fix_production_code(failure, diagnosis)

      apply(fix)
      report "  Applied: {fix.description}"

    previous_failures = failures

  report "MAX ITERATIONS with {len(failures)} failure(s) remaining"
  return FAIL
```

**Diagnosis Protocol:**

```
DIAGNOSING A TEST FAILURE:
1. Read the test code — understand the assertion and setup
2. Read the production code — understand the actual behavior
3. Determine root cause:
   a. Test expects wrong value → fix the test
   b. Production code has a bug → fix the production code
   c. Test setup is incomplete → fix the test setup
   d. API contract changed → update test to match new contract
4. NEVER fix a test by weakening the assertion without understanding why
   BAD:  Assert.Equal(expected, actual) → Assert.NotNull(actual)
   GOOD: Assert.Equal(expected, actual) → fix production code to return expected
```

### Refactor Loop

Multi-step refactoring with verification at each step.

```
REFACTOR LOOP:
  targets = identify_refactoring_targets()  # via MCP tools
  max_iterations = min(len(targets), 10)

  for iteration, target in enumerate(targets, 1):
    if iteration > max_iterations:
      report "MAX TARGETS reached, {len(targets) - iteration} remaining"
      return PARTIAL

    report "Refactoring {iteration}/{len(targets)}: {target.description}"

    # 1. Apply the refactoring
    apply_refactoring(target)

    # 2. Verify it builds
    build_result = build_fix_loop(max_iterations=3)  # nested, smaller budget
    if build_result != PASS:
      report "Build failed after refactoring {target}. Reverting."
      revert_changes()
      return FAIL

    # 3. Verify tests pass
    test_result = test_fix_loop(max_iterations=3)  # nested, smaller budget
    if test_result != PASS:
      report "Tests failed after refactoring {target}. Reverting."
      revert_changes()
      return FAIL

    # 4. Check diagnostics for new warnings
    diagnostics = get_diagnostics()
    if diagnostics.new_warnings > 0:
      report "New warnings introduced. Fixing..."
      fix_warnings(diagnostics.new_warnings)

    report "Refactoring {iteration} complete. Build: PASS, Tests: PASS"

  return PASS
```

### Scaffold Loop

Generate a new feature end-to-end and verify everything compiles and tests pass.

```
SCAFFOLD LOOP:
  1. GENERATE source files
     → Create feature file (endpoint + handler + validator)
     → Create EF configuration if needed
     → Create DTOs/contracts

  2. BUILD VERIFICATION
     → Run build-fix loop (max 5 iterations)
     → If FAIL: report and stop — generated code has fundamental issues

  3. GENERATE test files
     → Create unit tests for handler logic
     → Create integration tests for endpoint
     → Match project's test conventions (via instinct-system)

  4. TEST VERIFICATION
     → Run test-fix loop (max 5 iterations)
     → If FAIL: report which tests fail and why

  5. QUALITY CHECK
     → Run get_diagnostics — zero new warnings
     → Run detect_antipatterns — zero new anti-patterns
     → Verify naming matches project conventions

  FINAL REPORT:
  "Scaffold complete:
   - Source files: [list with paths]
   - Test files: [list with paths]
   - Build: PASS
   - Tests: [N/N] passing
   - Warnings: 0 new
   - Anti-patterns: 0 new"
```

### Progress Detection

How to measure whether a loop iteration made progress:

```
PROGRESS METRICS:
  Build-Fix:    error_count[N] < error_count[N-1]
  Test-Fix:     failure_count[N] < failure_count[N-1]
  Refactor:     target_count[N] < target_count[N-1]
  Scaffold:     phase advances (generate → build → test → verify)

STUCK DETECTION:
  Same errors/failures after a fix attempt → STUCK
  Error count oscillates (3 → 2 → 3 → 2) → STUCK (after 2 oscillations)
  Fix introduces errors in previously passing code → REGRESSION

NO-PROGRESS RESPONSE:
  1. Report the stuck state clearly
  2. List the errors/failures that could not be fixed
  3. Suggest what a human should investigate
  4. Do NOT retry the same approach
```

### Emergency Exit Conditions

Conditions that cause immediate loop termination:

```
EMERGENCY EXITS:
  1. MORE ERRORS THAN BEFORE — an iteration introduced more errors than it fixed
     → Revert the iteration's changes
     → Report: "Fix attempt introduced {N} new errors. Reverted."

  2. CRITICAL ERROR — error indicates a fundamental problem (wrong SDK, missing
     project file, corrupted solution)
     → Stop immediately
     → Report: "Critical error detected: {description}. Human intervention needed."

  3. CASCADING FAILURES — fixing one error causes 3+ new errors repeatedly
     → Stop after 2 cascades
     → Report: "Cascading failure pattern detected. The fix approach is wrong."

  4. TEST INFRASTRUCTURE FAILURE — test runner itself fails (not test assertions)
     → Stop immediately
     → Report: "Test infrastructure error: {description}. Check test setup."

  5. USER INTERRUPTION — user sends any message during the loop
     → Complete current iteration
     → Report progress so far
     → Ask how to proceed
```

### Loop Nesting and Reporting

When loops call other loops (e.g., refactor loop calls build-fix loop):

```
NESTING RULES:
  - Nested loops get a SMALLER budget (parent max 5 → nested max 3)
  - Maximum nesting depth: 2 (Refactor → Build-Fix → no further)
  - Nested loop failure = parent loop iteration failure (revert the target)
  - Total iteration budget across all nesting: 15

ITERATION REPORT FORMAT:
  [Loop Type] Iteration {N}/{MAX}: {error/failure count}
  → {file}: {what changed and why}
  → Result: {new count} | Status: {CONTINUE/PASS/STUCK/FAIL}
```

## Anti-patterns

### Unbounded Loops

```
# BAD — no iteration limit
"Keep fixing build errors until it compiles"
*Claude tries 47 iterations, burns through context window,
 keeps retrying the same broken approach*

# GOOD — explicit bounds with progress checks
build_fix_loop(max_iterations=5)
*After 5 iterations or zero progress, stops and reports*
```

### Retrying the Same Fix

```
# BAD — applying the same fix that failed
Iteration 1: Add `using System.Linq;` → CS0246 persists
Iteration 2: Add `using System.Linq;` → CS0246 persists
Iteration 3: Add `using System.Linq;` → CS0246 persists

# GOOD — detect no progress, try a different approach or exit
Iteration 1: Add `using System.Linq;` → CS0246 persists
Iteration 2: STUCK — same error after fix.
  "CS0246 persists after adding System.Linq. The type may be in a
   different namespace or require a NuGet package. Checking..."
  → Search for the type using find_symbol
```

### Fixing by Deletion

```
# BAD — making code compile by removing functionality
Error: CS0246 'OrderValidator' not found
Fix: Delete all validation code
*Builds successfully! ...but the feature is broken*

# GOOD — fix the root cause
Error: CS0246 'OrderValidator' not found
Fix: Add missing reference to Validation project, or create the missing class
*Builds successfully with all functionality intact*
```

### Silent Loops

```
# BAD — loop runs silently, user sees nothing for 2 minutes
*...silence...*
"Done! Fixed 7 build errors."
*User has no idea what changed or why*

# GOOD — transparent reporting per iteration
"Iteration 1/5: 4 errors found
  Fixed CS0246 in OrderHandler.cs → added using FluentValidation
  Fixed CS0029 in OrderResponse.cs → changed return type to match
  2 errors remain.
 Iteration 2/5: 2 errors found
  Fixed CS1061 in OrderEndpoint.cs → updated method name to CreateAsync
  Fixed CS8600 in OrderHandler.cs → added null check
  0 errors remain.
 BUILD PASS after 2 iterations."
```

### Over-Aggressive Test Fixing

```
# BAD — weakening assertions to make tests pass
Assert.Equal(200, response.StatusCode)  // fails with 404
→ Changed to: Assert.NotNull(response)  // passes but hides the bug

# GOOD — diagnose the failure, fix the right code
Assert.Equal(200, response.StatusCode)  // fails with 404
→ Diagnosis: endpoint routing is wrong, missing MapGet registration
→ Fix: add endpoint registration in OrderModule.cs
→ Test passes with correct 200 status
```

## Decision Guide

| Scenario | Loop Type | Max Iterations | Notes |
|----------|-----------|---------------|-------|
| Build fails after code changes | Build-Fix | 5 | Categorize errors, fix systematically |
| Tests fail after code changes | Test-Fix | 5 | Diagnose test vs production bug first |
| Tests fail after build-fix loop | Test-Fix | 3 | Smaller budget — build-fix may have introduced issues |
| Multi-file refactoring | Refactor | 10 (or target count) | Verify build+tests after each target |
| Generating a new feature | Scaffold | 1 (phases, not iterations) | Build-fix and test-fix nested inside |
| Same error persists after fix | Exit with STUCK | N/A | Report error, suggest human investigation |
| Fix introduces more errors | Emergency exit | N/A | Revert changes, report regression |
| Nested loop needed | Use smaller budget | Parent - 2 | Max nesting depth: 2 |
| User says "keep going" | Extend by 3 iterations | Current + 3 | Never exceed hard cap of 10 |
| User says "stop" | Exit immediately | N/A | Report progress, preserve current state |
| Error is in test infrastructure | Exit immediately | N/A | Test runner issues need human attention |
| 3+ cascading failures | Exit immediately | N/A | The approach is fundamentally wrong |
