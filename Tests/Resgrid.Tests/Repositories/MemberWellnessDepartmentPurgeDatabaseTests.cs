using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Repositories.DataRepository;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Owner decision 2026-11-26, on both engines: the Records subtree of a department deletion (<see cref="RmsDepartmentCleanup"/>, run by the
	/// SQL Server delete script and by the orphan sweep on either engine) empties every wellness and member-health table
	/// (<see cref="MemberWellnessDataTables.All"/>, migrated by M0360 and M0372-M0374) for the department and leaves other departments' rows.
	/// Each table is seeded generically from its schema, so a table added to the list is covered without a change here.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class MemberWellnessDepartmentPurgeDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "wellness_purge_";
		private string _master, _connection, _database;
		private int _counter;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string Q(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private void Migrate()
		{
			IMigration[] migrations = IsPostgres
				? new IMigration[]
				{
					new Resgrid.Providers.MigrationsPg.Migrations.M0360_AddResponderExposuresPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0372_AddExposureIncidentsPg(),
					new Resgrid.Providers.MigrationsPg.Migrations.M0373_AddMemberInjuriesPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0374_AddMedicalClearancesPg()
				}
				: new IMigration[]
				{
					new Resgrid.Providers.Migrations.Migrations.M0360_AddResponderExposures(), new Resgrid.Providers.Migrations.Migrations.M0372_AddExposureIncidents(),
					new Resgrid.Providers.Migrations.Migrations.M0373_AddMemberInjuries(), new Resgrid.Providers.Migrations.Migrations.M0374_AddMedicalClearances()
				};
			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(migrations);
			using var provider = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (IsPostgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			provider.GetRequiredService<IMigrationRunner>().MigrateUp();
		}

		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(IsPostgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database checks.");

			if (IsPostgres) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

			_database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + _database);
			_connection = IsPostgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				if (IsPostgres)
				{
					await database.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS citext;");
					await ((NpgsqlConnection)database).ReloadTypesAsync();
				}

				// The cleanup locks the department's row; only its key matters here.
				await database.ExecuteAsync($"CREATE TABLE {Q("Departments")} ({Q("DepartmentId")} int NOT NULL PRIMARY KEY)");
			}

			Migrate();
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			if (_database == null) return;
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)" : $"ALTER DATABASE {_database} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {_database}");
		}

		private sealed class Column
		{
			public string Name { get; set; }
			public string DataType { get; set; }
			public string UdtName { get; set; }
			public int? MaxLength { get; set; }
			public bool Nullable { get; set; }
			public bool HasDefault { get; set; }
		}

		private async Task<List<Column>> ColumnsAsync(DbConnection db, string table)
		{
			var rows = await db.QueryAsync<(string Name, string DataType, string UdtName, int? MaxLength, string IsNullable, string ColumnDefault)>(IsPostgres
				? "SELECT column_name, data_type, udt_name, character_maximum_length, is_nullable, column_default FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @Table ORDER BY ordinal_position"
				: "SELECT COLUMN_NAME, DATA_TYPE, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @Table ORDER BY ORDINAL_POSITION",
				new { Table = IsPostgres ? table.ToLowerInvariant() : table });
			return rows.Select(r => new Column
			{
				Name = r.Name,
				DataType = (r.DataType ?? string.Empty).ToLowerInvariant(),
				UdtName = (r.UdtName ?? string.Empty).ToLowerInvariant(),
				MaxLength = r.MaxLength,
				Nullable = string.Equals(r.IsNullable, "YES", StringComparison.OrdinalIgnoreCase),
				HasDefault = r.ColumnDefault != null
			}).ToList();
		}

		private object ValueFor(Column column)
		{
			var n = ++_counter;
			switch (column.DataType)
			{
				case "int": case "integer": case "smallint": case "tinyint":
					return 1;
				case "bigint":
					return (long)n;
				case "bit": case "boolean":
					return false;
				case "datetime": case "datetime2": case "date": case "timestamp without time zone": case "timestamp with time zone": case "datetimeoffset":
					return new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc).AddMinutes(n);
				case "decimal": case "numeric": case "float": case "real": case "double precision": case "money":
					return 1m;
				case "uniqueidentifier": case "uuid":
					return Guid.NewGuid();
				default:
					var text = Guid.NewGuid().ToString();
					return column.MaxLength.HasValue && column.MaxLength.Value > 0 && column.MaxLength.Value < text.Length ? text.Substring(0, column.MaxLength.Value) : text;
			}
		}

		/// <summary>One row of the table for the department: every required column generated, DepartmentId set.</summary>
		private async Task SeedAsync(DbConnection db, string table, int departmentId)
		{
			var columns = await ColumnsAsync(db, table);
			columns.Should().NotBeEmpty($"{table} exists after the wellness migrations");
			columns.Should().Contain(c => string.Equals(c.Name, "DepartmentId", StringComparison.OrdinalIgnoreCase), $"{table} carries DepartmentId");

			var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
			foreach (var column in columns)
			{
				if (string.Equals(column.Name, "DepartmentId", StringComparison.OrdinalIgnoreCase))
					values[column.Name] = departmentId;
				else if (!column.Nullable && !column.HasDefault)
					values[column.Name] = ValueFor(column);
			}

			var parameters = new DynamicParameters();
			var names = values.Keys.ToList();
			for (var i = 0; i < names.Count; i++)
				parameters.Add("p" + i, values[names[i]]);
			await db.ExecuteAsync($"INSERT INTO {Q(table)} ({string.Join(", ", names.Select(Q))}) VALUES ({string.Join(", ", names.Select((_, i) => "@p" + i))})", parameters);
		}

		private async Task<int> CountAsync(DbConnection db, string table, int departmentId) =>
			await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q(table)} WHERE {Q("DepartmentId")} = @DepartmentId", new { DepartmentId = departmentId });

		[Test]
		public async Task Deleting_a_department_empties_every_wellness_table_and_keeps_other_departments_rows()
		{
			const int deleted = 7101, kept = 7102;
			await using var db = Connect(_connection);
			await db.OpenAsync();
			foreach (var department in new[] { deleted, kept })
			{
				await db.ExecuteAsync($"INSERT INTO {Q("Departments")} ({Q("DepartmentId")}) VALUES (@DepartmentId)", new { DepartmentId = department });
				foreach (var table in MemberWellnessDataTables.All)
					await SeedAsync(db, table, department);
			}

			var discovered = await RmsDepartmentCleanup.DepartmentTablesAsync(db, null, type);
			discovered.Should().Contain(MemberWellnessDataTables.All.Select(t => IsPostgres ? t.ToLowerInvariant() : t), "the Records cleanup finds every wellness table by name");

			await using (var transaction = await db.BeginTransactionAsync())
			{
				(await RmsDepartmentCleanup.DeleteWithinTransactionAsync(db, transaction, deleted, type, false)).Should().BeTrue();
				await transaction.CommitAsync();
			}

			foreach (var table in MemberWellnessDataTables.All)
			{
				(await CountAsync(db, table, deleted)).Should().Be(0, $"the deleted department's {table} rows are purged");
				(await CountAsync(db, table, kept)).Should().Be(1, $"another department's {table} rows survive");
			}
		}

		[Test]
		public async Task The_orphan_sweep_empties_every_wellness_table_an_earlier_deletion_left()
		{
			const int orphaned = 7201, live = 7202;
			await using var db = Connect(_connection);
			await db.OpenAsync();
			await db.ExecuteAsync($"INSERT INTO {Q("Departments")} ({Q("DepartmentId")}) VALUES (@DepartmentId)", new { DepartmentId = live });
			foreach (var department in new[] { orphaned, live })
				foreach (var table in MemberWellnessDataTables.All)
					await SeedAsync(db, table, department);

			(await RmsDepartmentCleanup.GetOrphanedDepartmentIdsAsync(db, null, type)).Should().Contain(orphaned).And.NotContain(live);

			await using (var transaction = await db.BeginTransactionAsync())
			{
				(await RmsDepartmentCleanup.DeleteWithinTransactionAsync(db, transaction, orphaned, type, true)).Should().BeTrue();
				await transaction.CommitAsync();
			}

			foreach (var table in MemberWellnessDataTables.All)
			{
				(await CountAsync(db, table, orphaned)).Should().Be(0, $"the orphaned department's {table} rows are purged");
				(await CountAsync(db, table, live)).Should().Be(1, $"a live department's {table} rows survive");
			}
		}
	}
}
