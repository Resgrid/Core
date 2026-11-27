using System;
using System.Collections.Concurrent;
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
	/// Real-database proof for round 8 (ADP catalog 39) on both engines: M0403 (CallProtocolStepNotes) Up, Up again as a no-op, Down and Up; then
	/// the repository on rows read back from the database (department-scoped reads by id, an offline event's row by its client step id, the
	/// partial unique index refusing a second row for one event while notes without a client id repeat freely, a delete) and the protocol
	/// department cleanup taking the department's notes only.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class CallProtocolStepNotesDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "step_notes_";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private CallProtocolStepNotesRepository Repository()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			queries[typeof(InsertQuery)] = new InsertQuery(configuration);
			queries[typeof(UpdateQuery)] = new UpdateQuery(configuration);
			queries[typeof(SelectByIdQuery)] = new SelectByIdQuery(configuration);
			queries[typeof(DeleteQuery)] = new DeleteQuery(configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);
			return new CallProtocolStepNotesRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		private IMigration[] Migrations() => IsPostgres
			? new IMigration[] { new Resgrid.Providers.MigrationsPg.Migrations.M0403_AddCallProtocolStepNotesPg() }
			: new IMigration[] { new Resgrid.Providers.Migrations.Migrations.M0403_AddCallProtocolStepNotes() };

		private void Migrate(Action<IMigrationRunner> run)
		{
			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(Migrations());
			using var provider = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (IsPostgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			run(provider.GetRequiredService<IMigrationRunner>());
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

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				// The protocol cleanup runs only where the protocol tree exists: its root table is enough here.
				await database.ExecuteAsync(IsPostgres
					? "CREATE EXTENSION IF NOT EXISTS citext; CREATE TABLE dispatchprotocols (dispatchprotocolid serial PRIMARY KEY, departmentid integer NOT NULL, name citext NOT NULL);"
					: "CREATE TABLE DispatchProtocols (DispatchProtocolId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, Name nvarchar(100) NOT NULL);");
				if (database is NpgsqlConnection postgres)
					await postgres.ReloadTypesAsync();
				await database.ExecuteAsync($"INSERT INTO {T("DispatchProtocols")} ({T("DepartmentId")}, {T("Name")}) VALUES (1, 'Arrest'), (2, 'Theirs')");
			}

			Migrate(r => r.MigrateUp());
		}

		[OneTimeTearDown]
		public async Task Drop_isolated_database()
		{
			DataConfig.DatabaseType = _previous;
			if (string.IsNullOrWhiteSpace(_master) || _database == null)
				return;
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres
				? $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)"
				: $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]");
		}

		private async Task<int> CountSchemaAsync()
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(IsPostgres
				? "SELECT (SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'callprotocolstepnotes') + " +
				  "(SELECT COUNT(*) FROM pg_indexes WHERE indexname IN ('ix_callprotocolstepnotes_callprotocol', 'ux_callprotocolstepnotes_clientstep'))"
				: "SELECT (SELECT COUNT(*) FROM sys.tables WHERE name = 'CallProtocolStepNotes') + " +
				  "(SELECT COUNT(*) FROM sys.indexes WHERE name IN ('IX_CallProtocolStepNotes_CallProtocol', 'UX_CallProtocolStepNotes_ClientStep'))");
		}

		[Test, Order(1)]
		public async Task Up_creates_the_table_and_indexes_and_up_again_changes_nothing()
		{
			(await CountSchemaAsync()).Should().Be(3);
			await using (var database = Connect(_connection))
				await database.ExecuteAsync(IsPostgres ? "DELETE FROM \"VersionInfo\" WHERE \"Version\" = 403" : "DELETE FROM [VersionInfo] WHERE [Version] = 403");

			Migrate(r => r.MigrateUp());
			(await CountSchemaAsync()).Should().Be(3);
		}

		[Test, Order(2)]
		public async Task Down_removes_it_and_up_rebuilds_it()
		{
			Migrate(r => r.MigrateDown(402));
			(await CountSchemaAsync()).Should().Be(0);

			Migrate(r => r.MigrateUp());
			(await CountSchemaAsync()).Should().Be(3);
		}

		private static CallProtocolStepNote Note(int departmentId, int callProtocolId, string clientStepId, string text) => new CallProtocolStepNote
		{
			CallProtocolStepNoteId = Guid.NewGuid().ToString("D"), DepartmentId = departmentId, CallId = 40, CallProtocolId = callProtocolId, StepId = "s1-a",
			ClientStepId = clientStepId, UserId = "Medic-1", Note = text, IsProtected = text.StartsWith("rgdp:"), CreatedOn = DateTime.UtcNow
		};

		[Test, Order(3)]
		public async Task Notes_are_read_by_department_and_by_an_offline_events_id_and_one_event_has_one_row()
		{
			var repository = Repository();
			var sealedNote = await repository.InsertAsync(Note(1, 300, "dev-1:9", "rgdp:1:1:c2VhbGVk"), CancellationToken.None, true);
			var plain = await repository.InsertAsync(Note(1, 300, null, "ok"), CancellationToken.None, true);
			await repository.InsertAsync(Note(1, 300, null, "ok again"), CancellationToken.None, true);
			var theirs = await repository.InsertAsync(Note(2, 900, "dev-1:9", "theirs"), CancellationToken.None, true);

			var mine = await repository.GetByIdsAsync(1, new[] { sealedNote.CallProtocolStepNoteId, plain.CallProtocolStepNoteId, theirs.CallProtocolStepNoteId });
			mine.Select(n => n.Note).Should().BeEquivalentTo(new[] { "rgdp:1:1:c2VhbGVk", "ok" }, "another department's note is never read");
			mine.Single(n => n.CallProtocolStepNoteId == sealedNote.CallProtocolStepNoteId).IsProtected.Should().BeTrue();
			mine.Should().OnlyContain(n => n.CreatedOn.Kind == DateTimeKind.Utc);

			(await repository.GetByClientStepIdAsync(1, 300, "dev-1:9")).CallProtocolStepNoteId.Should().Be(sealedNote.CallProtocolStepNoteId);
			(await repository.GetByClientStepIdAsync(2, 300, "dev-1:9")).Should().BeNull();

			await FluentActions.Awaiting(() => repository.InsertAsync(Note(1, 300, "dev-1:9", "replayed"), CancellationToken.None, true))
				.Should().ThrowAsync<Exception>("the partial unique index keeps one row per offline event");

			(await repository.DeleteAsync(plain, CancellationToken.None)).Should().BeTrue();
			(await repository.GetByIdsAsync(1, new[] { plain.CallProtocolStepNoteId })).Should().BeEmpty();
		}

		[Test, Order(4)]
		public async Task The_protocol_department_cleanup_takes_the_departments_notes_only()
		{
			await using var database = Connect(_connection);
			await database.OpenAsync();
			await using (var transaction = await database.BeginTransactionAsync())
			{
				await ProtocolDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, 1, type);
				await transaction.CommitAsync();
			}

			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("CallProtocolStepNotes")} WHERE {T("DepartmentId")} = 1")).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("CallProtocolStepNotes")} WHERE {T("DepartmentId")} = 2")).Should().Be(1);
		}
	}
}
