using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for M0413 (pack add-on billing accounts, written by the int-CommonApis Billing API) on both engines: the
	/// table is keyed by department and add-on type, CheckoutPacks defaults to 0, the migration re-runs as a no-op, rolls back and
	/// applies again, and department cleanup removes only that department's rows. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION /
	/// RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class PackAddonBillingAccountsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "pack_bill_";
		private const int Department = 4601;
		private const int OtherDepartment = 4602;
		private const int LmsAddon = 9;

		private DatabaseTypes _previousType;
		private string _previousConnection, _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private ServiceProvider Runner() => new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
		{
			if (IsPostgres) r.AddPostgres11_0().WithGlobalConnectionString(_connection).ScanIn(typeof(M0001_InitialMigrationPg).Assembly).For.All();
			else r.AddSqlServer().WithGlobalConnectionString(_connection).ScanIn(typeof(M0001_InitialMigration).Assembly).For.All();
		}).BuildServiceProvider(false);

		private async Task<int> ScalarAsync(string sql, object parameters = null)
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(sql, parameters);
		}

		private async Task ExecuteAsync(string sql, object parameters = null)
		{
			await using var database = Connect(_connection);
			await database.ExecuteAsync(sql, parameters);
		}

		private Task<int> TableExistsAsync() => ScalarAsync(IsPostgres
			? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'packaddonbillingaccounts'"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'PackAddonBillingAccounts'");

		private Task InsertAsync(int department, int addonType, string customer) => ExecuteAsync(
			$"INSERT INTO {T("PackAddonBillingAccounts")} ({T("DepartmentId")}, {T("AddonType")}, {T("Provider")}, {T("CustomerId")}, {T("PlanAddonId")}, " +
			$"{T("PriceId")}, {T("UpdatedOn")}) VALUES (@Department, @AddonType, 'Stripe', @Customer, '7d4e2b91-3c58-4f0a-9e16-5b8a2c4d7f93', " +
			"@Price, @UpdatedOn)",
			new { Department = department, AddonType = addonType, Customer = customer, Price = new string('p', 400), UpdatedOn = DateTime.UtcNow });

		[OneTimeSetUp]
		public async Task Migrate_an_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(IsPostgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database checks.");

			if (IsPostgres) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
			_previousType = DataConfig.DatabaseType;
			_previousConnection = DataConfig.CoreConnectionString;
			DataConfig.DatabaseType = type;

			_database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + _database);
			_connection = IsPostgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;
			DataConfig.CoreConnectionString = _connection;

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
				scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			if (_database == null) return;
			DataConfig.DatabaseType = _previousType;
			DataConfig.CoreConnectionString = _previousConnection;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)" : $"ALTER DATABASE {_database} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {_database}");
		}

		[Test, Order(1)]
		public async Task One_row_per_department_and_addon_type_with_a_400_character_price_and_zero_checkout_packs()
		{
			await InsertAsync(Department, LmsAddon, "cus_a");
			await InsertAsync(Department, 8, "cus_b");

			(await ScalarAsync($"SELECT {T("CheckoutPacks")} FROM {T("PackAddonBillingAccounts")} WHERE {T("DepartmentId")} = @D AND {T("AddonType")} = @A",
				new { D = Department, A = LmsAddon })).Should().Be(0, "CheckoutPacks defaults to 0");
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("PackAddonBillingAccounts")} WHERE {T("ScheduledPacks")} IS NULL AND {T("DepartmentId")} = @D",
				new { D = Department })).Should().Be(2);

			Func<Task> duplicate = () => InsertAsync(Department, LmsAddon, "cus_c");
			await duplicate.Should().ThrowAsync<DbException>("the key is department and add-on type");
		}

		[Test, Order(2)]
		public async Task The_migration_reruns_as_a_no_op_rolls_back_and_applies_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} = 413");
			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await TableExistsAsync()).Should().Be(1);
				(await ScalarAsync($"SELECT COUNT(*) FROM {T("PackAddonBillingAccounts")}")).Should().Be(2, "a second Up over the existing table keeps its rows");

				migrations.MigrateDown(412);
				(await TableExistsAsync()).Should().Be(0, "rolling back to M0412 also rolls back everything after M0413");

				migrations.MigrateUp();
			}
			(await TableExistsAsync()).Should().Be(1);
		}

		[Test, Order(0)]
		public async Task M0420_puts_the_live_stripe_price_on_the_lms_addon_row_and_down_clears_only_that_price()
		{
			var price = $"SELECT COUNT(*) FROM {T("PlanAddons")} WHERE {T("PlanAddonId")} = '7d4e2b91-3c58-4f0a-9e16-5b8a2c4d7f93' AND {T("ExternalId")} = '{M0420_SetLmsAddonStripePrice.StripePriceId}'";
			(await ScalarAsync(price)).Should().Be(1, "the LMS add-on row carries the live Stripe graduated price");

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateDown(419);
				(await ScalarAsync(price)).Should().Be(0);

				await ExecuteAsync($"UPDATE {T("PlanAddons")} SET {T("ExternalId")} = 'price_operator' WHERE {T("PlanAddonId")} = '7d4e2b91-3c58-4f0a-9e16-5b8a2c4d7f93'");
				migrations.MigrateUp();
				(await ScalarAsync(price)).Should().Be(0, "an operator's own price is never overwritten");

				await ExecuteAsync($"UPDATE {T("PlanAddons")} SET {T("ExternalId")} = '' WHERE {T("PlanAddonId")} = '7d4e2b91-3c58-4f0a-9e16-5b8a2c4d7f93'");
				migrations.MigrateDown(419);
				migrations.MigrateUp();
			}
			(await ScalarAsync(price)).Should().Be(1);
		}

		[Test, Order(3)]
		public async Task Department_cleanup_removes_only_that_departments_rows()
		{
			await InsertAsync(Department, LmsAddon, "cus_a");
			await InsertAsync(OtherDepartment, LmsAddon, "cus_z");

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Department, type);
				await transaction.CommitAsync();
			}

			(await ScalarAsync($"SELECT COUNT(*) FROM {T("PackAddonBillingAccounts")} WHERE {T("DepartmentId")} = @D", new { D = Department })).Should().Be(0);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("PackAddonBillingAccounts")} WHERE {T("DepartmentId")} = @D", new { D = OtherDepartment })).Should().Be(1);
		}
	}
}
