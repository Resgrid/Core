import datetime as dt
import unittest

from seeder.build import Builder, RunContext, memberships, username_for
from seeder.catalog import load_catalog, resolve
from seeder.flags import Flag, overrides_for, predict
from seeder.identity import USERNAME_CHARACTERS
from seeder.sqlgen import Row

NOW = dt.datetime(2026, 11, 3, 12, 0, 0)
FLAGS = [
    Flag(1, 'Chat.System', False, False, 0, None, None, None, None, None, None, None, False),
    Flag(2, 'Records.System', True, False, 0, None, None, None, None, None, None, None, False),
    Flag(3, 'Records.Field.Responder', False, False, 0, None, None, None, None, None, None, None, False, ((2, None),)),
    Flag(4, 'Old.Thing', False, True, 0, None, None, None, None, None, None, None, False),
    Flag(5, 'Dev.Only', False, False, 0, None, None, None, 3, None, None, None, False),
]


def build_all():
    baseline, scenarios = load_catalog()
    run = RunContext('t1', NOW, 'hash', FLAGS, server_environment=2)
    return [Builder(run, resolve(baseline, scenario)).build() for scenario in scenarios]


class CatalogTests(unittest.TestCase):
    def test_every_scenario_builds(self):
        departments = build_all()
        ids = [d.id for d in departments]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertGreaterEqual(len(ids), 12)
        names = [d.name for d in departments]
        self.assertEqual(len(names), len(set(names)))
        usernames = [p.username for d in departments for p in d.people]
        self.assertEqual(len(usernames), len(set(usernames)), 'user names collide across departments')
        for username in usernames:
            self.assertRegex(username, USERNAME_CHARACTERS)
        self.assertTrue(memberships(departments, 't1'), 'the multi persona joins other departments')

    def test_every_department_has_a_signable_admin(self):
        for department in build_all():
            admin = [p for p in department.people if p.persona == 'admin']
            self.assertEqual(len(admin), 1, department.id)
            self.assertFalse(admin[0].spec.get('disabled'))

    def test_scenarios_cover_the_distinct_configurations(self):
        by_id = {d.id: d for d in build_all()}
        tables = {d.id: {s.table for s in d.statements if isinstance(s, Row)} for d in by_id.values()}
        self.assertNotIn('CustomStates', tables['base'])
        self.assertIn('CustomStates', tables['custom-statuses'])
        self.assertNotIn('DepartmentCallPriorities', tables['base'])
        self.assertIn('DepartmentDataProtectionPolicies', tables['adp'])
        self.assertIn('DepartmentSsoConfigs', tables['sso-required'])
        self.assertIn('AspNetUserTokens', tables['mfa'])
        self.assertIn('IncidentCommands', tables['incident-command'])
        self.assertEqual(len(by_id['empty'].people), 1)
        self.assertGreater(len(by_id['large'].people), 70)
        self.assertTrue(by_id['adp'].adp.call_fields, 'ADP calls are sealed in phase two')

    def test_flag_profiles_pair_on_and_off(self):
        by_id = {d.id: d for d in build_all()}
        on, off = by_id['flags-on'], by_id['flags-off']
        active = {f.id for f in FLAGS if not f.archived}
        self.assertEqual(set(on.flag_overrides), active)
        self.assertTrue(all(on.flag_overrides.values()))
        self.assertEqual(set(off.flag_overrides), active)
        self.assertFalse(any(off.flag_overrides.values()))
        self.assertEqual(by_id['base'].flag_overrides, {})
        self.assertEqual(on.flag_predictions['Records.Field.Responder'].enabled, True)
        self.assertEqual(on.flag_predictions['Dev.Only'].source, 'environment')
        self.assertEqual(off.flag_predictions['Records.System'].enabled, False)
        self.assertEqual(by_id['base'].flag_predictions['Records.System'].enabled, True)
        self.assertEqual(by_id['base'].flag_predictions['Old.Thing'].source, 'archived')


class FlagTests(unittest.TestCase):
    def test_prerequisite_off_wins_over_an_on_override(self):
        overrides = overrides_for('server', {'Records.Field.Responder': True, 'Records.System': False}, [f for f in FLAGS if not f.archived])
        predictions = predict(FLAGS, overrides, NOW, 2)
        self.assertEqual(predictions['Records.Field.Responder'].source, 'prerequisite')
        self.assertFalse(predictions['Records.Field.Responder'].enabled)

    def test_unknown_flag_is_refused(self):
        with self.assertRaises(ValueError):
            overrides_for('server', {'No.Such.Flag': True}, FLAGS)


class NamingTests(unittest.TestCase):
    def test_usernames(self):
        self.assertEqual(username_for('base', {'persona': 'admin'}, 'k3f9'), 'qa-base-admin-k3f9')
        self.assertEqual(username_for('large', {'first': 'Sam', 'last': "O'Neil"}, 'k3f9'), 'qa-large-sam.oneil-k3f9')


if __name__ == '__main__':
    unittest.main()
