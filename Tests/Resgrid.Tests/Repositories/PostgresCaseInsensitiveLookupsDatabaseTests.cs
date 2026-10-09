using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Queries;
using Resgrid.Repositories.DataRepository.Queries.CommunicationTests;
using Resgrid.Repositories.DataRepository.Queries.DepartmentGroups;
using Resgrid.Repositories.DataRepository.Queries.Departments;
using Resgrid.Repositories.DataRepository.Queries.DepartmentSettings;
using Resgrid.Repositories.DataRepository.Queries.DistributionLists;
using Resgrid.Repositories.DataRepository.Queries.Identity.Role;
using Resgrid.Repositories.DataRepository.Queries.Identity.User;
using Resgrid.Repositories.DataRepository.Queries.Invites;
using Resgrid.Repositories.DataRepository.Queries.PersonnelRoles;
using Resgrid.Repositories.DataRepository.Queries.Units;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// PostgreSQL proof that lookups by person-entered text (user names, e-mail addresses, inbound e-mail codes, link, run and
	/// linking codes, names, certification codes, hydrant numbers) match regardless of case, as SQL Server's collation
	/// matches them. Npgsql binds a C# string as text, and citext = text resolves to text = text: case-sensitive, and unable
	/// to use the column's citext index. Each lookup casts its parameter to citext (invite codes are uuid and need none). The
	/// whole PostgreSQL migration chain runs into an isolated database so every column has its shipped type. Set
	/// RESGRID_ADP_POSTGRES_TEST_CONNECTION (a server-level connection) to run.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class PostgresCaseInsensitiveLookupsDatabaseTests
	{
		private const string Prefix = "citext_lookups_";
		private const int DepartmentId = 900;
		private const string UserId = "7d1f3c2e-9a40-4b8e-a1a2-4c5d6e7f8a90";
		private const string RoleId = "0b6c4d2a-1e3f-4a5b-8c7d-9e0f1a2b3c4d";

		private DatabaseTypes _previousType;
		private string _previousConnection, _master, _connection, _database;
		private PostgreSqlConfiguration _sql;

		private NpgsqlConnection Connect() => new NpgsqlConnection(_connection);

		[OneTimeSetUp]
		public async Task Migrate_and_seed_an_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable("RESGRID_ADP_POSTGRES_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a PostgreSQL test connection to run real database checks.");

			AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
			_previousType = DataConfig.DatabaseType;
			_previousConnection = DataConfig.CoreConnectionString;
			DataConfig.DatabaseType = DatabaseTypes.Postgres;
			_sql = new PostgreSqlConfiguration();

			_database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = new NpgsqlConnection(_master))
				await master.ExecuteAsync("CREATE DATABASE " + _database);
			_connection = new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString;
			DataConfig.CoreConnectionString = _connection;

			// The production runner's dialect (Workers.Console and Resgrid.Console): later migrations create INCLUDE indexes.
			using (var services = new ServiceCollection().AddFluentMigratorCore()
				.ConfigureRunner(r => r.AddPostgres11_0().WithGlobalConnectionString(_connection).ScanIn(typeof(M0001_InitialMigrationPg).Assembly).For.All())
				.BuildServiceProvider())
				services.GetRequiredService<IMigrationRunner>().MigrateUp();

			// The pool loaded its types before M0001 created citext; without a reload a citext column cannot be read back.
			await using (var database = Connect())
			{
				await database.OpenAsync();
				await database.ReloadTypesAsync();
				await SeedAsync(database);
			}
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			if (_database == null) return;
			DataConfig.DatabaseType = _previousType;
			DataConfig.CoreConnectionString = _previousConnection;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			NpgsqlConnection.ClearAllPools();
			await using var master = new NpgsqlConnection(_master);
			await master.ExecuteAsync("DROP DATABASE " + _database + " WITH (FORCE)");
		}

		/// <summary>Inserts a row with the given columns and a placeholder in every other required column that has no default.</summary>
		private static async Task InsertAsync(DbConnection database, string table, object values)
		{
			var row = values.GetType().GetProperties().ToDictionary(p => p.Name.ToLowerInvariant(), p => p.GetValue(values));
			var required = await database.QueryAsync<(string Column, string Type)>(
				@"SELECT column_name, data_type FROM information_schema.columns
				  WHERE table_schema = 'public' AND table_name = @Table AND is_nullable = 'NO' AND column_default IS NULL AND is_identity = 'NO'",
				new { Table = table });

			var parameters = new DynamicParameters();
			var expressions = new List<(string Column, string Value)>();
			foreach (var (column, value) in row)
			{
				parameters.Add(column, value);
				expressions.Add((column, "@" + column));
			}
			foreach (var (column, type) in required.Where(r => !row.ContainsKey(r.Column)))
			{
				var placeholder = type switch
				{
					"boolean" => "false",
					"smallint" or "integer" or "bigint" or "numeric" or "double precision" or "real" => "0",
					"timestamp without time zone" or "timestamp with time zone" or "date" => "TIMESTAMP '2026-10-08'",
					"json" or "jsonb" => "'{}'",
					"bytea" => "''::bytea",
					"uuid" => "'" + Guid.NewGuid() + "'",
					_ => "'" + Guid.NewGuid().ToString("N") + "'"
				};
				expressions.Add((column, placeholder));
			}

			await database.ExecuteAsync(
				$"INSERT INTO {table} ({string.Join(", ", expressions.Select(e => "\"" + e.Column + "\""))}) VALUES ({string.Join(", ", expressions.Select(e => e.Value))})",
				parameters);
		}

		private static async Task SeedAsync(DbConnection database)
		{
			// Stored as people type them; every lookup below asks in a different case. normalizedusername is left in mixed case
			// the way M0001 seeds its own accounts ("TestAccount1"), so only a case-insensitive match finds it.
			await InsertAsync(database, "aspnetusers", new
			{
				Id = UserId, UserName = "Jane.Doe", NormalizedUserName = "Jane.Doe", Email = "Jane.Doe@Example.com",
				NormalizedEmail = "JANE.DOE@EXAMPLE.COM", EmailConfirmed = true, PhoneNumberConfirmed = false, TwoFactorEnabled = false,
				LockoutEnabled = false, AccessFailedCount = 0
			});
			await InsertAsync(database, "departments", new { DepartmentId, Name = "Station Nine Fire Rescue", Code = "NINE", ManagingUserId = UserId, ShowWelcome = false, LinkCode = "AbC9x7" });
			await InsertAsync(database, "departmentmembers", new { DepartmentId, UserId, IsAdmin = true, IsDisabled = false, IsHidden = false, IsDefault = true, IsActive = true, IsDeleted = false });
			await InsertAsync(database, "aspnetroles", new { Id = RoleId, Name = "Dispatchers", NormalizedName = "Dispatchers" });
			await InsertAsync(database, "aspnetuserroles", new { UserId, RoleId });
			await InsertAsync(database, "invites", new { DepartmentId, Code = Guid.NewGuid(), EmailAddress = "New.Member@Example.com", SendingUserId = UserId });
			await InsertAsync(database, "personnelroles", new { DepartmentId, Name = "Engineer" });
			await InsertAsync(database, "unittypes", new { DepartmentId, Type = "Engine" });
			await InsertAsync(database, "units", new { DepartmentId, Name = "Engine 9", Type = "Engine", IsDeleted = false });
			await InsertAsync(database, "distributionlists", new { DepartmentId, Name = "All Hands", EmailAddress = "AllHands9" });
			await InsertAsync(database, "departmentgroups", new { DepartmentId, Name = "Station 9", DispatchEmail = "St9Dispatch", MessageEmail = "St9Message" });
			await InsertAsync(database, "departmentsettings", new { DepartmentId, SettingType = (int)DepartmentSettingTypes.InternalDispatchEmail, Setting = "Ab12Cd" });

			var testId = Guid.NewGuid().ToString();
			var runId = Guid.NewGuid().ToString();
			await InsertAsync(database, "communicationtests", new { CommunicationTestId = testId, DepartmentId });
			await InsertAsync(database, "communicationtestruns", new { CommunicationTestRunId = runId, CommunicationTestId = testId, DepartmentId, RunCode = "RunXy9" });
			await InsertAsync(database, "communicationtestresults", new { CommunicationTestResultId = Guid.NewGuid().ToString(), CommunicationTestRunId = runId, DepartmentId, UserId, ResponseToken = "TokQ7z" });

			await InsertAsync(database, "chatbotlinkingcodes", new { Id = Guid.NewGuid().ToString(), Code = "LinkZ1", UserId, ExpiresAt = DateTime.UtcNow.AddHours(1) });
			await InsertAsync(database, "departmentcertificationtypes", new { DepartmentId, Type = "EMT Basic", Code = "EMT-B", IsDeleted = false, IsActive = true });
			await InsertAsync(database, "rmshydrants", new { RmsHydrantId = Guid.NewGuid().ToString(), DepartmentId, HydrantNumber = "H-12a" });
		}

		public static IEnumerable<TestCaseData> TemplateLookups()
		{
			TestCaseData Case(string name, Func<PostgreSqlConfiguration, string> query, object parameters) =>
				new TestCaseData(query, parameters).SetName("Template lookup ignores case: " + name);

			yield return Case("department by inbound dispatch e-mail setting", c => new SelectBySettingAndTypeQuery(c).GetQuery(),
				new { Setting = "AB12CD", SettingType = (int)DepartmentSettingTypes.InternalDispatchEmail });
			yield return Case("department manager by e-mail", c => new SelectManagerInfoByEmailQuery(c).GetQuery(), new { EmailAddress = "JANE.DOE@example.COM" });
			yield return Case("invite by e-mail", c => new SelectInviteByEmailQuery(c).GetQuery(), new { EmailAddress = "new.member@example.com" });
			yield return Case("department by link code", c => new SelectDepartmentByLinkCodeQuery(c).GetQuery(), new { Code = "abc9x7" });
			yield return Case("department by name", c => new SelectDepartmentByNameQuery(c).GetQuery(), new { Name = "station nine fire rescue" });
			yield return Case("department by user name", c => new SelectDepartmentByUsernameQuery(c).GetQuery(), new { Username = "JANE.DOE" });
			yield return Case("valid department by user name", c => new SelectValidDepartmentByUsernameQuery(c).GetQuery(), new { Username = "JANE.DOE" });
			yield return Case("personnel role by name", c => new SelectRoleByDidAndNameQuery(c).GetQuery(), new { DepartmentId, Name = "ENGINEER" });
			yield return Case("identity role by normalized name", c => new SelectRoleByNameQuery(c).GetQuery(), new { Name = "DISPATCHERS" });
			yield return Case("identity user by normalized user name", c => new SelectUserByUserNameQuery(c).GetQuery(), new { UserName = "JANE.DOE" });
			yield return Case("identity user by normalized e-mail", c => new SelectUserByEmailQuery(c).GetQuery(), new { Email = "JANE.DOE@EXAMPLE.COM" });
			// GetUsersInRoleQuery carries the same cast but cannot run on either engine: its column list selects the [NotMapped]
			// IdentityUser.CreateDate, which lives on AspNetUsersExt, not AspNetUsers.
			yield return Case("user is in role", c => new IsInRoleQuery(c).GetQuery<IdentityUser>(), new { RoleName = "DISPATCHERS", UserId });
			yield return Case("unit by name", c => new SelectUnitByDIdNameQuery(c).GetQuery(), new { DepartmentId, UnitName = "ENGINE 9" });
			yield return Case("unit type by name", c => new SelectUnitTypeByDIdNameQuery(c).GetQuery(), new { DepartmentId, TypeName = "engine" });
			yield return Case("units by type", c => new SelectUnitByDIdTypeQuery(c).GetQuery(), new { DepartmentId, Type = "ENGINE" });
			yield return Case("distribution list by e-mail", c => new SelectDListByEmailQuery(c).GetQuery(), new { EmailAddress = "allhands9" });
			yield return Case("group by dispatch e-mail code", c => new SelectGroupByDispatchCodeQuery(c).GetQuery(), new { DispatchEmail = "st9dispatch" });
			yield return Case("group by message e-mail code", c => new SelectGroupByMessageCodeQuery(c).GetQuery(), new { MessageEmail = "ST9MESSAGE" });
			yield return Case("communication test run by run code", c => new SelectCommTestRunByRunCodeQuery(c).GetQuery(), new { RunCode = "runxy9" });
			yield return Case("communication test result by response token", c => new SelectCommTestResultByResponseTokenQuery(c).GetQuery(), new { ResponseToken = "TOKQ7Z" });
		}

		[TestCaseSource(nameof(TemplateLookups))]
		public async Task Template_lookup_ignores_case(Func<PostgreSqlConfiguration, string> query, object parameters)
		{
			await using var database = Connect();
			var rows = (await database.QueryAsync(query(_sql), parameters)).ToList();

			rows.Should().NotBeEmpty();
		}

		[Test]
		public async Task Removing_a_user_from_a_role_ignores_the_role_name_case()
		{
			await using var database = Connect();
			await database.OpenAsync();
			await using var transaction = await database.BeginTransactionAsync();

			(await database.ExecuteAsync(new RemoveUserFromRoleQuery(_sql).GetQuery(), new { UserId, RoleName = "DISPATCHERS" }, transaction)).Should().Be(1);
			await transaction.RollbackAsync();
		}

		[Test]
		public async Task A_text_parameter_alone_matches_case_sensitively_and_cannot_use_the_index()
		{
			// The reason for every cast: the same predicate without it misses the row and plans a sequential scan.
			await using var database = Connect();
			await database.OpenAsync();
			await database.ExecuteAsync("SET enable_seqscan = off");

			(await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM communicationtestruns WHERE runcode = @RunCode", new { RunCode = "runxy9" })).Should().Be(0);
			string.Join("\n", await database.QueryAsync<string>("EXPLAIN SELECT * FROM communicationtestruns WHERE runcode = @RunCode", new { RunCode = "runxy9" }))
				.Should().Contain("Seq Scan");
		}

		[Test]
		public async Task A_cast_lookup_uses_the_citext_index()
		{
			await using var database = Connect();
			await database.OpenAsync();
			await database.ExecuteAsync("SET enable_seqscan = off");

			var plan = string.Join("\n", await database.QueryAsync<string>("EXPLAIN " + new SelectCommTestRunByRunCodeQuery(_sql).GetQuery(), new { RunCode = "runxy9" }));
			plan.Should().Contain("ix_communicationtestruns_runcode").And.NotContain("Seq Scan");
		}

		private static QueryFactory Queries(params IQuery[] queries)
		{
			var list = new ConcurrentDictionary<Type, IQuery>();
			foreach (var query in queries) list[query.GetType()] = query;
			var queryList = new Mock<IQueryList>();
			queryList.Setup(l => l.RetrieveQueryList()).Returns(list);
			return new QueryFactory(queryList.Object);
		}

		private IConnectionProvider Connections()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect());
			return connections.Object;
		}

		[Test]
		public async Task Identity_store_finds_users_by_normalized_user_name_and_email()
		{
			// UserManager hands the store upper-cased values (UpperInvariantLookupNormalizer); the store compares them with
			// UserName and Email as stored.
			var users = new IdentityUserRepository(Connections(), _sql, Mock.Of<IIdentityRoleRepository>(), Mock.Of<IUnitOfWork>(),
				Queries(new SelectUserByUserNameQuery(_sql), new SelectUserByEmailQuery(_sql)));

			((await users.GetByUserNameAsync("JANE.DOE"))?.Id).Should().Be(UserId);
			((await users.GetByEmailAsync("JANE.DOE@EXAMPLE.COM"))?.Id).Should().Be(UserId);
		}

		[Test]
		public async Task Identity_repository_finds_users_by_user_name_and_email()
		{
			var identity = new IdentityRepository();

			((await identity.GetUserByUserNameAsync("jane.doe"))?.Id).Should().Be(UserId);
			(identity.GetUserByEmail("JANE.DOE@EXAMPLE.COM")?.Id).Should().Be(UserId);
		}

		[Test]
		public async Task Chatbot_linking_code_lookup_ignores_case()
		{
			var codes = new ChatbotLinkingCodeRepository(Connections(), _sql, Mock.Of<IUnitOfWork>(), Queries());

			((await codes.GetByCodeAsync("linkz1"))?.Code).Should().Be("LinkZ1");
		}

		[Test]
		public async Task Certification_type_code_lookups_ignore_case()
		{
			var types = new DepartmentCertificationTypeRepository(Connections(), _sql, Mock.Of<IUnitOfWork>(), Queries());

			((await types.GetByCodeAsync(DepartmentId, "emt-b"))?.Code).Should().Be("EMT-B");
			(await types.GetByCodesAsync(DepartmentId, new[] { "emt-b" })).Select(t => t.Code).Should().Equal("EMT-B");
		}

		[Test]
		public async Task Hydrant_number_lookups_ignore_case()
		{
			var hydrants = new RmsHydrantsRepository(Connections(), _sql, Mock.Of<IUnitOfWork>(), Queries());

			((await hydrants.GetByNumberAsync(DepartmentId, "h-12A"))?.HydrantNumber).Should().Be("H-12a");
			var batch = await hydrants.GetByNumbersAsync(DepartmentId, new[] { "h-12A" });
			batch.Should().ContainKey("h-12A");
			batch["h-12A"].HydrantNumber.Should().Be("H-12a");
		}
	}
}
