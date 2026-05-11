---
name: self-correction-loop
description: >
  Self-improving correction capture system. After ANY user correction, detect it,
  generalize the lesson, and store it as a reusable rule in MEMORY.md. Ensures
  Claude's mistake rate drops over time by compounding corrections into permanent
  knowledge. Load this skill when a user corrects Claude's output, mentions
  "remember this", "don't do that again", "learn from mistakes", "update memory",
  or when starting a new session (to review existing rules).
---

# Self-Correction Loop

## Core Principles

1. **Every correction is a compounding investment** — A correction costs the user 30 seconds today but saves hours across all future sessions. Treat every correction as high-priority knowledge capture, not a one-time fix.

2. **Generalize before storing** — "Use `TimeProvider` not `DateTime.Now` in the Orders module" becomes "Always use `TimeProvider` instead of `DateTime.Now/UtcNow` across all modules." Specific corrections become class-level rules.

3. **Categorize for retrieval** — Rules organized by category (Code Style, Architecture, Naming, Testing, Data Access, API Design, Configuration, Performance) are findable. Uncategorized rules are forgotten.

4. **Deduplicate aggressively** — Before adding a rule, scan existing rules for overlap. Update an existing rule rather than adding a near-duplicate. Memory bloat defeats the purpose.

5. **Review memory at session start** — The first thing Claude should do in a new session is check `MEMORY.md` for project-specific rules. Knowledge captured but never reviewed is wasted effort.

## Patterns

### Correction Detection & Capture Flow

When a user corrects Claude's output, follow this exact sequence:

```
1. DETECT — User says something like:
   - "No, use X instead of Y"
   - "We don't do it that way here"
   - "That's wrong, it should be..."
   - "Always/Never do X in this project"
   - "Remember this for next time"

2. ACKNOWLEDGE — Confirm understanding of the correction
   "Got it — using HybridCache instead of IMemoryCache."

3. GENERALIZE — Extract the class-level rule
   Specific: "Don't use IMemoryCache in the Orders endpoint"
   General:  "Always use HybridCache instead of IMemoryCache — it provides
              stampede protection and L1+L2 caching out of the box."

4. CHECK — Scan MEMORY.md for existing related rules
   - If a related rule exists, UPDATE it (broader scope, better wording)
   - If no related rule exists, ADD a new one under the right category

5. STORE — Write to MEMORY.md under the appropriate category

6. CONFIRM — Tell the user what was captured
   "Added to Memory > Data Access: Always use HybridCache over IMemoryCache."
```

### MEMORY.md Organization Format

Structure memory by category with consistent rule formatting:

```markdown
# Project Memory

## Code Style
- Always use file-scoped namespaces — never block-scoped
- Use primary constructors for DI injection in services and handlers

## Architecture
- This project uses Vertical Slice Architecture — one file per feature operation

## Data Access
- Always use HybridCache over IMemoryCache — stampede protection + L1/L2
- Never use repository pattern over EF Core — use DbContext directly

## Testing
- Integration tests use ApiFixture base class — never raw WebApplicationFactory
```

Use categories: Code Style, Architecture, Naming, Data Access, API Design, Testing, Configuration, Performance. Each rule: one line, actionable, with rationale after the dash.

### Rule Generalization: Specific to Class

Transform specific corrections into broadly applicable rules:

```
SPECIFIC CORRECTION:
"Don't use DateTime.Now in the CreateOrder handler"

GENERALIZATION STEPS:
1. Is this specific to CreateOrder? → No, it's a .NET-wide concern
2. Is this specific to handlers? → No, applies everywhere
3. What's the underlying principle? → TimeProvider is testable, DateTime is not
4. What's the broadest correct statement?

GENERALIZED RULE:
"Always use TimeProvider instead of DateTime.Now/UtcNow — TimeProvider is
injectable and testable. This applies to all production code."
```

### Periodic Memory Audit

Every 5-10 sessions (or when memory exceeds 50 rules), audit for quality:

```
AUDIT CHECKLIST:
1. Remove rules that contradict each other — keep the most recent
2. Merge rules that overlap — combine into a single, clearer rule
3. Remove rules that are now obvious (e.g., captured early but now second nature)
4. Verify rules are still accurate — .NET evolves, patterns change
5. Check that categories are balanced — a category with 20+ rules needs subcategories
```

### Session-Start Memory Review

At session start, read MEMORY.md and apply relevant rules proactively. Do not wait to be reminded of rules that were already captured.

## Anti-patterns

### Ignoring Corrections

```
// BAD — user corrects, Claude fixes but doesn't capture
User: "No, we use HybridCache here, not IMemoryCache"
Claude: "Fixed. Here's the updated code with HybridCache..."
*Next session: makes the same mistake*

// GOOD — fix AND capture
User: "No, we use HybridCache here, not IMemoryCache"
Claude: "Fixed. Here's the updated code with HybridCache.
         Added to Memory > Data Access: Always use HybridCache over IMemoryCache."
*Next session: checks memory, uses HybridCache from the start*
```

### Overly Specific Rules

```
// BAD — rule is too narrow to be useful
"In the CreateOrder handler on line 47, use TimeProvider"

// GOOD — generalized to apply broadly
"Always use TimeProvider instead of DateTime.Now/UtcNow in all production code"
```

### Never Reviewing Memory

```
// BAD — 50 rules captured, none ever reviewed
MEMORY.md grows to 200 lines, contains duplicates and contradictions,
Claude doesn't read it because it's too long to be useful

// GOOD — periodic audit keeps memory lean and accurate
MEMORY.md stays under 80 rules, well-categorized, no duplicates,
Claude reads it at session start and applies rules proactively
```

### Storing Session-Specific Context

```
// BAD — temporary state saved as permanent memory
"Currently working on the Orders module refactor, file is at src/Orders/Handler.cs"

// GOOD — only permanent, reusable knowledge
"The Orders module uses VSA with one file per feature under Features/"
```

## Decision Guide

| Scenario | Action |
|----------|--------|
| User explicitly corrects Claude's code | Capture generalized rule in MEMORY.md |
| User says "remember this" or "always/never" | Capture exactly as stated, generalize if possible |
| Same correction given twice | High priority — the rule wasn't captured or wasn't reviewed |
| Correction is project-specific | Store in MEMORY.md with project context |
| Correction is universal .NET | Store in MEMORY.md — it applies to this project |
| MEMORY.md exceeds 50 rules | Trigger an audit — deduplicate, merge, prune |
| Starting a new session | Review MEMORY.md before writing any code |
| Rule contradicts an existing rule | Keep the most recent correction, remove the old one |
| Correction is about a one-time task | Don't store — only capture reusable patterns |
| User asks to forget a rule | Remove it from MEMORY.md immediately |
| Pattern observed but not yet confirmed | Create an instinct via `instinct-system` skill (confidence 0.3) instead of a MEMORY.md rule |
| Instinct reaches 0.9 confidence | Promote to MEMORY.md as a permanent rule (see `instinct-system` skill) |
