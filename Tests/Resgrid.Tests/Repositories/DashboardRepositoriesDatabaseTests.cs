using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
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
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.PostgreSql;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// M0307 dashboard tables on real SQL Server and PostgreSQL (home-dashboard-redesign-plan.md §13): the migration, the
	/// citext user-id comparison, the row-version conditional update, the unique home-board index and the single-default
	/// template index. Needs RESGRID_DASHBOARD_{SQLSERVER|POSTGRES}_TEST_CONNECTION (or the checklist ones); ignored otherwise.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class DashboardRepositoriesDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "dashboard_verification_";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private DbConnection Connect(string connection) => type == DatabaseTypes.Postgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);
		private SqlConfiguration Configuration() => type == DatabaseTypes.Postgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
		private string Q(string name) => type == DatabaseTypes.Postgres ? name.ToLowerInvariant() : "[" + name + "]";

		private IConnectionProvider Connections()
		{
			var provider = new Mock<IConnectionProvider>();
			provider.Setup(p => p.Create()).Returns(() => Connect(_connection));
			return provider.Object;
		}

		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			var suffix = type == DatabaseTypes.Postgres ? "POSTGRES" : "SQLSERVER";
			_master = Environment.GetEnvironmentVariable($"RESGRID_DASHBOARD_{suffix}_TEST_CONNECTION")
				?? Environment.GetEnvironmentVariable($"RESGRID_CHECKLIST_{suffix}_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master))
				Assert.Ignore("Set a dashboard (or checklist) test connection for " + type + " to run real database verification.");

			_previous = DataConfig.DatabaseType;
			DataConfig.DatabaseType = type;
			_database = Prefix + Guid.NewGuid().ToString("N");

			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + _database);

			_connection = type == DatabaseTypes.Postgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			await using (var db = Connect(_connection))
			{
				var featureFlags = type == DatabaseTypes.Postgres
					? "CREATE EXTENSION IF NOT EXISTS citext; CREATE TABLE featureflags (flagkey citext PRIMARY KEY, name text, description text, category text, isenabledglobally boolean);"
					: "CREATE TABLE [FeatureFlags] ([FlagKey] nvarchar(200) PRIMARY KEY, [Name] nvarchar(200), [Description] nvarchar(max), [Category] nvarchar(100), [IsEnabledGlobally] bit);";
				await db.ExecuteAsync(featureFlags);
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(new IMigration[] { type == DatabaseTypes.Postgres ? new M0307_AddHomeDashboardsPg() : new M0307_AddHomeDashboards() });
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixtures_database()
		{
			if (_database == null)
				return;

			_runner?.Dispose();
			DataConfig.DatabaseType = _previous;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");

			if (type == DatabaseTypes.Postgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(type == DatabaseTypes.Postgres
				? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		private async Task InsertBoardAsync(string id, int departmentId, string userId, int sortOrder, int rowVersion = 1)
		{
			await using var db = Connect(_connection);
			await db.ExecuteAsync(
				$"INSERT INTO {Q("UserDashboards")} ({Q("UserDashboardId")}, {Q("DepartmentId")}, {Q("UserId")}, {Q("SortOrder")}, {Q("LayoutJson")}, " +
				$"{Q("SchemaVersion")}, {Q("UseClassic")}, {Q("CreatedOn")}, {Q("UpdatedOn")}, {Q("RowVersion")}) " +
				"VALUES (@Id, @DepartmentId, @UserId, @SortOrder, '{}', 1, @UseClassic, @Now, @Now, @RowVersion)",
				new { Id = id, DepartmentId = departmentId, UserId = userId, SortOrder = sortOrder, UseClassic = false, Now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified), RowVersion = rowVersion });
		}

		private UserDashboardsRepository Boards(IUnitOfWork uow) =>
			new UserDashboardsRepository(Connections(), Configuration(), uow, new Mock<IQueryFactory>().Object);

		[Test]
		public async Task The_migration_seeds_the_flag_off()
		{
			await using var db = Connect(_connection);
			var enabled = await db.ExecuteScalarAsync<bool>($"SELECT {Q("IsEnabledGlobally")} FROM {Q("FeatureFlags")} WHERE {Q("FlagKey")} = 'Dashboard.Modern'");
			enabled.Should().BeFalse();
		}

		[Test]
		public async Task Boards_are_found_regardless_of_user_id_case_and_ordered_home_first()
		{
			await InsertBoardAsync("b-2", 41, "Mixed-Case-User", 1);
			await InsertBoardAsync("b-1", 41, "Mixed-Case-User", 0);
			await InsertBoardAsync("other", 42, "Mixed-Case-User", 0);

			using var uow = new UnitOfWork(Connections());
			var boards = await Boards(uow).GetForUserAsync(41, "mixed-case-user");

			if (type == DatabaseTypes.Postgres)
				boards.Select(b => b.UserDashboardId).Should().Equal("b-1", "b-2");
			else
				boards.Select(b => b.UserDashboardId).Should().Equal(new[] { "b-1", "b-2" }, "SQL Server's default collation is case-insensitive too");
		}

		[Test]
		public async Task The_row_version_update_refuses_a_stale_writer()
		{
			await InsertBoardAsync("rv", 51, "u-51", 0, rowVersion: 3);

			using var uow = new UnitOfWork(Connections());
			var repository = Boards(uow);
			var board = (await repository.GetForUserAsync(51, "u-51")).Single();
			board.LayoutJson = "{\"SchemaVersion\":1}";
			board.UpdatedOn = DateTime.UtcNow;

			(await repository.UpdateWithRowVersionAsync(board, 2, CancellationToken.None)).Should().BeFalse();
			(await repository.UpdateWithRowVersionAsync(board, 3, CancellationToken.None)).Should().BeTrue();
			board.RowVersion.Should().Be(4);
			(await repository.GetForUserAsync(51, "u-51")).Single().RowVersion.Should().Be(4);
		}

		[Test]
		public async Task One_home_board_per_member_per_department()
		{
			await InsertBoardAsync("h-1", 61, "u-61", 0);

			Func<Task> second = () => InsertBoardAsync("h-2", 61, "u-61", 0);
			await second.Should().ThrowAsync<DbException>("the unique (department, user, sort order) index stops two browsers creating two home boards");
		}

		[Test]
		public async Task Only_one_default_template_per_department_and_clear_default_works()
		{
			await using (var db = Connect(_connection))
			{
				var insert = $"INSERT INTO {Q("DepartmentDashboardTemplates")} ({Q("DepartmentDashboardTemplateId")}, {Q("DepartmentId")}, {Q("Name")}, {Q("Audience")}, " +
					$"{Q("Priority")}, {Q("SchemaVersion")}, {Q("IsDefault")}, {Q("CreatedOn")}, {Q("UpdatedOn")}) VALUES (@Id, 71, @Id, 0, 0, 1, @IsDefault, @Now, @Now)";
				var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
				await db.ExecuteAsync(insert, new { Id = "t-1", IsDefault = true, Now = now });

				Func<Task> secondDefault = () => db.ExecuteAsync(insert, new { Id = "t-2", IsDefault = true, Now = now });
				await secondDefault.Should().ThrowAsync<DbException>();

				await db.ExecuteAsync(insert, new { Id = "t-3", IsDefault = false, Now = now });
			}

			using var uow = new UnitOfWork(Connections());
			var templates = new DepartmentDashboardTemplatesRepository(Connections(), Configuration(), uow, new Mock<IQueryFactory>().Object);
			(await templates.ClearDefaultAsync(71, "t-3", CancellationToken.None)).Should().Be(1);
			(await templates.GetForDepartmentAsync(71)).Should().OnlyContain(t => !t.IsDefault);
		}
	}
}
