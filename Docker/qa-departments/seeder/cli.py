"""resgrid-qa-departments: seed isolated, ad hoc QA departments into a Resgrid database.

  plan     list the scenarios (no database)
  sql      write the seed scripts for review (reads the schema and flags, writes nothing to the database)
  seed     validate against the live schema and insert every department in one transaction, then write the manifest
  verify   sign in as each persona through the API and check the app bootstrap endpoints and the real flag states
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import secrets
import string
import sys
from pathlib import Path
from typing import Any

from . import adp
from .build import Builder, Department, RunContext, adp_phase, custom_status_floors, memberships
from .catalog import load_catalog, resolve
from .flags import Flag
from .identity import generate_password, hash_password, password_problems
from .sqlgen import Guard, IdentityFloor, SeedError, Statement, validate

MANIFEST_VERSION = 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog='resgrid-qa-departments', description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest='command', required=True)

    plan = commands.add_parser('plan', help='list the scenarios')
    plan.add_argument('--json', action='store_true')

    for name in ('sql', 'seed'):
        command = commands.add_parser(name)
        command.add_argument('--scenarios', default='all', help='comma-separated scenario ids (default: all)')
        command.add_argument('--run-tag', default=os.environ.get('SEED_RUN_TAG'), help='suffix that keeps this run apart (default: random)')
        command.add_argument('--dialect', choices=('postgres', 'sqlserver'))
        command.add_argument('--host')
        command.add_argument('--port')
        command.add_argument('--database')
        command.add_argument('--user')
        command.add_argument('--server-environment', type=int, default=_int_env('SEED_SERVER_ENVIRONMENT'),
                             help="the server's SystemBehaviorConfig.Environment (0 Prod, 1 Staging, 2 QA, 3 Dev), for flag predictions")
        command.add_argument('--adp-mode', choices=('envelopes', 'plaintext'), default=os.environ.get('SEED_ADP_MODE', 'envelopes'),
                             help='envelopes: LocalDev-sealed values (server must run the LocalDev key provider); plaintext: policy on, values in clear')
        command.add_argument('--adp-catalog-version', type=int, default=_int_env('SEED_ADP_CATALOG_VERSION') or adp.CATALOG_VERSION)
        command.add_argument('--offline', action='store_true', help='(sql) no database: no schema validation, no flag overrides')
        command.add_argument('--out', default=os.environ.get('SEED_OUT', '/out'), help='folder for scripts and the manifest')
    commands.choices['seed'].add_argument('--api-url', default=os.environ.get('SEED_API_URL'), help='recorded in the manifest for the app suites')
    commands.choices['seed'].add_argument('--allow-database', default=os.environ.get('SEED_ALLOW_DATABASE'),
                                          help='the database name the seed may write to (must match the connection; required)')

    verify = commands.add_parser('verify', help='sign in as each persona and check the bootstrap endpoints')
    verify.add_argument('--manifest', default=os.environ.get('SEED_MANIFEST', '/out/qa-departments.json'))
    verify.add_argument('--api-url', default=os.environ.get('SEED_API_URL'))
    verify.add_argument('--insecure', action='store_true', help='accept a self-signed certificate (local QA stack)')

    args = parser.parse_args(argv)
    try:
        if args.command == 'plan':
            return _plan(args)
        if args.command == 'verify':
            from .verify import verify_manifest
            return verify_manifest(Path(args.manifest), args.api_url, args.insecure)
        return _sql_or_seed(args)
    except SeedError as error:
        print(f'error: {error}', file=sys.stderr)
        return 2


def _int_env(name: str) -> int | None:
    value = os.environ.get(name)
    return int(value) if value not in (None, '') else None


def _plan(args: argparse.Namespace) -> int:
    baseline, scenarios = load_catalog()
    resolved = [resolve(baseline, scenario) for scenario in scenarios]
    if args.json:
        print(json.dumps([{'id': s['id'], 'name': s['name'], 'purpose': s['purpose'], 'covers': s['covers']} for s in resolved], indent=2))
        return 0
    for s in resolved:
        print(f'{s["id"]:<18} {s["name"]}\n{"":<18} {s["purpose"]}\n{"":<18} people {len(s.get("people", []))}, units {len(s.get("units", []))}, calls {len(s.get("calls", []))}\n')
    return 0


def _selected(scenarios: list[dict[str, Any]], wanted: str) -> list[dict[str, Any]]:
    if wanted == 'all':
        return scenarios
    ids = [item.strip() for item in wanted.split(',') if item.strip()]
    known = {scenario['id'] for scenario in scenarios}
    unknown = [item for item in ids if item not in known]
    if unknown:
        raise SeedError(f'unknown scenarios {unknown}; known: {sorted(known)}')
    return [scenario for scenario in scenarios if scenario['id'] in ids]


def _run_tag(value: str | None) -> str:
    if value:
        if not all(c in string.ascii_lowercase + string.digits for c in value) or not 2 <= len(value) <= 12:
            raise SeedError('--run-tag must be 2-12 lower-case letters or digits')
        return value
    return ''.join(secrets.choice(string.ascii_lowercase + string.digits) for _ in range(5))


def _password() -> tuple[str, bool]:
    path = os.environ.get('SEED_USER_PASSWORD_FILE')
    password = Path(path).read_text().strip() if path else os.environ.get('SEED_USER_PASSWORD', '')
    if not password:
        return generate_password(), True
    problems = password_problems(password)
    if problems:
        raise SeedError(f'SEED_USER_PASSWORD must have {", ".join(problems)} (Identity password rules)')
    return password, False


def _sql_or_seed(args: argparse.Namespace) -> int:
    from .db import Database, Target

    tag = _run_tag(args.run_tag)
    baseline, scenarios = load_catalog()
    selected = [resolve(baseline, scenario) for scenario in _selected(scenarios, args.scenarios)]
    password, generated = _password()
    now = dt.datetime.now(dt.timezone.utc).replace(tzinfo=None, microsecond=0)

    database = None
    flags: list[Flag] = []
    targeted: set[int] = set()
    schema = None
    if not (args.command == 'sql' and args.offline):
        database = Database(Target.from_env(vars(args)))
        schema = database.schema()
        flags, targeted = database.flags(database.dialect(schema))
    try:
        run = RunContext(tag, now, hash_password(password), flags, args.server_environment, targeted, args.adp_mode, args.adp_catalog_version)
        departments = [Builder(run, scenario).build() for scenario in selected]
        statements: list[Statement] = [statement for department in departments for statement in department.statements] + memberships(departments, tag)
        floors = [IdentityFloor(*floor) for floor in custom_status_floors()]
        dialect_name = database.target.dialect if database else (args.dialect or 'postgres')
        dialect = database.dialect(schema) if database else _offline_dialect(dialect_name)
        declared: set[tuple[str, str]] = set()
        if schema is not None:
            problems = validate(statements, schema, declared)
            if problems:
                raise SeedError('the catalog does not fit this database schema:\n  ' + '\n  '.join(problems[:60]) + (f'\n  ... and {len(problems) - 60} more' if len(problems) > 60 else ''))
        db_name = database.database_name() if database else (args.database or os.environ.get('SEED_DB_NAME') or 'resgrid')
        guard = Guard(db_name, tag, tuple(d.name for d in departments), tuple(p.username for d in departments for p in d.people))
        out = Path(args.out)
        out.mkdir(parents=True, exist_ok=True)

        if args.command == 'sql':
            header = [f'Resgrid QA departments seed ({dialect.name}), run tag {tag}, {len(departments)} departments, {len(statements)} statements.',
                      'Generated by Docker/qa-departments. Review, then run in one transaction (psql -f / sqlcmd -b -i).',
                      'User password hashes are for a generated password that is not stored anywhere: use `seed` for usable credentials.' if generated else
                      'User password hashes are for SEED_USER_PASSWORD.',
                      'ADP key material and sealed values need the generated ids and are written only by `seed`.' if any(d.adp for d in departments) else '']
            script = dialect.script(statements, guard, floors, [line for line in header if line])
            path = out / f'qa-departments-{tag}.{dialect.name}.sql'
            path.write_text(script)
            print(f'Wrote {path} ({len(statements)} statements).')
            return 0

        if not args.allow_database or args.allow_database != db_name:
            raise SeedError(f'refusing to write to database "{db_name}": pass --allow-database {db_name} (or SEED_ALLOW_DATABASE) to confirm the target')

        def phase_two(keys: dict[tuple[str, str], int]) -> list[Statement]:
            later: list[Statement] = []
            for department in departments:
                department_id = keys[('department', f'{department.id}/')]
                rows, _ = adp_phase(department, department_id, keys)
                later += rows
            problems = validate(later, schema, set(declared)) if schema is not None else []
            if problems:
                raise SeedError('ADP phase does not fit this database schema:\n  ' + '\n  '.join(problems))
            return later

        prelude = dialect.prelude(guard, floors)
        body = dialect.body(statements)
        print(f'Seeding {len(departments)} departments ({len(statements)} statements) into {dialect.name} database {db_name}, run tag {tag}...', flush=True)
        keys = database.seed(dialect, prelude, body, phase_two)
        manifest = build_manifest(departments, keys, tag, now, password, database.target, args.api_url, run, flags)
        path = out / 'qa-departments.json'
        write_private(path, json.dumps(manifest, indent=2) + '\n')
        write_private(out / f'qa-departments-{tag}.json', json.dumps(manifest, indent=2) + '\n')
        print(f'Seeded run {tag}. Manifest with credentials: {path} (owner-only). Sign in with any persona user name and the run password.')
        for department in manifest['departments']:
            print(f'  {department["scenario"]:<18} #{department["departmentId"]:<6} {department["name"]}')
        return 0
    finally:
        if database:
            database.close()


def _offline_dialect(name: str):
    from .sqlgen import DIALECTS
    return DIALECTS[name](None)


def write_private(path: Path, text: str) -> None:
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, 'w', encoding='utf-8') as handle:
        handle.write(text)
    os.chmod(path, 0o600)


def build_manifest(departments: list[Department], keys: dict[tuple[str, str], int], tag: str, now: dt.datetime, password: str, target: Any, api_url: str | None,
                   run: RunContext, flags: list[Flag]) -> dict[str, Any]:
    """What the app regression suites read: departments, sign-in personas, ids of named records, expected flag states."""
    result: dict[str, Any] = {
        'version': MANIFEST_VERSION, 'runTag': tag, 'createdOn': now.isoformat() + 'Z', 'apiUrl': api_url,
        'database': {'dialect': target.dialect, 'host': target.host, 'name': target.database},
        'serverEnvironment': run.server_environment, 'adpMode': run.adp_mode,
        'credentials': {'password': password, 'note': 'Every seeded user shares this password. Test credentials only; never reuse.'},
        'flags': [{'key': flag.key, 'id': flag.id, 'enabledGlobally': flag.enabled_globally, 'archived': flag.archived, 'environment': flag.environment,
                   'prerequisites': [p[0] for p in flag.prerequisites]} for flag in flags],
        'departments': [],
    }
    for department in departments:
        s = department.scenario
        sid = department.id

        def ids(kind: str) -> dict[str, int]:
            prefix = f'{sid}/'
            return {name[len(prefix):]: value for (k, name), value in keys.items() if k == kind and name.startswith(prefix)}

        security = s.get('security', {})
        sso = security.get('sso', [])
        result['departments'].append({
            'scenario': sid, 'departmentId': keys[('department', f'{sid}/')], 'name': department.name, 'purpose': s['purpose'], 'covers': s['covers'],
            'timeZone': s['department']['timeZone'], 'use24HourTime': bool(s['department'].get('use24HourTime')),
            'features': {
                'customStatuses': {'personnel': 'personnel' in (s.get('statuses') or {}), 'staffing': 'staffing' in (s.get('statuses') or {}),
                                   'unitTypes': sorted(t for u in (s.get('statuses') or {}).get('units', []) for t in u['unitTypes'])},
                'departmentCallPriorities': bool(s.get('callPriorities')),
                'adp': bool(security.get('adp', {}).get('enabled')),
                'sso': 'required' if security.get('policy', {}).get('requireSso') else ('optional' if sso else 'none'),
                'ssoProviders': [p['provider'] for p in sso],
                'mfaRequired': bool(security.get('policy', {}).get('requireMfa')),
                'terminology': s.get('settings', {}).get('terminology', {}),
                'disabledModules': s.get('settings', {}).get('disabledModules', []),
                'permissions': s.get('permissions', []),
                'incidentCommand': bool(s.get('incidentCommand')),
                'empty': s.get('people', []) and len(s.get('people', [])) == 1,
            },
            'users': [{
                'persona': p.persona, 'username': p.username, 'userId': p.user_id, 'name': f'{p.first} {p.last}', 'admin': bool(p.spec.get('admin')),
                'groupAdmin': bool(p.spec.get('groupAdmin')), 'disabled': bool(p.spec.get('disabled')), 'hidden': bool(p.spec.get('hidden')),
                'group': p.spec.get('group'), 'roles': p.spec.get('roles', []), 'totpSecret': department.totp_secrets.get(p.username),
                'alsoMemberOf': p.spec.get('alsoMemberOf', []),
            } for p in department.people if p.persona],
            'otherPeople': len([p for p in department.people if not p.persona]),
            'groups': ids('group'), 'units': ids('unit'), 'pois': ids('poi'), 'calls': {k: {'id': v, 'name': next(c['name'] for c in s.get('calls', []) if c['key'] == k),
                                                                       'state': next(c.get('state', 'active') for c in s.get('calls', []) if c['key'] == k)} for k, v in ids('call').items()},
            'flags': {'profile': s.get('flags', {}).get('profile', 'server'), 'overrides': {f.key: department.flag_overrides[f.id] for f in flags if f.id in department.flag_overrides},
                      'expected': {key: {'enabled': p.enabled, 'source': p.source} for key, p in department.flag_predictions.items()}},
        })
    return result
