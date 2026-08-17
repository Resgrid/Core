# Localization Audit — Resgrid.Localization

**Generated:** 2026-08-16, against `develop`.
**Scope:** every `*.en.resx` under `Core/Resgrid.Localization` compared against its nine
translated siblings (`de`, `es`, `fr`, `it`, `pl`, `sv`, `uk`, `el`, `ar`).

## Headline

| Measure | Count |
|---|---|
| Area/language pairs | 414 |
| Fully translated | 116 |
| Partially English | 143 |
| **Never populated** (Visual Studio starter template) | **134** |
| Resource file missing entirely | 21 |
| Untranslated strings, approximate | **~17,500** |

Only ~28% of area/language pairs are complete. The debt predates the communication test work and
is not visible from the code, because every one of these files exists, is embedded, and resolves —
it just returns English. A member who sets their profile to Polish sees a mix of Polish and English
with no error anywhere.

## The three failure modes

### 1. Never populated (134 pairs, 22 areas)

The resx still contains the Visual Studio starter entries (`Name1`, `Bitmap1`, `Icon1`) and no real
keys. Lookups fall through to English for every string in the area.

Affected areas: Calendar, Call, Common, Contacts, CustomStatuses, Dashboard, DeleteAccount,
Department, DepartmentTypes, Documents, EditProfile, HomeDashboard, Login, Logs, Messages, Note,
Person, Profile, Shifts, Subscription, Templates, Units — each in `de`, `fr`, `it`, `pl`, `sv`, `uk`
(and `Common`, `Department` also in `es`).

`Common` and `Login` are the most damaging: they back shared UI and the sign-in screen.

### 2. Missing file (21 pairs, 3 areas)

No resx at all for the language. CustomMaps, IndoorMaps and Mapping have only `en`, `el` and `ar`.

### 3. Partially English (143 pairs)

The file is populated but many values are still the English text. Worst offenders:

| Area | Language | English values | Total keys |
|---|---|---|---|
| Security | fr | 263 | 328 |
| Security | sv | 260 | 328 |
| Security | de / it / pl / uk | 259 | 328 |
| Workflows | it | 141 | 177 |
| Workflows | fr | 140 | 177 |
| TwoFactor | de / fr / it / pl / sv / uk | 50 | 51 |
| Logs | es | 30 | 79 |

`Security` and `TwoFactor` matter disproportionately — they cover account access and recovery.

## What is complete

- **CommunicationTest** — 128 keys × 10 locales, at parity. Covers both the screens and the
  messages a test sends.
- **SystemMessages** — 7 keys × 10 locales. Verification codes (email + SMS), GDPR export notice,
  and the generic notification/calendar email subjects.
- **Moderation** — was already complete before this audit.

These three are pinned by `Tests/Resgrid.Tests/Localization/TranslationCompletenessTests.cs`, which
fails if a new English placeholder, missing key, missing file or starter-template stub appears in
them. Add an area to its `GuardedAreas` list once that area is brought up to full translation.

The guard deliberately does **not** assert the backlog above. A test that fails from day one gets
muted, and a muted test guards nothing.

## Recommended order of work

1. `Common` and `Login` — shared UI and sign-in, visible to everyone in every locale.
2. `Security` and `TwoFactor` — account access and recovery.
3. `Call`, `Dispatch`, `Messages` — the operational path.
4. Everything else.

## Re-running the audit

The comparison is a straightforward resx diff: parse each `*.en.resx`, then for each language check
for a missing file, the `Name1`/`Bitmap1` starter markers, keys absent from the translation, and
values byte-identical to English. Short brand/protocol strings ("SMS", "Push", "min") and words that
are genuinely the same in the target language are false positives — the guard test keeps an explicit
allow-list of those pairs rather than guessing with a heuristic.

## Caveat on the translations added in this pass

The CommunicationTest and SystemMessages values were authored by an AI, not a native speaker. The
structure is correct and placeholder integrity is test-verified across all ten locales, but the
wording should get a native review before it reaches members — particularly the SMS and voice text,
which people act on under pressure.
