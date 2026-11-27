using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq;
using System.Threading;
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

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for the round 10 A3 follow-up (placeholder M0418 shared LMS set files, M0419 deployment partner acceptance) on both
	/// engines, over the full schema: the columns exist, the guarded migrations re-run as a no-op, roll back exactly their columns and apply again;
	/// the order's partner is written only at the version read and read back through the metadata select; the fill's partner columns are written
	/// on their own (never the fill's status) and list for the partner; shared media and packages keep the department whose objects they name.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class ContentSetFollowUpDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "sets_followup_";

		private DatabaseTypes _previousType;
		private string _previousConnection, _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private TRepository Repository<TRepository>(Func<IConnectionProvider, SqlConfiguration, IUnitOfWork, IQueryFactory, TRepository> build)
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			foreach (var query in new[] { typeof(Resgrid.Repositories.DataRepository.Queries.Common.InsertQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.UpdateQuery),
				typeof(Resgrid.Repositories.DataRepository.Queries.Common.DeleteQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.DeleteMultipleQuery),
				typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectAllQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectByIdQuery),
				typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectByDepartmentIdQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectByUserIdQuery) })
				queries[query] = (IQuery)Activator.CreateInstance(query, configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);
			return build(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		private ServiceProvider Runner() => new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
		{
			if (IsPostgres) r.AddPostgres11_0().WithGlobalConnectionString(_connection).ScanIn(typeof(M0001_InitialMigrationPg).Assembly).For.All();
			else r.AddSqlServer().WithGlobalConnectionString(_connection).ScanIn(typeof(M0001_InitialMigration).Assembly).For.All();
		}).BuildServiceProvider(false);

		private void Migrate(Action<IMigrationRunner> run)
		{
			using var runner = Runner();
			using var scope = runner.CreateScope();
			run(scope.ServiceProvider.GetRequiredService<IMigrationRunner>());
		}

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

			Migrate(r => r.MigrateUp());
			await ReloadAsync();
		}

		private async Task ReloadAsync()
		{
			await using var database = Connect(_connection);
			await database.OpenAsync();
			if (database is NpgsqlConnection postgres)
				await postgres.ReloadTypesAsync();
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

		private static readonly (string Table, string[] Columns)[] Columns =
		{
			("LmsMediaAssets", new[] { "SharedFromDepartmentId" }),
			("LmsContentPackages", new[] { "SharedFromDepartmentId" }),
			("RmsExternalOrders", new[] { "PartnerDepartmentLinkId", "PartnerDepartmentId" }),
			("RmsExternalOrderFills", new[] { "PartnerDepartmentId", "PartnerSignStatus", "PartnerSignRequestId", "PartnerSignRequestedOn", "PartnerSignedOn", "PartnerSignedByUserId" })
		};

		private async Task<int> ColumnCountAsync()
		{
			await using var database = Connect(_connection);
			var count = 0;
			foreach (var (table, names) in Columns)
				count += await database.ExecuteScalarAsync<int>(IsPostgres
					? "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = @Table AND column_name = ANY(@Names)"
					: "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(@Table) AND name IN @Names",
					new { Table = IsPostgres ? table.ToLowerInvariant() : "dbo." + table, Names = IsPostgres ? names.Select(n => n.ToLowerInvariant()).ToArray() : names });
			return count;
		}

		private static int Expected => Columns.Sum(c => c.Columns.Length);

		[Test, Order(1)]
		public async Task Up_adds_the_columns_re_runs_as_a_no_op_rolls_back_exactly_them_and_applies_again()
		{
			(await ColumnCountAsync()).Should().Be(Expected);

			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} IN (418, 419)");
			Migrate(r => r.MigrateUp());
			(await ColumnCountAsync()).Should().Be(Expected);

			Migrate(r => r.MigrateDown(417));
			(await ColumnCountAsync()).Should().Be(0);
			Migrate(r => r.MigrateUp());
			(await ColumnCountAsync()).Should().Be(Expected);
			await ReloadAsync();
		}

		[Test, Order(2)]
		public async Task Deployment_partner_columns_write_only_through_their_own_updates_and_list_for_the_partner()
		{
			var orders = Repository((c, s, u, q) => new RmsExternalOrdersRepository(c, s, u, q));
			var fills = Repository((c, s, u, q) => new RmsExternalOrderFillsRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var order = await orders.InsertAsync(new RmsExternalOrder
			{
				RmsExternalOrderId = Guid.NewGuid().ToString(), DepartmentId = 70, ProtectionId = Guid.NewGuid().ToString(), RecordId = Guid.NewGuid().ToString(), ProfileKey = "generic",
				ProfileVersion = 1, OrderNumber = "CA-1", IncidentName = "Ridge", Status = (int)RmsExternalOrderStatus.Open, CreatedOn = now, ModifiedOn = now, RowVersion = 1
			}, CancellationToken.None, true);
			var fill = await fills.InsertAsync(new RmsExternalOrderFill
			{
				RmsExternalOrderFillId = Guid.NewGuid().ToString(), DepartmentId = 70, ProtectionId = Guid.NewGuid().ToString(), RmsExternalOrderId = order.RmsExternalOrderId,
				RecordId = order.RecordId, RequestNumber = "O-1", Status = (int)RmsDeploymentFillStatus.Accepted, CreatedOn = now, ModifiedOn = now, RowVersion = 1
			}, CancellationToken.None, true);

			(await orders.TrySetPartnerAsync(70, order.RmsExternalOrderId, 5, 71, "officer", 2)).Should().BeFalse("a stale version is refused");
			(await orders.TrySetPartnerAsync(70, order.RmsExternalOrderId, 5, 71, "officer", 1)).Should().BeTrue();
			var read = await orders.GetByIdForDepartmentAsync(70, order.RmsExternalOrderId, false);
			read.PartnerDepartmentLinkId.Should().Be(5);
			read.PartnerDepartmentId.Should().Be(71);
			read.RowVersion.Should().Be(2);
			(await orders.TrySetPartnerAsync(71, order.RmsExternalOrderId, null, null, "outsider", 2)).Should().BeFalse("another department's order is never written");

			fill.PartnerDepartmentId = 71;
			fill.PartnerSignStatus = (int)RmsDeploymentPartnerSignStatuses.Requested;
			fill.PartnerSignRequestId = 88;
			fill.PartnerSignRequestedOn = now;
			fill.Status = (int)RmsDeploymentFillStatus.Returned; // the partner update never writes the fill's own state
			(await fills.UpdatePartnerSignAsync(fill)).Should().BeTrue();
			var stored = await fills.GetByIdForDepartmentAsync(70, fill.RmsExternalOrderFillId);
			stored.Status.Should().Be((int)RmsDeploymentFillStatus.Accepted);
			stored.PartnerSignStatus.Should().Be((int)RmsDeploymentPartnerSignStatuses.Requested);
			stored.PartnerSignRequestId.Should().Be(88);

			(await fills.GetPartnerSignRequestedAsync(71)).Should().ContainSingle(f => f.RmsExternalOrderFillId == fill.RmsExternalOrderFillId);
			(await fills.GetPartnerSignRequestedAsync(70)).Should().BeEmpty();
			(await fills.GetAllPartnerSignRequestedAsync(10)).Should().ContainSingle();

			stored.PartnerSignStatus = (int)RmsDeploymentPartnerSignStatuses.Signed;
			stored.PartnerSignedOn = now;
			stored.PartnerSignedByUserId = "partner-admin";
			await fills.UpdatePartnerSignAsync(stored);
			(await fills.GetAllPartnerSignRequestedAsync(10)).Should().BeEmpty();
			(await fills.GetByIdForDepartmentAsync(70, fill.RmsExternalOrderFillId)).PartnerSignedByUserId.Should().Be("partner-admin");
		}

		[Test, Order(3)]
		public async Task A_shared_set_video_and_package_keep_the_department_whose_objects_they_name()
		{
			var media = Repository((c, s, u, q) => new LmsMediaAssetsRepository(c, s, u, q));
			var packages = Repository((c, s, u, q) => new LmsContentPackagesRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var asset = await media.InsertAsync(new LmsMediaAsset
			{
				DepartmentId = 80, SharedFromDepartmentId = 81, Kind = 0, FileName = "drill.mp4", ContentType = "video/mp4", SourceKey = "lms/81/1/source.mp4", HlsPrefix = "lms/81/1/hls/",
				Status = (int)LmsMediaStatuses.Ready, CreatedOn = now, CreatedByUserId = "admin", ReadyOn = now
			}, CancellationToken.None, true);
			var package = await packages.InsertAsync(new LmsContentPackage
			{
				DepartmentId = 80, SharedFromDepartmentId = 81, Standard = (int)LmsContentStandards.Cmi5, Title = "Module", FileName = "m.zip", Sha256 = "abc", SourceKey = "lms/81/shared/p/source.zip",
				ContentPrefix = "lms/81/shared/p/content/", Status = (int)LmsContentPackageStatuses.Ready, CreatedOn = now, CreatedByUserId = "admin", ReadyOn = now
			}, CancellationToken.None, true);

			(await media.GetByIdForDepartmentAsync(80, asset.LmsMediaAssetId)).SharedFromDepartmentId.Should().Be(81);
			(await packages.GetByIdForDepartmentAsync(80, package.LmsContentPackageId)).SharedFromDepartmentId.Should().Be(81);
			(await media.GetByIdForDepartmentAsync(81, asset.LmsMediaAssetId)).Should().BeNull("the row is the subscriber's own");
		}
	}
}
