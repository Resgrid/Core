"""Loads the scenario catalog: catalog/baseline.json plus one file per department in catalog/scenarios/."""
from __future__ import annotations

import copy
import json
import re
from pathlib import Path
from typing import Any

from .sqlgen import SeedError

CATALOG = Path(__file__).resolve().parent.parent / 'catalog'
# Sections a scenario may replace or extend.
SECTIONS = ('department', 'settings', 'statuses', 'groups', 'roles', 'ranks', 'people', 'unitTypes', 'units', 'callPriorities', 'callTypes', 'calls', 'messages', 'notes',
            'calendar', 'shifts', 'contacts', 'poiTypes', 'protocols', 'callTemplates', 'callNoteTemplates', 'checkInTimers', 'trainings', 'documents', 'permissions',
            'security', 'incidentCommand', 'flags')
SCENARIO_FIELDS = {'id', 'name', 'purpose', 'covers', 'baseline', 'extra', 'generate', 'personas', '$comment'} | set(SECTIONS)
_ID = re.compile(r'^[a-z][a-z0-9-]{1,30}$')


def load_catalog(root: Path = CATALOG) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    baseline = json.loads((root / 'baseline.json').read_text())
    scenarios = [json.loads(path.read_text()) for path in sorted((root / 'scenarios').glob('*.json'))]
    return baseline, scenarios


def resolve(baseline: dict[str, Any], scenario: dict[str, Any]) -> dict[str, Any]:
    """The full department definition: the baseline (or nothing), the scenario's own sections, then its extras."""
    unknown = set(scenario) - SCENARIO_FIELDS
    if unknown:
        raise SeedError(f'{scenario.get("id")}: unknown fields {sorted(unknown)}')
    if not _ID.match(scenario.get('id', '')):
        raise SeedError(f'scenario id "{scenario.get("id")}" must be lower-case letters, digits and dashes (2-31 characters)')
    kind = scenario.get('baseline', 'standard')
    if kind not in ('standard', 'empty'):
        raise SeedError(f'{scenario["id"]}: baseline must be "standard" or "empty"')
    if kind == 'standard':
        resolved: dict[str, Any] = {key: copy.deepcopy(value) for key, value in baseline.items() if key in SECTIONS}
    else:
        # An empty department still has what registration creates: its settings and the managing member, who is in no
        # group, has no role or rank and has never set a status.
        admin = copy.deepcopy(next(p for p in baseline['people'] if p.get('persona') == 'admin'))
        for detail in ('group', 'groupAdmin', 'roles', 'rank', 'statusCall'):
            admin.pop(detail, None)
        resolved = {'department': copy.deepcopy(baseline['department']), 'people': [admin]}
    for section in SECTIONS:
        if section in scenario:
            if section in ('department', 'settings', 'security', 'flags') and isinstance(resolved.get(section), dict):
                resolved[section] = {**resolved[section], **copy.deepcopy(scenario[section])}
            else:
                resolved[section] = copy.deepcopy(scenario[section])
    for section, items in scenario.get('extra', {}).items():
        if section not in SECTIONS or not isinstance(items, list):
            raise SeedError(f'{scenario["id"]}: extra.{section} must be a list section')
        resolved[section] = resolved.get(section, []) + copy.deepcopy(items)
    for persona, changes in scenario.get('personas', {}).items():
        person = next((p for p in resolved.get('people', []) if p.get('persona') == persona), None)
        if person is None:
            raise SeedError(f'{scenario["id"]}: personas.{persona} changes a persona the department does not have')
        person.update(copy.deepcopy(changes))
    if scenario.get('generate'):
        _generate(resolved, scenario['generate'])
    resolved.update({'id': scenario['id'], 'name': scenario['name'], 'purpose': scenario.get('purpose', ''), 'covers': scenario.get('covers', [])})
    return resolved


# --- volume -------------------------------------------------------------------------------------------------------------

_FIRST = ['Adrian', 'Blair', 'Cameron', 'Drew', 'Elliot', 'Frankie', 'Gray', 'Hayden', 'Indy', 'Jesse', 'Kendall', 'Logan', 'Marley', 'Noel', 'Oakley', 'Parker',
          'Reese', 'Sam', 'Taylor', 'Val']
_LAST = ['Abbott', 'Brennan', 'Chavez', 'Dawson', 'Eriksen', 'Fischer', 'Garcia', 'Hughes', 'Ibarra', 'Jensen', 'Kowalski', 'Lindqvist', 'Moreau', 'Nakamura',
         'Okafor', 'Petrov', 'Quintero', 'Russo', 'Schmidt', 'Tanaka']
_STATUSES = ['available', 'responding', 'onScene', 'notResponding', 'available']
_STAFFING = ['available', 'onShift', 'delayed', 'unavailable', 'committed']
_NATURES = [('Medical Emergency', 'Fall with possible hip injury.', 'medium'), ('Fire Alarm', 'Commercial alarm, zone 2.', 'low'),
            ('Vehicle Accident', 'Two-car collision, minor injuries.', 'high'), ('Structure Fire', 'Smoke from a detached garage.', 'emergency'),
            ('Public Service', 'Lift assist, no injuries.', 'low')]


def _generate(resolved: dict[str, Any], spec: dict[str, Any]) -> None:
    """Deterministic extra people, units, calls and messages for list, paging and map-density checks."""
    groups = [g['name'] for g in resolved.get('groups', []) if g.get('type') == 'station'] or [None]
    roles = [r['name'] for r in resolved.get('roles', [])]
    people = resolved.setdefault('people', [])
    for index in range(spec.get('people', 0)):
        people.append({'first': _FIRST[index % len(_FIRST)], 'last': f'{_LAST[(index // len(_FIRST)) % len(_LAST)]}{index // (len(_FIRST) * len(_LAST)) or ""}',
                       'group': groups[index % len(groups)], 'roles': roles[index % len(roles):index % len(roles) + 1] if roles else [],
                       'status': _STATUSES[index % len(_STATUSES)], 'staffing': _STAFFING[index % len(_STAFFING)]})
    unit_types = [t['name'] for t in resolved.get('unitTypes', [])]
    units = resolved.setdefault('units', [])
    for index in range(spec.get('units', 0)):
        unit_type = unit_types[index % len(unit_types)] if unit_types else None
        units.append({'name': f'{unit_type or "Unit"} {100 + index}', 'type': unit_type, 'station': groups[index % len(groups)],
                      'status': ['available', 'responding', 'onScene', 'available', 'outOfService'][index % 5],
                      'location': [39.70 + (index % 10) * 0.012, -89.72 + (index // 10) * 0.015]})
    calls = resolved.setdefault('calls', [])
    for index in range(spec.get('activeCalls', 0)):
        call_type, nature, priority = _NATURES[index % len(_NATURES)]
        calls.append({'key': f'generated-{index}', 'name': f'{call_type} - {200 + index} Generated Ave', 'nature': nature, 'type': call_type, 'priority': priority,
                      'state': 'active', 'loggedOn': f'-{5 + index * 3}m', 'address': f'{200 + index} Generated Ave, Springfield, IL 62702',
                      'location': [39.72 + (index % 8) * 0.01, -89.70 + (index // 8) * 0.012], 'number': f'QA-G{index:04d}'})
    personas = [p['persona'] for p in people if p.get('persona') and not p.get('disabled')]
    messages = resolved.setdefault('messages', [])
    for index in range(spec.get('messages', 0)):
        sender = personas[index % len(personas)]
        recipients = [p for p in personas if p != sender][:3]
        messages.append({'from': sender, 'to': recipients, 'subject': f'Generated message {index + 1}', 'body': f'Volume check message {index + 1}.', 'at': f'-{index * 7 + 1}m',
                         'readBy': recipients[:1] if index % 2 else []})
