"""Connections to the target Resgrid database: schema and flag introspection, and running a seed in one transaction."""
from __future__ import annotations

import os
from dataclasses import dataclass
from typing import Any, Callable

from .flags import Flag
from .sqlgen import Dialect, Postgres, Schema, SqlServer, Statement

_SCHEMA_SQL = {
    'postgres': """
        SELECT table_name, column_name, CASE WHEN data_type = 'USER-DEFINED' THEN udt_name ELSE data_type END,
               is_nullable = 'YES', column_default IS NOT NULL, is_identity = 'YES', character_maximum_length
        FROM information_schema.columns WHERE table_schema = current_schema()""",
    'sqlserver': """
        SELECT c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
               CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END, CASE WHEN c.COLUMN_DEFAULT IS NULL THEN 0 ELSE 1 END,
               COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsIdentity'), c.CHARACTER_MAXIMUM_LENGTH
        FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_SCHEMA = 'dbo'""",
}
_DATABASE_SQL = {'postgres': 'SELECT current_database()', 'sqlserver': 'SELECT DB_NAME()'}


@dataclass(frozen=True)
class Target:
    dialect: str
    host: str
    port: int
    database: str
    user: str
    password: str

    @classmethod
    def from_env(cls, overrides: dict[str, Any]) -> 'Target':
        dialect = overrides.get('dialect') or os.environ.get('SEED_DB_DIALECT', 'postgres')
        default_port = 5432 if dialect == 'postgres' else 1433
        values = {
            'host': overrides.get('host') or os.environ.get('SEED_DB_HOST', 'localhost'),
            'port': int(overrides.get('port') or os.environ.get('SEED_DB_PORT', default_port)),
            'database': overrides.get('database') or os.environ.get('SEED_DB_NAME', ''),
            'user': overrides.get('user') or os.environ.get('SEED_DB_USER', ''),
            # Passwords only from the environment or a file, never the command line (it shows in process lists).
            'password': _secret('SEED_DB_PASSWORD'),
        }
        if not values['database'] or not values['user']:
            raise SystemExit('Set the target database: SEED_DB_NAME and SEED_DB_USER (and SEED_DB_PASSWORD or SEED_DB_PASSWORD_FILE).')
        return cls(dialect, **values)


def _secret(name: str) -> str:
    path = os.environ.get(name + '_FILE')
    if path:
        with open(path, encoding='utf-8') as handle:
            return handle.read().strip()
    return os.environ.get(name, '')


class Database:
    def __init__(self, target: Target):
        self.target = target
        if target.dialect == 'postgres':
            import psycopg
            self.connection = psycopg.connect(host=target.host, port=target.port, dbname=target.database, user=target.user, password=target.password, autocommit=True,
                                              application_name='resgrid-qa-departments')
        elif target.dialect == 'sqlserver':
            import pymssql
            self.connection = pymssql.connect(server=target.host, port=str(target.port), database=target.database, user=target.user, password=target.password,
                                              autocommit=True, appname='resgrid-qa-departments')
        else:
            raise SystemExit(f'Unknown dialect "{target.dialect}": use postgres or sqlserver.')

    def close(self) -> None:
        self.connection.close()

    def query(self, sql: str) -> list[tuple[Any, ...]]:
        cursor = self.connection.cursor()
        try:
            cursor.execute(sql)
            return list(cursor.fetchall())
        finally:
            cursor.close()

    def execute(self, sql: str) -> None:
        cursor = self.connection.cursor()
        try:
            cursor.execute(sql)
        finally:
            cursor.close()

    def database_name(self) -> str:
        return self.query(_DATABASE_SQL[self.target.dialect])[0][0]

    def schema(self) -> Schema:
        rows = self.query(_SCHEMA_SQL[self.target.dialect])
        return Schema.from_columns((r[0], r[1], r[2], bool(r[3]), bool(r[4]), bool(r[5]), r[6]) for r in rows)

    def dialect(self, schema: Schema | None = None) -> Dialect:
        return (Postgres if self.target.dialect == 'postgres' else SqlServer)(schema)

    def flags(self, dialect: Dialect) -> tuple[list[Flag], set[int]]:
        t, c = dialect.table, dialect.column
        flag_columns = ['FeatureFlagId', 'FlagKey', 'IsEnabledGlobally', 'IsArchived', 'FlagType', 'DefaultValue', 'OffValue', 'RolloutPercentage', 'Environment', 'EnableOn', 'DisableOn',
                        'MinimumPlanType', 'IsPermanent']
        rows = self.query(f'SELECT {", ".join(c("FeatureFlags", name) for name in flag_columns)} FROM {t("FeatureFlags")}')
        prerequisites: dict[int, list[tuple[int, str | None]]] = {}
        for flag_id, required_id, required_value in self.query(
                f'SELECT {c("FeatureFlagPrerequisites", "FeatureFlagId")}, {c("FeatureFlagPrerequisites", "RequiredFeatureFlagId")}, {c("FeatureFlagPrerequisites", "RequiredValue")} FROM {t("FeatureFlagPrerequisites")}'):
            prerequisites.setdefault(flag_id, []).append((required_id, required_value))
        targeted = {row[0] for row in self.query(f'SELECT DISTINCT {c("FeatureFlagTargetingRules", "FeatureFlagId")} FROM {t("FeatureFlagTargetingRules")}')}
        flags = [Flag(r[0], r[1], bool(r[2]), bool(r[3]), r[4], r[5], r[6], r[7], r[8], r[9], r[10], r[11], bool(r[12]), tuple(prerequisites.get(r[0], ()))) for r in rows]
        return sorted(flags, key=lambda flag: flag.key.lower()), targeted

    def department_ids(self, dialect: Dialect, names: list[str]) -> dict[str, int]:
        t, c = dialect.table('Departments'), dialect.column
        listed = ', '.join(dialect.string(name) for name in names)
        return {name: department_id for department_id, name in self.query(f'SELECT {c("Departments", "DepartmentId")}, {c("Departments", "Name")} FROM {t} WHERE {c("Departments", "Name")} IN ({listed})')}

    def seed(self, dialect: Dialect, prelude: list[str], body: list[str], phase_two: Callable[[dict[tuple[str, str], int]], list[Statement]]) -> dict[tuple[str, str], int]:
        """Runs the whole seed in one transaction: guards, inserts, then the statements that need the generated ids."""
        try:
            self.execute('\n'.join(prelude + body))
            keys = {(kind, name): int(id_) for kind, name, id_ in self.query(dialect.keys_query())}
            later = phase_two(keys)
            if later:
                self.execute('\n'.join(dialect.body(later)))
            self.execute(dialect.commit())
            return keys
        except BaseException:
            try:
                self.execute('ROLLBACK' if dialect.name == 'postgres' else 'IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION')
            except Exception:  # The connection may already be gone; the server rolls back an open transaction on disconnect.
                pass
            raise
