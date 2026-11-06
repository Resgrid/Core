import datetime as dt
import unittest

from seeder.sqlgen import Guard, IdentityFloor, Key, KeyList, Postgres, Row, Schema, SqlServer, Update, validate

SCHEMA = Schema.from_columns([
    ('departments', 'departmentid', 'integer', False, True, False, None),
    ('departments', 'name', 'citext', False, False, False, None),
    ('departments', 'createdon', 'timestamp without time zone', True, False, False, None),
    ('units', 'unitid', 'integer', False, True, True, None),
    ('units', 'departmentid', 'integer', False, False, False, None),
    ('units', 'name', 'character varying', False, False, False, 10),
    ('units', 'fourwheel', 'boolean', True, False, False, None),
    ('customstatedetails', 'customstatedetailid', 'integer', False, True, False, None),
    ('customstatedetails', 'order', 'integer', True, False, False, None),
    ('customstatedetails', 'nextstatedetailids', 'character varying', True, False, False, 1000),
    ('aspnetusers', 'normalizedusername', 'citext', True, False, False, None),
])
GUARD = Guard('resgrid_qa', 'abc', ('QA Base (abc)',), ('qa-base-admin-abc',))


class ValidateTests(unittest.TestCase):
    def test_accepts_a_consistent_plan(self):
        rows = [Row('Departments', {'Name': 'QA'}, ('department', 'base/'), 'DepartmentId'),
                Row('Units', {'DepartmentId': Key('department', 'base/'), 'Name': 'Engine 1', 'FourWheel': False})]
        self.assertEqual(validate(rows, SCHEMA), [])

    def test_reports_every_problem(self):
        rows = [Row('Units', {'DepartmentId': Key('department', 'base/'), 'Name': 'A very long unit name', 'Colour': 'red', 'FourWheel': 'yes'}, label='unit'),
                Row('Departments', {}, label='department'), Row('Nope', {})]
        problems = '\n'.join(validate(rows, SCHEMA))
        self.assertIn('before it is inserted', problems)
        self.assertIn('Units.Colour does not exist', problems)
        self.assertIn('at most 10 characters', problems)
        self.assertIn('not text', problems)
        self.assertIn('departments.name is required', problems.lower())
        self.assertIn('table Nope does not exist', problems)


class RenderTests(unittest.TestCase):
    def test_postgres_captures_generated_ids_and_quotes_reserved_words(self):
        dialect = Postgres(SCHEMA)
        sql = dialect.insert(Row('Departments', {'Name': "O'Brien", 'CreatedOn': dt.datetime(2026, 1, 2, 3, 4, 5)}, ('department', 'base/'), 'DepartmentId'))
        self.assertEqual(sql, "WITH inserted AS (INSERT INTO departments (name, createdon) VALUES ('O''Brien', '2026-01-02 03:04:05') RETURNING departmentid) "
                              "INSERT INTO qa_seed_keys (kind, name, id) SELECT 'department', 'base/', departmentid FROM inserted;")
        self.assertIn('"order"', dialect.insert(Row('CustomStateDetails', {'Order': 1})))
        update = dialect.update(Update('CustomStateDetails', {'NextStateDetailIds': KeyList('statusdetail', ('a', 'b'))}, 'CustomStateDetailId', Key('statusdetail', 'a')))
        self.assertIn("string_agg(id::text, ','", update)
        self.assertTrue(update.startswith('UPDATE customstatedetails SET nextstatedetailids = '))

    def test_sql_server_uses_output_into_and_bit_literals(self):
        dialect = SqlServer(None)
        sql = dialect.insert(Row('Units', {'Name': 'Engine 1', 'FourWheel': True}, ('unit', 'base/Engine 1'), 'UnitId'))
        self.assertEqual(sql, "INSERT INTO [dbo].[Units] ([Name], [FourWheel]) OUTPUT N'unit', N'base/Engine 1', inserted.[UnitId] INTO #qa_seed_keys (kind, name, id) SELECT N'Engine 1', 1;")

    def test_sql_server_dates_are_unambiguous(self):
        self.assertEqual(SqlServer(None).literal(dt.datetime(2026, 1, 2, 3, 4, 5)), "N'2026-01-02T03:04:05'")

    def test_scripts_guard_the_database_and_run_tag(self):
        floors = [IdentityFloor('CustomStateDetails', 'CustomStateDetailId', 25)]
        pg = Postgres(SCHEMA).script([], GUARD, floors)
        self.assertIn("IF current_database() <> 'resgrid_qa'", pg)
        self.assertIn("'QA-BASE-ADMIN-ABC'", pg)
        self.assertIn('GREATEST(25', pg)
        self.assertTrue(pg.rstrip().endswith('COMMIT;'))
        ss = SqlServer(None).script([], GUARD, floors)
        self.assertIn("IF DB_NAME() <> N'resgrid_qa' THROW", ss)
        self.assertIn('DBCC CHECKIDENT', ss)


if __name__ == '__main__':
    unittest.main()
