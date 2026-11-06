# QA departments

Isolated, ad hoc test departments for the app regression suites (Resgrid Omni: Responder, Unit, IC, Dispatch, BigBoard on every platform). One run seeds **15 departments**, each a distinct configuration, with the same static baseline data, straight into a Resgrid database (PostgreSQL or SQL Server) in one transaction. The run writes a manifest with the sign-in personas, the ids of named records and the state every feature flag should evaluate to; the suites read it.

```
qa-departments/
  Dockerfile           the seeder image (Python 3.13, psycopg, pymssql, cryptography); unit tests run during the build
  seeder/              catalog -> rows -> SQL (both dialects), schema checks, guards, ADP sealing, manifest, verify
  catalog/
    baseline.json      the standard data every department gets (roster, stations, units, calls, messages, ...)
    scenarios/*.json   one file per department: what it changes and what it covers
  tests/               python -m unittest discover -s tests -t .
  stack/               an isolated Core stack built from this checkout (qa-stack.py, compose.yaml, core.Dockerfile)
```

## Run it locally (isolated stack)

Needs Docker with Compose and Python 3.10+. From the Core repository root:

```bash
python3 Docker/qa-departments/stack/qa-stack.py up
```

`up` builds the console, API and eventing hosts from this checkout (`stack/core.Dockerfile`) and the seeder, generates passwords, Core's `ResgridConfig.json` and certificates into `stack/private/` (git-ignored, owner-only), starts PostgreSQL, Redis, RabbitMQ and Mailpit, migrates with Core's own console (`--DbUpdate`, `--OidcUpdate`), starts the API and eventing behind a TLS gateway, seeds run `local` and verifies it. Rerunning `up` keeps an already-seeded run. Add `--no-build` to reuse the images.

| What | Where |
| --- | --- |
| API | `https://localhost:5443` (self-signed `localhost` certificate) |
| Eventing (SignalR) | `https://localhost:5444` |
| API and eventing for native development builds | `http://localhost:5450`, `http://localhost:5451` (same Core origin; see below) |
| Captured mail | `http://localhost:5480` |
| Manifest with credentials | `stack/private/out/qa-departments.json` |

```bash
python3 Docker/qa-departments/stack/qa-stack.py seed --scenarios base,adp   # another ad hoc set with a new run tag
python3 Docker/qa-departments/stack/qa-stack.py verify                      # sign in as every persona, check the bootstrap endpoints and flags
python3 Docker/qa-departments/stack/qa-stack.py unlock                      # clear sign-in lockouts of seeded users
python3 Docker/qa-departments/stack/qa-stack.py down                        # delete the stack and its data
```

The gateway also has plain http doors for simulator and emulator development builds (the Omni apps allow http only to localhost in a development build): it presents those requests to Core as `https://localhost:5443`, so a token has one issuer whichever door it came through, and rewrites the https URLs in Core's answers (eventing URL, avatars) to the http doors. Core's CORS list holds the e2e static server ports (19300-19309), Expo web (8081) and the Electron origins (`app://bundle`, `app://.`, `http://127.0.0.1:29317`). From Omni, `pnpm qa:regression <variant>` brings this stack up and runs every app platform against it; `pnpm qa:stack <command>` forwards to `qa-stack.py`.

Sign in to any app with a persona's user name (`qa-<scenario>-<persona>-<run tag>`, e.g. `qa-base-admin-local`) and the run password from the manifest.

## Seed another server

The image runs against any Resgrid database you can reach. It never assumes or reserves ids: every generated id is captured as it is inserted and later rows refer to it, so it can share a database with real departments. Run tags keep runs apart.

```bash
docker build -t resgrid-qa/departments:local Docker/qa-departments
docker run --rm -v "$PWD/qa-out:/out" \
  -e SEED_DB_DIALECT=postgres -e SEED_DB_HOST=db.qa.internal -e SEED_DB_NAME=resgrid -e SEED_DB_USER=resgrid \
  -e SEED_DB_PASSWORD_FILE=/run/secrets/db -v "$PWD/db-password:/run/secrets/db:ro" \
  -e SEED_USER_PASSWORD_FILE=/run/secrets/users -v "$PWD/user-password:/run/secrets/users:ro" \
  -e SEED_SERVER_ENVIRONMENT=2 -e SEED_API_URL=https://qaapi.example.com \
  resgrid-qa/departments:local seed --allow-database resgrid
docker run --rm -v "$PWD/qa-out:/out" resgrid-qa/departments:local verify --api-url https://qaapi.example.com
```

- `sql` writes the scripts for review instead (`/out/qa-departments-<tag>.<dialect>.sql`, run with `psql -f` or `sqlcmd -b -i`); `--offline` skips the database (no schema check, no flag overrides). Only `seed` writes ADP keys and sealed values, which need the generated ids.
- `plan` lists the scenarios.
- Options: `--scenarios a,b`, `--run-tag`, `--dialect postgres|sqlserver`, `--adp-mode envelopes|plaintext`, `--adp-catalog-version`, `--server-environment` (0 Prod, 1 Staging, 2 QA, 3 Development; used for flag predictions).
- Database passwords come only from `SEED_DB_PASSWORD` / `SEED_DB_PASSWORD_FILE`, never the command line. Without `SEED_USER_PASSWORD(_FILE)` a password is generated and written to the manifest.

### Guards

- `seed` refuses to write unless `--allow-database` names the database it is connected to, and the script itself re-checks the database name inside the transaction.
- A run tag can be used once: the script stops if any of its department or user names already exist.
- One seed at a time per database (`pg_advisory_xact_lock` / `sp_getapplock`); everything is one transaction, so a failure leaves nothing behind.
- Before anything runs, every row is checked against the live schema: unknown tables or columns, missing required columns, wrong types and values longer than a column fail with the list of problems.
- ADP envelopes use Core's LocalDev key provider (`DataProtectionConfig.KeyWrappingProviderType = LocalDev`), which Core refuses to start in production. On a server with a real key provider use `--adp-mode plaintext` (policy on, values stored in clear, still flagged protected).

## The departments

| Scenario | What it is for |
| --- | --- |
| `base` | Reference department: built-in statuses, staffing, unit statuses and call priorities; standard data in every module; server-default flags; 12-hour clock, US Eastern. Persona `multi` is also a member of `custom-statuses` and `terminology` (department switching). |
| `custom-statuses` | Custom personnel status, staffing and unit status sets (destinations, required notes, GPS, next-status workflows); Command and Utility units stay on built-in statuses; department call priorities; 24-hour clock, US Pacific. |
| `flags-on` | Every active feature flag forced on (operator overrides); India time zone (UTC+05:30). |
| `flags-off` | Every active feature flag forced off, including flags that are on globally; same data as `flags-on`. |
| `adp` | Advanced Data Protection enabled: call nature, notes, contact and call notes are sealed and come back `REDACTED` without a step-up grant. Admin and officer are TOTP-enrolled; member is not. |
| `sso-optional` | Enabled OIDC provider with password sign-in still allowed. |
| `sso-required` | SSO required by policy with OIDC and SAML providers: password sign-in is refused. |
| `mfa` | MFA required by policy and for admins; admin, officer, dispatcher and crew are TOTP-enrolled (secrets in the manifest); member must enroll. |
| `terminology` | Department terminology (Apparatus, Incidents, Members, Rotations, Directory) on a UK department with a 24-hour clock. |
| `restricted` | Restrictive permissions: only dispatchers create calls, only admins see locations, contacts and personal information, group-locked visibility, app sign-in limited by role. |
| `modules-off` | Every optional module switched off. |
| `empty` | Exactly what registration leaves: the managing member and nothing else (empty states). |
| `large` | Volume: about 80 personnel, 30 units, 47 active calls, 60 messages; AUS Eastern. |
| `incident-command` | Established incident command on the structure fire: divisions, groups, assignments, objectives, needs, timers, check-ins, notes. |
| `dispatch-center` | Department priorities, a call number pattern, pending, scheduled and linked calls, call contacts, check-in timers on new calls. |

**Baseline data** (`catalog/baseline.json`): 4 groups (2 stations), 6 roles, 4 ranks, 14 people (personas `admin`, `officer`, `member`, `dispatcher`, `crew`, `disabled`, `hidden` plus 7 others), 5 unit types, 6 units with crews, 5 call types, 7 calls (active, scheduled, closed, cancelled, a linked call; with dispatches to people, units, groups and roles, call notes, contacts and references), current statuses and staffing linked to calls, messages with read and unread recipients, notes, calendar items with RSVPs, shifts with days, signups and an approval, contacts, POI types and POIs, dispatch protocols, call and call-note templates, check-in timers, a training and a document. Times are relative to the seed (`-25m`, `+2h`), so calls are always fresh.

Statuses are named by meaning (`available`, `responding`, `onScene`, ...). A base department stores the built-in value; a department with custom sets stores the option with that base type. Custom status option ids are kept above Core's built-in range (25).

## Feature flags

Flags are read from the target database at seed time: every flag that is not archived is covered, so a flag added by a later migration needs no catalog change. A scenario's `flags.profile` is `server` (no overrides), `all-on` or `all-off`, plus optional per-key `overrides`. Overrides are operator overrides (no author), which Core honours while a flag is off globally or below its plan gate. The manifest records, per department, the state each flag should evaluate to and why (`override`, `global`, `prerequisite`, `environment`, `schedule`, `archived`; `null` when the server decides: rollout, targeting, plan gate). `verify` compares that with `FeatureToggles/GetAll` for each department's admin, and the Omni suites check that the app received it and that flag-gated navigation follows it.

## The manifest

`/out/qa-departments.json` (also `qa-departments-<tag>.json`), owner-only: run tag, API URL, the shared test password, the server's flags, and per department its id, name, purpose, what it covers, time zone, features (custom statuses, ADP, SSO, MFA, terminology, disabled modules, permissions), personas (user name, id, roles, group, TOTP secret), ids of groups, units and calls, and expected flag states. `verify` adds `verification`. It holds test credentials only; never commit it.

## Adding a scenario

Add `catalog/scenarios/<id>.json` with `id`, `name`, `purpose` and `covers`. Any top-level section replaces the baseline's (`department`, `settings`, `security` and `flags` merge); `extra.<section>` appends to a list; `personas.<id>` changes one persona; `baseline: "empty"` starts from registration only; `generate` adds volume. Then run the tests and `sql` against a migrated database to check it fits the schema:

```bash
cd Docker/qa-departments && python3 -m unittest discover -s tests -t .
```

## Limits

- PostgreSQL is verified end to end (Core migrations, seed, API sign-in for every persona, bootstrap endpoints, flags, ADP redaction). The SQL Server dialect is generated and unit-tested but has not been run against a live SQL Server yet.
- Seeded departments stay on a shared server; remove them with Core's department deletion. The isolated stack's `down` deletes everything.
- Locations shown from the document store (unit and personnel location history) are not seeded; units carry their position on their current status.
- With `SystemBehaviorConfig.CacheEnabled` on, new departments are unaffected (nothing is cached for them yet).
