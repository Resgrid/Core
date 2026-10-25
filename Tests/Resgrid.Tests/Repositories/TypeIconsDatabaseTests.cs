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
	/// Real-database proof for M0285 and M0286 on both engines: the migrations create TypeIcons, an IconKey on UnitTypes,
	/// CallTypes, POITypes and PersonnelRoles, and PersonnelRoles.SortOrder (0 for existing roles); an icon round-trips through the generic insert and is found by its key (the service refuses
	/// anything but lowercase hex first, since SQL Server's default collation compares case-insensitively); the type's key
	/// is set only on the department's own row; a type's delete keeps only the key it is told to and the other kinds;
	/// and the role order is written only to the department's own roles. M0289: a status or priority icon (Variant) is its own
	/// slot, so replacing or removing the type's own icon keeps it and replacing one slot keeps the others, while the type
	/// delete removes them all; the department's slots list without their bytes.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class TypeIconsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "type_icons_";
		private const int DepartmentId = 5;

		private DatabaseTypes _previous;
		private bool _configured;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string table) => IsPostgres ? table.ToLowerInvariant() : table;

		private TypeIconsRepository Repository()
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

			return new TypeIconsRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(IsPostgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database checks.");

			if (IsPostgres) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

			_previous = DataConfig.DatabaseType;
			DataConfig.DatabaseType = type;
			_configured = true;
			var database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + database);
			_database = database;
			_connection = IsPostgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			await using (var setup = Connect(_connection))
			{
				await setup.OpenAsync();

				// M0285 alters the three type tables and M0286 PersonnelRoles, so the fixture starts from the columns they had before.
				if (IsPostgres)
				{
					await setup.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS citext;");
					await ((NpgsqlConnection)setup).ReloadTypesAsync();
					await setup.ExecuteAsync("CREATE TABLE unittypes (unittypeid serial PRIMARY KEY, departmentid integer NOT NULL, type citext NOT NULL, mapicontype integer NULL);");
					await setup.ExecuteAsync("CREATE TABLE calltypes (calltypeid serial PRIMARY KEY, departmentid integer NOT NULL, type citext NOT NULL, mapicontype integer NULL);");
					await setup.ExecuteAsync("CREATE TABLE poitypes (poitypeid serial PRIMARY KEY, departmentid integer NOT NULL, name citext NOT NULL);");
					await setup.ExecuteAsync("CREATE TABLE personnelroles (personnelroleid serial PRIMARY KEY, departmentid integer NOT NULL, name citext NOT NULL, description citext NULL);");
				}
				else
				{
					await setup.ExecuteAsync("CREATE TABLE UnitTypes (UnitTypeId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, Type nvarchar(100) NOT NULL, MapIconType int NULL);");
					await setup.ExecuteAsync("CREATE TABLE CallTypes (CallTypeId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, Type nvarchar(100) NOT NULL, MapIconType int NULL);");
					await setup.ExecuteAsync("CREATE TABLE POITypes (PoiTypeId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, Name nvarchar(100) NOT NULL);");
					await setup.ExecuteAsync("CREATE TABLE PersonnelRoles (PersonnelRoleId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, Name nvarchar(250) NOT NULL, Description nvarchar(3000) NULL);");
				}
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres
				? new IMigration[] { new M0285_AddTypeIconsPg(), new M0286_AddPersonnelRoleIconsPg(), new M0289_AddTypeIconVariantsPg() }
				: new IMigration[] { new M0285_AddTypeIcons(), new M0286_AddPersonnelRoleIcons(), new M0289_AddTypeIconVariants() });
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
			_runner?.Dispose();
			if (_configured) DataConfig.DatabaseType = _previous;
			// Only a database this fixture created is ever dropped.
			if (_database == null) return;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		[SetUp]
		public async Task Empty_the_tables()
		{
			await using var database = Connect(_connection);
			foreach (var table in new[] { "TypeIcons", "UnitTypes", "CallTypes", "POITypes", "PersonnelRoles" })
				await database.ExecuteAsync("DELETE FROM " + T(table));
		}

		private static TypeIcon NewIcon(TypeIconKinds kind, int typeId) => new TypeIcon
		{
			TypeIconId = Guid.NewGuid().ToString(),
			DepartmentId = DepartmentId,
			TypeKind = (int)kind,
			TypeId = typeId,
			IconKey = Guid.NewGuid().ToString("N"),
			ContentType = "image/png",
			Width = 128,
			Height = 128,
			ByteSize = 4,
			Checksum = new string('a', 64),
			Data = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
			UploadedByUserId = "admin",
			UploadedOn = new DateTime(2026, 10, 21, 12, 0, 0)
		};

		private async Task<int> InsertTypeAsync(string table, string nameColumn, int departmentId)
		{
			await using var database = Connect(_connection);
			var idColumn = table == "POITypes" ? "PoiTypeId" : table.TrimEnd('s') + "Id";
			return await database.ExecuteScalarAsync<int>(IsPostgres
				? $"INSERT INTO {T(table)} (departmentid, {nameColumn.ToLowerInvariant()}) VALUES ({departmentId}, 'MUG') RETURNING {idColumn.ToLowerInvariant()}"
				: $"INSERT INTO {table} (DepartmentId, {nameColumn}) OUTPUT INSERTED.{idColumn} VALUES ({departmentId}, 'MUG')");
		}

		private async Task<string> IconKeyOfAsync(string table, int id)
		{
			await using var database = Connect(_connection);
			var idColumn = table == "POITypes" ? "PoiTypeId" : table.TrimEnd('s') + "Id";
			return await database.ExecuteScalarAsync<string>(IsPostgres
				? $"SELECT iconkey FROM {T(table)} WHERE {idColumn.ToLowerInvariant()} = {id}"
				: $"SELECT IconKey FROM {table} WHERE {idColumn} = {id}");
		}

		[TestCase(TypeIconKinds.UnitType, "UnitTypes", "Type")]
		[TestCase(TypeIconKinds.CallType, "CallTypes", "Type")]
		[TestCase(TypeIconKinds.PoiType, "POITypes", "Name")]
		[TestCase(TypeIconKinds.PersonnelRole, "PersonnelRoles", "Name")]
		public async Task SetTypeIconKeyAsync_sets_only_the_departments_own_type(TypeIconKinds kind, string table, string nameColumn)
		{
			var own = await InsertTypeAsync(table, nameColumn, DepartmentId);
			var foreign = await InsertTypeAsync(table, nameColumn, DepartmentId + 1);

			(await Repository().SetTypeIconKeyAsync(DepartmentId, kind, own, "abc")).Should().Be(1);
			(await Repository().SetTypeIconKeyAsync(DepartmentId, kind, foreign, "abc")).Should().Be(0, "another department's type is never touched");

			(await IconKeyOfAsync(table, own)).Should().Be("abc");
			(await IconKeyOfAsync(table, foreign)).Should().BeNull();

			(await Repository().SetTypeIconKeyAsync(DepartmentId, kind, own, null)).Should().Be(1);
			(await IconKeyOfAsync(table, own)).Should().BeNull();
		}

		[Test]
		public async Task SetRoleSortOrdersAsync_orders_only_the_departments_own_roles()
		{
			var captain = await InsertTypeAsync("PersonnelRoles", "Name", DepartmentId);
			var driver = await InsertTypeAsync("PersonnelRoles", "Name", DepartmentId);
			var emt = await InsertTypeAsync("PersonnelRoles", "Name", DepartmentId);
			var foreign = await InsertTypeAsync("PersonnelRoles", "Name", DepartmentId + 1);

			(await SortOrderOfAsync(captain)).Should().Be(0, "M0286 gives every existing role order 0, so they list by name");

			var updated = await RolesRepository().SetRoleSortOrdersAsync(DepartmentId, new[] { emt, captain, foreign, driver });

			updated.Should().Be(3, "another department's role is never touched");
			(await SortOrderOfAsync(emt)).Should().Be(1);
			(await SortOrderOfAsync(captain)).Should().Be(2);
			(await SortOrderOfAsync(driver)).Should().Be(4);
			(await SortOrderOfAsync(foreign)).Should().Be(0);
		}

		private async Task<int> SortOrderOfAsync(int roleId)
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(IsPostgres
				? $"SELECT sortorder FROM personnelroles WHERE personnelroleid = {roleId}"
				: $"SELECT SortOrder FROM PersonnelRoles WHERE PersonnelRoleId = {roleId}");
		}

		private PersonnelRolesRepository RolesRepository()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));

			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(new ConcurrentDictionary<Type, IQuery>());

			return new PersonnelRolesRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		[Test]
		public async Task An_icon_round_trips_and_is_found_by_its_key()
		{
			var icon = NewIcon(TypeIconKinds.CallType, 31);
			await Repository().InsertAsync(icon, CancellationToken.None);

			var found = await Repository().GetByIconKeyAsync(icon.IconKey);

			found.Should().NotBeNull();
			found.TypeKind.Should().Be((int)TypeIconKinds.CallType);
			found.TypeId.Should().Be(31);
			found.Data.Should().Equal(icon.Data);
			found.Checksum.Should().Be(icon.Checksum);
			(await Repository().GetByIconKeyAsync(Guid.NewGuid().ToString("N"))).Should().BeNull();
		}

		[Test]
		public async Task DeleteForTypeAsync_keeps_only_the_named_key_the_other_types_and_the_other_kinds()
		{
			var replaced = NewIcon(TypeIconKinds.UnitType, 31);
			var current = NewIcon(TypeIconKinds.UnitType, 31);
			var otherType = NewIcon(TypeIconKinds.UnitType, 32);
			var otherKind = NewIcon(TypeIconKinds.PoiType, 31);
			foreach (var icon in new[] { replaced, current, otherType, otherKind })
				await Repository().InsertAsync(icon, CancellationToken.None);

			(await Repository().DeleteForTypeAsync(DepartmentId, TypeIconKinds.UnitType, 31, current.IconKey)).Should().Be(1);
			(await Repository().GetByIconKeyAsync(replaced.IconKey)).Should().BeNull();
			(await Repository().GetByIconKeyAsync(current.IconKey)).Should().NotBeNull();

			(await Repository().DeleteForTypeAsync(DepartmentId, TypeIconKinds.UnitType, 31)).Should().Be(1);
			(await Repository().GetByIconKeyAsync(current.IconKey)).Should().BeNull();
			(await Repository().GetByIconKeyAsync(otherType.IconKey)).Should().NotBeNull();
			(await Repository().GetByIconKeyAsync(otherKind.IconKey)).Should().NotBeNull("the POI type with the same id is a different type");
		}

		private static TypeIcon NewVariant(TypeIconKinds kind, int typeId, string variant)
		{
			var icon = NewIcon(kind, typeId);
			icon.Variant = variant;
			return icon;
		}

		[Test]
		public async Task DeleteSlotAsync_touches_only_its_own_slot()
		{
			var own = NewIcon(TypeIconKinds.UnitType, 31);
			var oldResponding = NewVariant(TypeIconKinds.UnitType, 31, "s5");
			var responding = NewVariant(TypeIconKinds.UnitType, 31, "s5");
			var onScene = NewVariant(TypeIconKinds.UnitType, 31, "o44");
			foreach (var icon in new[] { own, oldResponding, responding, onScene })
				await Repository().InsertAsync(icon, CancellationToken.None);

			(await Repository().DeleteSlotAsync(DepartmentId, TypeIconKinds.UnitType, 31, "s5", responding.IconKey)).Should().Be(1);
			(await Repository().GetByIconKeyAsync(oldResponding.IconKey)).Should().BeNull();
			(await Repository().GetByIconKeyAsync(responding.IconKey)).Should().NotBeNull();

			(await Repository().DeleteSlotAsync(DepartmentId, TypeIconKinds.UnitType, 31, null)).Should().Be(1, "only the type's own icon is in the null slot");
			(await Repository().GetByIconKeyAsync(own.IconKey)).Should().BeNull();
			(await Repository().GetByIconKeyAsync(responding.IconKey)).Should().NotBeNull();
			(await Repository().GetByIconKeyAsync(onScene.IconKey)).Should().NotBeNull();

			(await Repository().DeleteForTypeAsync(DepartmentId, TypeIconKinds.UnitType, 31)).Should().Be(2, "the type delete removes every slot");
		}

		[Test]
		public async Task GetVariantsForDepartmentAsync_lists_only_the_departments_status_and_priority_icons()
		{
			var own = NewIcon(TypeIconKinds.CallType, 31);
			var high = NewVariant(TypeIconKinds.CallType, 31, "p2");
			var foreign = NewVariant(TypeIconKinds.CallType, 31, "p3");
			foreign.DepartmentId = DepartmentId + 1;
			foreach (var icon in new[] { own, high, foreign })
				await Repository().InsertAsync(icon, CancellationToken.None);

			var variants = await Repository().GetVariantsForDepartmentAsync(DepartmentId);

			variants.Should().ContainSingle();
			variants[0].TypeKind.Should().Be((int)TypeIconKinds.CallType);
			variants[0].TypeId.Should().Be(31);
			variants[0].Variant.Should().Be("p2");
			variants[0].IconKey.Should().Be(high.IconKey);
		}

		[TestCase(TypeIconKinds.UnitType, "UnitTypes", "Type")]
		[TestCase(TypeIconKinds.PersonnelRole, "PersonnelRoles", "Name")]
		public async Task TypeBelongsToDepartmentAsync_is_true_only_for_the_departments_own_type(TypeIconKinds kind, string table, string nameColumn)
		{
			var own = await InsertTypeAsync(table, nameColumn, DepartmentId);
			var foreign = await InsertTypeAsync(table, nameColumn, DepartmentId + 1);

			(await Repository().TypeBelongsToDepartmentAsync(DepartmentId, kind, own)).Should().BeTrue();
			(await Repository().TypeBelongsToDepartmentAsync(DepartmentId, kind, foreign)).Should().BeFalse();
		}

		[Test]
		public async Task The_icon_key_is_unique()
		{
			var first = NewIcon(TypeIconKinds.UnitType, 31);
			await Repository().InsertAsync(first, CancellationToken.None);
			var duplicate = NewIcon(TypeIconKinds.CallType, 32);
			duplicate.IconKey = first.IconKey;

			Func<Task> act = () => Repository().InsertAsync(duplicate, CancellationToken.None);

			await act.Should().ThrowAsync<Exception>();
		}
	}
}
