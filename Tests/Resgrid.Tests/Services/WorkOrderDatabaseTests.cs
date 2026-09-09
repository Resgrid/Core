using System;
using System.Threading;
using Resgrid.Model.WorkOrders;
using System.Collections.Generic;
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
using Resgrid.Model.Checklists;
using Resgrid.Model;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;
using Resgrid.Repositories.DataRepository.Transactions;
using Resgrid.Services;
using Newtonsoft.Json.Linq;

namespace Resgrid.Tests.Services
{
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public partial class WorkOrderDatabaseTests
	{
		private readonly DatabaseTypes _type;
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;
		private string Q(string name) => _type == DatabaseTypes.Postgres ? name.ToLowerInvariant() : "[" + name + "]";
		private string TextType => _type == DatabaseTypes.Postgres ? "text" : "nvarchar(max)";
		public WorkOrderDatabaseTests(DatabaseTypes type) { _type = type; }
		private DbConnection Connect(string connection) => _type == DatabaseTypes.Postgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);
		[OneTimeSetUp]
		public async Task CreateIsolatedDatabaseAndMigrate()
		{
			_master = Environment.GetEnvironmentVariable(_type == DatabaseTypes.Postgres ? "RESGRID_CHECKLIST_POSTGRES_TEST_CONNECTION" : "RESGRID_CHECKLIST_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set the checklist test connection for " + _type + " to run real database verification.");
			_previous = DataConfig.DatabaseType; DataConfig.DatabaseType = _type;
			_database = "checklist_verification_" + Guid.NewGuid().ToString("N");
			await using var master = Connect(_master); await master.ExecuteAsync("CREATE DATABASE " + _database);
			if (_type == DatabaseTypes.Postgres) { var builder = new NpgsqlConnectionStringBuilder(_master) { Database = _database }; _connection = builder.ConnectionString; }
			else { var builder = new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }; _connection = builder.ConnectionString; }
			await using var db = Connect(_connection);
			await db.ExecuteAsync(_type == DatabaseTypes.Postgres ? "CREATE TABLE departments(departmentid integer PRIMARY KEY); INSERT INTO departments VALUES(77),(88);" : "CREATE TABLE Departments(DepartmentId int PRIMARY KEY); INSERT INTO Departments VALUES(77),(88);");
			await db.ExecuteAsync($"CREATE TABLE {Q("AuditLogs")} ({Q("AuditLogId")} int PRIMARY KEY, {Q("DepartmentId")} int, {Q("LogType")} int, {Q("Data")} {TextType}); CREATE TABLE {Q("DomainEventOutbox")} ({Q("DomainEventOutboxId")} bigint PRIMARY KEY, {Q("DepartmentId")} int, {Q("ProducerSubsystem")} varchar(100), {Q("PayloadJson")} {TextType}, {Q("LastError")} {TextType}, {Q("AggregateId")} varchar(36)); CREATE TABLE {Q("WorkflowRuns")} ({Q("WorkflowRunId")} varchar(36) PRIMARY KEY, {Q("WorkflowId")} varchar(36), {Q("DepartmentId")} int, {Q("TriggerEventType")} int, {Q("Status")} int, {Q("AttemptNumber")} int, {Q("InputPayload")} {TextType}, {Q("ErrorMessage")} varchar(4000)); CREATE TABLE {Q("WorkflowRunLogs")} ({Q("WorkflowRunLogId")} varchar(36) PRIMARY KEY, {Q("WorkflowRunId")} varchar(36), {Q("RenderedOutput")} {TextType}, {Q("ActionResult")} varchar(4000), {Q("ErrorMessage")} varchar(4000));");
			var source = new Mock<IMigrationSource>(); source.Setup(s => s.GetMigrations()).Returns(new IMigration[] { _type == DatabaseTypes.Postgres ? new M0197_AddWorkOrdersPg() : new M0197_AddWorkOrders() });
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (_type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
		}
		[OneTimeTearDown]
		public async Task RemoveOnlyThisFixturesDatabase()
		{
			if (_database == null) return;
			_runner?.Dispose(); DataConfig.DatabaseType = _previous;
			if (!_database.StartsWith("checklist_verification_", StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(23), "N", out _)) throw new InvalidOperationException("Unexpected test database name.");
			if (_type == DatabaseTypes.Postgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(_type == DatabaseTypes.Postgres ? "DROP DATABASE " + _database + " WITH (FORCE)" : "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}
		private IConnectionProvider Connections()
		{ var provider = new Mock<IConnectionProvider>(); provider.Setup(p => p.Create()).Returns(() => Connect(_connection)); return provider.Object; }
		private SqlConfiguration Configuration() => _type == DatabaseTypes.Postgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
		private ChecklistRepository Repository(IConnectionProvider provider, IUnitOfWork uow) => new ChecklistRepository(provider, Configuration(), uow, new Mock<IQueryFactory>().Object);

        private WorkOrderRepository Orders(IUnitOfWork uow) => new WorkOrderRepository(Connections(), Configuration(), uow, Mock.Of<IQueryFactory>());
        private WorkOrder Order(int dept = 77) => new WorkOrder { DepartmentId = dept, CreatedBy = "author", CreatedOn = DateTime.UtcNow, UpdatedOn = DateTime.UtcNow, RequestId = Guid.NewGuid().ToString("D"), NumberYear = 2026 };
        private async Task<WorkOrder> Insert(WorkOrderRepository store, int dept = 77)
        {
            var row = Order(dept); row.NumberSequence = await store.NextNumberAsync(dept, row.NumberYear);
            await store.AllocateAsync(row); row.Content = "{\"Title\":\"synthetic evidence\"}"; await store.WriteAsync(row); return row;
        }
        [Test, Order(0)]
        public void Empty_migration_can_reverse_and_reapply()
        {
            var runner = _runner.GetRequiredService<IMigrationRunner>(); runner.MigrateDown(0); runner.MigrateUp(); runner.MigrateUp();
        }
        [Test, Order(10)]
        public async Task Identity_is_allocated_empty_children_are_tenant_scoped_and_labor_is_immutable()
        {
            using var uow = new UnitOfWork(Connections()); var store = Orders(uow);
            await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77); var row = await Insert(store);
            var labor = new WorkOrderLabor { DepartmentId = 77, WorkOrderId = row.Id, UserId = "author", CreatedBy = "author", CreatedOn = DateTime.UtcNow, UpdatedOn = DateTime.UtcNow, WorkDate = DateTime.UtcNow };
            await store.AllocateAsync(labor); labor.Content = "{\"Hours\":2}"; await store.WriteAsync(labor); uow.CommitChanges();
            (await store.GetAsync<WorkOrder>(88, row.Id)).Should().BeNull();
            (await store.ChildrenAsync<WorkOrderLabor>(88, row.Id)).Should().BeEmpty();
            (await store.ChildrenAsync<WorkOrderLabor>(77, row.Id)).Should().ContainSingle();
            await uow.CreateOrGetConnectionAsync(); labor.Content = "overwritten";
            await FluentActions.Awaiting(() => store.WriteAsync(labor)).Should().ThrowAsync<InvalidOperationException>(); uow.DiscardChanges();
            await uow.CreateOrGetConnectionAsync(); var foreign = new WorkOrderPart { DepartmentId = 88, WorkOrderId = row.Id, CreatedBy = "other", CreatedOn = DateTime.UtcNow, UpdatedOn = DateTime.UtcNow };
            await FluentActions.Awaiting(() => store.AllocateAsync(foreign)).Should().ThrowAsync<DbException>(); uow.DiscardChanges();
            (await store.GetAsync<WorkOrderLabor>(77, labor.Id)).Content.Should().Contain("Hours");
        }
        [Test, Order(10)]
        public async Task Two_department_writers_get_distinct_atomic_numbers_and_rollback_leaves_no_order()
        {
            async Task<WorkOrder> Write() { using var uow = new UnitOfWork(Connections()); var store = Orders(uow); await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77); var row = await Insert(store); uow.CommitChanges(); return row; }
            var rows = await Task.WhenAll(Write(), Write()); rows.Select(r => r.NumberSequence).Should().OnlyHaveUniqueItems();
            using var rollback = new UnitOfWork(Connections()); var repository = Orders(rollback);
            await rollback.CreateOrGetConnectionAsync(); await repository.LockDepartmentAsync(77); var abandoned = await Insert(repository); rollback.DiscardChanges();
            (await repository.GetAsync<WorkOrder>(77, abandoned.Id)).Should().BeNull();
            await rollback.CreateOrGetConnectionAsync(); await repository.LockDepartmentAsync(77); var duplicate = Order(); duplicate.NumberSequence = rows[0].NumberSequence;
            await FluentActions.Awaiting(() => repository.AllocateAsync(duplicate)).Should().ThrowAsync<DbException>(); rollback.DiscardChanges();
        }
        [Test, Order(10)]
        public async Task Scoped_queue_filters_role_assignments_and_keeps_page_boundaries()
        {
            using var uow = new UnitOfWork(Connections()); var store = Orders(uow);
            await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
            for (var i = 0; i < 55; i++) { var row = await Insert(store); row.AssignedToRoleId = 456; row.TargetGroupId = 123; await store.WriteAsync(row); }
            uow.CommitChanges();
            var scope = new WorkOrderReadScope { UserId = "technician", RoleIds = new[] { 456 } };
            var first = await store.ListAsync(77, scope, new WorkOrderFilter { AssignedToMe = true });
            var second = await store.ListAsync(77, scope, new WorkOrderFilter { AssignedToMe = true, Page = 1 });
            first.Should().HaveCount(51); second.Should().HaveCount(5); first.Take(50).Concat(second).Select(x => x.Id).Should().OnlyHaveUniqueItems();
            (await store.ListAsync(88, scope, new WorkOrderFilter())).Should().BeEmpty();
            (await store.ListAsync(77, new WorkOrderReadScope { UserId = "outsider" }, new WorkOrderFilter())).Should().BeEmpty();
        }
        [Test, Order(10)]
        public async Task File_metadata_omits_blobs_and_notification_leases_fence_retries()
        {
            using var uow = new UnitOfWork(Connections()); var store = Orders(uow);
            await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77); var row = await Insert(store);
            var file = new WorkOrderFile { DepartmentId = 77, WorkOrderId = row.Id, CreatedBy = "author", CreatedOn = DateTime.UtcNow, UpdatedOn = DateTime.UtcNow, ContentType = "application/pdf", Size = 3, Sha256 = new string('a',64) };
            await store.AllocateAsync(file); file.Content = "synthetic.pdf"; file.Data = new byte[] { 1,2,3 }; await store.WriteAsync(file); uow.CommitChanges();
            (await store.ChildrenAsync<WorkOrderFile>(77, row.Id)).Single().Data.Should().BeNull();
            (await store.GetAsync<WorkOrderFile>(77, file.Id)).Data.Should().Equal(1,2,3);
            var notice = new WorkOrderNotification { DepartmentId = 77, WorkOrderId = row.Id, EventId = Guid.NewGuid().ToString(), UserId = "author", LeaseOwner = Guid.NewGuid().ToString() };
            var now = DateTime.UtcNow; await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
            (await store.ClaimNotificationAsync(notice, now)).Should().Be(1); uow.CommitChanges();
            await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
            (await store.ClaimNotificationAsync(notice, now.AddMinutes(1))).Should().Be(-1);
            var owner = notice.LeaseOwner; notice.LeaseOwner = Guid.NewGuid().ToString();
            (await store.FinishNotificationAsync(notice, 2, now.AddMinutes(1))).Should().BeFalse();
            notice.LeaseOwner = owner; (await store.FinishNotificationAsync(notice, 2, now.AddMinutes(1))).Should().BeTrue(); uow.CommitChanges();
            await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
            (await store.ClaimNotificationAsync(notice, now.AddHours(1))).Should().Be(2); uow.CommitChanges();
        }
        [Test, Order(10)]
        public async Task Billing_account_and_paid_intervals_round_trip_transactionally()
        {
            await using var db = Connect(_connection);
            var payment = new PaymentAddon();
            var columns = typeof(PaymentAddon).GetProperties().Where(p => p.CanWrite && !payment.IgnoredProperties.Contains(p.Name));
            string Type(Type t) => t == typeof(int) ? "int" : t == typeof(long) ? "bigint" : t == typeof(bool) ? (_type == DatabaseTypes.Postgres ? "boolean" : "bit") :
                t == typeof(double) ? (_type == DatabaseTypes.Postgres ? "double precision" : "float") : (t == typeof(DateTime) || t == typeof(DateTime?)) ? (_type == DatabaseTypes.Postgres ? "timestamp" : "datetime2") : TextType;
            await db.ExecuteAsync($"CREATE TABLE {Q("PaymentAddons")} ({string.Join(",", columns.Select(p => Q(p.Name) + " " + Type(p.PropertyType)))})");
            using var uow = new UnitOfWork(Connections()); var store = new ReadinessProBillingRepository(Connections(), Configuration(), uow, Mock.Of<IQueryFactory>());
            var account = new ReadinessProBillingAccount { DepartmentId = 77, Provider = "Stripe", CustomerId = "cus_synthetic", PlanAddonId = Guid.NewGuid().ToString(), PriceId = "price_synthetic", CheckoutAttempt = Guid.NewGuid().ToString(), UpdatedOn = DateTime.UtcNow };
            await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77); await store.SaveAsync(account); uow.CommitChanges();
            (await store.GetAsync(88)).Should().BeNull(); (await store.FindAsync("Stripe", account.CustomerId, null, null)).DepartmentId.Should().Be(77);
            payment.PaymentAddonId = Guid.NewGuid().ToString(); payment.DepartmentId = 77; payment.PlanAddonId = account.PlanAddonId; payment.TransactionId = "in_synthetic"; payment.SubscriptionId = "sub_synthetic"; payment.PurchaseOn = payment.EffectiveOn = DateTime.UtcNow; payment.EndingOn = payment.EffectiveOn.AddMonths(1); payment.Amount = 150; payment.Quantity = 1;
            await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77); await store.SavePaymentAsync(payment, true); uow.CommitChanges();
            (await store.PaymentsAsync(77, account.PlanAddonId)).Should().ContainSingle(); (await store.PaymentsAsync(88, account.PlanAddonId)).Should().BeEmpty();
            payment.IsCancelled = true; await uow.CreateOrGetConnectionAsync(); await store.SavePaymentAsync(payment, false); uow.DiscardChanges();
            (await store.PaymentsAsync(77, account.PlanAddonId)).Single().IsCancelled.Should().BeFalse();
        }
        [Test, Order(1000)]
        public void Populated_migration_refuses_destructive_rollback()
        {
            Action down = () => _runner.GetRequiredService<IMigrationRunner>().MigrateDown(0);
            down.Should().Throw<Exception>();
        }
    }
}
