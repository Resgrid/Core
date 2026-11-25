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
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
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
	/// Real-database proof for K1 on both engines: M0368-M0370 in the full chain, Up again as a no-op, Down and Up; the chunk store with
	/// its float32 vector bytes and the revision a write bumps; the reindex queue (idempotent, a re-request survives the processed delete,
	/// due departments only and never a sweep); the gaps report; the AiGenerations exchange log; the header reads (no document bytes); and
	/// the department cleanup. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class KnowledgeDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "knowledge_k1_";
		private const string UserA = "4f1d0e1c-1a2b-4c3d-8e9f-000000000001";
		private const string UserB = "4f1d0e1c-1a2b-4c3d-8e9f-000000000002";

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
			// A real unit of work: the chunk replace and the revision bump share one transaction.
			return build(connections.Object, configuration, new UnitOfWork(connections.Object), new QueryFactory(list.Object));
		}

		private KnowledgeIndexRepository Index() => Repository((c, s, u, q) => new KnowledgeIndexRepository(c, s, u, q));

		private KnowledgeGapsRepository Gaps() => Repository((c, s, u, q) => new KnowledgeGapsRepository(c, s, u, q));

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

			foreach (var (id, name) in new[] { (UserA, "Kay.Member"), (UserB, "Lee.Member") })
				await InsertAsync(database, "AspNetUsers", new
				{
					Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@example.com", NormalizedEmail = (name + "@example.com").ToUpperInvariant(),
					EmailConfirmed = true, PhoneNumberConfirmed = false, TwoFactorEnabled = false, LockoutEnabled = false, AccessFailedCount = 0
				});
			await InsertAsync(database, "Departments", new { Name = "K1 One", Code = "K1A", ManagingUserId = UserA, ShowWelcome = false });
			await InsertAsync(database, "Departments", new { Name = "K1 Two", Code = "K1B", ManagingUserId = UserB, ShowWelcome = false });
			_department = await database.ExecuteScalarAsync<int>($"SELECT {T("DepartmentId")} FROM {T("Departments")} WHERE {T("Code")} = 'K1A'");
			_otherDepartment = await database.ExecuteScalarAsync<int>($"SELECT {T("DepartmentId")} FROM {T("Departments")} WHERE {T("Code")} = 'K1B'");
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
				parameters.Add(column, value);
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

		private KnowledgeChunk Chunk(int departmentId, string itemId, int ordinal, string text, float[] vector) => new KnowledgeChunk
		{
			DepartmentId = departmentId, SourceKind = (int)KnowledgeSourceKinds.Protocol, ItemId = itemId, VersionId = "51", VersionLabel = "2.1", Ordinal = ordinal,
			Anchor = "p-section-" + ordinal, HeadingPath = "Section " + ordinal, Title = "SOG " + itemId, Text = text, TextHash = KnowledgeChunker.Hash(text),
			EffectiveOn = new DateTime(2026, 1, 5, 8, 30, 0, DateTimeKind.Utc), AudienceJson = "[{\"Kind\":1,\"RefId\":\"3\"}]",
			AccessFlags = (int)KnowledgeAccessFlags.DispatchersRead, Embedding = KnowledgeVectorCodec.Encode(vector), EmbeddingModel = vector == null ? null : "BAAI/bge-m3",
			EmbeddedOn = vector == null ? null : DateTime.UtcNow, IndexedOn = DateTime.UtcNow
		};

		[Test, Order(1)]
		public async Task Chunks_round_trip_with_their_vector_bytes_and_every_write_bumps_the_revision()
		{
			var index = Index();
			var vector = new[] { 0.5f, -1.25f, 3.0e-8f, 1024f };

			await index.ReplaceItemChunksAsync(_department, 0, "1", new[] { Chunk(_department, "1", 0, "Stage uphill.", vector), Chunk(_department, "1", 1, "Wear gloves.", null) });

			var corpus = await index.GetCorpusAsync(_department);
			corpus.Should().HaveCount(2);
			corpus[0].Should().BeEquivalentTo(new { Anchor = "p-section-0", VersionLabel = "2.1", AccessFlags = (int)KnowledgeAccessFlags.DispatchersRead, AudienceJson = "[{\"Kind\":1,\"RefId\":\"3\"}]" },
				o => o.ExcludingMissingMembers());
			KnowledgeVectorCodec.Decode(corpus[0].Embedding).Should().Equal(vector);
			corpus[0].EffectiveOn.Should().Be(new DateTime(2026, 1, 5, 8, 30, 0, DateTimeKind.Utc));
			corpus[0].EffectiveOn.Value.Kind.Should().Be(DateTimeKind.Utc);
			corpus[1].Embedding.Should().BeNull();
			var state = await index.GetStateAsync(_department);
			(state.Revision, state.ChunkCount, state.EmbeddedCount).Should().Be((1L, 2, 1));

			await index.ReplaceItemChunksAsync(_department, 0, "1", new[] { Chunk(_department, "1", 0, "Stage uphill and upwind.", null) });
			(await index.GetItemChunksAsync(_department, 0, "1")).Select(c => c.Text).Should().Equal("Stage uphill and upwind.");
			(await index.GetRevisionsAsync(new[] { _department, _otherDepartment }))[_department].Should().Be(2);
			(await index.GetRevisionsAsync(new[] { _otherDepartment }))[_otherDepartment].Should().Be(0);

			// Embeddings are stored only on the chunk whose text they were made from.
			var pending = await index.GetChunksToEmbedAsync(_department, "BAAI/bge-m3", 10);
			pending.Should().ContainSingle();
			var stale = pending.Single();
			stale.Embedding = KnowledgeVectorCodec.Encode(new[] { 1f });
			stale.EmbeddingModel = "BAAI/bge-m3";
			var wrongHash = new KnowledgeChunk { KnowledgeChunkId = stale.KnowledgeChunkId, DepartmentId = _department, TextHash = "other", Embedding = stale.Embedding, EmbeddingModel = "BAAI/bge-m3" };
			(await index.SaveEmbeddingsAsync(_department, new[] { wrongHash })).Should().Be(0);
			(await index.SaveEmbeddingsAsync(_department, new[] { stale })).Should().Be(1);
			(await index.GetChunksToEmbedAsync(_department, "BAAI/bge-m3", 10)).Should().BeEmpty();
			(await index.GetChunksToEmbedAsync(_department, "another/model", 10)).Should().ContainSingle();

			(await index.GetIndexedItemsAsync(_department)).Should().ContainSingle(i => i.ItemId == "1" && i.SourceKind == 0);
			(await index.DeleteItemAsync(_department, 0, "1")).Should().Be(1);
			(await index.DeleteItemAsync(_department, 0, "1")).Should().Be(0);
			(await index.GetStateAsync(_department)).Should().BeEquivalentTo(new { Revision = 4L, ChunkCount = 0, EmbeddedCount = 0 }, o => o.ExcludingMissingMembers());
		}

		[Test, Order(2)]
		public async Task The_queue_is_idempotent_a_re_request_survives_the_processed_delete_and_only_due_departments_come_back()
		{
			var index = Index();
			var now = DateTime.UtcNow;
			await index.EnqueueAsync(_department, 0, "7", now.AddMinutes(-5));
			await index.EnqueueAsync(_department, 0, "7", now.AddMinutes(-4));
			var requests = await index.GetRequestsAsync(_department, now, 10);
			requests.Should().ContainSingle();

			// Asked for again while it was being processed: the processed delete leaves it queued.
			await index.EnqueueAsync(_department, 0, "7", now.AddMinutes(-1));
			await index.DeleteRequestsAsync(requests);
			(await index.GetRequestsAsync(_department, now, 10)).Should().ContainSingle();
			await index.DeleteRequestsAsync(await index.GetRequestsAsync(_department, now, 10));
			(await index.GetRequestsAsync(_department, now, 10)).Should().BeEmpty();

			// A failed request waits out its backoff.
			await index.EnqueueAsync(_department, KnowledgeIndexRequest.DepartmentRebuild, "", now.AddMinutes(-1));
			var rebuild = (await index.GetRequestsAsync(_department, now, 10)).Single();
			await index.PostponeRequestAsync(rebuild, now.AddMinutes(30));
			(await index.GetDueDepartmentsAsync(now, 50)).Should().NotContain(_department);
			(await index.GetDueDepartmentsAsync(now.AddMinutes(31), 50)).Should().Contain(_department);

			// An active index whose repair is due is due; an inactive one and a department with nothing to do never come back.
			await index.SaveStateAsync(new KnowledgeIndexState { DepartmentId = _otherDepartment, Active = true, NextRepairOn = now.AddMinutes(-2) });
			(await index.GetDueDepartmentsAsync(now, 50)).Should().Equal(_otherDepartment);
			await index.SaveStateAsync(new KnowledgeIndexState { DepartmentId = _otherDepartment, Active = false, NextRepairOn = now.AddMinutes(-2) });
			(await index.GetDueDepartmentsAsync(now, 50)).Should().BeEmpty();

			await index.DeleteDepartmentRequestsAsync(_department);
			(await index.GetDueDepartmentsAsync(now.AddHours(1), 50)).Should().BeEmpty();
		}

		[Test, Order(3)]
		public async Task Gaps_and_the_exchange_log_round_trip_in_their_department()
		{
			var gaps = Gaps();
			var gap = await gaps.InsertAsync(new KnowledgeGap
			{
				DepartmentId = _department, UserId = UserA, AskerRole = "Paramedic", Question = "How do we handle lithium battery fires?", Reason = (int)KnowledgeGapReasons.NotCovered,
				Surface = (int)KnowledgeAskSurfaces.Library, AskedOn = DateTime.UtcNow, NearestSourcesJson = "[{\"SourceKind\":0,\"ItemId\":\"4\",\"Title\":\"SOG 4\"}]",
				AiGenerationId = "0b7b2c8e-1d6a-4b0e-9a55-3c1f2d4e5f60"
			});
			gap.KnowledgeGapId.Should().BeGreaterThan(0);
			(await gaps.CountAsync(_department, (int)KnowledgeGapStatuses.Open)).Should().Be(1);
			(await gaps.CountAsync(_otherDepartment, null)).Should().Be(0);
			(await gaps.GetByExchangeAsync(_department, "0b7b2c8e-1d6a-4b0e-9a55-3c1f2d4e5f60")).KnowledgeGapId.Should().Be(gap.KnowledgeGapId);
			(await gaps.GetByIdAsync(_otherDepartment, gap.KnowledgeGapId)).Should().BeNull();

			gap.Status = (int)KnowledgeGapStatuses.Resolved;
			gap.ResolvedOn = DateTime.UtcNow;
			gap.ResolvedByUserId = UserA;
			gap.ResolutionNote = "SOG 12 rev 3";
			(await gaps.UpdateStatusAsync(gap)).Should().BeTrue();
			var read = (await gaps.GetForDepartmentAsync(_department, (int)KnowledgeGapStatuses.Resolved, 0, 10)).Single();
			read.ResolutionNote.Should().Be("SOG 12 rev 3");
			read.NearestSources.Single().Title.Should().Be("SOG 4");
			(await gaps.GetForDepartmentAsync(_department, (int)KnowledgeGapStatuses.Open, 0, 10)).Should().BeEmpty();

			var id = Guid.NewGuid().ToString("D");
			await gaps.InsertGenerationAsync(new AiGenerationRow
			{
				Id = id, ConversationId = id, DepartmentId = _department, UserId = UserA, Revision = 1, CreatedOnUtc = DateTime.UtcNow, Content = "enc2:sealed",
				PromptVersion = "knowledge-answer-v1", ModelRevision = new string('a', 40), RuntimeDigest = "sha256:" + new string('b', 64), RequestDigest = new string('c', 64),
				InputTokens = 1200, OutputTokens = 80, Outcome = KnowledgeOutcomes.Answered
			});
			(await gaps.GetGenerationAsync(_department, UserA, id)).Outcome.Should().Be(KnowledgeOutcomes.Answered);
			(await gaps.GetGenerationAsync(_department, UserB, id)).Should().BeNull("only the asker's own exchange");
			Func<Task> plaintext = () => gaps.InsertGenerationAsync(new AiGenerationRow
			{
				Id = Guid.NewGuid().ToString("D"), ConversationId = Guid.NewGuid().ToString("D"), DepartmentId = _department, UserId = UserA, Content = "the question in clear", Outcome = "Answered"
			});
			await plaintext.Should().ThrowAsync<ArgumentException>();
		}

		[Test, Order(4)]
		public async Task Protocol_and_document_headers_are_read_without_content_or_bytes()
		{
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await InsertAsync(database, "DispatchProtocols", new
				{
					DepartmentId = _department, Name = "Hazmat", Code = "HZ1", IsDisabled = false, IsDeleted = false, Description = "d", ProtocolText = "<p>t</p>", CreatedOn = DateTime.UtcNow,
					CreatedByUserId = UserA, MinimumWeight = 0, Kind = (int)ProtocolKinds.Sog, Status = (int)ProtocolStatuses.Published, ExcludeFromAi = true, IsLicensedContent = false
				});
				await InsertAsync(database, "Documents", new
				{
					DepartmentId = _department, Name = "Hose testing", Category = "Ops", Description = "Annual hose test.", AdminsOnly = true, Type = "application/pdf", Filename = "hose.pdf",
					Data = new byte[] { 1, 2, 3 }, UserId = UserA, AddedOn = DateTime.UtcNow, IsProtected = false
				});
			}

			var index = Index();
			var protocol = (await index.GetProtocolHeadersAsync(_department)).Single();
			protocol.Should().BeEquivalentTo(new { Name = "Hazmat", Code = "HZ1", ExcludeFromAi = true, Kind = (int)ProtocolKinds.Sog, Status = (int)ProtocolStatuses.Published }, o => o.ExcludingMissingMembers());
			protocol.ProtocolText.Should().BeNull("the header read never loads content");
			(await index.GetProtocolHeadersAsync(_otherDepartment)).Should().BeEmpty();
			var document = (await index.GetDocumentHeadersAsync(_department)).Single();
			document.Should().BeEquivalentTo(new { Name = "Hose testing", AdminsOnly = true, Filename = "hose.pdf" }, o => o.ExcludingMissingMembers());
		}

		[Test, Order(5)]
		public async Task The_department_cleanup_clears_its_knowledge_rows_and_keeps_other_departments_and_the_system_corpus()
		{
			var index = Index();
			await index.ReplaceItemChunksAsync(_department, 0, "9", new[] { Chunk(_department, "9", 0, "Mine.", new[] { 1f }) });
			await index.ReplaceItemChunksAsync(_otherDepartment, 0, "9", new[] { Chunk(_otherDepartment, "9", 0, "Theirs.", null) });
			await index.ReplaceItemChunksAsync(0, (int)KnowledgeSourceKinds.Academy, "05-calls", new[]
			{
				new KnowledgeChunk { DepartmentId = 0, SourceKind = (int)KnowledgeSourceKinds.Academy, ItemId = "05-calls", Title = "Calls", Text = "Calls.", TextHash = "h", IndexedOn = DateTime.UtcNow }
			});
			await index.EnqueueAsync(_department, 0, "9", DateTime.UtcNow);
			await Gaps().InsertAsync(new KnowledgeGap { DepartmentId = _department, Reason = 0, AskedOn = DateTime.UtcNow });

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, _department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in new[] { "KnowledgeChunks", "KnowledgeIndexRequests", "KnowledgeIndexStates", "KnowledgeGaps" })
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = @D", new { D = _department })).Should().Be(0, table);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("KnowledgeChunks")} WHERE {T("DepartmentId")} = @D", new { D = _otherDepartment })).Should().Be(1);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("KnowledgeChunks")} WHERE {T("DepartmentId")} = 0")).Should().Be(1, "the Academy corpus belongs to no department");
		}

		[Test, Order(6)]
		public async Task M0368_to_M0370_rerun_as_no_ops_roll_back_and_apply_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 368");
			var tables = IsPostgres
				? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('knowledgechunks', 'knowledgeindexrequests', 'knowledgeindexstates', 'knowledgegaps')"
				: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('KnowledgeChunks', 'KnowledgeIndexRequests', 'KnowledgeIndexStates', 'KnowledgeGaps')";
			var column = IsPostgres
				? "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'trainings' AND column_name = 'excludefromai'"
				: "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Trainings') AND name = 'ExcludeFromAi'";

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				// Up a second time over the existing schema: every guard holds and the rows stay.
				migrations.MigrateUp();
				(await ScalarAsync(tables)).Should().Be(4);
				(await ScalarAsync(column)).Should().Be(1);
				(await ScalarAsync($"SELECT COUNT(*) FROM {T("KnowledgeChunks")} WHERE {T("DepartmentId")} = @D", new { D = _otherDepartment })).Should().Be(1);

				migrations.MigrateDown(367);
				(await ScalarAsync(tables)).Should().Be(0);
				(await ScalarAsync(column)).Should().Be(0);

				migrations.MigrateUp();
			}

			(await ScalarAsync(tables)).Should().Be(4);
			(await ScalarAsync(column)).Should().Be(1);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("Trainings")} WHERE {T("ExcludeFromAi")} = {(IsPostgres ? "true" : "1")}")).Should().Be(0, "off by default");
		}
	}
}
