"""Dialect-neutral rows and the SQL scripts that insert them (PostgreSQL and SQL Server).

Rows never carry database-generated ids. A row that other rows point at declares a `key`; its generated id is captured
in the same statement (`RETURNING` on PostgreSQL, `OUTPUT ... INTO` on SQL Server) into a session key table, and later
rows reference it with `Key(kind, name)`. The seed therefore never assumes or reserves id ranges and can run on a shared
server next to real departments.
"""
from __future__ import annotations

import datetime as dt
import json
import re
import uuid
from dataclasses import dataclass, field
from typing import Any, Iterable, Mapping, Union

KEYS_TABLE = 'qa_seed_keys'


@dataclass(frozen=True)
class Key:
    """The generated id of an earlier row that declared `key=(kind, name)`."""

    kind: str
    name: str


@dataclass(frozen=True)
class KeyList:
    """Comma-separated generated ids of earlier rows, in the given order (Permissions.Data, next-status lists)."""

    kind: str
    names: tuple[str, ...]


@dataclass(frozen=True)
class Row:
    """One INSERT. `table` and column names use Core's entity names (SQL Server casing); PostgreSQL lowercases them."""

    table: str
    values: Mapping[str, Any]
    # (kind, name) under which the generated id of `returns` is captured for later Key lookups.
    key: tuple[str, str] | None = None
    returns: str | None = None
    # What the row is, for error messages ("base: unit Engine 1").
    label: str = ''


@dataclass(frozen=True)
class Update:
    """`UPDATE table SET values WHERE id_column = id`, for values that need ids generated after the row was inserted."""

    table: str
    values: Mapping[str, Any]
    id_column: str
    id: Any
    label: str = ''


Statement = Union[Row, Update]


@dataclass(frozen=True)
class IdentityFloor:
    """Moves a table's id sequence to at least `floor` so new ids never fall in a reserved range (never moves it back)."""

    table: str
    column: str
    floor: int


@dataclass(frozen=True)
class Column:
    name: str
    data_type: str
    nullable: bool
    has_default: bool
    identity: bool
    max_length: int | None


@dataclass
class Schema:
    """The target database's tables and columns, looked up case-insensitively."""

    tables: dict[str, dict[str, Column]] = field(default_factory=dict)
    # Lower-cased name -> the name the database uses.
    table_names: dict[str, str] = field(default_factory=dict)

    @classmethod
    def from_columns(cls, columns: Iterable[tuple[str, str, str, bool, bool, bool, int | None]]) -> 'Schema':
        schema = cls()
        for table, name, data_type, nullable, has_default, identity, max_length in columns:
            schema.table_names.setdefault(table.lower(), table)
            schema.tables.setdefault(table.lower(), {})[name.lower()] = Column(name, data_type.lower(), nullable, has_default, identity, max_length)
        return schema

    def table(self, name: str) -> dict[str, Column] | None:
        return self.tables.get(name.lower())

    def column(self, table: str, name: str) -> Column | None:
        columns = self.table(table)
        return columns.get(name.lower()) if columns else None


class SeedError(Exception):
    pass


BOOLEAN_TYPES = {'boolean', 'bit'}
INTEGER_TYPES = {'integer', 'int', 'bigint', 'smallint', 'tinyint'}
NUMERIC_TYPES = INTEGER_TYPES | {'numeric', 'decimal', 'real', 'double precision', 'float', 'money'}
TEXT_TYPES = {'text', 'character varying', 'varchar', 'nvarchar', 'character', 'char', 'nchar', 'ntext', 'citext'}
TIME_TYPES = {'timestamp without time zone', 'timestamp with time zone', 'datetime', 'datetime2', 'datetimeoffset', 'date', 'smalldatetime', 'time without time zone', 'time'}
GUID_TYPES = {'uuid', 'uniqueidentifier'}
BINARY_TYPES = {'bytea', 'varbinary', 'binary', 'image'}
JSON_TYPES = {'json', 'jsonb'}


def validate(statements: list[Statement], schema: Schema, declared: set[tuple[str, str]] | None = None) -> list[str]:
    """Every problem that would make the script fail or silently store the wrong value, before anything runs.

    `declared` carries keys inserted by an earlier phase of the same run (and receives this phase's keys).
    """
    problems: list[str] = []
    declared = declared if declared is not None else set()
    for statement in statements:
        where = statement.label or statement.table
        columns = schema.table(statement.table)
        if columns is None:
            problems.append(f'{where}: table {statement.table} does not exist')
            continue
        values = dict(statement.values)
        if isinstance(statement, Update):
            values[statement.id_column] = statement.id
        for name, value in values.items():
            column = columns.get(name.lower())
            if column is None:
                problems.append(f'{where}: {statement.table}.{name} does not exist')
                continue
            problems += [f'{where}: {statement.table}.{name} {problem}' for problem in _value_problems(column, value, declared)]
        if isinstance(statement, Update):
            continue
        for column in columns.values():
            if column.nullable or column.has_default or column.identity:
                continue
            if not any(name.lower() == column.name.lower() for name in statement.values):
                problems.append(f'{where}: {statement.table}.{column.name} is required')
        if statement.key:
            if not statement.returns or (statement.returns.lower() not in columns):
                problems.append(f'{where}: key {statement.key} needs the generated id column of {statement.table}')
            if statement.key in declared:
                problems.append(f'{where}: key {statement.key} is declared twice')
            declared.add(statement.key)
    return problems


def _value_problems(column: Column, value: Any, declared: set[tuple[str, str]]) -> list[str]:
    if isinstance(value, Key):
        if (value.kind, value.name) not in declared:
            return [f'refers to {value.kind} "{value.name}" before it is inserted']
        return [] if column.data_type in NUMERIC_TYPES | TEXT_TYPES else [f'is {column.data_type}, not an id']
    if isinstance(value, KeyList):
        missing = [name for name in value.names if (value.kind, name) not in declared]
        if missing:
            return [f'refers to {value.kind} {missing} before they are inserted']
        return [] if column.data_type in TEXT_TYPES else [f'is {column.data_type}, not text for an id list']
    problem = _type_problem(column, value)
    return [problem] if problem else []


def _type_problem(column: Column, value: Any) -> str | None:
    if value is None:
        return None if column.nullable else 'cannot be NULL'
    kind = column.data_type
    if isinstance(value, bool):
        return None if kind in BOOLEAN_TYPES | INTEGER_TYPES else f'is {kind}, not a boolean'
    if isinstance(value, (int, float)):
        return None if kind in NUMERIC_TYPES | BOOLEAN_TYPES else f'is {kind}, not a number'
    if isinstance(value, (dt.datetime, dt.date)):
        return None if kind in TIME_TYPES else f'is {kind}, not a date/time'
    if isinstance(value, bytes):
        return None if kind in BINARY_TYPES else f'is {kind}, not binary'
    if isinstance(value, uuid.UUID):
        return None if kind in GUID_TYPES | TEXT_TYPES else f'is {kind}, not a GUID'
    if isinstance(value, (dict, list)):
        return None if kind in JSON_TYPES | TEXT_TYPES else f'is {kind}, not JSON/text'
    if isinstance(value, str):
        if kind in GUID_TYPES:
            return None if _is_guid(value) else f'is {kind}, "{value}" is not a GUID'
        if kind not in TEXT_TYPES | JSON_TYPES:
            return f'is {kind}, not text'
        # -1 is nvarchar(max) on SQL Server.
        if column.max_length and column.max_length > 0 and len(value) > column.max_length:
            return f'holds at most {column.max_length} characters, got {len(value)}'
        return None
    return f'has an unsupported value type {type(value).__name__}'


def _is_guid(value: str) -> bool:
    try:
        uuid.UUID(value)
        return True
    except ValueError:
        return False


@dataclass(frozen=True)
class Guard:
    """Refusals checked inside the seed transaction before the first insert."""

    database: str
    run_tag: str
    # Department names this run creates: any that already exists means the run tag was used before.
    department_names: tuple[str, ...]
    user_names: tuple[str, ...]


class Dialect:
    name = ''

    def __init__(self, schema: Schema | None = None):
        self.schema = schema

    # --- names -------------------------------------------------------------------------------------------------------
    def table(self, name: str) -> str:
        raise NotImplementedError

    def column(self, table: str, name: str) -> str:
        raise NotImplementedError

    def _actual_table(self, name: str, fallback: str) -> str:
        if self.schema and name.lower() in self.schema.table_names:
            return self.schema.table_names[name.lower()]
        return fallback

    def _actual_column(self, table: str, name: str, fallback: str) -> str:
        column = self.schema.column(table, name) if self.schema else None
        return column.name if column else fallback

    # --- values ------------------------------------------------------------------------------------------------------
    def string(self, value: str) -> str:
        raise NotImplementedError

    def boolean(self, value: bool, column: Column | None) -> str:
        raise NotImplementedError

    def binary(self, value: bytes) -> str:
        raise NotImplementedError

    def id_list(self, value: KeyList) -> str:
        raise NotImplementedError

    def literal(self, value: Any, column: Column | None = None) -> str:
        if value is None:
            return 'NULL'
        if isinstance(value, Key):
            return f'(SELECT id FROM {self.keys_table} WHERE kind = {self.string(value.kind)} AND name = {self.string(value.name)})'
        if isinstance(value, KeyList):
            return self.id_list(value)
        if isinstance(value, bool):
            return self.boolean(value, column)
        if isinstance(value, int):
            return str(value)
        if isinstance(value, float):
            return repr(value)
        if isinstance(value, dt.datetime):
            return self.string(value.strftime('%Y-%m-%d %H:%M:%S'))
        if isinstance(value, dt.date):
            return self.string(value.isoformat())
        if isinstance(value, bytes):
            return self.binary(value)
        if isinstance(value, uuid.UUID):
            return self.string(str(value))
        if isinstance(value, (dict, list)):
            return self.string(json.dumps(value, ensure_ascii=False, separators=(',', ':')))
        return self.string(str(value))

    def _column_of(self, table: str, name: str) -> Column | None:
        return self.schema.column(table, name) if self.schema else None

    # --- statements --------------------------------------------------------------------------------------------------
    @property
    def keys_table(self) -> str:
        raise NotImplementedError

    def insert(self, row: Row) -> str:
        raise NotImplementedError

    def update(self, update: Update) -> str:
        assignments = ', '.join(f'{self.column(update.table, name)} = {self.literal(value, self._column_of(update.table, name))}' for name, value in update.values.items())
        return f'UPDATE {self.table(update.table)} SET {assignments} WHERE {self.column(update.table, update.id_column)} = {self.literal(update.id, self._column_of(update.table, update.id_column))};'

    def statement(self, statement: Statement) -> str:
        return self.update(statement) if isinstance(statement, Update) else self.insert(statement)

    def _parts(self, row: Row) -> tuple[str, str, str]:
        table = self.table(row.table)
        names = ', '.join(self.column(row.table, name) for name in row.values)
        values = ', '.join(self.literal(value, self._column_of(row.table, name)) for name, value in row.values.items())
        return table, names, values

    def prelude(self, guard: Guard, floors: list[IdentityFloor], for_psql: bool = False) -> list[str]:
        raise NotImplementedError

    def body(self, statements: list[Statement]) -> list[str]:
        return [self.statement(statement) for statement in statements]

    def keys_query(self) -> str:
        return f'SELECT kind, name, id FROM {self.keys_table} ORDER BY kind, name'

    def commit(self) -> str:
        raise NotImplementedError

    def script(self, statements: list[Statement], guard: Guard, floors: list[IdentityFloor], header: list[str] | None = None) -> str:
        lines = [f'-- {line}' for line in (header or [])]
        lines += self.prelude(guard, floors, for_psql=True)
        lines += self.body(statements)
        lines += [self.keys_query() + ';', self.commit(), '']
        return '\n'.join(lines)


class Postgres(Dialect):
    name = 'postgres'

    def table(self, name: str) -> str:
        return _pg_ident(self._actual_table(name, name.lower()))

    def column(self, table: str, name: str) -> str:
        return _pg_ident(self._actual_column(table, name, name.lower()))

    def string(self, value: str) -> str:
        return "'" + value.replace("'", "''") + "'"

    def boolean(self, value: bool, column: Column | None) -> str:
        if column and column.data_type in INTEGER_TYPES:
            return '1' if value else '0'
        return 'true' if value else 'false'

    def binary(self, value: bytes) -> str:
        return f"decode('{value.hex()}', 'hex')"

    def id_list(self, value: KeyList) -> str:
        names = ', '.join(self.string(name) for name in value.names)
        order = ', '.join(self.string(name) for name in value.names)
        return f"(SELECT string_agg(id::text, ',' ORDER BY array_position(ARRAY[{order}]::text[], name::text)) FROM {KEYS_TABLE} WHERE kind = {self.string(value.kind)} AND name IN ({names}))"

    @property
    def keys_table(self) -> str:
        return KEYS_TABLE

    def insert(self, row: Row) -> str:
        table, names, values = self._parts(row)
        statement = f'INSERT INTO {table} ({names}) VALUES ({values})'
        if not row.key:
            return statement + ';'
        returns = self.column(row.table, row.returns or '')
        return f'WITH inserted AS ({statement} RETURNING {returns}) INSERT INTO {KEYS_TABLE} (kind, name, id) SELECT {self.string(row.key[0])}, {self.string(row.key[1])}, {returns} FROM inserted;'

    def prelude(self, guard: Guard, floors: list[IdentityFloor], for_psql: bool = False) -> list[str]:
        departments = self.table('Departments')
        users = self.table('AspNetUsers')
        lines = ['\\set ON_ERROR_STOP on'] if for_psql else []
        lines += [
            "SET TIME ZONE 'UTC';",
            'BEGIN;',
            # One seed at a time per database; a second run waits rather than racing the uniqueness checks.
            "SELECT pg_advisory_xact_lock(hashtext('resgrid-qa-departments'));",
            'DO $guard$ BEGIN',
            f"  IF current_database() <> {self.string(guard.database)} THEN RAISE EXCEPTION 'QA seed refuses database %: expected {guard.database}', current_database(); END IF;",
            f'  IF EXISTS (SELECT 1 FROM {departments} WHERE {self.column("Departments", "Name")} IN ({", ".join(self.string(n) for n in guard.department_names)})) THEN',
            f"    RAISE EXCEPTION 'QA seed run tag {guard.run_tag} was already used: a department with its name exists'; END IF;",
            f'  IF EXISTS (SELECT 1 FROM {users} WHERE {self.column("AspNetUsers", "NormalizedUserName")} IN ({", ".join(self.string(n.upper()) for n in guard.user_names)})) THEN',
            f"    RAISE EXCEPTION 'QA seed run tag {guard.run_tag} was already used: a user name exists'; END IF;",
            'END $guard$;',
        ]
        for floor in floors:
            table, column = self.table(floor.table), self.column(floor.table, floor.column)
            lines.append(
                f"DO $floor$ DECLARE s text := pg_get_serial_sequence({self.string(table)}, {self.string(column)}); BEGIN "
                f"EXECUTE format('SELECT setval(%L, GREATEST({floor.floor}, (SELECT last_value FROM %s), (SELECT COALESCE(MAX({column}), 0) FROM {table})))', s, s); END $floor$;"
            )
        lines.append(f'CREATE TEMP TABLE {KEYS_TABLE} (kind varchar(64) NOT NULL, name varchar(512) NOT NULL, id bigint NOT NULL, PRIMARY KEY (kind, name)) ON COMMIT DROP;')
        return lines

    def commit(self) -> str:
        return 'COMMIT;'


class SqlServer(Dialect):
    name = 'sqlserver'

    def table(self, name: str) -> str:
        return f'[dbo].[{self._actual_table(name, name)}]'

    def column(self, table: str, name: str) -> str:
        return f'[{self._actual_column(table, name, name)}]'

    def string(self, value: str) -> str:
        return "N'" + value.replace("'", "''") + "'"

    def boolean(self, value: bool, column: Column | None) -> str:
        return '1' if value else '0'

    def literal(self, value: Any, column: Column | None = None) -> str:
        # The ISO 8601 'T' form is the one string the legacy datetime type reads the same under every DATEFORMAT.
        if isinstance(value, dt.datetime):
            return self.string(value.strftime('%Y-%m-%dT%H:%M:%S'))
        return super().literal(value, column)

    def binary(self, value: bytes) -> str:
        return '0x' + value.hex()

    def id_list(self, value: KeyList) -> str:
        names = ', '.join(self.string(name) for name in value.names)
        order = ' '.join(f'WHEN {self.string(name)} THEN {index}' for index, name in enumerate(value.names))
        return f"(SELECT STRING_AGG(CAST(id AS nvarchar(20)), N',') WITHIN GROUP (ORDER BY CASE name {order} END) FROM {self.keys_table} WHERE kind = {self.string(value.kind)} AND name IN ({names}))"

    @property
    def keys_table(self) -> str:
        return '#' + KEYS_TABLE

    def insert(self, row: Row) -> str:
        table, names, values = self._parts(row)
        output = ''
        if row.key:
            returns = self.column(row.table, row.returns or '')
            output = f' OUTPUT {self.string(row.key[0])}, {self.string(row.key[1])}, inserted.{returns} INTO {self.keys_table} (kind, name, id)'
        return f'INSERT INTO {table} ({names}){output} SELECT {values};'

    def prelude(self, guard: Guard, floors: list[IdentityFloor], for_psql: bool = False) -> list[str]:
        departments = self.table('Departments')
        users = self.table('AspNetUsers')
        names = ', '.join(self.string(n) for n in guard.department_names)
        user_names = ', '.join(self.string(n.upper()) for n in guard.user_names)
        lines = [
            'SET NOCOUNT ON;',
            'SET XACT_ABORT ON;',
            'BEGIN TRANSACTION;',
            "EXEC sp_getapplock @Resource = N'resgrid-qa-departments', @LockMode = N'Exclusive', @LockOwner = N'Transaction';",
            f"IF DB_NAME() <> {self.string(guard.database)} THROW 50001, N'QA seed refuses this database: expected {guard.database}', 1;",
            f"IF EXISTS (SELECT 1 FROM {departments} WHERE {self.column('Departments', 'Name')} IN ({names})) THROW 50002, N'QA seed run tag {guard.run_tag} was already used: a department with its name exists', 1;",
            f"IF EXISTS (SELECT 1 FROM {users} WHERE {self.column('AspNetUsers', 'NormalizedUserName')} IN ({user_names})) THROW 50003, N'QA seed run tag {guard.run_tag} was already used: a user name exists', 1;",
        ]
        for floor in floors:
            name = self.string(f'dbo.{self._actual_table(floor.table, floor.table)}')
            # RESEED n gives n on a table that never had rows and n + 1 otherwise; both clear the floor.
            lines.append(f'IF IDENT_CURRENT({name}) < {floor.floor + 1} DBCC CHECKIDENT ({name}, RESEED, {floor.floor + 1}) WITH NO_INFOMSGS;')
        lines.append(f'CREATE TABLE {self.keys_table} (kind nvarchar(64) NOT NULL, name nvarchar(512) NOT NULL, id bigint NOT NULL, PRIMARY KEY (kind, name));')
        return lines

    def commit(self) -> str:
        return 'COMMIT TRANSACTION;'


DIALECTS: dict[str, type[Dialect]] = {'postgres': Postgres, 'sqlserver': SqlServer}

_PLAIN_IDENT = re.compile(r'^[a-z_][a-z0-9_]*$')
# PostgreSQL reserved words that appear as Core column names ("order", "end", "start", "type" is fine).
_PG_RESERVED = {'order', 'end', 'user', 'default', 'group', 'table', 'select', 'from', 'where', 'limit', 'offset', 'check', 'primary', 'references', 'when', 'case', 'to', 'do', 'for', 'only', 'all', 'and', 'or', 'not', 'null', 'true', 'false'}


def _pg_ident(name: str) -> str:
    return name if _PLAIN_IDENT.match(name) and name not in _PG_RESERVED else '"' + name.replace('"', '""') + '"'
