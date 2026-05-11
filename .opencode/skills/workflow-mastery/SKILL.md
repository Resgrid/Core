---
name: workflow-mastery
description: >
  Claude Code workflow mastery for .NET developers. Covers parallel execution
  with git worktrees, plan mode strategy, verification loops, auto-formatting
  hooks, permission setup for dotnet CLI, prompting techniques, and subagent
  patterns — all adapted for the .NET ecosystem.
  Load this skill when setting up Claude Code for a .NET project, optimizing
  workflows, running parallel sessions, or when the user mentions "productivity",
  "workflow", "parallel", "worktree", "plan mode", "permissions", "hooks",
  "10x", "setup Claude Code", or "speed up development".
  Inspired by tips from Boris Cherny (creator of Claude Code) and the Anthropic team.
---

# Workflow Mastery for .NET

## Core Principles

1. **Parallel over sequential** — Run 3-5 Claude sessions simultaneously using git worktrees. Build a feature in one, fix a bug in another, run tests in a third. The single biggest productivity unlock.
2. **Plan then execute** — For any non-trivial task, start in plan mode, iterate until the plan is bulletproof, then switch to auto-accept. A good plan means Claude 1-shots the implementation.
3. **Verification closes the loop** — Give Claude a way to prove its work: `dotnet build`, `dotnet test`, `get_diagnostics` via MCP. This single practice 2-3x the quality of the output.
4. **Automate the repetitive** — If you do it more than once a day, make it a hook, a slash command, or a subagent. Pre-allow safe permissions. Eliminate friction.
5. **Compound your knowledge** — Every correction becomes a rule in `MEMORY.md` (see `self-correction-loop` skill). Every PR review adds a learning. Over time, Claude's mistake rate drops because your project's knowledge base grows.

## Patterns

### Parallel Sessions with Git Worktrees

The biggest productivity multiplier. Each worktree gets its own Claude session, its own files, zero conflicts.

```bash
# Create worktrees for parallel work
git worktree add ../my-project-feature origin/main
git worktree add ../my-project-bugfix origin/main
git worktree add ../my-project-tests origin/main

# Start Claude in each (separate terminal tabs)
cd ../my-project-feature && claude
cd ../my-project-bugfix && claude
cd ../my-project-tests && claude
```

**Practical .NET workflow:**

| Worktree | Task | Claude Session |
|----------|------|---------------|
| `feature` | Build new endpoint + handler | Main development |
| `bugfix` | Fix the failing CI test | Autonomous bug fix |
| `tests` | Write integration tests for existing feature | Test generation |
| `analysis` | Query the Roslyn MCP, read logs, review architecture | Read-only research |

**Tips:**
- Name your terminal tabs by task so you never lose track
- Use shell aliases (`alias zf='cd ../my-project-feature'`) for one-keystroke switching
- Enable terminal notifications so you know when a session needs input

### Auto-Format Hook for .NET

Catch formatting issues on every file write — eliminates the "CI failed on formatting" loop.

```json
// .claude/settings.json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Write|Edit",
        "hooks": [
          {
            "type": "command",
            "command": "dotnet format --include \"$CLAUDE_FILE_PATH\" --no-restore 2>/dev/null || true"
          }
        ]
      }
    ]
  }
}
```

Why `|| true`: The hook should never block Claude's workflow. If formatting fails (e.g., on a non-C# file), silently continue.

### Pre-Allow Safe .NET Permissions

Stop clicking "allow" for every `dotnet` command. Add these to `.claude/settings.json`:

```json
{
  "permissions": {
    "allow": [
      "Bash(dotnet build *)",
      "Bash(dotnet test *)",
      "Bash(dotnet run *)",
      "Bash(dotnet ef *)",
      "Bash(dotnet format *)",
      "Bash(dotnet restore *)",
      "Bash(dotnet pack *)",
      "Bash(dotnet tool *)"
    ]
  }
}
```

Check this into git so the whole team gets frictionless workflows.

### Plan Mode Strategy

For any task touching 3+ files or involving architecture decisions:

```
Step 1: Enter plan mode (Shift+Tab twice)
Step 2: Describe the task with full context
Step 3: Iterate on the plan — challenge assumptions, ask "what about edge cases?"
Step 4: Once the plan is solid, switch to normal mode
Step 5: Claude executes with auto-accept — often 1-shots the implementation
```

**Advanced pattern:** Have one Claude write the plan, then spin up a second Claude session to review it as a staff engineer:

```
"Review this plan as a staff .NET engineer. Challenge every assumption.
What could go wrong? What's missing? What would you do differently?"
```

**When things go sideways:** The moment implementation deviates from the plan, STOP. Don't push through. Switch back to plan mode, understand what changed, re-plan, then resume.

### Verification Loop for .NET

> For the full 7-phase verification pipeline (build, diagnostics, anti-patterns, tests, security, format, diff review) with structured PASS/FAIL reporting, see the **verification-loop** skill.

Boris's #1 tip: "Give Claude a way to verify its work." The short version: always tell Claude to run `dotnet build`, `dotnet test`, `get_diagnostics`, and `dotnet format --verify-no-changes` before declaring done. The verification-loop skill has the complete pipeline with short-circuit rules and report templates.

### Compounding Knowledge via Corrections

For the full correction capture system — detection, generalization, categorized storage, and periodic audits — see the **`self-correction-loop`** skill. The short version: after every correction, capture a generalized rule in `MEMORY.md` so the same mistake never recurs.

### Prompting Techniques for .NET

**Challenge Claude's work:**
```
"Grill me on these changes. Would this pass a staff .NET engineer's code review?
Check for: N+1 queries, missing CancellationToken, exposed domain entities,
missing validation, incorrect service lifetimes."
```

**Demand proof:**
```
"Prove this works. Run the tests, show me the output.
Then diff the API response between main and this branch."
```

**After a mediocre fix:**
```
"Knowing everything you know now, scrap this and implement the elegant solution.
No hacks, no workarounds."
```

**For EF Core migrations:**
```
"Generate the migration, then show me the raw SQL it produces.
I want to verify the migration before applying it."
```

### Subagent Patterns for .NET

> **Context-aware delegation:** For guidance on when to use subagents to manage token budgets effectively, see the **`context-discipline`** skill.

Create reusable subagents in `.claude/agents/`:

```markdown
<!-- .claude/agents/verify-api.md -->
You are a .NET API verification agent. Your job:
1. Run `dotnet build` and fix any compilation errors
2. Run `dotnet test` and fix any test failures
3. Use `get_diagnostics` to check for warnings
4. Verify all endpoints return proper TypedResults
5. Check that no domain entities leak into API responses
Report: PASS with summary, or FAIL with specific issues.
```

```markdown
<!-- .claude/agents/code-simplifier.md -->
You are a code simplification agent for .NET projects.
Review the recent changes and simplify:
- Replace verbose LINQ with simpler alternatives
- Use primary constructors where applicable
- Replace manual null checks with pattern matching
- Consolidate duplicated code
- Remove unnecessary using statements
Do not change behavior. Only simplify.
```

**Use them:**
```
"Run the verify-api agent on my changes before I create the PR."
"Run code-simplifier on the files I just modified."
```

## Anti-patterns

### Don't Skip Plan Mode for Complex Tasks

```
// BAD — dive straight into a multi-file refactor
"Refactor the Orders module to use DDD with aggregates and value objects"
*Claude modifies 15 files, misses half the invariants, tangles the migration*

// GOOD — plan first, execute after
"Enter plan mode. I want to refactor the Orders module to use DDD.
Let's plan which files change, what the aggregate boundary is,
how value objects map to EF Core, and what the migration strategy is."
```

### Don't Work in a Single Session When You Could Parallelize

```
// BAD — sequential work in one session
1. Build feature       (20 min)
2. Write tests         (15 min)
3. Fix formatting      (5 min)
4. Update docs         (10 min)
Total: 50 minutes

// GOOD — parallel worktrees
Worktree 1: Build feature     (20 min)
Worktree 2: Write tests       (15 min, started simultaneously)
Worktree 3: Update docs       (10 min, started simultaneously)
Total: ~20 minutes (wall clock)
```

### Don't Accept the First Solution

```
// BAD — accept mediocre code
Claude: "Here's the implementation" *generic, works but not great*
You: "Looks good, ship it"

// GOOD — push for quality
Claude: "Here's the implementation"
You: "Would a staff .NET engineer approve this?
      What about the service lifetime? Is this N+1 safe?
      Is there a more elegant way using C# 14 features?"
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| Task touches 3+ files | Plan mode first |
| Task is a simple bug fix | Just fix it, verify with `dotnet test` |
| Need to build + test + review | 3 parallel worktrees |
| CI keeps failing on format | Add PostToolUse format hook |
| Tired of permission prompts | Pre-allow `dotnet *` commands |
| Claude made a mistake | "Update CLAUDE.md so you don't make that mistake again" |
| Code feels hacky | "Knowing everything you know now, implement the elegant solution" |
| Want to verify architecture | Spin up a second session as staff reviewer |
| Repetitive PR workflow | Create a subagent (verify-api, code-simplifier) |
| Learning a new codebase | Use "Explanatory" output style via `/config` |
