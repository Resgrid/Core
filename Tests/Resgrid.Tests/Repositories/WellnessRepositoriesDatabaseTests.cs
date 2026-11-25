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
using Resgrid.Model;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries;
using Resgrid.Repositories.DataRepository.Queries.Common;
using Resgrid.Repositories.DataRepository.Servers.PostgreSql;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for member wellness WL0 (M0376) on both engines: the three tables, the repository queries worker 91 relies on
	/// (only due anniversaries, a claim that wins once, only departments with an entry past the 30-year minimum, pending flags and their
	/// claim/release), the case-insensitive member lookup, removal of only unconfirmed suggestions, then Up again after the version row
	/// is gone changes nothing, and Down removes the tables and Up rebuilds them.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class WellnessRepositoriesDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "wellness_wl0_";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private T2 Repository<T2>(Func<IConnectionProvider, SqlConfiguration, IUnitOfWork, IQueryFactory, T2> build)
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			queries[typeof(InsertQuery)] = new InsertQuery(configuration);
			queries[typeof(UpdateQuery)] = new UpdateQuery(configuration);
			queries[typeof(SelectByIdQuery)] = new SelectByIdQuery(configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);
			return build(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		private void Migrate(Action<IMigrationRunner> run)
		{
			IMigration migration = IsPostgres
				? new Resgrid.Providers.MigrationsPg.Migrations.M0376_AddResponderExposuresPg()
				: new Resgrid.Providers.Migrations.Migrations.M0376_AddResponderExposures();
			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(new[] { migration });
			using var provider = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (IsPostgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			run(provider.GetRequiredService<IMigrationRunner>());
		}

		private RmsResponderExposuresRepository Exposures() => Repository((c, s, u, q) => new RmsResponderExposuresRepository(c, s, u, q));
		private RmsCallExposureFlagsRepository Flags() => Repository((c, s, u, q) => new RmsCallExposureFlagsRepository(c, s, u, q));
		private RmsResponderScreeningFlagsRepository Screening() => Repository((c, s, u, q) => new RmsResponderScreeningFlagsRepository(c, s, u, q));

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

			if (IsPostgres)
			{
				await using var database = (NpgsqlConnection)Connect(_connection);
				await database.OpenAsync();
				await database.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS citext;");
				await database.ReloadTypesAsync();
			}

			Migrate(r => r.MigrateUp());
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			if (_database == null) return;
			DataConfig.DatabaseType = _previous;
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)" : $"ALTER DATABASE {_database} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {_database}");
		}

		private static RmsResponderExposure Entry(int departmentId, string userId, ResponderExposureCategories category, ResponderExposureStatuses status, DateTime occurredOn,
			int? callId = null, string flagId = null) => new RmsResponderExposure
		{
			RmsResponderExposureId = Guid.NewGuid().ToString(),
			DepartmentId = departmentId,
			UserId = userId,
			CallId = callId,
			Category = (int)category,
			Status = (int)status,
			OccurredOn = occurredOn,
			CallExposureFlagId = flagId,
			Substance = "Benzene",
			CreatedOn = DateTime.UtcNow,
			ModifiedOn = DateTime.UtcNow,
			RowVersion = 1
		};

		[Test, Order(1)]
		public async Task The_member_log_and_the_worker_queries_read_what_they_should()
		{
			var repository = Exposures();
			var now = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

			var own = Entry(1, "MiXeD-User", ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Confirmed, now.AddDays(-400), 10);
			own.RemindMember = true;
			own.NextAnniversaryOn = new DateTime(2026, 11, 30);
			var notOptedIn = Entry(1, "other", ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Confirmed, now.AddDays(-400));
			notOptedIn.NextAnniversaryOn = new DateTime(2026, 11, 28);
			var farAnniversary = Entry(2, "far", ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Confirmed, now.AddDays(-300));
			farAnniversary.RemindPeerTeam = true;
			farAnniversary.NextAnniversaryOn = new DateTime(2027, 1, 20);
			var ancient = Entry(3, "old", ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, now.AddYears(-31));
			var suggested = Entry(1, "mixed-user", ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Suggested, now.AddDays(-1), 11, "flag-a");
			var confirmedFromFlag = Entry(1, "other", ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Confirmed, now.AddDays(-1), 11, "flag-a");
			foreach (var row in new[] { own, notOptedIn, farAnniversary, ancient, suggested, confirmedFromFlag })
				await repository.InsertAsync(row, default, true);

			(await repository.GetForUserAsync(1, "MIXED-USER")).Select(r => r.RmsResponderExposureId).Should().BeEquivalentTo(new[] { own.RmsResponderExposureId, suggested.RmsResponderExposureId },
				"member ids compare case-insensitively on both engines");
			(await repository.GetForDepartmentAsync(1, now.AddDays(-2), null)).Should().HaveCount(2);
			(await repository.GetForDepartmentAsync(1, null, null)).Should().HaveCount(4);
			(await repository.GetForCallAsync(1, 11)).Should().HaveCount(2);

			var dueBy = new DateTime(2026, 12, 3);
			(await repository.GetDepartmentsWithDueAnniversariesAsync(dueBy, 50)).Should().BeEquivalentTo(new[] { 1 }, "only a confirmed, opted-in entry with a near anniversary is due");
			var due = (await repository.GetDueAnniversariesAsync(1, dueBy, 50)).ToList();
			due.Should().ContainSingle().Which.RmsResponderExposureId.Should().Be(own.RmsResponderExposureId);
			due[0].RemindMember.Should().BeTrue();
			due[0].Substance.Should().Be("Benzene");

			(await repository.ClaimAnniversaryAsync(1, own.RmsResponderExposureId, new DateTime(2026, 11, 30), new DateTime(2027, 11, 30), now)).Should().BeTrue();
			(await repository.ClaimAnniversaryAsync(1, own.RmsResponderExposureId, new DateTime(2026, 11, 30), new DateTime(2027, 11, 30), now)).Should().BeFalse("the claim wins once");
			(await repository.GetDepartmentsWithDueAnniversariesAsync(dueBy, 50)).Should().BeEmpty();

			(await repository.GetDepartmentsWithEntriesBeforeAsync(now.AddYears(-30), 50)).Should().BeEquivalentTo(new[] { 3 });
			(await repository.GetBeforeAsync(3, now.AddYears(-30), 50)).Should().ContainSingle();

			(await repository.DeleteSuggestionsFromFlagAsync(1, "flag-a")).Should().Be(1);
			(await repository.GetForCallAsync(1, 11)).Should().ContainSingle().Which.RmsResponderExposureId.Should().Be(confirmedFromFlag.RmsResponderExposureId);
			(await repository.DeleteForDepartmentAsync(3, ancient.RmsResponderExposureId)).Should().Be(1);
			(await repository.GetByIdForDepartmentAsync(2, own.RmsResponderExposureId)).Should().BeNull("an id from another department is never read");
		}

		[Test, Order(2)]
		public async Task Call_flags_claim_once_and_release_and_screening_flags_read_the_active_one()
		{
			var flags = Flags();
			var flag = new RmsCallExposureFlag
			{
				RmsCallExposureFlagId = Guid.NewGuid().ToString(), DepartmentId = 1, CallId = 10, Category = (int)ResponderExposureCategories.CriticalIncident,
				FlaggedOn = DateTime.UtcNow, FlaggedByUserId = "officer", RowVersion = 1
			};
			await flags.InsertAsync(flag, default, true);
			(await flags.GetPendingAsync(50)).Should().ContainSingle();
			(await flags.ClaimGenerationAsync(1, flag.RmsCallExposureFlagId, DateTime.UtcNow)).Should().BeTrue();
			(await flags.ClaimGenerationAsync(1, flag.RmsCallExposureFlagId, DateTime.UtcNow)).Should().BeFalse();
			(await flags.GetPendingAsync(50)).Should().BeEmpty();
			await flags.ReleaseGenerationAsync(1, flag.RmsCallExposureFlagId);
			(await flags.GetPendingAsync(50)).Should().ContainSingle();
			await flags.SetSuggestionCountAsync(1, flag.RmsCallExposureFlagId, 4);
			(await flags.GetByIdForDepartmentAsync(1, flag.RmsCallExposureFlagId)).SuggestionCount.Should().Be(4);
			(await flags.GetForCallAsync(1, 10)).Should().ContainSingle();
			(await flags.GetRecentForDepartmentAsync(1, 10)).Should().ContainSingle();

			var duplicate = new RmsCallExposureFlag
			{
				RmsCallExposureFlagId = Guid.NewGuid().ToString(), DepartmentId = 1, CallId = 10, Category = (int)ResponderExposureCategories.CriticalIncident,
				FlaggedOn = DateTime.UtcNow, RowVersion = 1
			};
			Func<Task> insertDuplicate = () => flags.InsertAsync(duplicate, default, true);
			await insertDuplicate.Should().ThrowAsync<Exception>("one flag per call and category");

			var screening = Screening();
			var cleared = new RmsResponderScreeningFlag { RmsResponderScreeningFlagId = Guid.NewGuid().ToString(), DepartmentId = 1, UserId = "Member", FlaggedOn = DateTime.UtcNow.AddDays(-30), ClearedOn = DateTime.UtcNow.AddDays(-1), ModifiedOn = DateTime.UtcNow, RowVersion = 1 };
			var active = new RmsResponderScreeningFlag { RmsResponderScreeningFlagId = Guid.NewGuid().ToString(), DepartmentId = 1, UserId = "Member", FlaggedOn = DateTime.UtcNow, PlanNotes = "Plan", ModifiedOn = DateTime.UtcNow, RowVersion = 1 };
			await screening.InsertAsync(cleared, default, true);
			await screening.InsertAsync(active, default, true);
			(await screening.GetActiveForUserAsync(1, "MEMBER")).RmsResponderScreeningFlagId.Should().Be(active.RmsResponderScreeningFlagId);
			(await screening.GetActiveForDepartmentAsync(1)).Should().ContainSingle();
			(await screening.GetForUserAsync(1, "member")).Should().HaveCount(2);
		}

		private const string VersionDelete = "DELETE FROM {0} WHERE {1} = 376";

		[Test, Order(3)]
		public async Task Up_again_after_the_version_row_is_gone_changes_nothing()
		{
			int before;
			await using (var database = Connect(_connection))
			{
				before = await database.ExecuteScalarAsync<int>(IsPostgres ? "SELECT COUNT(*) FROM rmsresponderexposures" : "SELECT COUNT(*) FROM [RmsResponderExposures]");
				// FluentMigrator's version table keeps its quoted mixed-case name on PostgreSQL.
				await database.ExecuteAsync(IsPostgres ? string.Format(VersionDelete, "\"VersionInfo\"", "\"Version\"") : string.Format(VersionDelete, "[VersionInfo]", "[Version]"));
			}

			Migrate(r => r.MigrateUp());

			await using (var database = Connect(_connection))
				(await database.ExecuteScalarAsync<int>(IsPostgres ? "SELECT COUNT(*) FROM rmsresponderexposures" : "SELECT COUNT(*) FROM [RmsResponderExposures]")).Should().Be(before);
		}

		[Test, Order(4)]
		public async Task Down_removes_the_tables_and_up_rebuilds_them()
		{
			var exists = IsPostgres
				? "SELECT COUNT(*) FROM information_schema.tables WHERE table_name IN ('rmsresponderexposures', 'rmscallexposureflags', 'rmsresponderscreeningflags')"
				: "SELECT COUNT(*) FROM sys.tables WHERE name IN ('RmsResponderExposures', 'RmsCallExposureFlags', 'RmsResponderScreeningFlags')";

			Migrate(r => r.MigrateDown(375));
			await using (var database = Connect(_connection))
				(await database.ExecuteScalarAsync<int>(exists)).Should().Be(0);

			Migrate(r => r.MigrateUp());
			await using (var database = Connect(_connection))
				(await database.ExecuteScalarAsync<int>(exists)).Should().Be(3);
		}
	}
}
