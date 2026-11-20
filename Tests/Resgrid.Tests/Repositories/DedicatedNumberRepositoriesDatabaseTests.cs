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
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for the M0323 Dedicated Number tables on both engines, through the queries DepartmentPhoneNumbersService and
	/// the Billing API really call: a Provisioning, Active or Lapsed number belongs to one department (UX_DepartmentPhoneNumbers_Number,
	/// even when departments claim it at once) while a Released or Failed one can be provisioned again; lookups by E.164 number read only
	/// the statuses asked for; a department's rows (history included) and worker 80's held-number sweep; the lapse, renewal-delay
	/// (AddonInactiveSince) and restore fields round trip through the entity update; and the billing account and paid intervals round
	/// trip only inside the department transaction. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION
	/// (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class DedicatedNumberRepositoriesDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "dedicated_number_repositories_";
		private const string AddonId = "3b1c7f0e-5d2a-4c86-9a4e-7f2d1e6b8c40";
		private const string Number = "+17755550100";
		private static readonly DateTime Base = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

		private static readonly int[] HeldStatuses =
		{
			(int)DepartmentPhoneNumberStatuses.Provisioning, (int)DepartmentPhoneNumberStatuses.Active, (int)DepartmentPhoneNumberStatuses.Lapsed
		};

		private static readonly int[] RoutingStatuses = { (int)DepartmentPhoneNumberStatuses.Active, (int)DepartmentPhoneNumberStatuses.Lapsed };

		private DatabaseTypes _previous;
		private bool _configured;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string Table(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private IConnectionProvider Connections()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			return connections.Object;
		}

		private SqlConfiguration Configuration() => IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();

		private IQueryFactory Queries()
		{
			var configuration = Configuration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			queries[typeof(InsertQuery)] = new InsertQuery(configuration);
			queries[typeof(UpdateQuery)] = new UpdateQuery(configuration);
			queries[typeof(SelectByIdQuery)] = new SelectByIdQuery(configuration);
			queries[typeof(SelectByDepartmentIdQuery)] = new SelectByDepartmentIdQuery(configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);
			return new QueryFactory(list.Object);
		}

		private DepartmentPhoneNumbersRepository Numbers() => new DepartmentPhoneNumbersRepository(Connections(), Configuration(), Mock.Of<IUnitOfWork>(), Queries());

		private DedicatedNumberBillingRepository Billing(IUnitOfWork unitOfWork) => new DedicatedNumberBillingRepository(Connections(), Configuration(), unitOfWork, Queries());

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
				// M0323 seeds a PlanAddons row and feature flags; those tables belong to earlier migrations. The billing repository also locks
				// the Departments row and writes PaymentAddons (M0001): minimal stand-ins.
				if (IsPostgres)
				{
					await setup.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS citext;");
					await ((NpgsqlConnection)setup).ReloadTypesAsync();
					await setup.ExecuteAsync("CREATE TABLE planaddons (planaddonid varchar(36) PRIMARY KEY, addontype int NOT NULL, cost numeric NOT NULL, externalid citext, testexternalid citext);" +
						"CREATE TABLE featureflags (featureflagid serial PRIMARY KEY, flagkey citext NOT NULL, name citext, description citext, category citext, isenabledglobally boolean NOT NULL);" +
						"CREATE TABLE featureflagprerequisites (featureflagid int NOT NULL, requiredfeatureflagid int NOT NULL, requiredvalue citext);" +
						"INSERT INTO featureflags (flagkey, name, isenabledglobally) VALUES ('Ai.Enhanced', 'Enhanced AI', false);" +
						"CREATE TABLE departments (departmentid integer PRIMARY KEY); INSERT INTO departments VALUES (55), (56), (99);");
				}
				else
				{
					await setup.ExecuteAsync("CREATE TABLE [PlanAddons] ([PlanAddonId] nvarchar(36) PRIMARY KEY, [AddonType] int NOT NULL, [Cost] decimal(18,2) NOT NULL, [ExternalId] nvarchar(200), [TestExternalId] nvarchar(200));" +
						"CREATE TABLE [FeatureFlags] ([FeatureFlagId] int IDENTITY PRIMARY KEY, [FlagKey] nvarchar(200) NOT NULL, [Name] nvarchar(200), [Description] nvarchar(max), [Category] nvarchar(100), [IsEnabledGlobally] bit NOT NULL);" +
						"CREATE TABLE [FeatureFlagPrerequisites] ([FeatureFlagId] int NOT NULL, [RequiredFeatureFlagId] int NOT NULL, [RequiredValue] nvarchar(200));" +
						"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [IsEnabledGlobally]) VALUES ('Ai.Enhanced', 'Enhanced AI', 0);" +
						"CREATE TABLE [Departments] ([DepartmentId] int PRIMARY KEY); INSERT INTO [Departments] VALUES (55), (56), (99);");
				}

				var payment = new PaymentAddon();
				string ColumnType(Type t) => t == typeof(int) ? "int" : t == typeof(long) ? "bigint" : t == typeof(bool) ? (IsPostgres ? "boolean" : "bit") :
					t == typeof(double) ? (IsPostgres ? "double precision" : "float") : t == typeof(DateTime) || t == typeof(DateTime?) ? (IsPostgres ? "timestamp" : "datetime2") :
					IsPostgres ? "text" : "nvarchar(max)";
				var columns = typeof(PaymentAddon).GetProperties().Where(p => p.CanWrite && !payment.IgnoredProperties.Contains(p.Name))
					.Select(p => (IsPostgres ? p.Name.ToLowerInvariant() : "[" + p.Name + "]") + " " + ColumnType(p.PropertyType));
				await setup.ExecuteAsync("CREATE TABLE " + Table("PaymentAddons") + " (" + string.Join(", ", columns) + ")");
			}

			var source = new Mock<IMigrationSource>();
			// M0325 adds the release warning columns (plan §8 D12) to the M0323 table.
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres
				? new IMigration[] { new M0323_AddIntakeChannelsAndDedicatedNumbersPg(), new M0325_AddIntakeConversationsPg() }
				: new IMigration[] { new M0323_AddIntakeChannelsAndDedicatedNumbers(), new M0325_AddIntakeConversations() });
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
			foreach (var table in new[] { "DepartmentPhoneNumbers", "DedicatedNumberBillingAccounts", "PaymentAddons" })
				await database.ExecuteAsync("DELETE FROM " + Table(table));
		}

		/// <summary>A row as ProvisionAsync builds it before anything is bought.</summary>
		private static DepartmentPhoneNumber Row(int departmentId, string number, DepartmentPhoneNumberStatuses status, DateTime createdOn) => new DepartmentPhoneNumber
		{
			DepartmentPhoneNumberId = Guid.NewGuid().ToString(),
			DepartmentId = departmentId,
			PhoneNumber = number,
			Provider = "Twilio",
			CountryCode = "US",
			SmsCapable = true,
			VoiceCapable = true,
			Status = (int)status,
			SmsIntakeEnabled = true,
			VoiceIntakeEnabled = false,
			ProvisionedByUserId = "admin-" + departmentId,
			CreatedOn = createdOn
		};

		/// <summary>The service's claim: the entity insert, first level only.</summary>
		private async Task<bool> TryClaimAsync(DepartmentPhoneNumber row)
		{
			try
			{
				await Numbers().InsertAsync(row, CancellationToken.None, true);
				return true;
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				return false;
			}
		}

		private Task SaveAsync(DepartmentPhoneNumber row) => Numbers().UpdateAsync(row, CancellationToken.None, true);

		[Test]
		public async Task A_held_number_belongs_to_one_department_and_a_released_one_can_be_provisioned_again()
		{
			var held = Row(55, Number, DepartmentPhoneNumberStatuses.Provisioning, Base);
			(await TryClaimAsync(held)).Should().BeTrue();
			(await TryClaimAsync(Row(56, Number, DepartmentPhoneNumberStatuses.Provisioning, Base.AddMinutes(1)))).Should().BeFalse("UX_DepartmentPhoneNumbers_Number: a provisioning number is held");

			held.Status = (int)DepartmentPhoneNumberStatuses.Active;
			held.ProviderNumberId = "PN00000000000000000000000000000055";
			held.ProvisionedOn = Base.AddMinutes(1);
			held.UpdatedOn = Base.AddMinutes(1);
			await SaveAsync(held);
			(await TryClaimAsync(Row(56, Number, DepartmentPhoneNumberStatuses.Provisioning, Base.AddMinutes(2)))).Should().BeFalse("an active number is held");

			held.Status = (int)DepartmentPhoneNumberStatuses.Lapsed;
			held.ReleaseAfter = Base.AddDays(7);
			await SaveAsync(held);
			(await TryClaimAsync(Row(56, Number, DepartmentPhoneNumberStatuses.Provisioning, Base.AddMinutes(3)))).Should().BeFalse("a lapsed number is held until it is released");

			held.Status = (int)DepartmentPhoneNumberStatuses.Released;
			held.ReleaseAfter = null;
			held.ReleasedOn = Base.AddDays(7);
			await SaveAsync(held);
			var again = Row(56, Number, DepartmentPhoneNumberStatuses.Provisioning, Base.AddDays(8));
			(await TryClaimAsync(again)).Should().BeTrue("a released number can be provisioned again, by another department");

			// A failed purchase holds nothing either.
			again.Status = (int)DepartmentPhoneNumberStatuses.Failed;
			again.LastError = "The carrier returned no number.";
			await SaveAsync(again);
			var third = Row(99, Number, DepartmentPhoneNumberStatuses.Provisioning, Base.AddDays(9));
			(await TryClaimAsync(third)).Should().BeTrue();

			// History rows of one number coexist; a released row cannot come back while another department holds the number.
			held.Status = (int)DepartmentPhoneNumberStatuses.Active;
			Func<Task> restore = () => SaveAsync(held);
			await restore.Should().ThrowAsync<Exception>("the number is held by department 99 now");
			(await Numbers().GetByNumberAsync(Number, new[] { (int)DepartmentPhoneNumberStatuses.Released })).Single().DepartmentId.Should().Be(55);
		}

		[Test]
		public async Task Departments_claiming_one_number_at_once_get_it_once()
		{
			var claims = await Task.WhenAll(new[] { 55, 56, 99, 55, 56, 99 }.Select((department, i) =>
				TryClaimAsync(Row(department, "+17755550111", DepartmentPhoneNumberStatuses.Provisioning, Base.AddSeconds(i)))));

			claims.Count(c => c).Should().Be(1);
			(await Numbers().GetByNumberAsync("+17755550111", HeldStatuses)).Should().ContainSingle();
		}

		[Test]
		public async Task Lookups_by_number_read_only_the_statuses_asked_for_newest_first()
		{
			var numbers = Numbers();
			var released = Row(55, Number, DepartmentPhoneNumberStatuses.Released, Base.AddDays(-30));
			var failed = Row(56, Number, DepartmentPhoneNumberStatuses.Failed, Base.AddDays(-20));
			var provisioning = Row(99, Number, DepartmentPhoneNumberStatuses.Provisioning, Base);
			var other = Row(55, "+17755550199", DepartmentPhoneNumberStatuses.Active, Base);
			foreach (var row in new[] { released, failed, provisioning, other })
				(await TryClaimAsync(row)).Should().BeTrue();

			(await numbers.GetByNumberAsync(Number, RoutingStatuses)).Should().BeEmpty("inbound routing never reaches a number still provisioning");
			(await numbers.GetByNumberAsync(Number, HeldStatuses)).Select(r => r.DepartmentPhoneNumberId).Should().Equal(new[] { provisioning.DepartmentPhoneNumberId });
			(await numbers.GetByNumberAsync(Number, new[] { 1, 2, 3, 4, 5 })).Select(r => r.DepartmentPhoneNumberId)
				.Should().Equal(new[] { provisioning.DepartmentPhoneNumberId, failed.DepartmentPhoneNumberId, released.DepartmentPhoneNumberId }, "newest first");

			provisioning.Status = (int)DepartmentPhoneNumberStatuses.Active;
			await SaveAsync(provisioning);
			var routed = (await numbers.GetByNumberAsync(Number, RoutingStatuses)).Single();
			routed.DepartmentId.Should().Be(99);
			routed.PhoneNumber.Should().Be(Number);

			(await numbers.GetByNumberAsync("+17755550123", HeldStatuses)).Should().BeEmpty();
			(await numbers.GetByNumberAsync(" ", HeldStatuses)).Should().BeEmpty();
			(await numbers.GetByNumberAsync(Number, Array.Empty<int>())).Should().BeEmpty();
			(await numbers.GetByNumberAsync(Number, null)).Should().BeEmpty();
		}

		[Test]
		public async Task A_departments_rows_and_the_lifecycle_sweep_read_back_what_each_needs()
		{
			var numbers = Numbers();
			var history = Row(55, "+17755550101", DepartmentPhoneNumberStatuses.Released, Base.AddDays(-30));
			history.ReleasedOn = Base.AddDays(-2);
			var active = Row(55, Number, DepartmentPhoneNumberStatuses.Active, Base.AddDays(-1));
			var lapsedDue = Row(56, "+17755550102", DepartmentPhoneNumberStatuses.Lapsed, Base.AddDays(-20));
			lapsedDue.ReleaseAfter = Base.AddHours(-1);
			lapsedDue.AddonInactiveSince = Base.AddDays(-9);
			var lapsedHeld = Row(56, "+17755550103", DepartmentPhoneNumberStatuses.Lapsed, Base.AddDays(-10));
			lapsedHeld.ReleaseAfter = Base.AddDays(3);
			var failed = Row(99, "+17755550104", DepartmentPhoneNumberStatuses.Failed, Base.AddDays(-3));
			var provisioning = Row(99, "+17755550105", DepartmentPhoneNumberStatuses.Provisioning, Base);
			foreach (var row in new[] { history, active, lapsedDue, lapsedHeld, failed, provisioning })
				(await TryClaimAsync(row)).Should().BeTrue();

			// GetAllForDepartmentAsync / GetActiveForDepartmentAsync read the department's rows, released history included.
			(await numbers.GetAllByDepartmentIdAsync(55)).Select(r => r.DepartmentPhoneNumberId)
				.Should().BeEquivalentTo(new[] { history.DepartmentPhoneNumberId, active.DepartmentPhoneNumberId });
			(await numbers.GetAllByDepartmentIdAsync(56)).Should().HaveCount(2).And.OnlyContain(r => r.DepartmentId == 56);
			(await numbers.GetAllByDepartmentIdAsync(12345)).Should().BeEmpty();
			(await numbers.GetByIdAsync(active.DepartmentPhoneNumberId)).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active);

			// Worker 80 asks only about numbers a department holds, grouped by department.
			var sweep = await numbers.GetByStatusesAsync(HeldStatuses);
			sweep.Select(r => r.DepartmentPhoneNumberId).Should().Equal(new[]
			{
				active.DepartmentPhoneNumberId, lapsedDue.DepartmentPhoneNumberId, lapsedHeld.DepartmentPhoneNumberId, provisioning.DepartmentPhoneNumberId
			}, "by department, oldest first, never released or failed history");

			// A lapsed number past its ReleaseAfter is the one the sweep releases.
			sweep.Where(r => r.Status == (int)DepartmentPhoneNumberStatuses.Lapsed && r.ReleaseAfter <= Base).Select(r => r.DepartmentPhoneNumberId)
				.Should().Equal(new[] { lapsedDue.DepartmentPhoneNumberId });
			var due = sweep.Single(r => r.DepartmentPhoneNumberId == lapsedDue.DepartmentPhoneNumberId);
			due.ReleaseAfter.Should().Be(Base.AddHours(-1));
			due.AddonInactiveSince.Should().Be(Base.AddDays(-9));
			due.CreatedOn.Should().Be(Base.AddDays(-20));

			(await numbers.GetByStatusesAsync(new[] { (int)DepartmentPhoneNumberStatuses.Provisioning })).Select(r => r.DepartmentId).Should().Equal(new[] { 99 });
			(await numbers.GetByStatusesAsync(Array.Empty<int>())).Should().BeEmpty();
		}

		[Test]
		public async Task Renewal_delay_lapse_and_restore_fields_round_trip_through_the_entity_update()
		{
			var numbers = Numbers();
			var row = Row(55, Number, DepartmentPhoneNumberStatuses.Active, Base.AddDays(-40));
			row.ProviderNumberId = "PN00000000000000000000000000000055";
			row.ProvisionedOn = Base.AddDays(-40);
			(await TryClaimAsync(row)).Should().BeTrue();

			// Worker 80 first sees the department without a paid interval.
			row.AddonInactiveSince = Base;
			row.UpdatedOn = Base;
			await SaveAsync(row);
			var stored = await numbers.GetByIdAsync(row.DepartmentPhoneNumberId);
			stored.AddonInactiveSince.Should().Be(Base);
			stored.UpdatedOn.Should().Be(Base);
			stored.Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active);

			// The renewal was paid: the delay clears.
			row.AddonInactiveSince = null;
			row.UpdatedOn = Base.AddHours(2);
			await SaveAsync(row);
			(await numbers.GetByIdAsync(row.DepartmentPhoneNumberId)).AddonInactiveSince.Should().BeNull();

			// It lapses after the delay, then the add-on comes back.
			row.AddonInactiveSince = Base.AddDays(1);
			row.Status = (int)DepartmentPhoneNumberStatuses.Lapsed;
			row.ReleaseAfter = Base.AddDays(9);
			row.SmsIntakeEnabled = false;
			row.VoiceIntakeEnabled = true;
			row.LastError = "The carrier did not release the number; it will be retried.";
			await SaveAsync(row);
			stored = (await numbers.GetByStatusesAsync(new[] { (int)DepartmentPhoneNumberStatuses.Lapsed })).Single();
			stored.ReleaseAfter.Should().Be(Base.AddDays(9));
			stored.AddonInactiveSince.Should().Be(Base.AddDays(1));
			stored.SmsIntakeEnabled.Should().BeFalse();
			stored.VoiceIntakeEnabled.Should().BeTrue();
			stored.LastError.Should().Be(row.LastError);

			row.Status = (int)DepartmentPhoneNumberStatuses.Active;
			row.ReleaseAfter = null;
			row.AddonInactiveSince = null;
			row.LastError = null;
			await SaveAsync(row);
			stored = await numbers.GetByIdAsync(row.DepartmentPhoneNumberId);
			stored.Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active);
			stored.ReleaseAfter.Should().BeNull();
			stored.AddonInactiveSince.Should().BeNull();
			stored.LastError.Should().BeNull();
			stored.ProviderNumberId.Should().Be(row.ProviderNumberId);
			stored.Provider.Should().Be("Twilio");
			stored.CountryCode.Should().Be("US");
			stored.SmsCapable.Should().BeTrue();
			stored.VoiceCapable.Should().BeTrue();
			stored.ProvisionedByUserId.Should().Be("admin-55");
			stored.ProvisionedOn.Should().Be(Base.AddDays(-40));
			stored.CreatedOn.Should().Be(Base.AddDays(-40));
		}

		[Test]
		public async Task Release_warning_flags_round_trip_and_default_to_none()
		{
			var numbers = Numbers();
			var row = Row(55, Number, DepartmentPhoneNumberStatuses.Active, Base.AddDays(-40));
			(await TryClaimAsync(row)).Should().BeTrue();

			// A row written without the columns (as before M0325) reads back with no warnings sent.
			await using (var database = Connect(_connection))
				await database.ExecuteAsync("INSERT INTO " + Table("DepartmentPhoneNumbers") + " (" +
					string.Join(", ", new[] { "DepartmentPhoneNumberId", "DepartmentId", "PhoneNumber", "Provider", "CountryCode", "SmsCapable", "VoiceCapable", "Status",
						"SmsIntakeEnabled", "VoiceIntakeEnabled", "CreatedOn" }.Select(c => IsPostgres ? c.ToLowerInvariant() : "[" + c + "]")) +
					") VALUES ('legacy-row', 56, '+17755550109', 'Twilio', 'US', " + (IsPostgres ? "true, true" : "1, 1") + ", 2, " + (IsPostgres ? "true, false" : "1, 0") + ", @created)",
					new { created = Base });
			var legacy = await numbers.GetByIdAsync("legacy-row");
			legacy.ReleaseWarningsSent.Should().Be(0);
			legacy.LastReleaseWarningOn.Should().BeNull();

			(await numbers.GetByIdAsync(row.DepartmentPhoneNumberId)).ReleaseWarningsSent.Should().Be(0);

			// Worker 80 sent "payment stopped" and the 15-day warning.
			row.AddonInactiveSince = Base;
			row.ReleaseWarningsSent = (int)(DedicatedNumberReleaseWarnings.Stopped | DedicatedNumberReleaseWarnings.FifteenDays);
			row.LastReleaseWarningOn = Base.AddDays(15);
			row.UpdatedOn = Base.AddDays(15);
			await SaveAsync(row);
			var stored = (await numbers.GetByStatusesAsync(HeldStatuses)).Single(r => r.DepartmentPhoneNumberId == row.DepartmentPhoneNumberId);
			stored.ReleaseWarningsSent.Should().Be(3);
			stored.LastReleaseWarningOn.Should().Be(Base.AddDays(15));
			stored.AddonInactiveSince.Should().Be(Base);
			((DedicatedNumberReleaseWarnings)stored.ReleaseWarningsSent).Should().HaveFlag(DedicatedNumberReleaseWarnings.FifteenDays);

			// Paying again clears them.
			row.AddonInactiveSince = null;
			row.ReleaseWarningsSent = 0;
			row.LastReleaseWarningOn = null;
			await SaveAsync(row);
			stored = await numbers.GetByIdAsync(row.DepartmentPhoneNumberId);
			stored.ReleaseWarningsSent.Should().Be(0);
			stored.LastReleaseWarningOn.Should().BeNull();
			stored.AddonInactiveSince.Should().BeNull();
			(await numbers.GetByNumberAsync(Number, RoutingStatuses)).Single().ReleaseWarningsSent.Should().Be(0);
		}

		private static DedicatedNumberBillingAccount Account(int departmentId) => new DedicatedNumberBillingAccount
		{
			DepartmentId = departmentId,
			Provider = "Stripe",
			CustomerId = "cus_synthetic" + departmentId,
			PlanAddonId = AddonId,
			PriceId = "price_synthetic",
			CheckoutId = "cs_synthetic" + departmentId,
			CheckoutUrl = "https://checkout.example.test/cs_synthetic" + departmentId,
			CheckoutExpiresOn = Base.AddHours(1),
			CheckoutAttempt = Guid.NewGuid().ToString(),
			UpdatedOn = Base
		};

		[Test]
		public async Task The_billing_account_round_trips_only_inside_the_department_transaction()
		{
			using var unitOfWork = new UnitOfWork(Connections());
			var billing = Billing(unitOfWork);
			var account = Account(55);

			await FluentActions.Awaiting(() => billing.SaveAsync(account)).Should().ThrowAsync<InvalidOperationException>("writes need the department transaction");
			await FluentActions.Awaiting(() => billing.LockDepartmentAsync(55)).Should().ThrowAsync<InvalidOperationException>();

			await unitOfWork.CreateOrGetConnectionAsync();
			await billing.LockDepartmentAsync(55);
			await billing.SaveAsync(account);
			unitOfWork.CommitChanges();

			var stored = await billing.GetAsync(55);
			stored.Should().NotBeNull();
			stored.Provider.Should().Be("Stripe");
			stored.CustomerId.Should().Be(account.CustomerId);
			stored.PlanAddonId.Should().Be(AddonId);
			stored.PriceId.Should().Be("price_synthetic");
			stored.SubscriptionId.Should().BeNull();
			stored.CheckoutId.Should().Be(account.CheckoutId);
			stored.CheckoutUrl.Should().Be(account.CheckoutUrl);
			stored.CheckoutExpiresOn.Should().Be(Base.AddHours(1));
			stored.CheckoutAttempt.Should().Be(account.CheckoutAttempt);
			stored.UpdatedOn.Should().Be(Base);
			(await billing.GetAsync(56)).Should().BeNull();

			// Checkout completed: the second save updates the row.
			account.SubscriptionId = "sub_synthetic55";
			account.CheckoutId = null;
			account.CheckoutUrl = null;
			account.CheckoutExpiresOn = null;
			account.CheckoutAttempt = null;
			account.UpdatedOn = Base.AddMinutes(5);
			await unitOfWork.CreateOrGetConnectionAsync();
			await billing.LockDepartmentAsync(55);
			await billing.SaveAsync(account);
			unitOfWork.CommitChanges();

			stored = await billing.GetAsync(55);
			stored.SubscriptionId.Should().Be("sub_synthetic55");
			stored.CheckoutId.Should().BeNull();
			stored.CheckoutExpiresOn.Should().BeNull();
			stored.UpdatedOn.Should().Be(Base.AddMinutes(5));

			// A save the transaction discards leaves the row as it was.
			account.CustomerId = "cus_discarded";
			await unitOfWork.CreateOrGetConnectionAsync();
			await billing.SaveAsync(account);
			unitOfWork.DiscardChanges();
			(await billing.GetAsync(55)).CustomerId.Should().Be("cus_synthetic55");

			await unitOfWork.CreateOrGetConnectionAsync();
			await FluentActions.Awaiting(() => billing.LockDepartmentAsync(12345)).Should().ThrowAsync<InvalidOperationException>("the department does not exist");
			unitOfWork.DiscardChanges();
		}

		[Test]
		public async Task A_provider_event_finds_its_one_billing_account()
		{
			using var unitOfWork = new UnitOfWork(Connections());
			var billing = Billing(unitOfWork);
			var first = Account(55);
			first.SubscriptionId = "sub_synthetic55";
			var second = Account(56);
			second.SubscriptionId = "sub_synthetic56";
			foreach (var account in new[] { first, second })
			{
				await unitOfWork.CreateOrGetConnectionAsync();
				await billing.LockDepartmentAsync(account.DepartmentId);
				await billing.SaveAsync(account);
				unitOfWork.CommitChanges();
			}

			(await billing.FindAsync("Stripe", first.CustomerId, null, null)).DepartmentId.Should().Be(55);
			(await billing.FindAsync("Stripe", null, "sub_synthetic56", null)).DepartmentId.Should().Be(56);
			(await billing.FindAsync("Stripe", " ", "", second.CheckoutId)).DepartmentId.Should().Be(56);
			(await billing.FindAsync("Stripe", first.CustomerId, first.SubscriptionId, first.CheckoutId)).DepartmentId.Should().Be(55);
			(await billing.FindAsync("Paddle", first.CustomerId, null, null)).Should().BeNull("another provider's ids are another account");
			(await billing.FindAsync("Stripe", "cus_unknown", null, null)).Should().BeNull();
			(await billing.FindAsync("Stripe", null, " ", null)).Should().BeNull();

			await FluentActions.Awaiting(() => billing.FindAsync("Stripe", first.CustomerId, second.SubscriptionId, null))
				.Should().ThrowAsync<InvalidOperationException>("ids naming two departments are ambiguous");
		}

		[Test]
		public async Task Paid_intervals_round_trip_for_the_department_add_on()
		{
			using var unitOfWork = new UnitOfWork(Connections());
			var billing = Billing(unitOfWork);
			var payment = new PaymentAddon
			{
				PaymentAddonId = Guid.NewGuid().ToString(),
				DepartmentId = 55,
				PlanAddonId = AddonId,
				TransactionId = "in_synthetic",
				SubscriptionId = "sub_synthetic55",
				PurchaseOn = Base,
				EffectiveOn = Base,
				EndingOn = Base.AddMonths(1),
				Amount = 15,
				Quantity = 1
			};

			await FluentActions.Awaiting(() => billing.SavePaymentAsync(payment, true)).Should().ThrowAsync<InvalidOperationException>();

			await unitOfWork.CreateOrGetConnectionAsync();
			await billing.LockDepartmentAsync(55);
			await billing.SavePaymentAsync(payment, true);
			unitOfWork.CommitChanges();

			var stored = (await billing.PaymentsAsync(55, AddonId)).Single();
			stored.EndingOn.Should().Be(Base.AddMonths(1));
			stored.Amount.Should().Be(15);
			stored.IsCancelled.Should().BeFalse();
			(await billing.PaymentsAsync(56, AddonId)).Should().BeEmpty();
			(await billing.PaymentsAsync(55, Guid.NewGuid().ToString())).Should().BeEmpty("another add-on's intervals");

			payment.IsCancelled = true;
			payment.CancelledOn = Base.AddDays(3);
			await unitOfWork.CreateOrGetConnectionAsync();
			await billing.LockDepartmentAsync(55);
			await billing.SavePaymentAsync(payment, false);
			unitOfWork.CommitChanges();
			stored = (await billing.PaymentsAsync(55, AddonId)).Single();
			stored.IsCancelled.Should().BeTrue();
			stored.CancelledOn.Should().Be(Base.AddDays(3));

			// An update matches department, interval and add-on, or fails.
			payment.DepartmentId = 56;
			await unitOfWork.CreateOrGetConnectionAsync();
			await FluentActions.Awaiting(() => billing.SavePaymentAsync(payment, false)).Should().ThrowAsync<InvalidOperationException>();
			unitOfWork.DiscardChanges();
		}
	}
}
