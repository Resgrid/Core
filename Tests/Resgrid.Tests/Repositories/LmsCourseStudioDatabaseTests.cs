using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries;
using Resgrid.Repositories.DataRepository.Servers.PostgreSql;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for round 6F on both engines: M0398 in the full chain, Up again as a no-op, Down and Up; course origins (one per
	/// course, citations left out of a department list), the flashcard supersede update (only the department's active cards of that source drafted
	/// from another version; a flagged card keeps its first flag), the per-card flag, a review written back, and the department cleanup.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class LmsCourseStudioDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "lms_r6f_";
		private const string UserA = "7b2e0f1c-1a2b-4c3d-8e9f-00000000000a";
		private const string UserB = "7b2e0f1c-1a2b-4c3d-8e9f-00000000000b";

		private DatabaseTypes _previousType;
		private string _previousConnection;
		private string _master, _connection, _database;
		private int _department, _otherDepartment;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private TRepository Repository<TRepository>(Func<IConnectionProvider, SqlConfiguration, IUnitOfWork, IQueryFactory, TRepository> build)
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			foreach (var query in new[] { typeof(Resgrid.Repositories.DataRepository.Queries.Common.InsertQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.UpdateQuery),
				typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectByIdQuery) })
				queries[query] = (IQuery)Activator.CreateInstance(query, configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);
			return build(connections.Object, configuration, new UnitOfWork(connections.Object), new QueryFactory(list.Object));
		}

		private LmsCourseOriginsRepository Origins() => Repository((c, s, u, q) => new LmsCourseOriginsRepository(c, s, u, q));
		private LmsFlashcardsRepository Cards() => Repository((c, s, u, q) => new LmsFlashcardsRepository(c, s, u, q));

		private ServiceProvider Runner() => new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
		{
			if (IsPostgres) r.AddPostgres11_0().WithGlobalConnectionString(_connection).ScanIn(typeof(M0001_InitialMigrationPg).Assembly).For.All();
			else r.AddSqlServer().WithGlobalConnectionString(_connection).ScanIn(typeof(M0001_InitialMigration).Assembly).For.All();
		}).BuildServiceProvider(false);

		[OneTimeSetUp]
		public async Task Migrate_and_seed_an_isolated_database()
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

			await using var database = Connect(_connection);
			await database.OpenAsync();
			if (database is NpgsqlConnection postgres)
				await postgres.ReloadTypesAsync();

			foreach (var (id, name) in new[] { (UserA, "Ann.Member"), (UserB, "Ben.Member") })
				await InsertAsync(database, "AspNetUsers", new
				{
					Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@example.com", NormalizedEmail = (name + "@example.com").ToUpperInvariant(),
					EmailConfirmed = true, PhoneNumberConfirmed = false, TwoFactorEnabled = false, LockoutEnabled = false, AccessFailedCount = 0
				});
			await InsertAsync(database, "Departments", new { Name = "R6F One", Code = "R6FA", ManagingUserId = UserA, ShowWelcome = false });
			await InsertAsync(database, "Departments", new { Name = "R6F Two", Code = "R6FB", ManagingUserId = UserB, ShowWelcome = false });
			_department = await database.ExecuteScalarAsync<int>($"SELECT {T("DepartmentId")} FROM {T("Departments")} WHERE {T("Code")} = 'R6FA'");
			_otherDepartment = await database.ExecuteScalarAsync<int>($"SELECT {T("DepartmentId")} FROM {T("Departments")} WHERE {T("Code")} = 'R6FB'");
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

		private async Task InsertAsync(DbConnection database, string table, object values)
		{
			var row = values.GetType().GetProperties().ToDictionary(p => IsPostgres ? p.Name.ToLowerInvariant() : p.Name, p => p.GetValue(values), StringComparer.OrdinalIgnoreCase);
			var required = IsPostgres
				? await database.QueryAsync<(string Column, string Type)>(
					@"SELECT column_name, data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @Table AND is_nullable = 'NO' AND column_default IS NULL AND is_identity = 'NO'",
					new { Table = table.ToLowerInvariant() })
				: await database.QueryAsync<(string Column, string Type)>(
					@"SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @Table AND IS_NULLABLE = 'NO' AND COLUMN_DEFAULT IS NULL
					  AND COLUMNPROPERTY(OBJECT_ID(TABLE_SCHEMA + '.' + TABLE_NAME), COLUMN_NAME, 'IsIdentity') = 0",
					new { Table = table });

			var parameters = new DynamicParameters();
			var expressions = new List<(string Column, string Value)>();
			foreach (var (column, value) in row)
			{
				parameters.Add(column, value is DateTime d && IsPostgres ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified) : value);
				expressions.Add((column, "@" + column));
			}

			foreach (var (column, kind) in required.Where(r => !row.ContainsKey(r.Column)))
			{
				var placeholder = kind switch
				{
					"boolean" => "false",
					"bit" => "0",
					"smallint" or "integer" or "bigint" or "numeric" or "double precision" or "real" or "int" or "tinyint" or "decimal" or "float" or "money" => "0",
					"timestamp without time zone" or "timestamp with time zone" or "date" or "datetime" or "datetime2" or "datetimeoffset" or "smalldatetime" => IsPostgres ? "TIMESTAMP '2026-10-08'" : "'2026-10-08'",
					"json" or "jsonb" => "'{}'",
					"bytea" => "'\\x00'::bytea",
					"varbinary" or "binary" or "image" => "0x00",
					"uuid" => "'" + Guid.NewGuid() + "'",
					"uniqueidentifier" => "'" + Guid.NewGuid() + "'",
					_ => "'" + Guid.NewGuid().ToString("N").Substring(0, 12) + "'"
				};
				expressions.Add((column, placeholder));
			}

			var quote = IsPostgres ? (Func<string, string>)(c => "\"" + c + "\"") : c => "[" + c + "]";
			await database.ExecuteAsync(
				$"INSERT INTO {T(table)} ({string.Join(", ", expressions.Select(e => quote(e.Column)))}) VALUES ({string.Join(", ", expressions.Select(e => e.Value))})",
				parameters);
		}

		private async Task<int> ScalarAsync(string sql, object parameters = null)
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(sql, parameters);
		}

		private LmsFlashcard Card(int department, int course, int kind, int item, int version, string front, LmsFlashcardStatuses status = LmsFlashcardStatuses.Active) => new LmsFlashcard
		{
			DepartmentId = department, LmsCourseId = course, SourceKind = kind, SourceItemId = item, SourceVersionId = version, SourceVersionLabel = "1.0",
			SourceAnchor = "b-k1", SourceHeading = "Adult cardiac arrest", SourceTitle = "Cardiac arrest", Front = front, Back = "Back of " + front, Status = (int)status,
			SortOrder = 1, ExchangeId = Guid.NewGuid().ToString("D"), CreatedOn = DateTime.UtcNow, CreatedByUserId = UserA
		};

		[Test, Order(1)]
		public async Task Course_origins_are_one_per_course_and_a_department_list_leaves_the_citations_out()
		{
			var origins = Origins();
			await origins.InsertAsync(new LmsCourseOrigin
			{
				DepartmentId = _department, LmsCourseId = 501, LmsCourseVersionId = 601, OriginKind = (int)LmsCourseOriginKinds.Starter, SourceKey = "starter:resgrid-onboarding",
				SourceName = "Resgrid", SourceVersion = "1", License = "resgrid-starter", Attribution = "Based on the Resgrid starter course.", CreatedOn = DateTime.UtcNow, CreatedByUserId = UserA
			}, default, true);
			await origins.InsertAsync(new LmsCourseOrigin
			{
				DepartmentId = _department, LmsCourseId = 502, LmsCourseVersionId = 602, OriginKind = (int)LmsCourseOriginKinds.AiDraftFromProtocol,
				SourceKey = LmsCourseOriginKeys.Protocol(40, 401), SourceName = "Cardiac arrest", SourceVersion = "1.0", SourceUrl = "/User/Protocols/View/40?versionId=401",
				License = "department-own", ExchangeId = Guid.NewGuid().ToString("D"), CitationsJson = "[{\"LmsLessonId\":9,\"LessonTitle\":\"Rate\",\"Anchors\":[\"b-k1\"],\"Labels\":[\"Cardiac arrest 1.0\"]}]",
				CreatedOn = DateTime.UtcNow, CreatedByUserId = UserA
			}, default, true);

			var drafted = await origins.GetForCourseAsync(_department, 502);
			drafted.Should().NotBeNull();
			drafted.IsAiDraft.Should().BeTrue();
			LmsLessonCitation.Parse(drafted.CitationsJson).Single().Anchors.Should().Equal("b-k1");
			drafted.CreatedOn.Kind.Should().Be(DateTimeKind.Utc);
			(await origins.GetForCourseAsync(_otherDepartment, 502)).Should().BeNull("another department never reads it");

			var list = await origins.GetForDepartmentAsync(_department);
			list.Select(o => o.LmsCourseId).Should().Equal(501, 502);
			list.Should().OnlyContain(o => o.CitationsJson == null);

			Func<Task> twice = () => origins.InsertAsync(new LmsCourseOrigin { DepartmentId = _department, LmsCourseId = 501, LmsCourseVersionId = 603, CreatedOn = DateTime.UtcNow }, default, true);
			await twice.Should().ThrowAsync<Exception>("one origin per department and course");
		}

		[Test, Order(2)]
		public async Task The_supersede_update_flags_only_active_cards_of_that_source_drafted_from_another_version()
		{
			var cards = Cards();
			var protocolOld = await cards.InsertAsync(Card(_department, 502, (int)LmsFlashcardSourceKinds.ProtocolVersion, 40, 401, "Old version"), default, true);
			var protocolNew = await cards.InsertAsync(Card(_department, 502, (int)LmsFlashcardSourceKinds.ProtocolVersion, 40, 402, "New version"), default, true);
			var otherProtocol = await cards.InsertAsync(Card(_department, 502, (int)LmsFlashcardSourceKinds.ProtocolVersion, 41, 401, "Other protocol"), default, true);
			var courseCard = await cards.InsertAsync(Card(_department, 502, (int)LmsFlashcardSourceKinds.CourseVersion, 40, 401, "Course source"), default, true);
			var otherDepartment = await cards.InsertAsync(Card(_otherDepartment, 700, (int)LmsFlashcardSourceKinds.ProtocolVersion, 40, 401, "Other department"), default, true);
			var now = DateTime.UtcNow;

			(await cards.FlagOtherVersionsAsync(_department, (int)LmsFlashcardSourceKinds.ProtocolVersion, 40, 402, (int)LmsFlashcardFlagReasons.SourceSuperseded, now)).Should().Be(1);
			(await cards.FlagOtherVersionsAsync(_department, (int)LmsFlashcardSourceKinds.ProtocolVersion, 40, 402, (int)LmsFlashcardFlagReasons.SourceSuperseded, now)).Should().Be(0,
				"a flagged card keeps its first flag");

			var stored = await cards.GetForCourseAsync(_department, 502);
			stored.Should().HaveCount(4);
			var flagged = stored.Single(c => c.LmsFlashcardId == protocolOld.LmsFlashcardId);
			flagged.Status.Should().Be((int)LmsFlashcardStatuses.NeedsReview);
			flagged.FlagReason.Should().Be((int)LmsFlashcardFlagReasons.SourceSuperseded);
			flagged.FlaggedOn.Should().BeCloseTo(now, TimeSpan.FromSeconds(2));
			flagged.FlaggedOn.Value.Kind.Should().Be(DateTimeKind.Utc);
			stored.Where(c => c.LmsFlashcardId != protocolOld.LmsFlashcardId).Should().OnlyContain(c => c.Status == (int)LmsFlashcardStatuses.Active);
			(await cards.GetByIdForDepartmentAsync(_department, otherDepartment.LmsFlashcardId)).Should().BeNull();
			(await cards.GetByIdForDepartmentAsync(_otherDepartment, otherDepartment.LmsFlashcardId)).Status.Should().Be((int)LmsFlashcardStatuses.Active);

			(await cards.FlagCardsAsync(_department, new[] { courseCard.LmsFlashcardId, otherDepartment.LmsFlashcardId, protocolOld.LmsFlashcardId },
				(int)LmsFlashcardFlagReasons.SourceUnavailable, now)).Should().Be(1, "only the department's own active cards");
			(await cards.GetByIdForDepartmentAsync(_department, courseCard.LmsFlashcardId)).FlagReason.Should().Be((int)LmsFlashcardFlagReasons.SourceUnavailable);
			(await cards.FlagCardsAsync(_department, Array.Empty<int>(), 1, now)).Should().Be(0);

			// An author's review re-confirms a card (written through the shared update).
			flagged.Status = (int)LmsFlashcardStatuses.Active;
			flagged.FlagReason = 0;
			flagged.FlaggedOn = null;
			flagged.SourceVersionId = 402;
			flagged.ReviewedOn = now;
			flagged.ReviewedByUserId = UserB.ToUpperInvariant();
			await cards.UpdateAsync(flagged, default, true);
			var reviewed = await cards.GetByIdForDepartmentAsync(_department, protocolOld.LmsFlashcardId);
			reviewed.Status.Should().Be((int)LmsFlashcardStatuses.Active);
			reviewed.SourceVersionId.Should().Be(402);
			reviewed.FlaggedOn.Should().BeNull();
			_ = protocolNew;
			_ = otherProtocol;
		}

		[Test, Order(3)]
		public async Task The_department_cleanup_clears_the_round_6F_tables_of_that_department_only()
		{
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, _department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in new[] { "LmsCourseOrigins", "LmsFlashcards" })
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = @D", new { D = _department })).Should().Be(0, table);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsFlashcards")} WHERE {T("DepartmentId")} = @D", new { D = _otherDepartment })).Should().Be(1);
		}

		[Test, Order(4)]
		public async Task M0398_reruns_as_a_no_op_rolls_back_and_applies_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 398");
			var tables = IsPostgres
				? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('lmscourseorigins', 'lmsflashcards')"
				: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('LmsCourseOrigins', 'LmsFlashcards')";

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await ScalarAsync(tables)).Should().Be(2);
				(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsFlashcards")}")).Should().Be(1, "the second Up keeps the rows");

				migrations.MigrateDown(397);
				(await ScalarAsync(tables)).Should().Be(0);

				migrations.MigrateUp();
			}

			(await ScalarAsync(tables)).Should().Be(2);
		}
	}
}
