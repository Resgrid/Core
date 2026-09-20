using System;
using System.Threading.Tasks;
using Dapper;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Resgrid.Providers.Migrations.Maintenance;

namespace Resgrid.Tests.Migrations
{
	/// <summary>Runs the actual maintenance migration against an isolated SQL Server database.</summary>
	[TestFixture, NonParallelizable]
	public class SqlServerCompatibilityLevelTests
	{
		private const string DatabasePrefix = "resgrid_compatibility_test_";
		private string _database;
		private string _master;
		private string _connection;
		private int _maximumCompatibility;
		private bool _created;

		[OneTimeSetUp]
		public async Task CreateDisposableDatabase()
		{
			var configured = Environment.GetEnvironmentVariable("RESGRID_SQLSERVER_COMPATIBILITY_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(configured))
				Assert.Ignore("Set RESGRID_SQLSERVER_COMPATIBILITY_TEST_CONNECTION to a SQL Server master connection to run compatibility tests.");

			var builder = new SqlConnectionStringBuilder(configured);
			if (!string.IsNullOrEmpty(builder.InitialCatalog) && !string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("Compatibility tests require a master connection, never an application database.");
			builder.InitialCatalog = "master";
			builder.Pooling = false;
			_master = builder.ConnectionString;
			_database = DatabasePrefix + Guid.NewGuid().ToString("N");
			await using var master = new SqlConnection(_master);
			var major = await master.ExecuteScalarAsync<int>("SELECT CONVERT(int, SERVERPROPERTY('ProductMajorVersion'))");
			Assert.That(major, Is.GreaterThanOrEqualTo(15), "The fixture requires SQL Server 2019 or later.");
			_maximumCompatibility = major * 10;
			await master.ExecuteAsync($"CREATE DATABASE [{_database}]");
			_created = true;
			builder.InitialCatalog = _database;
			_connection = builder.ConnectionString;
			await using var db = new SqlConnection(_connection);
			await db.ExecuteAsync("CREATE TABLE CompatibilityProbe (Payload nvarchar(max) NOT NULL); INSERT CompatibilityProbe VALUES (N'{\"StartedOn\":\"2026-09-20T01:02:03\",\"CallId\":42}');");
		}

		[OneTimeTearDown]
		public async Task RemoveOnlyTheCreatedDatabase()
		{
			if (!_created) return;
			if (!_database.StartsWith(DatabasePrefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(DatabasePrefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected disposable database name.");
			await using var master = new SqlConnection(_master);
			await master.ExecuteAsync($"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]");
		}

		[TestCase(100, 150)]
		[TestCase(110, 150)]
		[TestCase(140, 150)]
		[TestCase(150, 150)]
		[TestCase(160, 160)]
		public async Task Upgrade_enforces_the_floor_and_preserves_data_and_higher_levels(int initial, int expected)
		{
			if (initial > _maximumCompatibility) Assert.Ignore("This SQL Server does not support the requested higher compatibility level.");
			await SetCompatibility(initial);
			RunMaintenance();
			RunMaintenance(); // Retry is safe even with no numbered migrations pending.

			await using var db = new SqlConnection(_connection);
			Assert.That(await db.ExecuteScalarAsync<int>("SELECT compatibility_level FROM sys.databases WHERE database_id = DB_ID()"), Is.EqualTo(expected));
			Assert.That(await db.ExecuteScalarAsync<string>("SELECT JSON_VALUE(Payload, '$.CallId') FROM CompatibilityProbe"), Is.EqualTo("42"));
			Assert.That(await db.ExecuteScalarAsync<DateTime>("SELECT TRY_CONVERT(datetime2, JSON_VALUE(Payload, '$.StartedOn'), 127) FROM CompatibilityProbe"),
				Is.EqualTo(new DateTime(2026, 9, 20, 1, 2, 3)));
		}

		[Test]
		public async Task Upgrade_rechecks_compatibility_after_it_was_lowered_with_no_schema_changes()
		{
			await SetCompatibility(150);
			RunMaintenance();
			await SetCompatibility(100);
			RunMaintenance();
			await using var db = new SqlConnection(_connection);
			Assert.That(await db.ExecuteScalarAsync<int>("SELECT compatibility_level FROM sys.databases WHERE database_id = DB_ID()"), Is.EqualTo(150));
		}

		private async Task SetCompatibility(int level)
		{
			await using var master = new SqlConnection(_master);
			await master.ExecuteAsync($"ALTER DATABASE [{_database}] SET COMPATIBILITY_LEVEL = {level}");
		}

		private void RunMaintenance()
		{
			using var services = new ServiceCollection().AddFluentMigratorCore()
				.ConfigureRunner(r => r.AddSqlServer().WithGlobalConnectionString(_connection)
					.ScanIn(typeof(EnsureSqlServerCompatibilityLevel).Assembly).For.All())
				.BuildServiceProvider();
			using var scope = services.CreateScope();
			// Target zero exercises BeforeAll through the real runner without installing application tables.
			scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp(0);
		}
	}
}
