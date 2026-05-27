---
name: context-discipline
description: >
  Token budget management for Claude Code sessions. Teaches how to minimize
  context consumption using MCP-first navigation, lazy loading, subagent
  isolation, and strategic file reading. Keeps Claude effective throughout
  long sessions by treating the 200k token window as a budget, not a dumping
  ground. Load this skill when context is running low, sessions feel sluggish,
  Claude starts forgetting earlier context, or when planning how to explore
  a large codebase efficiently. Keywords: "context", "tokens", "budget",
  "running out of context", "too many files", "large codebase", "memory".
---

# Context Discipline

## Core Principles

1. **MCP tools first, file reads second** — A Roslyn MCP query costs 30-150 tokens. Reading a file costs 500-2000+ tokens. For navigation and understanding, always try MCP tools before opening files. Only read files when you need to modify them or MCP tools don't provide enough detail.

2. **Lazy load everything** — Don't read files "just in case." Don't load all skills upfront. Don't explore directories you aren't about to modify. Load information at the moment you need it, not before.

3. **Subagents are context isolation chambers** — Every subagent gets its own context window. Offload exploration, research, and analysis to subagents. They process information and return a summary — your main context stays clean.

4. **Summarize and discard** — After exploring a subsystem, summarize what you learned in a few lines. The summary is what stays in context, not the raw file contents. Think of it as compressing information.

5. **Know your budget** — A 200k token window sounds large but fills fast. A typical .cs file is 500-2000 tokens. Loading 50 files can consume half your budget. Plan your reads like you plan your sprints — deliberately.

## Patterns

### MCP-First Navigation

Always prefer MCP tools for understanding code structure:

```
TASK: Understand how OrderService works

EXPENSIVE APPROACH (file reads):
1. Read src/Orders/OrderService.cs          → ~1200 tokens
2. Read src/Orders/IOrderService.cs         → ~300 tokens
3. Read src/Orders/OrderRepository.cs       → ~800 tokens
4. Read src/Orders/Models/Order.cs          → ~600 tokens
Total: ~2900 tokens consumed

TOKEN-EFFICIENT APPROACH (MCP-first):
1. find_symbol "OrderService"               → ~50 tokens (file path + line)
2. get_public_api "OrderService"            → ~120 tokens (method signatures)
3. find_references "OrderService"           → ~80 tokens (who uses it)
4. get_type_hierarchy "OrderService"        → ~60 tokens (inheritance chain)
Total: ~310 tokens consumed — 9x cheaper

Only THEN read the specific method you need to modify: ~200 tokens
Grand total: ~510 tokens vs 2900 — 5.7x savings
```

### Subagent Offloading Decision Matrix

Decide when to offload to a subagent vs. handle in main context:

```
USE A SUBAGENT WHEN:
- Exploring an unfamiliar part of the codebase (> 3 files to read)
- Researching a question that requires reading docs or multiple files
- Running analysis that produces verbose output (test results, diagnostics)
- Comparing approaches that require loading multiple examples
- Any task where the journey is verbose but the answer is concise

STAY IN MAIN CONTEXT WHEN:
- Modifying a file you've already read
- Quick lookups (1-2 MCP queries)
- Tasks where you need to see prior conversation context
- Writing code that builds on discussion with the user
```

Example subagent delegation:

```
TASK: "Find all places where we handle authentication"

MAIN CONTEXT APPROACH (expensive):
- Read 8 files, consume ~8000 tokens
- All that content stays in context forever

SUBAGENT APPROACH (efficient):
- Spawn subagent: "Find all authentication handling in this codebase.
  Use find_symbol and find_references for auth-related types.
  Return: file paths, line numbers, and a 1-line summary per location."
- Subagent returns ~200 tokens of summarized findings
- Main context stays clean
```

### Context Budget Planning

Before a complex task, estimate token spend per phase:

```
UNDERSTAND (~5k): MCP queries (~300) + subagent exploration (~500) + reserve
PLAN (~2k):       Discussion + plan documentation
IMPLEMENT (~15k): Read files to modify (~3k) + write code (~5k) + iteration (~7k)
VERIFY (~3k):     Build + test + diagnostics + format check
REMAINING: ~175k for conversation — comfortable
```

### File Reading Prioritization

When you must read files, prioritize by impact:

```
PRIORITY 1 — Files you will modify
Read fully. You need exact content to make correct edits.

PRIORITY 2 — Files with interfaces/contracts you must satisfy
Read the interface/base class. Skip implementation details.

PRIORITY 3 — Files for reference patterns
Use get_public_api first. Only read if the API surface isn't enough.

PRIORITY 4 — Files for general context
Use subagent to summarize. Don't read in main context.

NEVER READ:
- Entire directories "to understand the project" — use get_project_graph
- Test files for context (unless modifying tests) — use get_test_coverage_map
- Generated files (.designer.cs, migrations) — use get_diagnostics for issues
- Package/config files unless specifically needed
```

### Context Pruning & Large Codebase Strategy

```
WARNING SIGNS (take action if any apply):
- Read 10+ files, 50+ exchanges, forgetting earlier details, re-reading files

RECOVERY: Summarize in 5-10 lines → subagents for remaining exploration →
MCP-only for new lookups → reference prior line numbers → suggest new session if needed

LARGE CODEBASES (50+ projects):
get_project_graph → identify 2-3 relevant projects → find_symbol for key types →
get_public_api for interfaces → read ONLY files you'll modify → subagents for cross-cutting

NEVER: Read every file to "understand" a project, load all skills upfront,
open a file to find one function (use find_symbol)
```

## Anti-patterns

### Reading Entire Files for One Function

```
// BAD — read 1500 tokens to find one 10-line method
Read: src/Orders/OrderService.cs (full file, 80 lines)
*Only needed the ProcessOrder method on line 42*

// GOOD — targeted approach
MCP: find_symbol "ProcessOrder" → "src/Orders/OrderService.cs:42"
Read: src/Orders/OrderService.cs lines 42-55 → ~200 tokens
```

### Loading All Skills Upfront

```
// BAD — dump 15 skills into context at session start
"Load: modern-csharp, ef-core, minimal-api, testing, docker,
 authentication, logging, caching, messaging, resilience..."
*15 skills × ~300 tokens each = ~4500 tokens before any work starts*

// GOOD — load skills as topics arise
Session start: modern-csharp (always relevant)
User asks about EF: load ef-core
User asks about tests: load testing
*Only pay for what you use*
```

### Not Using Subagents for Exploration

```
// BAD — explore in main context, polluting the window
Read 12 files across 4 projects to understand auth flow
*~15,000 tokens consumed, all staying in context*

// GOOD — subagent explores, returns summary
Subagent: "Trace the authentication flow from login to token validation.
           Return: the flow as numbered steps with file:line references."
*~300 tokens in main context*
```

### Loading Everything Because the Window Is Large

```
// BAD — "200k tokens is huge, let's load everything"
Read all 30 files in the Orders module
Read all 15 test files
Read the entire docker-compose.yml
Read all migration files
*80k tokens consumed before writing a single line of code*

// GOOD — minimum viable context
MCP: get_project_graph (solution shape)
MCP: find_symbol (locate target types)
Read: 2-3 files you'll actually modify
Subagent: summarize anything else you need
*~3k tokens consumed, 197k remaining for actual work*
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| Need to find where a type is defined | `find_symbol` — never grep |
| Need to understand a type's API | `get_public_api` — don't read the file |
| Need to modify a file | Read it fully — you need exact content |
| Need to understand project structure | `get_project_graph` — don't browse directories |
| Need to explore unfamiliar code | Spawn a subagent — keep main context clean |
| Read more than 10 files in a session | Pause — switch to MCP + subagents |
| Context feels heavy or sluggish | Summarize what you know, use subagents going forward |
| Large codebase (50+ projects) | MCP-first, subagent-heavy, read only files you modify |
| User asks about a new topic mid-session | Load the relevant skill on demand, not in advance |
| Need to compare two approaches | Subagent per approach, compare summaries |
