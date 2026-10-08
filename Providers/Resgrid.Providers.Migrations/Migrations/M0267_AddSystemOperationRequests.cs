using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// System operation requests: staff ask the worker (command 76) from BackOffice -> System Operations to rebuild cached
	/// state (the Redis security matrices, department caches) or run a daily job now; the worker also queues a matrix
	/// rebuild on its own when it finds Redis came back empty. The worker claims the oldest Pending row with a conditional
	/// update, refreshes HeartbeatOn while it runs, and records the outcome. A system record: no foreign key to
	/// Departments and not part of the department purge (TargetDepartmentId only narrows an operation).
	/// </summary>
	[Migration(267)]
	public class M0267_AddSystemOperationRequests : Migration
	{
		private const string Table = "SystemOperationRequests";

		public override void Up()
		{
			if (!Schema.Table(Table).Exists())
			{
				Create.Table(Table)
					.WithColumn("SystemOperationRequestId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("OperationType").AsInt32().NotNullable()
					.WithColumn("TargetDepartmentId").AsInt32().Nullable()
					.WithColumn("Status").AsInt32().NotNullable()
					.WithColumn("Source").AsInt32().NotNullable()
					.WithColumn("RequestedBy").AsString(256).NotNullable()
					.WithColumn("Reason").AsString(500).Nullable()
					.WithColumn("RequestedOn").AsDateTime2().NotNullable()
					.WithColumn("StartedOn").AsDateTime2().Nullable()
					.WithColumn("HeartbeatOn").AsDateTime2().Nullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable()
					.WithColumn("WorkerName").AsString(256).Nullable()
					.WithColumn("Progress").AsString(500).Nullable()
					.WithColumn("Result").AsString(2000).Nullable()
					.WithColumn("CancelledBy").AsString(256).Nullable();
			}

			if (!Schema.Table(Table).Index("IX_SystemOperationRequests_Status_RequestedOn").Exists())
			{
				Create.Index("IX_SystemOperationRequests_Status_RequestedOn")
					.OnTable(Table)
					.OnColumn("Status").Ascending()
					.OnColumn("RequestedOn").Ascending();
			}

			if (!Schema.Table(Table).Index("IX_SystemOperationRequests_RequestedOn").Exists())
			{
				Create.Index("IX_SystemOperationRequests_RequestedOn")
					.OnTable(Table)
					.OnColumn("RequestedOn").Descending();
			}
		}

		public override void Down()
		{
			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}
