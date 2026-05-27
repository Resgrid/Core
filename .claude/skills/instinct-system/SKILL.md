---
name: instinct-system
description: >
  Confidence-scored instinct system for learning project-specific patterns through
  an observe-hypothesize-confirm cycle. Instincts start as low-confidence hypotheses
  and graduate to permanent rules in MEMORY.md once confirmed. Stored per-project
  in .claude/instincts.md. Load this skill when you notice a recurring pattern,
  want to track a project convention, encounter "learn this", "I think they always",
  "notice a pattern", "instinct", "hypothesis", "confidence", or when starting a
  session (to load existing instincts).
---

# Instinct System

## Core Principles

1. **Instincts are hypotheses, not rules** — An instinct starts as a guess based on a single observation. It has no authority until confirmed across multiple instances. Never treat a first observation as a project convention. "I saw one handler use `sealed` " is an instinct at 0.3; "all 12 handlers use `sealed`" is a rule at 0.9.

2. **Confidence scoring drives behavior (0.3-0.9)** — Each instinct carries a numeric confidence that determines how Claude acts on it. At 0.3, merely note it. At 0.5, mention it when relevant. At 0.7, follow it by default. At 0.9, promote it to a permanent rule. Never apply a low-confidence instinct without flagging the uncertainty.

3. **Project-scoped, never global** — Instincts are stored per-project in `.claude/instincts.md`. What holds true in one codebase may be wrong in another. A project using `internal sealed class` everywhere says nothing about a different project that uses `public class` with interface testing. Instincts do not transfer at full confidence.

4. **Observe-Hypothesize-Confirm cycle** — The lifecycle is disciplined: see a pattern, form a hypothesis, actively seek confirming or disconfirming evidence, adjust confidence accordingly. Passive observation is not enough. When you form an instinct, look for it in the next 2-3 related files.

5. **Evolution path to permanence** — Instincts are temporary by design. At 0.9 confidence, they graduate to `MEMORY.md` as permanent rules or trigger updates to relevant skills. An instinct that never reaches 0.7 after 5+ observations should be discarded, not left to rot.

## Patterns

### Instinct Lifecycle

The full lifecycle from first observation to permanent rule:

```
1. OBSERVE
   During normal work, notice something that might be a pattern.
   "This handler is internal sealed class. Is that the convention?"

2. HYPOTHESIZE
   Create an instinct with initial confidence 0.3.
   Write to .claude/instincts.md:
   - Use `sealed` on all handler classes | confidence: 0.3 | seen: 1 | last: 2025-07-15

3. SEEK CONFIRMATION
   When working in related areas, actively check for the pattern.
   Open 2-3 other handlers. Do they all use `internal sealed class`?

4. CONFIRM or DISCONFIRM
   - If confirmed: increment `seen`, raise confidence per the adjustment rules
   - If contradicted: halve confidence, note the exception
   - If mixed: hold confidence steady, note the split

5. PROMOTE
   At 0.9, present to the user for promotion to MEMORY.md.
   "I've observed [pattern] across [N] instances. Should I add this
   as a permanent rule to MEMORY.md?"
```

### Instinct Storage Format

Store instincts in `.claude/instincts.md` with categories and structured metadata:

```markdown
# Project Instincts

## Code Style [0.7]
- Use `sealed` on all handler classes | confidence: 0.8 | seen: 5 | last: 2025-07-15
- Prefix private fields with underscore | confidence: 0.5 | seen: 2 | last: 2025-07-14

## Architecture [0.6]
- Feature folders use singular names (Order, not Orders) | confidence: 0.7 | seen: 4 | last: 2025-07-15

## Naming [0.5]
- Command classes end in Command, not Request | confidence: 0.5 | seen: 3 | last: 2025-07-15
```

Category header `[0.7]` = average confidence. Use standard categories: Code Style, Architecture, Naming, Testing, Data Access, API Design, Configuration, Performance, Tooling. Each entry: `description | confidence: N | seen: N | last: date`.

### Confidence Adjustment Rules

Follow these rules precisely. No ad-hoc scoring.

```
CONFIRMATION TRACK:
  First observation         → 0.3   (hypothesis formed)
  Second confirmation       → 0.5   (pattern emerging)
  Third confirmation        → 0.7   (likely convention)
  Fourth+ confirmation      → 0.8   (strong convention)
  Fifth+ with zero contradictions → 0.9 (promotion candidate)

CONTRADICTION HANDLING:
  Any contradiction          → halve current confidence
  Example: instinct at 0.7, contradicted → drops to 0.35
  Two contradictions in a row → drop to 0.1 (effectively dead)

USER OVERRIDE:
  User explicitly confirms   → jump to 0.8
  User explicitly corrects   → drop to 0.0 (remove the instinct)
  User says "sometimes"      → cap at 0.5 (conditional pattern)

STALENESS:
  No new observations for 10+ sessions → flag for review
  Contradicted and not re-confirmed for 5 sessions → remove
```

### Acting on Instincts by Confidence Level

```
0.0 - 0.2  → IGNORE — insufficient evidence, do not mention
0.3 - 0.4  → NOTE — record internally, do not apply
0.5 - 0.6  → MENTION — "I notice this project may use [pattern]. Should I follow it?"
0.7 - 0.8  → FOLLOW — apply by default, mention when first applied
0.9        → PROMOTE — offer to add to MEMORY.md as a permanent rule
```

When generating code, apply instincts at 0.7+ silently. For instincts at 0.5-0.6, mention them as suggestions. Never silently apply an instinct below 0.7.

### Seeking Confirmation Actively

Do not wait passively for evidence. When you form a new instinct, look for it:

```
ACTIVE SEEKING PROTOCOL:
1. Form instinct: "This project uses Result<T> for handler returns"
2. Before next code generation, check 2-3 existing handlers:
   → Use find_symbol to locate other handlers
   → Use get_public_api to check their return types
3. Count matches and mismatches
4. Adjust confidence based on findings
5. Update .claude/instincts.md with new count and confidence

EXAMPLE:
  New instinct: "Handlers return Result<T>" at 0.3
  → Check CreateOrderHandler: returns Result<OrderResponse> ✓
  → Check GetProductHandler: returns Result<ProductResponse> ✓
  → Check DeleteUserHandler: returns Task<IResult> ✗
  Result: 2/3 match → raise to 0.5, note the exception
  Updated: Handlers return Result<T> | confidence: 0.5 | seen: 3 | last: 2025-07-15
           Exception: DeleteUserHandler uses Task<IResult>
```

### Promotion Protocol

When an instinct reaches 0.9 confidence:

```
PROMOTION STEPS:
1. Present the evidence to the user:
   "I've observed [pattern] across [N] instances with zero contradictions:
    - CreateOrderHandler: ✓
    - UpdateOrderHandler: ✓
    - DeleteOrderHandler: ✓
    - GetProductHandler: ✓
    - CreateProductHandler: ✓
    Should I add this as a permanent rule to MEMORY.md?"

2. If user approves:
   - Add to MEMORY.md under the appropriate category
   - Remove from .claude/instincts.md
   - Format as a clear, generalized rule (use self-correction-loop generalization)

3. If user declines:
   - Cap confidence at 0.8
   - Note: "User reviewed, chose not to promote"
   - Keep in instincts for continued reference

4. If user provides context:
   - "That's only for command handlers, not query handlers"
   - Adjust the instinct to be more specific
   - Reset confidence to 0.5 (narrowed scope needs re-confirmation)
```

### Export and Import Between Projects

Export instincts at 0.7+ to `.claude/instincts-export.md`. On import, apply 0.2 confidence decay (0.9 becomes 0.7, 0.7 becomes 0.5). Never import above 0.7 — every project must confirm locally. Mark imported instincts with `source: "imported from [project]"`.

### Session-Start Instinct Loading

At the beginning of each session, load and apply instincts:

```
SESSION START:
1. Read .claude/instincts.md (if it exists)
2. Load all instincts at 0.7+ into active context
3. Note instincts at 0.5-0.6 for mention-when-relevant
4. Ignore instincts below 0.5 (they'll be confirmed or discarded organically)
5. Check for stale instincts (no updates in 10+ sessions) — flag for review
```

## Anti-patterns

### Over-Eager Pattern Recognition

```
# BAD — treating first observation as a rule
*Reads one handler file*
"This project always uses internal sealed class on handlers."
*Generates 5 new handlers with internal sealed — but the project
 actually uses public class in 8 out of 9 existing handlers*

# GOOD — forming a hypothesis and seeking confirmation
*Reads one handler file*
"Noticed CreateOrderHandler uses internal sealed class.
 Forming instinct at 0.3. Let me check a few more handlers..."
*Checks 3 more handlers, finds they all use public class*
"Disconfirmed. The one internal sealed handler was an exception."
```

### Stagnant Instincts

```
# BAD — instincts sit at 0.3 forever, never confirmed or discarded
.claude/instincts.md has 40 instincts, 35 of them at confidence 0.3
from 3 months ago — useless noise

# GOOD — actively seek confirmation or discard
After forming an instinct, check 2-3 related files in the same session.
Instincts that can't reach 0.5 within 3 sessions get removed.
```

### Global Instincts

```
# BAD — applying instincts from one project to another at full confidence
"ProjectA uses FluentValidation, so ProjectB must too."
*ProjectB uses DataAnnotations exclusively*

# GOOD — project-scoped instincts with import decay
"ProjectA used FluentValidation (0.9). Importing to ProjectB at 0.7.
 Let me check what ProjectB actually uses..."
*Finds DataAnnotations → drops to 0.0, removes the instinct*
```

### Instinct Hoarding

```
# BAD — never cleaning up the instinct file
.claude/instincts.md grows to 200 lines, full of contradictions
and instincts that were never confirmed

# GOOD — periodic cleanup
Every 5 sessions, scan instincts:
- Remove anything below 0.2
- Remove anything stale (no update in 10+ sessions)
- Promote anything at 0.9+
- Keep the file under 50 active instincts
```

## Decision Guide

| Scenario | Action |
|----------|--------|
| First time seeing a pattern | Create instinct at 0.3, seek confirmation in 2-3 related files |
| Pattern seen twice | Raise to 0.5, keep seeking |
| Pattern seen 3+ times with no contradictions | Raise to 0.7, start following by default |
| Pattern contradicted | Halve confidence, note the exception |
| User says "we always do X" | Create instinct at 0.8 (user confirmation) |
| User says "no, that's wrong" | Drop instinct to 0.0, remove it |
| Instinct at 0.9 | Present evidence to user, offer promotion to MEMORY.md |
| Instinct stale for 10+ sessions | Flag for review, ask user if it's still valid |
| Starting a new project | Do not import instincts from other projects above 0.7 |
| Similar project, want to share instincts | Export at 0.7+, import with 0.2 decay |
| Instinct file exceeds 50 entries | Audit: remove dead instincts, promote mature ones |
| Generating code and instinct is 0.5-0.6 | Mention the instinct, ask before applying |
| Generating code and instinct is 0.7+ | Apply silently, mention on first use |
| Conflicting instincts in same category | Keep both, note the conflict, seek the distinguishing condition |
| User partially confirms ("only for commands") | Narrow scope, reset to 0.5, re-confirm with narrowed definition |
