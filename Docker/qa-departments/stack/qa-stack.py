#!/usr/bin/env python3
"""The isolated Resgrid QA stack: Core from this checkout, a fresh database, the QA departments seeded and verified.

  python3 Docker/qa-departments/stack/qa-stack.py up [--no-build] [--run-tag local]
  python3 Docker/qa-departments/stack/qa-stack.py seed [--run-tag TAG] [--scenarios base,adp]   another ad hoc set
  python3 Docker/qa-departments/stack/qa-stack.py verify
  python3 Docker/qa-departments/stack/qa-stack.py unlock                                         clear sign-in lockouts of seeded users
  python3 Docker/qa-departments/stack/qa-stack.py status | logs [service] | down

Needs Docker with Compose and Python 3.10+. Everything secret stays in stack/private/ (git-ignored, owner-only):
generated passwords, Core's ResgridConfig.json, certificates, and out/qa-departments.json, the manifest the app
regression suites read (E2E_QA_MANIFEST). The API is https://localhost:5443 and eventing https://localhost:5444,
both with a self-signed localhost certificate; native development builds use http://localhost:5450 and :5451 instead;
captured mail is http://localhost:5480.
"""
from __future__ import annotations

import argparse
import json
import os
import secrets
import shutil
import ssl
import string
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

STACK = Path(__file__).resolve().parent
CORE = STACK.parents[2]
PRIVATE = STACK / 'private'
NAME = os.environ.get('QA_STACK_NAME', 'resgrid-qa')
API_PORT = int(os.environ.get('QA_API_PORT', '5443'))
SUBNET = os.environ.get('QA_SUBNET', '172.31.250.0/24')
EVENTING_PORT = int(os.environ.get('QA_EVENTING_PORT', '5444'))
# Origins the app suites serve from: the e2e static server (E2E_WEB_PORT=19300 plus one port per Playwright worker) and Expo web dev.
CORS_ORIGINS = [f'http://{host}:{port}' for host in ('127.0.0.1', 'localhost') for port in [*range(19300, 19310), 8081]]
# Electron serves the web export from app://bundle (Dispatch: app://.) and BigBoard from a fixed loopback origin.
CORS_ORIGINS += ['app://bundle', 'app://.', 'http://127.0.0.1:29317']
IMAGES = {'console': 'resgrid-qa/console:local', 'api': 'resgrid-qa/api:local', 'eventing': 'resgrid-qa/eventing:local'}


def run(*args: str, check: bool = True, capture: bool = False, **kwargs) -> subprocess.CompletedProcess:
    return subprocess.run(list(args), check=check, text=True, capture_output=capture, **kwargs)


def compose(*args: str, check: bool = True, capture: bool = False) -> subprocess.CompletedProcess:
    return run('docker', 'compose', '--project-name', NAME, '--env-file', str(PRIVATE / 'stack.env'), '-f', str(STACK / 'compose.yaml'), *args, check=check, capture=capture)


def secret_file(name: str, factory) -> str:
    path = PRIVATE / name
    if not path.exists():
        path.write_text(factory())
        path.chmod(0o600)
    return path.read_text().strip()


def prepare() -> None:
    PRIVATE.mkdir(exist_ok=True)
    PRIVATE.chmod(0o700)
    (PRIVATE / 'out').mkdir(exist_ok=True)
    # The seeder runs as uid 10001 and writes the manifest here.
    (PRIVATE / 'out').chmod(0o777)
    db = secret_file('db-password', lambda: secrets.token_hex(20))
    rabbit = secret_file('rabbit-password', lambda: secrets.token_hex(20))
    secret_file('user-password', lambda: 'Qa' + secrets.token_hex(6) + 'X7')
    keys = {name: secret_file(name, lambda: secrets.token_hex(24)) for name in ('encryption-key', 'encryption-salt', 'jwt-key', 'link-passphrase', 'backend-api-key', 'worker-payload-key')}
    (PRIVATE / 'stack.env').write_text(f'QA_RABBIT_PASSWORD={rabbit}\nQA_API_PORT={API_PORT}\nQA_EVENTING_PORT={EVENTING_PORT}\nQA_SUBNET={SUBNET}\n')
    (PRIVATE / 'stack.env').chmod(0o600)
    run('docker', 'run', '--rm', '-v', f'{STACK}:/stack', '-v', f'{PRIVATE}:/private', '--user', f'{os.getuid()}:{os.getgid()}', '--entrypoint', 'python',
        'resgrid-qa/departments:local', '/stack/certs.py', '/private')
    connection = f'Host=postgres;Port=5432;Database=resgrid_qa;Username=resgrid;Password={db}'
    other = lambda database: connection.replace('Database=resgrid_qa;', f'Database={database};')  # noqa: E731
    api, eventing = f'https://localhost:{API_PORT}', f'https://localhost:{EVENTING_PORT}'
    config = {
        'DataConfig.DatabaseType': '1', 'DataConfig.DocDatabaseType': '1', 'DataConfig.ConnectionString': connection, 'DataConfig.CoreConnectionString': connection,
        'DataConfig.DocumentConnectionString': other('resgrid_qa_docs'),
        'OidcConfig.DatabaseType': '1', 'OidcConfig.ConnectionString': other('resgrid_qa_oidc'),
        'OidcConfig.EncryptionCert': (PRIVATE / 'oidc-encryption.b64').read_text(), 'OidcConfig.SigningCert': (PRIVATE / 'oidc-signing.b64').read_text(),
        'WorkerConfig.DatabaseType': '1', 'WorkerConfig.WorkerDbConnectionString': other('resgrid_qa_workers'), 'WorkerConfig.PayloadKey': keys['worker-payload-key'],
        'CacheConfig.RedisConnectionString': 'redis:6379,abortConnect=false,syncTimeout=10000',
        'SystemBehaviorConfig.Environment': '3', 'SystemBehaviorConfig.IsAzure': 'false',
        # Redis caching as in production: without it every feature flag evaluation reads the database and FeatureToggles/GetAll
        # takes seconds. The seed writes before the API first reads a new department, so nothing stale is cached.
        'SystemBehaviorConfig.CacheEnabled': 'true',
        'SystemBehaviorConfig.ResgridBaseUrl': api, 'SystemBehaviorConfig.ResgridApiBaseUrl': api, 'SystemBehaviorConfig.ResgridEventingBaseUrl': eventing,
        'SystemBehaviorConfig.DoNotBroadcast': 'true', 'SystemBehaviorConfig.ServiceBusType': '1', 'SystemBehaviorConfig.OutboundEmailType': '1',
        'SystemBehaviorConfig.LocationName': 'Resgrid QA', 'SystemBehaviorConfig.LoginPageNotice': 'LOCAL QA SYSTEM - fictional test departments',
        'SystemBehaviorConfig.ApiTokenEncryptionPassphrase': keys['link-passphrase'], 'SystemBehaviorConfig.ExternalLinkUrlParamPassphrase': keys['link-passphrase'],
        'SystemBehaviorConfig.ExternalAudioUrlParamPasshprase': keys['link-passphrase'], 'SystemBehaviorConfig.BillingApiBaseUrl': '',
        'ServiceBusConfig.RabbitHostname': 'rabbit', 'ServiceBusConfig.RabbitUsername': 'resgrid', 'ServiceBusConfig.RabbbitPassword': rabbit, 'ServiceBusConfig.RabbbitExchange': '',
        'OutboundEmailServerConfig.Host': 'mail', 'OutboundEmailServerConfig.Port': '1025', 'OutboundEmailServerConfig.EnableSsl': 'false',
        'OutboundEmailServerConfig.FromMail': 'qa@qa.example.com', 'OutboundEmailServerConfig.ToMail': 'qa@qa.example.com',
        'SecurityConfig.EncryptionKey': keys['encryption-key'], 'SecurityConfig.EncryptionSaltValue': keys['encryption-salt'],
        'JwtConfig.Key': keys['jwt-key'], 'JwtConfig.EventsClientSecret': keys['jwt-key'],
        'ApiConfig.CorsAllowedOrigins': ','.join(CORS_ORIGINS), 'ApiConfig.BackendInternalApikey': keys['backend-api-key'],
        'FeatureFlagsConfig.TrackEvaluations': 'false',
        # The gateway's X-Forwarded-Proto is trusted from the stack network only.
        'WebConfig.IngressProxyNetwork': SUBNET.split('/')[0], 'WebConfig.IngressProxyNetworkCidr': SUBNET.split('/')[1],
        # The ADP department's key is sealed with Core's LocalDev provider (refused in production).
        'DataProtectionConfig.KeyWrappingProviderType': 'LocalDev',
        # So the MFA department's policy is enforced at sign-in.
        'TwoFactorConfig.RequireMfaEnforcementEnabled': 'true',
    }
    path = PRIVATE / 'ResgridConfig.json'
    path.write_text(json.dumps(config, indent=2))
    # Read by the containers' non-root users; the folder itself stays owner-only on the host.
    path.chmod(0o644)


def build() -> None:
    for target, image in IMAGES.items():
        print(f'Building {image} from {CORE} ...', flush=True)
        run('docker', 'build', '-f', str(STACK / 'core.Dockerfile'), '--target', target, '-t', image, str(CORE))
    build_seeder()


def build_seeder() -> None:
    run('docker', 'build', '-t', 'resgrid-qa/departments:local', str(STACK.parent))


def wait_for_api(timeout: int = 240) -> None:
    context = ssl._create_unverified_context()  # noqa: S323 - our own self-signed localhost certificate
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            with urllib.request.urlopen(f'https://localhost:{API_PORT}/api/v4/Health/GetCurrent', timeout=5, context=context) as response:
                if response.status == 200:
                    return
        except Exception:
            pass
        time.sleep(3)
    compose('logs', '--tail', '80', 'api', check=False)
    raise SystemExit('The API did not become healthy; its log is above.')


def seed(tag: str, scenarios: str) -> None:
    compose('run', '--rm', 'seed', 'seed', '--run-tag', tag, '--scenarios', scenarios)


def run_tag(value: str) -> str:
    """--run-tag, held to the seeder's rule (2-12 lower-case letters or digits) before it reaches SQL or a command line."""
    if not 2 <= len(value) <= 12 or not all(c in string.ascii_lowercase + string.digits for c in value):
        raise argparse.ArgumentTypeError('must be 2-12 lower-case letters or digits')
    return value


def seeded(tag: str) -> bool:
    """Whether this stack's database already holds the departments of run `tag` (and its manifest is still here)."""
    query = f"SELECT count(*) FROM departments WHERE name LIKE '% ({run_tag(tag)})'"
    found = compose('exec', '-T', 'postgres', 'psql', '-U', 'resgrid', '-d', 'resgrid_qa', '-Atc', query, check=False, capture=True)
    return found.returncode == 0 and found.stdout.strip() not in ('', '0') and (PRIVATE / 'out' / 'qa-departments.json').exists()


def verify() -> int:
    # Inside the stack network the gateway is https://gateway:8443 (its certificate names gateway too).
    return compose('run', '--rm', 'seed', 'verify', '--manifest', '/out/qa-departments.json', '--api-url', 'https://gateway:8443', '--insecure', check=False).returncode


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=('up', 'seed', 'verify', 'unlock', 'status', 'logs', 'down'))
    parser.add_argument('service', nargs='?')
    parser.add_argument('--no-build', action='store_true', help='reuse the resgrid-qa/* images already built')
    parser.add_argument('--run-tag', default=None, type=run_tag)
    parser.add_argument('--scenarios', default='all')
    args = parser.parse_args()
    if not shutil.which('docker'):
        raise SystemExit('Docker is required.')
    if args.command == 'down':
        compose('down', '--volumes', '--remove-orphans', check=False)
        return 0
    if args.command == 'unlock':
        # Failed passwords or TOTP codes lock an account for 15 minutes (Identity lockout); only the seeded qa-* users.
        query = "UPDATE aspnetusers SET accessfailedcount = 0, lockoutend = NULL WHERE normalizedusername LIKE 'QA-%' AND (accessfailedcount > 0 OR lockoutend IS NOT NULL)"
        return compose('exec', '-T', 'postgres', 'psql', '-U', 'resgrid', '-d', 'resgrid_qa', '-c', query, check=False).returncode
    if args.command in ('status', 'logs'):
        return compose('ps' if args.command == 'status' else 'logs', *([args.service] if args.service else []), check=False).returncode
    if args.command == 'up':
        if args.no_build:
            build_seeder()
        else:
            build()
        prepare()
        compose('up', '-d', '--wait', 'postgres', 'redis', 'rabbit', 'mail')
        print('Migrating the database with Core\'s console (--DbUpdate, --OidcUpdate) ...', flush=True)
        compose('run', '--rm', 'migrate', '--DbUpdate')
        compose('run', '--rm', 'migrate', '--OidcUpdate')
        compose('up', '-d', 'api', 'eventing', 'api-loopback', 'eventing-loopback', 'gateway')
        wait_for_api()
        tag = args.run_tag or 'local'
        if seeded(tag):
            print(f'Run {tag} is already seeded in this stack; keeping it (seed another set with: qa-stack.py seed).', flush=True)
        else:
            seed(tag, args.scenarios)
        status = verify()
        print(f'\nQA stack up: API https://localhost:{API_PORT}, eventing https://localhost:{EVENTING_PORT}, mail http://localhost:5480.')
        print(f'Manifest (credentials, owner-only): {PRIVATE / "out" / "qa-departments.json"}')
        return status
    prepare()
    if args.command == 'seed':
        seed(args.run_tag or ''.join(secrets.choice('abcdefghjkmnpqrstuvwxyz23456789') for _ in range(5)), args.scenarios)
        return verify()
    return verify()


if __name__ == '__main__':
    sys.exit(main())
