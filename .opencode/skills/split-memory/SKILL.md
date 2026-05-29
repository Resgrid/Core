---
name: split-memory
description: >
  Modular CLAUDE.md management strategy for projects that outgrow a single
  instruction file. Covers when and how to split a monolithic CLAUDE.md into
  multiple files, organizing by concern, module, or team. Includes precedence
  rules to prevent conflicting instructions. Load this skill when CLAUDE.md
  exceeds 300 lines, when multiple teams need different instructions, when
  the user mentions "split CLAUDE.md", "modular instructions", "too long",
  "organize instructions", or "multiple CLAUDE files".
---

# Split Memory: Modular CLAUDE.md Strategy

## Core Principles

1. **Start monolithic, split when it hurts** — A single CLAUDE.md is simpler to maintain, easier to understand, and has no conflict risk. Only split when the file exceeds ~300 lines, when multiple teams need different instructions, or when finding the right rule takes too long.

2. **Root CLAUDE.md is the index** — After splitting, the root CLAUDE.md becomes a concise index that points to detailed files. It contains only universal rules and references. Think of it as a table of contents, not the full book.

3. **Claude auto-discovers `.claude/` files** — Claude Code automatically reads files in the `.claude/` directory. Use this to your advantage: place instruction files where Claude will find them without explicit loading directives.

4. **No conflicting instructions across files** — When instructions span multiple files, contradictions cause unpredictable behavior. Establish clear precedence: root CLAUDE.md > module-level > team-level. Never define the same rule in two places.

5. **Split by a single axis** — Split by concern (architecture, testing, API) OR by module (Orders, Catalog, Identity) OR by team. Never mix axes — it creates overlapping ownership and conflicting rules.

## Patterns

### Pattern 1: Single File (Default)

For most projects, one CLAUDE.md is sufficient:

```
project-root/
├── CLAUDE.md              # Everything in one file (under 300 lines)
├── src/
└── tests/
```

**When this works:**
- Single team, single architecture
- Under 300 lines of instructions
- Rules are easy to find with Ctrl+F

**When to move on:**
- File exceeds 300 lines
- You spend time scrolling to find rules
- Multiple concerns compete for space (architecture, testing, deployment, conventions)

### Pattern 2: Split by Concern

Group instructions by domain (architecture, testing, deployment, etc.):

```
project-root/
├── CLAUDE.md                          # Index + universal rules (~50 lines)
├── .claude/
│   └── instructions/
│       ├── architecture.md            # Architecture patterns, module boundaries
│       ├── coding-standards.md        # C# conventions, naming, formatting
│       ├── testing.md                 # Test strategy, fixtures, conventions
│       ├── api-design.md             # Endpoint patterns, versioning, auth
│       ├── data-access.md            # EF Core patterns, migrations
│       └── deployment.md             # Docker, CI/CD, environments
```

Root CLAUDE.md becomes an index:

```markdown
# [Project Name]

## Universal Rules
- .NET 10 / C# 14 — use modern language features everywhere
- TimeProvider over DateTime.Now — always
- No repository pattern over EF Core

## Detailed Instructions
See `.claude/instructions/` for topic-specific guidance:
- `architecture.md` — Project structure, module boundaries, patterns
- `coding-standards.md` — C# conventions, naming, formatting rules
- `testing.md` — Test strategy, fixtures, what to test and how
- `api-design.md` — Endpoint patterns, versioning, authentication
- `data-access.md` — EF Core usage, query patterns, migrations
- `deployment.md` — Docker, CI/CD pipeline, environment config
```

### Pattern 3: Split by Module

For modular monoliths or large solutions, place instructions near the code they govern:

```
project-root/
├── CLAUDE.md                          # Index + cross-cutting rules
├── src/
│   ├── Modules/
│   │   ├── Orders/
│   │   │   ├── CLAUDE.md              # Orders-specific patterns and rules
│   │   │   └── ...
│   │   ├── Catalog/
│   │   │   ├── CLAUDE.md              # Catalog-specific patterns and rules
│   │   │   └── ...
│   │   └── Identity/
│   │       ├── CLAUDE.md              # Identity-specific patterns and rules
│   │       └── ...
│   └── Shared/
│       └── CLAUDE.md                  # Shared kernel rules
```

Module CLAUDE.md example:

```markdown
# Orders Module

## Architecture
This module uses Vertical Slice Architecture. Each feature is one file under Features/.

## Domain Rules
- OrderId is a strongly-typed ID (not raw Guid)
- All monetary values use decimal, never double
- Order state transitions: Draft → Confirmed → Shipped → Delivered → Cancelled

## Integration Events Published
- OrderCreated, OrderConfirmed, OrderShipped, OrderCancelled

## Integration Events Consumed
- ProductPriceChanged (from Catalog), PaymentCompleted (from Billing)
```

### Pattern 4: Split by Team

Place team-specific files in `.claude/teams/` (e.g., `backend.md`, `frontend.md`, `platform.md`). Root CLAUDE.md holds shared standards. Each team file covers only that team's conventions.

### Pattern 5: Conditional Loading

In root CLAUDE.md, add a "Load When Working On..." section that maps task domains to instruction files (e.g., "**API endpoints** → See `.claude/instructions/api-design.md`"). Universal rules stay in an "Always Loaded" section.

### Precedence Rules

When instructions exist in multiple files, apply this precedence:

```
HIGHEST PRIORITY:
1. Root CLAUDE.md — universal rules override everything
2. .claude/instructions/*.md — concern-specific rules
3. Module-level CLAUDE.md (src/Modules/X/CLAUDE.md) — module-specific rules
LOWEST PRIORITY:
4. Team-level files (.claude/teams/*.md) — team conventions

CONFLICT RESOLUTION:
- If root says "use TimeProvider" and module says "use DateTime.Now"
  → Root wins. Module file is wrong and should be fixed.
- If root is silent on a topic and module defines a rule
  → Module rule applies within its scope.
- If two module files contradict each other
  → Each applies only within its own module. No cross-module conflicts.
```

## Anti-patterns

### Premature Splitting

Do not split a sub-300-line CLAUDE.md into multiple files. The maintenance overhead of 6 tiny files exceeds the benefit. Keep it monolithic until finding rules becomes painful.

### Conflicting Cross-File Instructions

```
// BAD — same topic defined differently in two files
# .claude/instructions/api-design.md
"Use Results.Ok() for all endpoint return types"

# .claude/instructions/coding-standards.md
"Use TypedResults.Ok() for all endpoint return types"
*Claude gets contradictory instructions. Behavior is unpredictable.*

// GOOD — one owner per topic
# .claude/instructions/api-design.md
"Use TypedResults.Ok() for all endpoint return types — provides OpenAPI metadata"

# .claude/instructions/coding-standards.md
(no mention of API return types — that's api-design.md's domain)
```

### Split Without an Index

```
// BAD — files scattered without a map
project/
├── CLAUDE.md (doesn't mention the other files)
├── .claude/
│   └── instructions/
│       ├── architecture.md
│       ├── testing.md
│       └── data-access.md
*Claude may not know these files exist or how they relate*

// GOOD — root CLAUDE.md is the table of contents
project/
├── CLAUDE.md (lists all instruction files and their scope)
├── .claude/
│   └── instructions/
│       ├── architecture.md
│       ├── testing.md
│       └── data-access.md
```

### Mixing Split Axes

```
// BAD — split by concern AND by module simultaneously
.claude/
├── instructions/
│   ├── architecture.md       # talks about Orders module
│   └── testing.md            # also talks about Orders module
├── modules/
│   └── orders/
│       └── instructions.md   # also talks about architecture and testing
*Three files all have opinions about Orders testing. Who wins?*

// GOOD — pick one axis
# Option A: Split by concern (if cross-cutting rules dominate)
.claude/instructions/architecture.md
.claude/instructions/testing.md

# Option B: Split by module (if module-specific rules dominate)
src/Modules/Orders/CLAUDE.md
src/Modules/Catalog/CLAUDE.md
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| CLAUDE.md under 300 lines | Keep it as a single file |
| CLAUDE.md over 300 lines, single team | Split by concern into `.claude/instructions/` |
| Modular monolith with module-specific rules | Split by module with per-module CLAUDE.md |
| Multiple teams, different conventions | Split by team into `.claude/teams/` |
| Just started the project | Single CLAUDE.md — split later when needed |
| Rules are hard to find | Time to split — group by the most common lookup pattern |
| Two files contradict each other | Fix immediately — one owner per topic, clear precedence |
| Want to split by concern AND module | Pick one axis — the one that reduces conflicts most |
| Team member asks "where's the rule for X?" | If the answer isn't obvious in 5 seconds, reorganize |
| New module added to the system | Add a module-level CLAUDE.md only if it has unique rules |
