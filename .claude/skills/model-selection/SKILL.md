---
name: model-selection
description: >
  Strategic Claude model selection for .NET development workflows. Guides when
  to use Opus 4.6 (deep reasoning, architecture, ambiguous problems) vs Sonnet 4.6
  (throughput, large context, routine implementation) vs Haiku 4.5 (fast, cheap
  subagent tasks). Covers model switching workflows, subagent model assignment,
  and cost-effective task routing. Load this skill when choosing models for tasks,
  optimizing costs, working with subagents, or when the user mentions "model",
  "Opus", "Sonnet", "Haiku", "which model", "cost", "switch model", or "fast mode".
---

# Model Selection

## Core Principles

1. **Match model to complexity, not size** — A 50-file refactor that follows a clear pattern is a Sonnet task (high throughput, simple logic). A 3-file architecture decision with trade-offs is an Opus task (deep reasoning). File count and complexity are orthogonal.

2. **Sonnet is the workhorse** — 80% of .NET development tasks are routine: implement a feature following an established pattern, write tests, fix a known bug, run scaffolding. Sonnet 4.6 handles all of these at higher speed and lower cost.

3. **Opus is the architect** — Use Opus 4.6 for tasks that require weighing trade-offs, reasoning about system design, debugging subtle issues, or making decisions with incomplete information. Opus excels when the answer isn't obvious.

4. **Context window is a budget, not a dumping ground** — Sonnet 4.6's large context window enables working with big codebases but doesn't mean you should load everything. Apply `context-discipline` principles regardless of model. A focused Sonnet session outperforms a bloated one.

5. **Haiku for fire-and-forget subagents** — When a subagent does a simple lookup, runs a script, or fetches information, Haiku 4.5 is fast and cheap. Reserve heavier models for subagents that need to reason.

## Patterns

### Task Complexity Assessment

Classify each task to select the right model:

```
ROUTINE TASKS → Sonnet 4.6
- Implement a feature following an existing pattern
- Write tests for existing code
- Fix a bug with a clear stack trace
- Add a new endpoint matching the project's convention
- Run scaffolding or code generation
- Apply a known refactoring pattern
- Format, lint, or fix build errors
- Write documentation from existing code

COMPLEX TASKS → Opus 4.6
- Design a new module or subsystem from scratch
- Choose between architecture approaches (VSA vs CA vs DDD)
- Debug a subtle issue with no clear stack trace
- Refactor with trade-offs (performance vs readability, consistency vs simplicity)
- Review architecture for design flaws
- Make decisions with incomplete or conflicting requirements
- Untangle complex dependencies or circular references
- Write a migration strategy for breaking changes

SIMPLE TASKS → Haiku 4.5 (subagents only)
- Look up a file path or symbol location
- Run a build/test and report results
- Search for a pattern across files
- Summarize a file or module
- Format or validate data
```

### Model Switching Workflow

The most effective pattern: Opus plans, Sonnet executes, Opus reviews.

```
PHASE 1: PLAN (Opus 4.6)
├── Analyze requirements and constraints
├── Identify architectural trade-offs
├── Design the approach with rationale
├── Define acceptance criteria
└── Output: detailed implementation plan

PHASE 2: EXECUTE (Sonnet 4.6)
├── Implement following the Opus plan
├── Write code, tests, configurations
├── Run build and test verification
├── Handle routine issues (compilation errors, test fixes)
└── Output: working implementation

PHASE 3: REVIEW (Opus 4.6)
├── Review implementation against the plan
├── Check for subtle issues (race conditions, N+1, security)
├── Evaluate architectural compliance
├── Suggest refinements
└── Output: approval or specific revision requests
```

How to switch in practice:

```
Claude Code CLI:
- /model opus    → Switch to Opus 4.6 (planning/review phases)
- /model sonnet  → Switch to Sonnet 4.6 (implementation phase)
- /model auto    → Let Claude Code choose based on task

Toggle fast mode:
- /fast          → Toggle fast mode (Opus fast output for throughput)
```

### Subagent Model Assignment

Assign models to subagents based on task complexity:

```
SUBAGENT: "Find all authentication middleware in the project"
→ MODEL: Haiku 4.5
→ WHY: Simple search task, no reasoning required

SUBAGENT: "Run dotnet test and summarize failures"
→ MODEL: Haiku 4.5
→ WHY: Execute command, parse output, no complex analysis

SUBAGENT: "Analyze the dependency graph for circular references and suggest fixes"
→ MODEL: Sonnet 4.6
→ WHY: Needs to understand project structure and propose solutions

SUBAGENT: "Review this PR for architectural issues and security vulnerabilities"
→ MODEL: Opus 4.6
→ WHY: Deep reasoning about trade-offs, subtle issue detection

SUBAGENT: "Summarize what the Orders module does"
→ MODEL: Haiku 4.5 or Sonnet 4.6
→ WHY: Haiku for a quick overview, Sonnet if the module is complex
```

## Anti-patterns

### Using Opus for Simple Tasks

```
// BAD — Opus for a routine CRUD endpoint
Using Opus 4.6 to implement GetOrderById following the exact same pattern
as the existing GetCustomerById. No decisions to make, just pattern replication.
*Slower and more expensive than needed*

// GOOD — Sonnet for pattern replication
Using Sonnet 4.6 to implement GetOrderById. The pattern is established,
the code is straightforward, and Sonnet executes it faster.
```

### Using Sonnet for Architecture Decisions

```
// BAD — Sonnet for "should we use VSA or Clean Architecture?"
Sonnet gives a reasonable answer but may miss nuanced trade-offs
about team size, domain complexity, and long-term maintenance.
*The wrong architecture costs months of refactoring*

// GOOD — Opus for architectural decisions
Opus weighs team size, domain complexity, current codebase patterns,
and long-term implications. The architecture decision is worth the
extra reasoning power.
```

### Same Model for All Subagents

```
// BAD — all subagents use Opus
5 subagents running Opus 4.6:
- "Find OrderService.cs" (Haiku could do this)
- "Run dotnet test" (Haiku could do this)
- "Summarize the Catalog module" (Haiku/Sonnet could do this)
- "Analyze circular dependencies" (Sonnet is sufficient)
- "Review architecture for security issues" (Opus is appropriate)
*4 out of 5 subagents are using more model than needed*

// GOOD — model matches subagent task
- "Find OrderService.cs" → Haiku 4.5
- "Run dotnet test" → Haiku 4.5
- "Summarize the Catalog module" → Haiku 4.5
- "Analyze circular dependencies" → Sonnet 4.6
- "Review architecture for security issues" → Opus 4.6
```

## Decision Guide

| Scenario | Model | Rationale |
|----------|-------|-----------|
| Plan a new feature or module | Opus 4.6 | Requires weighing trade-offs |
| Implement a feature following existing patterns | Sonnet 4.6 | Pattern replication, high throughput |
| Debug a subtle intermittent issue | Opus 4.6 | Requires deep reasoning about state/timing |
| Fix a compilation error | Sonnet 4.6 | Clear error, mechanical fix |
| Write tests for existing code | Sonnet 4.6 | Test patterns are established |
| Architecture review / PR review | Opus 4.6 | Subtle issues need deep analysis |
| Code review for anti-patterns | Sonnet 4.6 | Pattern matching, well-defined rules |
| Refactor across many files (same pattern) | Sonnet 4.6 | Volume + consistency, not deep reasoning |
| Design database schema from requirements | Opus 4.6 | Normalization trade-offs, domain modeling |
| Subagent: file lookup or search | Haiku 4.5 | Simple task, fast and cheap |
| Subagent: summarize a module | Haiku 4.5 | Straightforward reading + compression |
| Subagent: analyze dependencies | Sonnet 4.6 | Needs to reason about structure |
| Working in a very large codebase | Sonnet 4.6 | Large context window + discipline |
| End-of-day wrap-up / handoff | Sonnet 4.6 | Structured capture, no deep reasoning |
