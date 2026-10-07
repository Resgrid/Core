using System;
using System.Collections.Concurrent;
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
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries;
using Resgrid.Repositories.DataRepository.Queries.Common;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for M0264 on both engines: the counter upsert issues each sequence once (also under concurrent
	/// callers), a raised next number only ever rises, renumbering sets the counter, and the seed scan reads the highest
	/// sequence already written into a scope's call numbers. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION /
	/// RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class CallNumberSequencesDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "call_number_sequences_";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private CallNumberSequencesRepository Repository()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));

			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			queries[typeof(InsertQuery)] = new InsertQuery(configuration);
			queries[typeof(UpdateQuery)] = new UpdateQuery(configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);

			return new CallNumberSequencesRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(IsPostgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database checks.");

			if (IsPostgres) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

			_previous = DataConfig.DatabaseType;
			DataConfig.DatabaseType = type;
			_database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + _database);
			_connection = IsPostgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			// A minimal stand-in for the M0001 Calls table the seed scan reads.
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await database.ExecuteAsync(IsPostgres
					? @"CREATE EXTENSION IF NOT EXISTS citext;
						CREATE TABLE calls (callid serial PRIMARY KEY, departmentid int NOT NULL, number citext NULL, loggedon timestamp NOT NULL, isdeleted boolean NOT NULL DEFAULT false);"
					: @"CREATE TABLE Calls (CallId int IDENTITY PRIMARY KEY, DepartmentId int NOT NULL, Number nvarchar(max) NULL, LoggedOn datetime2 NOT NULL, IsDeleted bit NOT NULL DEFAULT 0);");

				// The connection loaded its types before citext existed; without a reload a citext column cannot be read back.
				if (database is NpgsqlConnection npgsql)
					await npgsql.ReloadTypesAsync();
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres
				? new IMigration[] { new M0264_AddCallNumberSequencesPg() }
				: new IMigration[] { new M0264_AddCallNumberSequences() });
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (IsPostgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			if (_database == null) return;
			_runner?.Dispose();
			DataConfig.DatabaseType = _previous;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		private async Task SeedCallAsync(int departmentId, string number, DateTime loggedOn, bool deleted = false)
		{
			await using var database = Connect(_connection);
			await database.ExecuteAsync(IsPostgres
				? "INSERT INTO calls (departmentid, number, loggedon, isdeleted) VALUES (@DepartmentId, @Number, @LoggedOn, @Deleted)"
				: "INSERT INTO Calls (DepartmentId, Number, LoggedOn, IsDeleted) VALUES (@DepartmentId, @Number, @LoggedOn, @Deleted)",
				new { DepartmentId = departmentId, Number = number, LoggedOn = loggedOn, Deleted = deleted });
		}

		[Test]
		public async Task A_new_scope_starts_above_its_seed_and_then_counts_up()
		{
			var repository = Repository();

			(await repository.GetSequenceAsync(1, "26-#")).Should().BeNull();
			(await repository.TakeNextAsync(1, "26-#", 153)).Should().Be(154);
			(await repository.TakeNextAsync(1, "26-#", 153)).Should().Be(155, "the seed only applies to the first call of a scope");
			(await repository.TakeNextAsync(2, "26-#", 0)).Should().Be(1, "departments never share a sequence");

			var row = await repository.GetSequenceAsync(1, "26-#");
			row.LastSequence.Should().Be(155);
			row.FloorSequence.Should().Be(0);
			row.ScopeKey.Should().Be("26-#");
		}

		[Test]
		public async Task Concurrent_calls_never_share_a_sequence()
		{
			var repository = Repository();

			var issued = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => Task.Run(() => Repository().TakeNextAsync(3, "FD2026-#", 0))));

			issued.Should().OnlyHaveUniqueItems();
			issued.OrderBy(i => i).Should().Equal(Enumerable.Range(1, 25));
			(await repository.GetSequenceAsync(3, "FD2026-#")).LastSequence.Should().Be(25);
		}

		[Test]
		public async Task A_raised_next_number_only_ever_rises()
		{
			var repository = Repository();
			await repository.TakeNextAsync(4, "26-#", 10);

			await repository.RaiseFloorAsync(4, "26-#", 500, "admin", new DateTime(2026, 10, 6, 12, 0, 0));
			await repository.RaiseFloorAsync(4, "26-#", 200, "other", new DateTime(2026, 10, 6, 13, 0, 0));
			(await repository.TakeNextAsync(4, "26-#", 0)).Should().Be(500);

			var row = await repository.GetSequenceAsync(4, "26-#");
			row.FloorSequence.Should().Be(500);
			row.FloorSetOn.Should().NotBeNull();

			// A raise on a scope no call has used yet creates it.
			await repository.RaiseFloorAsync(4, "27-#", 40, "admin", DateTime.UtcNow);
			(await repository.TakeNextAsync(4, "27-#", 0)).Should().Be(40);
			(await repository.GetSequenceAsync(4, "27-#")).FloorSetByUserId.Should().Be("admin");
		}

		[Test]
		public async Task Renumbering_reserves_the_counter_and_keeps_the_floor()
		{
			var repository = Repository();
			await repository.RaiseFloorAsync(5, "2026-#", 40, "admin", DateTime.UtcNow);
			(await repository.TakeNextAsync(5, "2026-#", 0)).Should().Be(40);

			(await repository.RaiseLastSequenceAsync(5, "2026-#", 45)).Should().Be(45);
			(await repository.RaiseLastSequenceAsync(5, "2026-#", 30)).Should().Be(45, "a reservation never lowers the counter");
			(await repository.RaiseLastSequenceAsync(5, "2025-#", 7)).Should().Be(7, "a scope with no row is created at the reservation");

			(await repository.TrySetLastSequenceAsync(5, "2026-#", 41, 45)).Should().BeTrue();
			(await repository.GetSequenceAsync(5, "2026-#")).Should().Match<Resgrid.Model.CallNumberSequence>(r => r.LastSequence == 41 && r.FloorSequence == 40);
			(await repository.TakeNextAsync(5, "2025-#", 0)).Should().Be(8);
		}

		[Test]
		public async Task A_counter_a_new_call_moved_is_not_set_back()
		{
			var repository = Repository();
			var reserved = await repository.RaiseLastSequenceAsync(8, "2026-#", 20);
			(await repository.TakeNextAsync(8, "2026-#", 0)).Should().Be(21, "a call created while the year is rewritten");

			(await repository.TrySetLastSequenceAsync(8, "2026-#", 12, reserved)).Should().BeFalse();
			(await repository.TakeNextAsync(8, "2026-#", 0)).Should().Be(22);
		}

		[Test]
		public async Task Deleted_call_numbers_are_read_for_the_period_only()
		{
			var repository = Repository();
			var year = new DateTime(2026, 1, 1);
			await SeedCallAsync(9, "26-3", year.AddDays(3), deleted: true);
			await SeedCallAsync(9, "26-4", year.AddDays(4));
			await SeedCallAsync(9, "25-8", year.AddDays(-2), deleted: true);
			await SeedCallAsync(10, "26-5", year.AddDays(5), deleted: true);

			(await repository.GetDeletedCallNumbersAsync(9, year, year.AddYears(1))).Should().Equal(new[] { "26-3" });
		}

		[Test]
		public async Task The_seed_scan_reads_the_highest_sequence_in_the_scope_and_period()
		{
			var repository = Repository();
			var year = new DateTime(2026, 1, 1);
			await SeedCallAsync(6, "26-1", year.AddDays(1));
			await SeedCallAsync(6, "26-153", year.AddDays(40));
			await SeedCallAsync(6, "26-200", year.AddDays(41), deleted: true);
			await SeedCallAsync(6, "26-9x", year.AddDays(42));
			await SeedCallAsync(6, "26-", year.AddDays(43));
			await SeedCallAsync(6, "FD26-999", year.AddDays(44));
			await SeedCallAsync(6, "26-900", year.AddYears(-100));
			await SeedCallAsync(7, "26-5000", year.AddDays(2));
			await SeedCallAsync(6, "00153/2026", year.AddDays(5));
			await SeedCallAsync(6, "26_7", year.AddDays(6));

			(await repository.GetHighestIssuedAsync(6, "26-", "", year, year.AddYears(1))).Should().Be(200, "deleted calls keep their numbers");
			(await repository.GetHighestIssuedAsync(6, "26-", "", null, null)).Should().Be(900, "a pattern without a date reads every year");
			(await repository.GetHighestIssuedAsync(6, "", "/2026", year, year.AddYears(1))).Should().Be(153);
			(await repository.GetHighestIssuedAsync(6, "26_", "", year, year.AddYears(1))).Should().Be(7, "an underscore in the pattern is literal, not a LIKE wildcard");
			(await repository.GetHighestIssuedAsync(6, "27-", "", year, year.AddYears(1))).Should().Be(0);
		}
	}
}
