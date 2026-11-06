"""Turns one scenario (baseline plus its overrides) into the ordered rows that create its department.

Enum values mirror Core/Resgrid.Model; each table below lists the file it comes from. Rows reference each other by
natural keys scoped to the scenario ("base/Engine 1"), never by ids, so one run can seed every scenario into a shared
database.
"""
from __future__ import annotations

import datetime as dt
import hashlib
import re
from dataclasses import dataclass, field
from typing import Any

from . import adp, proto
from .flags import Flag, Prediction, override_value, overrides_for, predict
from .identity import Ids, USERNAME_CHARACTERS, generate_totp_secret
from .sqlgen import Key, KeyList, Row, SeedError, Statement, Update

# --- Core enums -----------------------------------------------------------------------------------------------------

# ActionTypes.cs (built-in personnel statuses) and ActionBaseTypes.cs (BaseType of a custom personnel/unit option).
PERSONNEL_BUILT_IN = {'available': 0, 'notResponding': 1, 'responding': 2, 'onScene': 3, 'availableStation': 4, 'respondingToStation': 5, 'respondingToScene': 6, 'onUnit': 7}
ACTION_BASE = {
    'available': 0, 'notResponding': 1, 'responding': 2, 'onScene': 3, 'madeContact': 4, 'investigating': 5, 'dispatched': 6, 'cleared': 7, 'returning': 8,
    'staging': 9, 'unavailable': 10, 'enroute': 11, 'transporting': 12, 'delivering': 13, 'atPatient': 14, 'atHospital': 15, 'searching': 16, 'loading': 17,
    'standby': 18, 'onPatrol': 19, 'maintenance': 20, 'onBreak': 21, 'completed': 22, 'inQuarters': 23,
}
# UserStateTypes.cs (built-in staffing) and StaffingBaseTypes.cs.
STAFFING_BUILT_IN = {'available': 0, 'delayed': 1, 'unavailable': 2, 'committed': 3, 'onShift': 4}
STAFFING_BASE = {'available': 0, 'delayed': 1, 'unavailable': 2, 'committed': 3, 'onShift': 4, 'offDuty': 5, 'onCall': 6}
# UnitStateTypes.cs (built-in unit statuses).
UNIT_BUILT_IN = {'available': 0, 'delayed': 1, 'unavailable': 2, 'committed': 3, 'outOfService': 4, 'responding': 5, 'onScene': 6, 'staging': 7, 'returning': 8, 'cancelled': 9, 'released': 10, 'manual': 11, 'enroute': 12}
# A meaning used in the baseline that a custom set expresses with a different base type.
BASE_ALIASES = {'outOfService': ('maintenance', 'unavailable'), 'availableStation': ('available',), 'respondingToStation': ('responding',), 'respondingToScene': ('responding',), 'onUnit': ('responding', 'onScene')}
# CustomStateTypes.cs, CustomStateNoteTypes.cs, CustomStateDetailTypes.cs.
CUSTOM_STATE_TYPES = {'personnel': 1, 'unit': 2, 'staffing': 3}
NOTE_TYPES = {'none': 0, 'optional': 1, 'required': 2}
DESTINATION_TYPES = {'none': 0, 'stations': 1, 'calls': 2, 'callsAndStations': 3, 'pois': 4, 'callsAndPois': 5, 'stationsAndPois': 6, 'callsStationsAndPois': 7}
# DestinationEntityTypes: where an ActionLog/UnitState points.
DESTINATION_STATION, DESTINATION_CALL = 1, 2
# CallStates.cs, CallPriority.cs, CallTypeClasses.cs, UnitTypeClasses.cs, CallSources.cs, CallNoteSources.cs.
CALL_STATES = {'active': 0, 'closed': 1, 'cancelled': 2, 'unfounded': 3, 'founded': 4, 'minor': 5, 'transferred': 6, 'falseAlarm': 7, 'pending': 8}
PRIORITY_BUILT_IN = {'low': 0, 'medium': 1, 'high': 2, 'emergency': 3}
CALL_TYPE_CLASSES = {'none': -1, 'fire': 1, 'medical': 2, 'rescue': 3, 'trafficCollision': 4, 'hazmat': 5, 'alarm': 6, 'serviceCall': 7, 'lawEnforcement': 8, 'disasterWeather': 9, 'mutualAid': 10, 'nonEmergency': 11, 'other': 12}
UNIT_TYPE_CLASSES = {'none': -1, 'engine': 1, 'aerial': 2, 'rescue': 3, 'tanker': 4, 'ambulance': 5, 'medicalResponse': 6, 'wildland': 7, 'hazmat': 8, 'command': 9, 'support': 10, 'watercraft': 11, 'aircraft': 12, 'patrol': 13, 'other': 14}
CALL_SOURCE_USER = 1
CALL_NOTE_MOBILE = 2
# DepartmentGroupTypes.cs (the typo "Orginizational" is Core's).
GROUP_TYPES = {'organizational': 1, 'station': 2}
# DepartmentSettingTypes.cs.
SETTING = {
    'BigBoardMapCenterGpsCoordinates': 5, 'DisabledAutoAvailable': 9, 'InternalDispatchEmail': 15, 'PersonnelSortOrder': 18, 'UnitsSortOrder': 19,
    'CallsSortOrder': 20, 'ModuleSettings': 32, 'Require2FAForAdmins': 36, 'CheckInTimersAutoEnableForNewCalls': 38, 'EnableModernNotifications': 53,
    'CallNumberingConfig': 115, 'DepartmentTerminology': 118,
}
SORT_ORDERS = {
    'personnelSortOrder': ('PersonnelSortOrder', {'default': 0, 'lastName': 1, 'firstName': 2, 'group': 3}),
    'unitsSortOrder': ('UnitsSortOrder', {'default': 0, 'name': 1}),
    'callsSortOrder': ('CallsSortOrder', {'default': 0, 'date': 1, 'priority': 2}),
}
# PermissionTypes.cs (implicit ordinals) and PermissionActions.cs.
PERMISSION_TYPES = {
    'AddPersonnel': 0, 'RemovePersonnel': 1, 'CreateCall': 2, 'CreateTraining': 3, 'CreateDocument': 4, 'CreateCalendarEntry': 5, 'CreateNote': 6,
    'CreateLog': 7, 'CreateShift': 8, 'ViewPersonalInfo': 9, 'AdjustInventory': 10, 'CanSeePersonnelLocations': 11, 'CanSeeUnitLocations': 12,
    'CreateMessage': 13, 'ViewGroupUsers': 14, 'DeleteCall': 15, 'CloseCall': 16, 'AddCallData': 17, 'ViewGroupUnits': 18, 'ContactEdit': 19,
    'ContactView': 20, 'ContactDelete': 21, 'CreateWorkflow': 22, 'ManageWorkflowCredentials': 23, 'ViewWorkflowRuns': 24, 'ViewUdfFields': 25,
    'ManageRoutes': 26, 'DeleteLog': 27, 'UseCalendarSync': 28, 'DispatchAppLogin': 29, 'CommandAppLogin': 30, 'ViewProtectedCallData': 32,
    'EditProtectedCallData': 33, 'ViewProtectedPersonnelData': 34, 'ViewProtectedContactData': 35, 'ViewProtectedOperationalData': 36, 'ExportProtectedData': 37,
}
PERMISSION_ACTIONS = {'departmentAdminsOnly': 0, 'departmentAndGroupAdmins': 1, 'departmentAdminsAndSelectRoles': 2, 'everyone': 3, 'departmentAndGroupAdminsAndSelectRoles': 4}
# Shifts, calendar, messages, check-in timers, incident command (IncidentCommandEnums.cs, CheckInTimerTargetType).
SHIFT_ASSIGNMENT = {'assigned': 0, 'signup': 1}
SHIFT_SCHEDULE_MANUAL = 1
ATTENDEE_RSVP = 1
SIGNUP_NONE, SIGNUP_RSVP = 0, 2
CHECK_IN_TARGETS = {'personnel': 0, 'unitType': 1, 'ic': 2, 'par': 3, 'hazmatExposure': 4, 'sectorRotation': 5, 'rehab': 6}
NODE_TYPES = {'division': 0, 'group': 1, 'branch': 2, 'sector': 3, 'strikeTeam': 4, 'taskForce': 5, 'staging': 6, 'unifiedCommand': 7}
OBJECTIVE_TYPES = {'general': 0, 'benchmark': 1, 'safety': 2}
OBJECTIVE_STATUS = {'pending': 0, 'complete': 1, 'inProgress': 2}
NEED_STATUS = {'open': 0, 'partiallyMet': 1, 'met': 2, 'cancelled': 3}
TIMER_TYPES = {'scene': 0, 'benchmark': 1, 'role': 2, 'custom': 3}
RESOURCE_UNIT, RESOURCE_PERSONNEL = 0, 1
# Identity, plans and security (DataConfig.cs, SubscriptionsService.CreateFreePlanPaymentAsync, AuthenticatorSeedProtector.cs).
USERS_ROLE_ID = '38B461D7-E848-46EF-8C06-ECE5B618D9D1'
FREE_PLAN_ID = 1
PAYMENT_METHOD_SYSTEM = 4
AUTHENTICATOR_PROVIDER, AUTHENTICATOR_NAME = '[AspNetUserStore]', 'AuthenticatorKey'
SSO_PROVIDERS = {'saml': 0, 'oidc': 1}

# Custom personnel/staffing/unit option ids must stay above the built-in range (CallStatusLinkage.MaxBuiltInStatusId).
MAX_BUILT_IN_STATUS_ID = 25

_OFFSET = re.compile(r'([+-])(\d+)([mhd])')


def parse_offset(text: str | int | None) -> dt.timedelta:
    """'-25m', '+2h', '-3d', '+2d+3h' -> timedelta; an int is minutes."""
    if text is None:
        return dt.timedelta(0)
    if isinstance(text, int):
        return dt.timedelta(minutes=text)
    total = dt.timedelta(0)
    position = 0
    for match in _OFFSET.finditer(text):
        if match.start() != position:
            break
        amount = int(match.group(2)) * (1 if match.group(1) == '+' else -1)
        total += {'m': dt.timedelta(minutes=amount), 'h': dt.timedelta(hours=amount), 'd': dt.timedelta(days=amount)}[match.group(3)]
        position = match.end()
    if position != len(text):
        raise SeedError(f'invalid relative time "{text}" (use e.g. -25m, +2h, -3d, +2d+3h)')
    return total


@dataclass
class RunContext:
    tag: str
    now: dt.datetime
    password_hash: str
    flags: list[Flag]
    server_environment: int | None = None
    targeted_flags: set[int] = field(default_factory=set)
    adp_mode: str = 'envelopes'
    adp_catalog_version: int = adp.CATALOG_VERSION

    @property
    def ids(self) -> Ids:
        return Ids(self.tag)


@dataclass
class Person:
    ref: str
    username: str
    user_id: str
    first: str
    last: str
    persona: str | None
    spec: dict[str, Any]


@dataclass
class AdpWork:
    """What the runner seals once the inserts returned their ids (phase 2, same transaction)."""

    call_fields: dict[str, dict[str, str]] = field(default_factory=dict)  # call key -> column -> plaintext
    call_notes: dict[str, str] = field(default_factory=dict)  # call note key -> plaintext


@dataclass
class Department:
    scenario: dict[str, Any]
    name: str
    statements: list[Statement]
    people: list[Person]
    flag_overrides: dict[int, bool]
    flag_predictions: dict[str, Prediction]
    totp_secrets: dict[str, str]
    adp: AdpWork | None

    @property
    def id(self) -> str:
        return self.scenario['id']


class Builder:
    def __init__(self, run: RunContext, scenario: dict[str, Any]):
        self.run = run
        self.s = scenario
        self.sid = scenario['id']
        self.statements: list[Statement] = []
        self.people: dict[str, Person] = {}
        # Everyone once, in catalog order (people is also indexed by "First Last").
        self.roster: list[Person] = []
        self.admin: Person | None = None
        self.custom_sets: dict[str, list[dict[str, Any]]] = {}
        self.unit_sets: dict[str, str] = {}  # unit type name -> custom set name
        self.priorities: dict[str, str] = {}  # base meaning -> priority name, for custom priorities
        self.call_types: set[str] = set()
        self.calls: dict[str, dict[str, Any]] = {}
        self.totp: dict[str, str] = {}
        self.adp = AdpWork() if self._adp_enabled() else None

    # --- helpers -----------------------------------------------------------------------------------------------------
    def key(self, kind: str, name: str = '') -> Key:
        return Key(kind, f'{self.sid}/{name}')

    def add(self, table: str, values: dict[str, Any], key: tuple[str, str] | None = None, returns: str | None = None, label: str = '') -> None:
        scoped = (key[0], f'{self.sid}/{key[1]}') if key else None
        self.statements.append(Row(table, values, scoped, returns, f'{self.sid}: {label or table}'))

    def at(self, offset: str | int | None) -> dt.datetime:
        return self.run.now + parse_offset(offset)

    @property
    def department(self) -> Key:
        return self.key('department')

    def person(self, ref: str) -> Person:
        person = self.people.get(ref.lower())
        if person is None:
            raise SeedError(f'{self.sid}: unknown person "{ref}" (use a persona id or "First Last")')
        return person

    def guid(self, *parts: str) -> str:
        return self.run.ids.guid(self.sid, *parts)

    def _adp_enabled(self) -> bool:
        return bool(self.s.get('security', {}).get('adp', {}).get('enabled'))

    def _protect(self, call_key: str, column: str, value: str | None) -> str | None:
        """ADP departments get the real value sealed in phase 2; the insert carries it in plaintext until then."""
        if value and self.adp is not None and self.run.adp_mode == 'envelopes':
            self.adp.call_fields.setdefault(call_key, {})[column] = value
        return value

    # --- build -------------------------------------------------------------------------------------------------------
    def build(self) -> Department:
        self._department()
        self._statuses()
        self._groups()
        self._roles_and_ranks()
        self._people()
        self._units()
        self._call_setup()
        self._contacts()
        self._calls()
        self._status_history()
        self._messages()
        self._notes_and_calendar()
        self._shifts()
        self._pois_protocols_templates()
        self._training_and_documents()
        self._permissions()
        self._security()
        self._incident_command()
        overrides, predictions = self._flags()
        return Department(self.s, department_name(self.s, self.run.tag), self.statements, self.roster, overrides, predictions, self.totp, self.adp)

    def _department(self) -> None:
        d = self.s['department']
        self._address('department', d.get('address'))
        name = department_name(self.s, self.run.tag)
        code = _code(self.run.tag + self.sid, 4)
        admin_spec = next((p for p in self.s['people'] if p.get('persona') == 'admin'), None)
        if admin_spec is None:
            raise SeedError(f'{self.sid}: every scenario needs an "admin" persona (the managing member)')
        admin_id = self.run.ids.user(username_for(self.sid, admin_spec, self.run.tag))
        self.add('Departments', {
            'Name': name, 'Code': code, 'ManagingUserId': admin_id, 'ShowWelcome': False, 'CreatedOn': self.at('-30d'), 'UpdatedOn': self.run.now,
            'TimeZone': d['timeZone'], 'DepartmentType': d['type'], 'Use24HourTime': bool(d.get('use24HourTime')),
            'AddressId': self.key('address', 'department') if d.get('address') else None,
        }, key=('department', ''), returns='DepartmentId', label='department')
        # What registration writes (DepartmentsService.CreateDepartmentAsync): a unique dispatch email code and modern sounds.
        self.setting('InternalDispatchEmail', _code(self.run.tag + self.sid + 'dispatch', 6).lower())
        self.setting('EnableModernNotifications', 'True')
        if d.get('location'):
            self.setting('BigBoardMapCenterGpsCoordinates', '{},{}'.format(*d['location']))
        settings = self.s.get('settings', {})
        for name_, (setting, values) in SORT_ORDERS.items():
            if name_ in settings:
                self.setting(setting, str(values[settings[name_]]))
        if settings.get('disabledModules'):
            self.setting('ModuleSettings', proto.module_settings(settings['disabledModules']))
        if settings.get('terminology'):
            self.setting('DepartmentTerminology', proto.terminology(settings['terminology']))
        if settings.get('callNumbering'):
            self.setting('CallNumberingConfig', proto.call_numbering(settings['callNumbering']['pattern'], settings['callNumbering'].get('width', 4)))
        if 'checkInTimersAutoEnable' in settings:
            self.setting('CheckInTimersAutoEnableForNewCalls', str(bool(settings['checkInTimersAutoEnable'])))
        if 'disableAutoAvailable' in settings:
            self.setting('DisabledAutoAvailable', str(bool(settings['disableAutoAvailable'])).lower())
        for raw in settings.get('raw', []):
            self.add('DepartmentSettings', {'DepartmentId': self.department, 'SettingType': raw['type'], 'Setting': raw['value']}, label=f'setting {raw["type"]}')
        self.add('Payments', {
            'DepartmentId': self.department, 'PlanId': FREE_PLAN_ID, 'Method': PAYMENT_METHOD_SYSTEM, 'IsTrial': False, 'PurchaseOn': self.at('-30d'),
            'PurchasingUserId': admin_id, 'TransactionId': 'SYSTEM', 'Successful': True, 'Description': 'Free Forever Plan', 'EffectiveOn': self.at('-31d'),
            'Amount': 0.0, 'EndingOn': dt.datetime(9999, 12, 31), 'IsUpgrade': False, 'Data': '',
        }, label='free plan payment')

    def setting(self, name: str, value: str) -> None:
        self.add('DepartmentSettings', {'DepartmentId': self.department, 'SettingType': SETTING[name], 'Setting': value}, label=f'setting {name}')

    def _address(self, key: str, address: dict[str, Any] | None) -> None:
        if not address:
            return
        self.add('Addresses', {'Address1': address['address1'], 'City': address['city'], 'State': address['state'], 'PostalCode': address.get('postalCode'), 'Country': address['country']},
                 key=('address', key), returns='AddressId', label=f'address {key}')

    # --- statuses ------------------------------------------------------------------------------------------------------
    def _statuses(self) -> None:
        statuses = self.s.get('statuses') or {}
        for kind in ('personnel', 'staffing'):
            if statuses.get(kind):
                self._custom_set(kind, kind, statuses[kind])
        for unit_set in statuses.get('units', []):
            self._custom_set('unit', f'unit:{unit_set["name"]}', unit_set)
            for unit_type in unit_set['unitTypes']:
                self.unit_sets[unit_type] = f'unit:{unit_set["name"]}'

    def _custom_set(self, kind: str, set_key: str, spec: dict[str, Any]) -> None:
        self.add('CustomStates', {'DepartmentId': self.department, 'Type': CUSTOM_STATE_TYPES[kind], 'Name': spec['name'], 'Description': spec.get('description'), 'IsDeleted': False},
                 key=('customstate', set_key), returns='CustomStateId', label=f'{kind} status set {spec["name"]}')
        bases = STAFFING_BASE if kind == 'staffing' else ACTION_BASE
        options = []
        for order, option in enumerate(spec['options']):
            base = option.get('base')
            if base is not None and base not in bases:
                raise SeedError(f'{self.sid}: {kind} option "{option["text"]}" has unknown base "{base}"; expected one of {sorted(bases)}')
            self.add('CustomStateDetails', {
                'CustomStateId': self.key('customstate', set_key), 'ButtonText': option['text'], 'ButtonColor': option['color'], 'TextColor': option.get('textColor', '#ffffff'),
                'GpsRequired': bool(option.get('gps')), 'NoteType': NOTE_TYPES[option.get('note', 'none')], 'DetailType': DESTINATION_TYPES[option.get('destination', 'none')],
                'Order': order, 'IsDeleted': False, 'BaseType': bases[base] if base is not None else -1,
            }, key=('statusdetail', f'{set_key}/{option["text"]}'), returns='CustomStateDetailId', label=f'{kind} status {option["text"]}')
            options.append(option)
        for option in options:
            if option.get('next'):
                names = tuple(f'{self.sid}/{set_key}/{text}' for text in option['next'])
                self.statements.append(Update('CustomStateDetails', {'NextStateDetailIds': KeyList('statusdetail', names)}, 'CustomStateDetailId',
                                              self.key('statusdetail', f'{set_key}/{option["text"]}'), f'{self.sid}: next statuses of {option["text"]}'))
        self.custom_sets[set_key] = options

    def _resolve_custom(self, set_key: str, meaning: str, bases: dict[str, int]) -> Key:
        options = self.custom_sets[set_key]
        for candidate in (meaning,) + BASE_ALIASES.get(meaning, ()):
            for option in options:
                if option.get('base') == candidate:
                    return self.key('statusdetail', f'{set_key}/{option["text"]}')
        # An option named exactly like the meaning ("On Patrol") is also accepted.
        for option in options:
            if option['text'] == meaning:
                return self.key('statusdetail', f'{set_key}/{option["text"]}')
        raise SeedError(f'{self.sid}: the {set_key} status set has no option with base "{meaning}"')

    def personnel_status(self, meaning: str) -> int | Key:
        if 'personnel' in self.custom_sets:
            return self._resolve_custom('personnel', meaning, ACTION_BASE)
        if meaning not in PERSONNEL_BUILT_IN:
            raise SeedError(f'{self.sid}: "{meaning}" is not a built-in personnel status ({sorted(PERSONNEL_BUILT_IN)})')
        return PERSONNEL_BUILT_IN[meaning]

    def staffing(self, meaning: str) -> int | Key:
        if 'staffing' in self.custom_sets:
            return self._resolve_custom('staffing', meaning, STAFFING_BASE)
        if meaning not in STAFFING_BUILT_IN:
            raise SeedError(f'{self.sid}: "{meaning}" is not a built-in staffing level ({sorted(STAFFING_BUILT_IN)})')
        return STAFFING_BUILT_IN[meaning]

    def unit_status(self, unit_type: str | None, meaning: str) -> int | Key:
        set_key = self.unit_sets.get(unit_type or '')
        if set_key:
            return self._resolve_custom(set_key, meaning, ACTION_BASE)
        if meaning not in UNIT_BUILT_IN:
            raise SeedError(f'{self.sid}: "{meaning}" is not a built-in unit status ({sorted(UNIT_BUILT_IN)})')
        return UNIT_BUILT_IN[meaning]

    # --- organisation ------------------------------------------------------------------------------------------------
    def _groups(self) -> None:
        groups = {group['name']: group for group in self.s.get('groups', [])}
        done: set[str] = set()

        def add_group(group: dict[str, Any]) -> None:
            if group['name'] in done:
                return
            parent = group.get('parent')
            if parent:
                if parent not in groups:
                    raise SeedError(f'{self.sid}: group "{group["name"]}" has unknown parent "{parent}"')
                add_group(groups[parent])
            self._address(f'group:{group["name"]}', group.get('address'))
            location = group.get('location')
            self.add('DepartmentGroups', {
                'DepartmentId': self.department, 'Name': group['name'], 'Type': GROUP_TYPES[group.get('type', 'organizational')],
                'AddressId': self.key('address', f'group:{group["name"]}') if group.get('address') else None,
                'ParentDepartmentGroupId': self.key('group', parent) if parent else None,
                'Latitude': str(location[0]) if location else None, 'Longitude': str(location[1]) if location else None,
                'DispatchToPrinter': False, 'DispatchToFax': False,
            }, key=('group', group['name']), returns='DepartmentGroupId', label=f'group {group["name"]}')
            done.add(group['name'])

        for group in groups.values():
            add_group(group)

    def _roles_and_ranks(self) -> None:
        for order, role in enumerate(self.s.get('roles', [])):
            self.add('PersonnelRoles', {'DepartmentId': self.department, 'Name': role['name'], 'Description': role.get('description'), 'SortOrder': order},
                     key=('role', role['name']), returns='PersonnelRoleId', label=f'role {role["name"]}')
        for order, rank in enumerate(self.s.get('ranks', [])):
            self.add('Ranks', {'DepartmentId': self.department, 'Name': rank['name'], 'Code': rank.get('code'), 'Color': rank.get('color'), 'SortWeight': order, 'TradeWeight': order},
                     key=('rank', rank['name']), returns='RankId', label=f'rank {rank["name"]}')

    def _people(self) -> None:
        tz = self.s['department']['timeZone']
        for spec in self.s.get('people', []):
            username = username_for(self.sid, spec, self.run.tag)
            if not USERNAME_CHARACTERS.match(username):
                raise SeedError(f'{self.sid}: user name "{username}" has characters Identity refuses')
            person = Person(spec.get('persona') or f'{spec["first"]} {spec["last"]}', username, self.run.ids.user(username), spec['first'], spec['last'], spec.get('persona'), spec)
            for ref in {person.ref.lower(), f'{spec["first"]} {spec["last"]}'.lower()}:
                if ref in self.people:
                    raise SeedError(f'{self.sid}: two people share "{ref}"')
                self.people[ref] = person
            self.roster.append(person)
            if spec.get('persona') == 'admin':
                self.admin = person
            self._user(person, tz)

    def _user(self, person: Person, tz: str) -> None:
        spec = person.spec
        email = f'{person.username}@qa.example.com'
        mfa = self.s.get('security', {}).get('mfa', {})
        enrolled = person.persona in mfa.get('enrolled', [])
        self.add('AspNetUsers', {
            'Id': person.user_id, 'UserName': person.username, 'NormalizedUserName': person.username.upper(), 'Email': email, 'NormalizedEmail': email.upper(),
            'EmailConfirmed': True, 'PasswordHash': self.run.password_hash, 'SecurityStamp': self.run.ids.guid('stamp', person.username).upper(),
            'ConcurrencyStamp': self.run.ids.guid('concurrency', person.username), 'PhoneNumberConfirmed': False, 'TwoFactorEnabled': enrolled,
            'LockoutEnabled': True, 'AccessFailedCount': 0,
        }, label=f'user {person.username}')
        self.add('AspNetUsersExt', {'UserId': person.user_id, 'CreateDate': self.at('-30d'), 'LastActivityDate': self.run.now}, label=f'user ext {person.username}')
        self.add('AspNetUserRoles', {'UserId': person.user_id, 'RoleId': USERS_ROLE_ID}, label=f'user role {person.username}')
        if enrolled:
            secret = generate_totp_secret()
            self.totp[person.username] = secret
            self.add('AspNetUserTokens', {'UserId': person.user_id, 'LoginProvider': AUTHENTICATOR_PROVIDER, 'Name': AUTHENTICATOR_NAME, 'Value': secret}, label=f'authenticator {person.username}')
        self.add('UserProfiles', {
            'UserId': person.user_id, 'FirstName': person.first, 'LastName': person.last, 'TimeZone': tz, 'MobileNumber': spec.get('mobile'), 'MobileCarrier': 0,
            'SendEmail': False, 'SendPush': True, 'SendSms': False, 'SendMessageEmail': False, 'SendMessagePush': True, 'SendMessageSms': False,
            'DoNotRecieveNewsletters': True, 'SendNotificationEmail': False, 'SendNotificationPush': True, 'SendNotificationSms': False,
            'VoiceForCall': False, 'VoiceCallMobile': False, 'VoiceCallHome': False, 'CustomPushSounds': False, 'LastUpdated': self.run.now,
            'StartDate': self.at('-365d'), 'IdentificationNumber': f'QA-{_code(person.username, 5)}', 'EmailVerified': True, 'Language': spec.get('language'),
        }, label=f'profile {person.username}')
        self.add('DepartmentMembers', {
            'DepartmentId': self.department, 'UserId': person.user_id, 'IsAdmin': bool(spec.get('admin')), 'IsDisabled': bool(spec.get('disabled')),
            'IsHidden': bool(spec.get('hidden')), 'IsDefault': True, 'IsActive': True, 'IsDeleted': False,
            'RankId': self.key('rank', spec['rank']) if spec.get('rank') else None,
        }, label=f'member {person.username}')
        if spec.get('group'):
            self.add('DepartmentGroupMembers', {'DepartmentGroupId': self.key('group', spec['group']), 'UserId': person.user_id, 'DepartmentId': self.department,
                                                 'IsAdmin': bool(spec.get('groupAdmin'))}, label=f'group member {person.username}')
        for role in spec.get('roles', []):
            self.add('PersonnelRoleUsers', {'PersonnelRoleId': self.key('role', role), 'UserId': person.user_id, 'DepartmentId': self.department}, label=f'role {role} for {person.username}')

    def _units(self) -> None:
        for unit_type in self.s.get('unitTypes', []):
            set_key = self.unit_sets.get(unit_type['name'])
            self.add('UnitTypes', {
                'DepartmentId': self.department, 'Type': unit_type['name'], 'CustomStatesId': self.key('customstate', set_key) if set_key else None,
                'BaseType': UNIT_TYPE_CLASSES[unit_type.get('class', 'none')],
            }, key=('unittype', unit_type['name']), returns='UnitTypeId', label=f'unit type {unit_type["name"]}')
        known_types = {unit_type['name'] for unit_type in self.s.get('unitTypes', [])}
        for unit in self.s.get('units', []):
            if unit.get('type') and unit['type'] not in known_types:
                raise SeedError(f'{self.sid}: unit "{unit["name"]}" has unknown type "{unit["type"]}"')
            self.add('Units', {
                'DepartmentId': self.department, 'Name': unit['name'], 'Type': unit.get('type'), 'StationGroupId': self.key('group', unit['station']) if unit.get('station') else None,
                'Vin': unit.get('vin'), 'PlateNumber': unit.get('plate'), 'FourWheel': False, 'SpecialPermit': False, 'IsDeleted': False,
            }, key=('unit', unit['name']), returns='UnitId', label=f'unit {unit["name"]}')
            for role in unit.get('roles', []):
                self.add('UnitRoles', {'UnitId': self.key('unit', unit['name']), 'Name': role, 'PersonnelRoleId': self.key('role', role) if any(r['name'] == role for r in self.s.get('roles', [])) else None},
                         label=f'unit role {unit["name"]}/{role}')
            for seat in unit.get('crew', []):
                person = self.person(seat['person'])
                # UnitActiveRoles.Role is the unit role's name; the API matches assignments by name.
                self.add('UnitActiveRoles', {'DepartmentId': self.department, 'UnitId': self.key('unit', unit['name']), 'Role': seat['role'], 'UserId': person.user_id,
                                             'UpdatedOn': self.at('-1h'), 'UpdatedBy': self._admin_id()}, label=f'crew {unit["name"]}/{seat["role"]}')

    def _admin_id(self) -> str:
        if self.admin is None:
            raise SeedError(f'{self.sid}: no admin persona')
        return self.admin.user_id

    # --- calls -------------------------------------------------------------------------------------------------------
    def _call_setup(self) -> None:
        for order, priority in enumerate(self.s.get('callPriorities', [])):
            base = priority.get('base')
            if base is not None:
                self.priorities.setdefault(base, priority['name'])
            self.add('DepartmentCallPriorities', {
                'DepartmentId': self.department, 'Name': priority['name'], 'Color': priority['color'], 'Sort': order, 'IsDeleted': False, 'IsDefault': bool(priority.get('default')),
                'DispatchPersonnel': priority.get('dispatchPersonnel', True), 'DispatchUnits': priority.get('dispatchUnits', True),
                'ForceNotifyAllPersonnel': bool(priority.get('forceNotifyAll')), 'Tone': priority.get('tone', 0), 'BaseType': PRIORITY_BUILT_IN[base] if base else -1,
            }, key=('priority', priority['name']), returns='DepartmentCallPriorityId', label=f'priority {priority["name"]}')
        for call_type in self.s.get('callTypes', []):
            self.call_types.add(call_type['name'])
            self.add('CallTypes', {'DepartmentId': self.department, 'Type': call_type['name'], 'BaseType': CALL_TYPE_CLASSES[call_type.get('class', 'none')]},
                     key=('calltype', call_type['name']), returns='CallTypeId', label=f'call type {call_type["name"]}')

    def priority(self, meaning: str) -> int | Key:
        """A department with priorities stores its priority id; one without stores the built-in value (CallPriority)."""
        if self.s.get('callPriorities'):
            name = self.priorities.get(meaning) or next((p['name'] for p in self.s['callPriorities'] if p['name'] == meaning), None)
            if name is None:
                raise SeedError(f'{self.sid}: no call priority with base "{meaning}"')
            return self.key('priority', name)
        if meaning not in PRIORITY_BUILT_IN:
            raise SeedError(f'{self.sid}: "{meaning}" is not a built-in priority ({sorted(PRIORITY_BUILT_IN)})')
        return PRIORITY_BUILT_IN[meaning]

    def _contacts(self) -> None:
        for contact in self.s.get('contacts', []):
            location = contact.get('location')
            self.add('Contacts', {
                'ContactId': self.guid('contact', contact['key']), 'DepartmentId': self.department, 'ContactType': 1 if contact.get('company') else 0,
                'FirstName': contact.get('first'), 'LastName': contact.get('last'), 'CompanyName': contact.get('company'), 'Email': contact.get('email'),
                'OfficePhoneNumber': contact.get('office'), 'CellPhoneNumber': contact.get('cell'), 'LocationGpsCoordinates': '{},{}'.format(*location) if location else None,
                'Description': contact.get('description'), 'IsDeleted': False, 'AddedOn': self.at('-20d'), 'AddedByUserId': self._admin_id(),
            }, label=f'contact {contact["key"]}')

    def _calls(self) -> None:
        for call in self.s.get('calls', []):
            self._call(call)

    def _call(self, call: dict[str, Any]) -> None:
        ckey = call['key']
        if ckey in self.calls:
            raise SeedError(f'{self.sid}: two calls share key "{ckey}"')
        state = CALL_STATES[call.get('state', 'active')]
        logged = self.at(call.get('loggedOn', '-10m'))
        dispatch = call.get('dispatch', {})
        dispatched = bool(dispatch) and not call.get('dispatchOn')
        location = call.get('location')
        values = {
            'DepartmentId': self.department, 'ReportingUserId': self.person(call.get('reportedBy', 'dispatcher' if 'dispatcher' in self.people else 'admin')).user_id,
            'Name': call['name'], 'NatureOfCall': self._protect(ckey, 'NatureOfCall', call['nature']), 'Notes': self._protect(ckey, 'Notes', call.get('notes')),
            'LoggedOn': logged, 'Priority': self.priority(call.get('priority', 'low')), 'IsCritical': bool(call.get('critical')),
            'Address': call['address'], 'GeoLocationData': '{},{}'.format(*location) if location else None, 'State': state, 'IsDeleted': False,
            'Type': call.get('type'), 'CallTypeId': self.key('calltype', call['type']) if call.get('type') in self.call_types else None,
            'IncidentNumber': call.get('number'), 'Number': call.get('number'), 'CallSource': CALL_SOURCE_USER,
            'DispatchCount': 1 if dispatched else 0, 'LastDispatchedOn': logged if dispatched else None, 'Public': False,
            'ExternalIdentifier': call.get('externalId'), 'ReferenceNumber': call.get('referenceNumber'),
            'ContactName': self._protect(ckey, 'ContactName', call.get('contactName')), 'ContactNumber': self._protect(ckey, 'ContactNumber', call.get('contactNumber')),
            'DispatchOn': self.at(call['dispatchOn']) if call.get('dispatchOn') else None, 'HasBeenDispatched': not call.get('dispatchOn'),
            'CheckInTimersEnabled': bool(call.get('checkInTimers')), 'AlarmLevel': call.get('alarmLevel', 1),
        }
        if state != CALL_STATES['active'] and state != CALL_STATES['pending']:
            values.update({'ClosedByUserId': self.person(call.get('closedBy', 'admin')).user_id, 'ClosedOn': self.at(call.get('closedOn', '-1m')),
                           'CompletedNotes': self._protect(ckey, 'CompletedNotes', call.get('completedNotes'))})
        self.add('Calls', values, key=('call', ckey), returns='CallId', label=f'call {ckey}')
        self.calls[ckey] = call
        call_id = self.key('call', ckey)
        for person_ref in dispatch.get('people', []):
            person = self.person(person_ref)
            group = person.spec.get('group')
            self.add('CallDispatches', {'CallId': call_id, 'UserId': person.user_id, 'GroupId': self.key('group', group) if group else None, 'DispatchCount': 1, 'LastDispatchedOn': logged},
                     label=f'dispatch {person.username} to {ckey}')
        for unit in dispatch.get('units', []):
            self.add('CallDispatchUnits', {'CallId': call_id, 'UnitId': self.key('unit', unit), 'DispatchCount': 1, 'LastDispatchedOn': logged}, label=f'dispatch {unit} to {ckey}')
        for group in dispatch.get('groups', []):
            self.add('CallDispatchGroups', {'CallId': call_id, 'DepartmentGroupId': self.key('group', group), 'DispatchCount': 1, 'LastDispatchedOn': logged}, label=f'dispatch {group} to {ckey}')
        for role in dispatch.get('roles', []):
            self.add('CallDispatchRoles', {'CallId': call_id, 'RoleId': self.key('role', role), 'DispatchCount': 1, 'LastDispatchedOn': logged}, label=f'dispatch role {role} to {ckey}')
        for index, note in enumerate(call.get('callNotes', [])):
            note_key = f'{ckey}/{index}'
            if self.adp is not None and self.run.adp_mode == 'envelopes':
                self.adp.call_notes[note_key] = note['text']
            self.add('CallNotes', {'CallId': call_id, 'UserId': self.person(note['by']).user_id, 'Note': note['text'], 'Source': CALL_NOTE_MOBILE, 'Timestamp': self.at(note.get('at', '-1m'))},
                     key=('callnote', note_key), returns='CallNoteId', label=f'call note {note_key}')
        if call.get('linkedTo'):
            if call['linkedTo'] not in self.calls:
                raise SeedError(f'{self.sid}: call "{ckey}" links to "{call["linkedTo"]}", which must be listed before it')
            self.add('CallReferences', {'CallReferenceId': self.guid('callref', ckey), 'SourceCallId': call_id, 'TargetCallId': self.key('call', call['linkedTo']),
                                        'AddedOn': logged, 'AddedByUserId': self._admin_id(), 'Note': 'Linked by the QA seed'}, label=f'link {ckey}')
        for index, contact in enumerate(call.get('contacts', [])):
            self.add('CallContacts', {'CallContactId': self.guid('callcontact', ckey, contact), 'DepartmentId': self.department, 'CallId': call_id,
                                      'ContactId': self.guid('contact', contact), 'CallContactType': 0 if index == 0 else 1}, label=f'contact {contact} on {ckey}')

    def _status_history(self) -> None:
        for person in self.roster:
            spec = person.spec
            call = spec.get('statusCall')
            if call and call not in self.calls:
                raise SeedError(f'{self.sid}: {person.ref} status points at unknown call "{call}"')
            self.add('ActionLogs', {
                'UserId': person.user_id, 'DepartmentId': self.department, 'ActionTypeId': self.personnel_status(spec.get('status', 'available')), 'Timestamp': self.at(spec.get('statusAt', '-8m')),
                'DestinationId': self.key('call', call) if call else None, 'DestinationType': DESTINATION_CALL if call else None, 'Note': spec.get('statusNote'),
                'GeoLocationData': '{},{}'.format(*spec['location']) if spec.get('location') else None,
            }, label=f'status {person.username}')
            self.add('UserStates', {'UserId': person.user_id, 'DepartmentId': self.department, 'State': self.staffing(spec.get('staffing', 'available')), 'Timestamp': self.at(spec.get('staffingAt', '-2h'))},
                     label=f'staffing {person.username}')
        for unit in self.s.get('units', []):
            call = unit.get('statusCall')
            location = unit.get('location')
            self.add('UnitStates', {
                'UnitId': self.key('unit', unit['name']), 'State': self.unit_status(unit.get('type'), unit.get('status', 'available')), 'Timestamp': self.at(unit.get('statusAt', '-7m')),
                'DestinationId': self.key('call', call) if call else None, 'DestinationType': DESTINATION_CALL if call else None,
                'GeoLocationData': '{},{}'.format(*location) if location else None, 'Latitude': location[0] if location else None, 'Longitude': location[1] if location else None,
                'Note': unit.get('statusNote'),
            }, label=f'unit status {unit["name"]}')

    # --- communication -----------------------------------------------------------------------------------------------
    def _messages(self) -> None:
        members = [p for p in self.roster if not p.spec.get('disabled')]
        for index, message in enumerate(self.s.get('messages', [])):
            sender = self.person(message['from'])
            recipients = members if message.get('broadcast') else [self.person(ref) for ref in message['to']]
            sent = self.at(message.get('at', '-1h'))
            self.add('Messages', {
                'Subject': message['subject'], 'IsBroadcast': bool(message.get('broadcast')), 'SendingUserId': sender.user_id, 'Body': message['body'], 'SentOn': sent,
                'Recipients': ','.join(p.user_id for p in recipients), 'IsDeleted': False, 'SystemGenerated': False, 'Type': 2 if message.get('poll') else 0,
                'DepartmentId': self.department, 'ExpireOn': self.at(message['expires']) if message.get('expires') else None,
            }, key=('message', str(index)), returns='MessageId', label=f'message {message["subject"]}')
            read_by = {self.person(ref).user_id for ref in message.get('readBy', [])}
            for recipient in recipients:
                self.add('MessageRecipients', {'MessageId': self.key('message', str(index)), 'UserId': recipient.user_id, 'IsDeleted': False,
                                               'ReadOn': sent + dt.timedelta(minutes=5) if recipient.user_id in read_by else None, 'DepartmentId': self.department},
                         label=f'recipient {recipient.username} of {message["subject"]}')

    def _notes_and_calendar(self) -> None:
        for note in self.s.get('notes', []):
            self.add('Notes', {'DepartmentId': self.department, 'UserId': self.person(note['by']).user_id, 'Title': note['title'], 'Body': note['body'],
                               'IsAdminOnly': bool(note.get('adminOnly')), 'Color': note.get('color'), 'AddedOn': self.at(note.get('at', '-2d')), 'Category': note.get('category')},
                     label=f'note {note["title"]}')
        tz = self.s['department']['timeZone']
        for index, item in enumerate(self.s.get('calendar', [])):
            start, end = self.at(item['start']), self.at(item['end'])
            if item.get('allDay'):
                start = start.replace(hour=0, minute=0, second=0)
                end = start + dt.timedelta(days=1)
            self.add('CalendarItems', {
                'DepartmentId': self.department, 'Title': item['title'], 'Start': start, 'End': end, 'StartTimezone': tz, 'EndTimezone': tz, 'Description': item.get('description'),
                'ItemType': 0, 'IsAllDay': bool(item.get('allDay')), 'Location': item.get('location'), 'SignupType': SIGNUP_RSVP if item.get('signup') else SIGNUP_NONE,
                'CreatorUserId': self.person(item['by']).user_id, 'Public': False, 'IsV2Schedule': True,
            }, key=('calendar', str(index)), returns='CalendarItemId', label=f'calendar {item["title"]}')
            for ref in item.get('attending', []):
                self.add('CalendarItemAttendees', {'CalendarItemId': self.key('calendar', str(index)), 'UserId': self.person(ref).user_id, 'AttendeeType': ATTENDEE_RSVP, 'Timestamp': self.at('-1d')},
                         label=f'attendee {ref} of {item["title"]}')

    def _shifts(self) -> None:
        today = self.run.now.replace(hour=0, minute=0, second=0, microsecond=0)
        for shift in self.s.get('shifts', []):
            name = shift['name']
            self.add('Shifts', {
                'DepartmentId': self.department, 'Name': name, 'Code': shift.get('code'), 'ScheduleType': SHIFT_SCHEDULE_MANUAL, 'AssignmentType': SHIFT_ASSIGNMENT[shift.get('assignment', 'assigned')],
                'Color': shift.get('color'), 'StartDay': today - dt.timedelta(days=1), 'StartTime': shift['start'], 'EndTime': shift['end'], 'Hours': shift.get('hours'),
                'AllowPartials': False, 'RequireApproval': bool(shift.get('requireApproval')),
            }, key=('shift', name), returns='ShiftId', label=f'shift {name}')
            shift_id = self.key('shift', name)
            for day in range(shift.get('days', 7)):
                self.add('ShiftDays', {'ShiftId': shift_id, 'Day': today + dt.timedelta(days=day), 'Processed': False}, label=f'shift day {name}+{day}')
            for group in shift.get('groups', []):
                self.add('ShiftGroups', {'ShiftId': shift_id, 'DepartmentGroupId': self.key('group', group)}, label=f'shift group {name}/{group}')
            for ref in shift.get('people', []):
                person = self.person(ref)
                group = person.spec.get('group')
                self.add('ShiftPersons', {'ShiftId': shift_id, 'UserId': person.user_id, 'GroupId': self.key('group', group) if group else None}, label=f'shift person {name}/{ref}')
            for ref in shift.get('admins', []):
                self.add('ShiftAdmins', {'ShiftId': shift_id, 'UserId': self.person(ref).user_id}, label=f'shift admin {name}/{ref}')
            for signup in shift.get('signups', []):
                person = self.person(signup['person'])
                group = person.spec.get('group')
                self.add('ShiftSignups', {'ShiftId': shift_id, 'UserId': person.user_id, 'SignupTimestamp': self.at('-1d'), 'ShiftDay': today + dt.timedelta(days=signup.get('day', 1)),
                                          'Denied': False, 'DepartmentGroupId': self.key('group', group) if group else None, 'ApprovalPending': bool(signup.get('pending'))},
                         label=f'shift signup {name}/{signup["person"]}')

    def _pois_protocols_templates(self) -> None:
        for poi_type in self.s.get('poiTypes', []):
            self.add('PoiTypes', {'DepartmentId': self.department, 'Name': poi_type['name'], 'Color': poi_type.get('color'), 'IsDestination': bool(poi_type.get('destination'))},
                     key=('poitype', poi_type['name']), returns='PoiTypeId', label=f'POI type {poi_type["name"]}')
            for poi in poi_type.get('pois', []):
                self.add('Pois', {'PoiTypeId': self.key('poitype', poi_type['name']), 'Latitude': poi['location'][0], 'Longitude': poi['location'][1], 'Name': poi.get('name'),
                                  'Address': poi.get('address'), 'Note': poi.get('note')}, label=f'POI {poi.get("name")}')
        trigger_types = {'priority': 0, 'callType': 1, 'priorityAndType': 2}
        for protocol in self.s.get('protocols', []):
            code = protocol['code']
            self.add('DispatchProtocols', {
                'DepartmentId': self.department, 'Name': protocol['name'], 'Code': code, 'IsDisabled': False, 'Description': protocol.get('description'), 'ProtocolText': protocol.get('text'),
                'CreatedOn': self.at('-10d'), 'CreatedByUserId': self._admin_id(), 'MinimumWeight': protocol.get('minimumWeight', 0), 'UpdatedByUserId': self._admin_id(), 'UpdatedOn': self.at('-10d'),
            }, key=('protocol', code), returns='DispatchProtocolId', label=f'protocol {code}')
            for trigger in protocol.get('triggers', []):
                self.add('DispatchProtocolTriggers', {'DispatchProtocolId': self.key('protocol', code), 'Type': trigger_types[trigger['type']], 'CallType': trigger.get('callType'),
                                                      'Priority': self._priority_int(trigger.get('priority'))}, label=f'protocol trigger {code}')
            for qindex, question in enumerate(protocol.get('questions', [])):
                qkey = f'{code}/{qindex}'
                self.add('DispatchProtocolQuestions', {'DispatchProtocolId': self.key('protocol', code), 'Question': question['question']},
                         key=('protocolquestion', qkey), returns='DispatchProtocolQuestionId', label=f'protocol question {qkey}')
                for answer in question.get('answers', []):
                    self.add('DispatchProtocolQuestionAnswers', {'DispatchProtocolQuestionId': self.key('protocolquestion', qkey), 'Answer': answer['answer'], 'Weight': answer['weight']},
                             label=f'protocol answer {qkey}/{answer["answer"]}')
        for order, template in enumerate(self.s.get('callTemplates', [])):
            self.add('CallQuickTemplates', {
                'DepartmentId': self.department, 'IsDisabled': False, 'Name': template['name'], 'CallName': template.get('callName'), 'CallNature': template.get('nature'),
                'CallType': template.get('type'), 'CallPriority': self.priority(template.get('priority', 'low')), 'CreatedByUserId': self._admin_id(), 'CreatedOn': self.at('-10d'), 'Sort': order,
            }, label=f'call template {template["name"]}')
        for order, template in enumerate(self.s.get('callNoteTemplates', [])):
            self.add('Autofills', {'AutofillId': self.guid('autofill', template['name']), 'DepartmentId': self.department, 'Type': 0, 'Sort': order, 'Name': template['name'],
                                   'Data': template['text'], 'AddedByUserId': self._admin_id(), 'AddedOn': self.at('-10d')}, label=f'call note template {template["name"]}')
        for timer in self.s.get('checkInTimers', []):
            unit_type = timer.get('unitType')
            self.add('CheckInTimerConfigs', {
                'CheckInTimerConfigId': self.guid('checkintimer', timer['target'], unit_type or ''), 'DepartmentId': self.department, 'TimerTargetType': CHECK_IN_TARGETS[timer['target']],
                'UnitTypeId': self.key('unittype', unit_type) if unit_type else None, 'DurationMinutes': timer['duration'], 'WarningThresholdMinutes': timer['warning'],
                'IsEnabled': True, 'CreatedByUserId': self._admin_id(), 'CreatedOn': self.at('-10d'),
            }, label=f'check-in timer {timer["target"]}')

    def _priority_int(self, meaning: str | None) -> int | Key | None:
        return self.priority(meaning) if meaning else None

    def _training_and_documents(self) -> None:
        members = [p for p in self.roster if p.persona and not p.spec.get('disabled')]
        for index, training in enumerate(self.s.get('trainings', [])):
            self.add('Trainings', {
                'DepartmentId': self.department, 'CreatedByUserId': self.person(training['by']).user_id, 'Name': training['name'], 'Description': training['description'],
                'TrainingText': training['text'], 'MinimumScore': float(training.get('minimumScore', 0)), 'CreatedOn': self.at('-3d'), 'ToBeCompletedBy': self.at(training['due']) if training.get('due') else None,
            }, key=('training', str(index)), returns='TrainingId', label=f'training {training["name"]}')
            for person in members:
                self.add('TrainingUsers', {'TrainingId': self.key('training', str(index)), 'UserId': person.user_id, 'Complete': False, 'Score': 0.0, 'Viewed': False},
                         label=f'training {training["name"]} for {person.username}')
        for document in self.s.get('documents', []):
            self.add('Documents', {
                'DepartmentId': self.department, 'Name': document['name'], 'Category': document.get('category'), 'Description': document.get('description'), 'AdminsOnly': bool(document.get('adminsOnly')),
                'Data': document['text'].encode('utf-8'), 'UserId': self.person(document['by']).user_id, 'AddedOn': self.at('-5d'), 'Type': document.get('contentType', 'text/plain'), 'FileName': document['fileName'],
            }, label=f'document {document["name"]}')

    # --- security ----------------------------------------------------------------------------------------------------
    def _permissions(self) -> None:
        for permission in self.s.get('permissions', []):
            roles = permission.get('roles', [])
            self.add('Permissions', {
                'DepartmentId': self.department, 'PermissionType': PERMISSION_TYPES[permission['type']], 'Action': PERMISSION_ACTIONS[permission['action']],
                'Data': KeyList('role', tuple(f'{self.sid}/{role}' for role in roles)) if roles else None, 'UpdatedBy': self._admin_id(), 'UpdatedOn': self.at('-10d'),
                'LockToGroup': bool(permission.get('lockToGroup')),
            }, label=f'permission {permission["type"]}')

    def _security(self) -> None:
        security = self.s.get('security', {})
        policy = security.get('policy')
        if policy:
            self.add('DepartmentSecurityPolicies', {
                'DepartmentId': self.department, 'RequireMfa': bool(policy.get('requireMfa')), 'RequireSso': bool(policy.get('requireSso')),
                'SessionTimeoutMinutes': policy.get('sessionTimeoutMinutes', 0), 'MaxConcurrentSessions': policy.get('maxConcurrentSessions', 0), 'CreatedOn': self.at('-10d'),
            }, label='security policy')
        if security.get('requireMfaForAdmins'):
            self.setting('Require2FAForAdmins', 'True')
        for provider in security.get('sso', []):
            kind = provider['provider']
            self.add('DepartmentSsoConfigs', {
                'DepartmentSsoConfigId': self.guid('sso', kind), 'DepartmentId': self.department, 'SsoProviderType': SSO_PROVIDERS[kind], 'IsEnabled': provider.get('enabled', True),
                'ClientId': provider.get('clientId'), 'Authority': provider.get('authority'), 'MetadataUrl': provider.get('metadataUrl'), 'EntityId': provider.get('entityId'),
                'AssertionConsumerServiceUrl': provider.get('acsUrl'), 'IdpSsoUrl': provider.get('idpSsoUrl'), 'AllowLocalLogin': provider.get('allowLocalLogin', True),
                'AutoProvisionUsers': bool(provider.get('autoProvision')), 'CreatedByUserId': self._admin_id(), 'CreatedOn': self.at('-10d'),
                'AttributeMappingJson': provider.get('attributeMapping'),
            }, label=f'SSO {kind}')
        adp_spec = security.get('adp', {})
        if adp_spec.get('enabled'):
            self.add('DepartmentDataProtectionPolicies', {
                'DepartmentId': self.department, 'State': adp.STATE_ENABLED, 'CatalogVersion': self.run.adp_catalog_version, 'StepUpWindowMinutes': adp_spec.get('stepUpWindowMinutes', 15),
                'PolicyEpoch': 0, 'CreatedOn': self.at('-10d'), 'CreatedByUserId': self._admin_id(), 'AcknowledgedByUserId': self._admin_id(), 'AcknowledgedOn': self.at('-10d'),
            }, label='ADP policy')

    def _incident_command(self) -> None:
        command = self.s.get('incidentCommand')
        if not command:
            return
        call_key = command['call']
        if call_key not in self.calls:
            raise SeedError(f'{self.sid}: incident command on unknown call "{call_key}"')
        call, ic = self.key('call', call_key), self.guid('ic', call_key)
        established = self.at(command.get('establishedAt', '-18m'))
        commander = self.person(command['commander'])
        common = {'IncidentCommandId': ic, 'DepartmentId': self.department, 'CallId': call}
        self.add('IncidentCommands', {
            'IncidentCommandId': ic, 'DepartmentId': self.department, 'CallId': call, 'EstablishedByUserId': commander.user_id, 'EstablishedOn': established,
            'CurrentCommanderUserId': commander.user_id, 'Status': 0, 'Name': command.get('name'), 'ModifiedOn': self.run.now, 'IncidentActionPlan': command.get('actionPlan'),
            'CommandPostLatitude': str(command['commandPost'][0]) if command.get('commandPost') else None, 'CommandPostLongitude': str(command['commandPost'][1]) if command.get('commandPost') else None,
            'CommandPostLocationText': command.get('commandPostText'), 'StagingLocationText': command.get('stagingText'),
        }, label=f'incident command on {call_key}')
        # As IncidentCommandService.EstablishCommandAsync does when the department has check-in timers.
        self.statements.append(Update('Calls', {'CheckInTimersEnabled': True}, 'CallId', call, f'{self.sid}: check-in timers on {call_key}'))
        self.add('CommandLogEntries', {**common, 'CommandLogEntryId': self.guid('iclog', call_key, '0'), 'EntryType': 0, 'Description': 'Command established', 'UserId': commander.user_id, 'OccurredOn': established},
                 label='command log: established')
        for order, node in enumerate(command.get('nodes', [])):
            node_id = self.guid('icnode', node['name'])
            supervisor = node.get('supervisor')
            self.add('CommandStructureNodes', {
                **common, 'CommandStructureNodeId': node_id, 'NodeType': NODE_TYPES[node['type']], 'Name': node['name'], 'ParentNodeId': self.guid('icnode', node['parent']) if node.get('parent') else None,
                'SupervisorUserId': self.person(supervisor).user_id if supervisor else None, 'SortOrder': order, 'ModifiedOn': self.run.now, 'Color': node.get('color'),
            }, label=f'command node {node["name"]}')
            for resource in node.get('resources', []):
                if 'unit' in resource:
                    kind, resource_id, name = RESOURCE_UNIT, KeyList('unit', (f'{self.sid}/{resource["unit"]}',)), resource['unit']
                else:
                    kind, resource_id, name = RESOURCE_PERSONNEL, self.person(resource['person']).user_id, resource['person']
                self.add('ResourceAssignments', {**common, 'ResourceAssignmentId': self.guid('icassign', node['name'], name), 'CommandStructureNodeId': node_id, 'ResourceKind': kind,
                                                 'ResourceId': resource_id, 'AssignedByUserId': commander.user_id, 'AssignedOn': self.at(resource.get('at', '-15m')), 'ModifiedOn': self.run.now},
                         label=f'assign {name} to {node["name"]}')
        for order, objective in enumerate(command.get('objectives', [])):
            self.add('TacticalObjectives', {
                **common, 'TacticalObjectiveId': self.guid('icobjective', objective['name']), 'Name': objective['name'], 'ObjectiveType': OBJECTIVE_TYPES[objective.get('type', 'general')],
                'Status': OBJECTIVE_STATUS[objective.get('status', 'pending')], 'SortOrder': order, 'ModifiedOn': self.run.now, 'ProgressPercent': objective.get('progress', 0),
                'CompletedByUserId': commander.user_id if objective.get('status') == 'complete' else None, 'CompletedOn': self.at('-5m') if objective.get('status') == 'complete' else None,
            }, label=f'objective {objective["name"]}')
        for order, need in enumerate(command.get('needs', [])):
            self.add('IncidentNeeds', {**common, 'IncidentNeedId': self.guid('icneed', need['name']), 'Name': need['name'], 'Status': NEED_STATUS[need.get('status', 'open')],
                                       'QuantityRequested': need.get('requested', 1), 'QuantityFulfilled': need.get('fulfilled', 0), 'CreatedByUserId': commander.user_id,
                                       'CreatedOn': self.at('-10m'), 'SortOrder': order, 'ModifiedOn': self.run.now}, label=f'need {need["name"]}')
        for timer in command.get('timers', []):
            started = self.at(timer.get('startedAt', '-18m'))
            interval = timer['intervalMinutes'] * 60
            self.add('IncidentTimers', {**common, 'IncidentTimerId': self.guid('ictimer', timer['name']), 'TimerType': TIMER_TYPES[timer.get('type', 'scene')], 'ScopeType': 0,
                                        'Name': timer['name'], 'IntervalSeconds': interval, 'StartedOn': started, 'NextDueOn': started + dt.timedelta(seconds=interval), 'Status': 0, 'ModifiedOn': self.run.now},
                     label=f'timer {timer["name"]}')
        for role in command.get('roles', []):
            person = self.person(role['person'])
            self.add('IncidentRoleAssignments', {**common, 'IncidentRoleAssignmentId': self.guid('icrole', role['person'], str(role['roleType'])), 'UserId': person.user_id, 'RoleType': role['roleType'],
                                                 'AssignedByUserId': commander.user_id, 'AssignedOn': established, 'ModifiedOn': self.run.now}, label=f'incident role {role["person"]}')
        for index, note in enumerate(command.get('notes', [])):
            self.add('IncidentNotes', {**common, 'IncidentNoteId': self.guid('icnote', str(index)), 'NoteType': note.get('type', 0), 'Visibility': 1 if note.get('public') else 0,
                                       'Title': note.get('title'), 'Body': note['body'], 'CreatedByUserId': self.person(note.get('by', command['commander'])).user_id,
                                       'CreatedOn': self.at(note.get('at', '-10m')), 'ModifiedOn': self.run.now}, label=f'incident note {index}')
        for check_in in command.get('checkIns', []):
            person = self.person(check_in['person'])
            self.add('CheckInRecords', {'CheckInRecordId': self.guid('checkin', check_in['person']), 'DepartmentId': self.department, 'CallId': call,
                                        'CheckInType': CHECK_IN_TARGETS[check_in.get('type', 'personnel')], 'UserId': person.user_id, 'Timestamp': self.at(check_in.get('at', '-5m'))},
                     label=f'check-in {check_in["person"]}')

    # --- flags -------------------------------------------------------------------------------------------------------
    def _flags(self) -> tuple[dict[int, bool], dict[str, Prediction]]:
        flags_spec = self.s.get('flags', {})
        active = [flag for flag in self.run.flags if not flag.archived]
        overrides = overrides_for(flags_spec.get('profile', 'server'), flags_spec.get('overrides', {}), active)
        by_id = {flag.id: flag for flag in active}
        for flag_id, enabled in sorted(overrides.items()):
            flag = by_id[flag_id]
            self.add('FeatureFlagOverrides', {
                'FeatureFlagId': flag_id, 'DepartmentId': self.department, 'IsEnabled': enabled, 'FlagValue': override_value(flag, enabled),
                'Reason': f'QA departments seed ({self.run.tag}): {flags_spec.get("profile", "server")} profile', 'ExpiresOn': self.at('+365d'), 'CreatedOn': self.run.now,
                # No author: an operator override, which Core honours while the flag is off globally.
                'CreatedByUserId': None,
            }, label=f'flag {flag.key} {"on" if enabled else "off"}')
        predictions = predict(self.run.flags, overrides, self.run.now, self.run.server_environment, self.run.targeted_flags)
        return overrides, predictions


def adp_phase(department: Department, department_id: int, keys: dict[tuple[str, str], int]) -> tuple[list[Statement], bytes]:
    """The key row and sealed values of an ADP department, built once its id and its calls' ids are known."""
    if department.adp is None:
        return [], b''
    data_key = adp.new_data_key()
    sid = department.id
    statements: list[Statement] = [Row('DepartmentDataProtectionKeys', {
        'DepartmentId': department_id, 'Version': 1, 'WrappedKey': adp.wrap_data_key(department_id, data_key), 'ProviderType': adp.PROVIDER_TYPE,
        'ProviderKeyReference': adp.PROVIDER_KEY_REFERENCE, 'ProviderKeyVersion': 1, 'Status': adp.KEY_ACTIVE,
        'CreatedOn': dt.datetime.now(dt.timezone.utc).replace(tzinfo=None, microsecond=0), 'ActivatedOn': dt.datetime.now(dt.timezone.utc).replace(tzinfo=None, microsecond=0),
    }, label=f'{sid}: ADP key')]
    for call_key, columns in department.adp.call_fields.items():
        call_id = keys[('call', f'{sid}/{call_key}')]
        sealed = {column: adp.envelope(data_key, 1, department_id, adp.CALL_FIELDS[column], str(call_id), value) for column, value in columns.items()}
        statements.append(Update('Calls', sealed, 'CallId', call_id, f'{sid}: protect call {call_key}'))
    for note_key, text in department.adp.call_notes.items():
        note_id = keys[('callnote', f'{sid}/{note_key}')]
        statements.append(Update('CallNotes', {'Note': adp.envelope(data_key, 1, department_id, adp.CALL_NOTE_FIELDS['Note'], str(note_id), text)}, 'CallNoteId', note_id, f'{sid}: protect note {note_key}'))
    return statements, data_key


def department_name(scenario: dict[str, Any], tag: str) -> str:
    return f'QA {scenario["name"]} ({tag})'


def username_for(scenario_id: str, spec: dict[str, Any], tag: str) -> str:
    who = spec.get('persona') or f'{spec["first"]}.{spec["last"]}'
    return re.sub(r'[^a-z0-9.\-]', '', f'qa-{scenario_id}-{who}-{tag}'.lower())


def _code(seed: str, length: int) -> str:
    alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'
    digest = hashlib.sha256(seed.encode()).digest()
    return ''.join(alphabet[b % len(alphabet)] for b in digest[:length])


def memberships(departments: list[Department], tag: str) -> list[Statement]:
    """People listed with "alsoMemberOf": a second, inactive membership in another seeded department (department switching)."""
    by_id = {department.id: department for department in departments}
    statements: list[Statement] = []
    for department in departments:
        for person in department.people:
            for other in person.spec.get('alsoMemberOf', []):
                if other not in by_id:
                    continue
                statements.append(Row('DepartmentMembers', {
                    'DepartmentId': Key('department', f'{other}/'), 'UserId': person.user_id, 'IsAdmin': False, 'IsDisabled': False, 'IsHidden': False,
                    'IsDefault': False, 'IsActive': False, 'IsDeleted': False,
                }, label=f'{department.id}: {person.username} also in {other}'))
    return statements


def custom_status_floors() -> list[tuple[str, str, int]]:
    return [('CustomStates', 'CustomStateId', MAX_BUILT_IN_STATUS_ID), ('CustomStateDetails', 'CustomStateDetailId', MAX_BUILT_IN_STATUS_ID)]
