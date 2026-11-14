"""Post-seed check through the real API: every persona signs in the way the apps do, the app bootstrap endpoints answer
without a server error, protected fields come back REDACTED, and each department's flags evaluate as the seed predicted.

Results are written back into the manifest (`verification`) so the app suites know which expectations held on this
server; the exit code is 1 when anything failed.
"""
from __future__ import annotations

import json
import ssl
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any

from .identity import totp

SCOPE = 'openid profile offline_access'
# What a freshly started app reads (resgrid-v4-api skill, bootstrap sequence) plus each module's list endpoint.
BOOTSTRAP = [
    'Config/GetConfig', 'Security/GetCurrentUsersRights', 'FeatureToggles/GetAll', 'DataProtection/Capabilities', 'Config/GetTerminology',
    'Calls/GetActiveCalls', 'Calls/GetPendingCalls', 'Calls/GetAllPendingScheduledCalls', 'CallPriorities/GetAllCallPriorites', 'CallTypes/GetAllCallTypes',
    'Units/GetAllUnitsInfos', 'UnitStatus/GetAllUnitStatuses', 'UnitRoles/GetAllUnitRolesAndAssignmentsForDepartment', 'Personnel/GetAllPersonnelInfos',
    'Statuses/GetAllStatusesForPersonnel', 'Statuses/GetAllStaffingsForPersonnel', 'Statuses/GetAllUnitStatuses', 'Groups/GetAllGroups',
    'Messages/GetInboxMessages', 'Contacts/GetAllContacts', 'Mapping/GetPoiTypes', 'Mapping/GetPoiCapabilities', 'Mapping/GetMapDataAndMarkers', 'Protocols/GetAllProtocols',
    'Notes/GetAllNotes', 'Calendar/GetDepartmentCalendarItems', 'Shifts/GetShifts', 'Shifts/GetTodaysShifts',
]


class Api:
    def __init__(self, base_url: str, insecure: bool):
        self.base = base_url.rstrip('/').removesuffix('/api/v4') + '/api/v4/'
        self.context = ssl._create_unverified_context() if insecure else None  # noqa: S323 - local self-signed QA stack only

    def request(self, method: str, path: str, token: str | None = None, form: dict[str, str] | None = None) -> tuple[int, Any]:
        data = urllib.parse.urlencode(form).encode() if form is not None else None
        request = urllib.request.Request(self.base + path, data=data, method=method)
        request.add_header('Accept', 'application/json')
        if form is not None:
            request.add_header('Content-Type', 'application/x-www-form-urlencoded')
        if token:
            request.add_header('Authorization', f'Bearer {token}')
        try:
            with urllib.request.urlopen(request, timeout=60, context=self.context) as response:
                return response.status, _json(response.read())
        except urllib.error.HTTPError as error:
            return error.code, _json(error.read())
        except (urllib.error.URLError, TimeoutError, ConnectionError) as error:
            # Status 0: recorded as a failure like any other answer, and the remaining checks still run.
            return 0, f'transport error: {error}'

    def sign_in(self, username: str, password: str, secret: str | None) -> tuple[int, Any]:
        form = {'grant_type': 'password', 'username': username, 'password': password, 'scope': SCOPE}
        if secret:
            form['totp_code'] = totp(secret)
        return self.request('POST', 'Connect/token', form=form)


def _json(body: bytes) -> Any:
    try:
        return json.loads(body) if body else None
    except ValueError:
        return body[:200].decode('utf-8', 'replace')


def verify_manifest(path: Path, api_url: str | None, insecure: bool) -> int:
    manifest = json.loads(path.read_text())
    api_url = api_url or manifest.get('apiUrl')
    if not api_url:
        raise SystemExit('verify needs --api-url (or apiUrl in the manifest).')
    api = Api(api_url, insecure)
    password = manifest['credentials']['password']
    failures: list[str] = []
    results: dict[str, Any] = {}
    for department in manifest['departments']:
        sid = department['scenario']
        report: dict[str, Any] = {'signIn': {}, 'endpoints': {}, 'flags': {}, 'checks': {}}
        results[sid] = report
        admin_token = None
        for user in department['users']:
            if user['disabled']:
                continue
            status, body = api.sign_in(user['username'], password, user.get('totpSecret'))
            error = body.get('error') if isinstance(body, dict) else None
            outcome = 'signed-in' if status == 200 and isinstance(body, dict) and body.get('access_token') else (error or f'http-{status}')
            report['signIn'][user['persona']] = outcome
            expected = _expected_sign_in(department, user)
            if outcome not in expected:
                failures.append(f'{sid}/{user["persona"]}: sign-in {outcome}, expected {" or ".join(expected)}')
            if outcome == 'signed-in' and user['persona'] == 'admin':
                admin_token = body['access_token']
        if admin_token is None:
            continue
        for endpoint in BOOTSTRAP:
            status, body = api.request('GET', endpoint, admin_token)
            envelope = body.get('Status') if isinstance(body, dict) else None
            report['endpoints'][endpoint] = {'http': status, 'status': envelope}
            # The admin may read every bootstrap endpoint. A Core refusal is JSON (problem details or the v4 envelope) and
            # is allowed only where the department switched the module off; anything else, and any non-JSON body, is a defect.
            refused_by_design = status in (400, 403, 404) and isinstance(body, dict) and _module_off(department, endpoint)
            if status != 200 and not refused_by_design:
                failures.append(f'{sid}: {endpoint} answered HTTP {status}{"" if isinstance(body, dict) else " (not from Core: " + str(body)[:60] + ")"}')
            elif status == 200 and not isinstance(body, dict):
                failures.append(f'{sid}: {endpoint} answered 200 without a JSON body')
            elif endpoint == 'FeatureToggles/GetAll' and status == 200:
                report['flags'] = {item['Key']: item['Enabled'] for item in body.get('Data') or []}
            elif endpoint == 'Calls/GetActiveCalls' and status == 200 and department['features']['adp'] and manifest.get('adpMode') == 'envelopes':
                natures = {call.get('Nature') for call in body.get('Data') or []}
                report['checks']['protectedNatureRedacted'] = natures == {'REDACTED'}
                if natures != {'REDACTED'}:
                    failures.append(f'{sid}: ADP call nature not REDACTED without a grant ({sorted(str(n) for n in natures)[:3]})')
        for key, expected in department['flags']['expected'].items():
            actual = report['flags'].get(key)
            if expected['enabled'] is None or actual is None:
                continue
            if actual != expected['enabled']:
                failures.append(f'{sid}: flag {key} is {"on" if actual else "off"}, expected {"on" if expected["enabled"] else "off"} ({expected["source"]})')
        time.sleep(0.2)
    manifest['verification'] = {'apiUrl': api_url, 'checkedOn': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()), 'failures': failures, 'departments': results}
    path.write_text(json.dumps(manifest, indent=2) + '\n')
    for failure in failures:
        print(f'FAIL {failure}', file=sys.stderr)
    signed_in = sum(1 for report in results.values() for outcome in report['signIn'].values() if outcome == 'signed-in')
    print(f'Verified {len(results)} departments against {api_url}: {signed_in} sign-ins, {len(failures)} failures.')
    return 1 if failures else 0


_MODULE_ENDPOINTS = {'Messages/': 'messaging', 'Shifts/': 'shifts', 'Calendar/': 'calendar', 'Notes/': 'notes', 'Mapping/': 'mapping'}


def _module_off(department: dict[str, Any], endpoint: str) -> bool:
    disabled = set(department['features']['disabledModules'])
    return any(endpoint.startswith(prefix) and module in disabled for prefix, module in _MODULE_ENDPOINTS.items())


def _expected_sign_in(department: dict[str, Any], user: dict[str, Any]) -> tuple[str, ...]:
    features = department['features']
    if features['sso'] == 'required':
        # Password sign-in is refused while SSO is required (ConnectController password grant).
        return ('invalid_grant', 'sso_required', 'access_denied', 'http-400', 'http-403')
    if features['mfaRequired'] and not user.get('totpSecret'):
        # Enforced only with TwoFactorConfig.RequireMfaEnforcementEnabled; either outcome is a correct server.
        return ('mfa_enrollment_required', 'signed-in')
    return ('signed-in',)
