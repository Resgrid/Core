using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(197)]
	public class M0197_AddWorkOrders : Migration
	{
		private static string N(string value) => value;
		public override void Up()
		{
			foreach (var name in new[] { "WorkOrders", "WorkOrderActivities", "WorkOrderLabors", "WorkOrderParts", "WorkOrderFiles" })
			{
				var table = Create.Table(N(name)).WithColumn(N("Id")).AsInt32().PrimaryKey().Identity()
					.WithColumn(N("DepartmentId")).AsInt32().NotNullable()
					.WithColumn(N("WorkOrderId")).AsInt32().Nullable()
					.WithColumn(N("Content")).AsString(int.MaxValue).Nullable()
					.WithColumn(N("Revision")).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn(N("CreatedOn")).AsDateTime2().NotNullable()
					.WithColumn(N("UpdatedOn")).AsDateTime2().NotNullable()
					.WithColumn(N("CreatedBy")).AsString(128).NotNullable()
					.WithColumn(N("IsProtected")).AsBoolean().NotNullable().WithDefaultValue(false);
				switch (name)
				{
					case "WorkOrders":
						table.WithColumn(N("RequestId")).AsString(36).NotNullable()
							.WithColumn(N("NumberYear")).AsInt32().NotNullable().WithColumn(N("NumberSequence")).AsInt32().NotNullable()
							.WithColumn(N("Type")).AsInt32().NotNullable().WithColumn(N("Priority")).AsInt32().NotNullable().WithColumn(N("Status")).AsInt32().NotNullable()
							.WithColumn(N("SourceType")).AsInt32().NotNullable()
							.WithColumn(N("SourceChecklistCompletionId")).AsString(36).Nullable().WithColumn(N("SourceChecklistItemId")).AsString(36).Nullable().WithColumn(N("SourceOccurrenceId")).AsString(36).Nullable()
							.WithColumn(N("TargetUnitId")).AsInt32().Nullable().WithColumn(N("TargetGroupId")).AsInt32().Nullable().WithColumn(N("InventoryAssetId")).AsString(36).Nullable()
							.WithColumn(N("AssignedToUserId")).AsString(128).Nullable().WithColumn(N("AssignedToRoleId")).AsInt32().Nullable()
							.WithColumn(N("DueOn")).AsDateTime2().Nullable().WithColumn(N("TriagedOn")).AsDateTime2().Nullable().WithColumn(N("AssignedOn")).AsDateTime2().Nullable()
							.WithColumn(N("AssignmentAcceptedOn")).AsDateTime2().Nullable().WithColumn(N("AssignmentAcceptedBy")).AsString(128).Nullable()
							.WithColumn(N("StartedOn")).AsDateTime2().Nullable().WithColumn(N("CompletedOn")).AsDateTime2().Nullable().WithColumn(N("CompletedBy")).AsString(128).Nullable()
							.WithColumn(N("ClosedOn")).AsDateTime2().Nullable().WithColumn(N("VerifiedBy")).AsString(128).Nullable().WithColumn(N("DuplicateOfId")).AsInt32().Nullable()
							.WithColumn(N("SetUnitOutOfService")).AsBoolean().NotNullable().WithDefaultValue(false).WithColumn(N("RestoreUnitStateOnClose")).AsBoolean().NotNullable().WithDefaultValue(false)
							.WithColumn(N("PreviousUnitStateType")).AsInt32().Nullable().WithColumn(N("WorkOrderRecurrenceId")).AsString(36).Nullable()
							.WithColumn(N("IsDeleted")).AsBoolean().NotNullable().WithDefaultValue(false); break;
					case "WorkOrderActivities": table.WithColumn(N("ActivityType")).AsInt32().NotNullable().WithColumn(N("OldStatus")).AsInt32().Nullable().WithColumn(N("NewStatus")).AsInt32().Nullable(); break;
					case "WorkOrderLabors": table.WithColumn(N("UserId")).AsString(128).NotNullable().WithColumn(N("WorkDate")).AsDateTime2().NotNullable(); break;
					case "WorkOrderParts": table.WithColumn(N("InventoryItemId")).AsString(36).Nullable().WithColumn(N("InventoryTransactionId")).AsString(36).Nullable().WithColumn(N("VoidedOn")).AsDateTime2().Nullable(); break;
					case "WorkOrderFiles": table.WithColumn(N("ContentType")).AsString(100).NotNullable().WithColumn(N("Size")).AsInt32().NotNullable().WithColumn(N("Sha256")).AsString(64).NotNullable()
						.WithColumn(N("Data")).AsBinary(int.MaxValue).Nullable().WithColumn(N("ScanState")).AsInt32().NotNullable().WithColumn(N("WithdrawnOn")).AsDateTime2().Nullable(); break;
				}
				if (name != "WorkOrders") Alter.Column(N("WorkOrderId")).OnTable(N(name)).AsInt32().NotNullable();
				Index(name, "TenantId", true, "DepartmentId", "Id");
				Index(name, "ParentDate", false, "DepartmentId", "WorkOrderId", "CreatedOn");
				if (name != "WorkOrders") Create.ForeignKey(N("FK_" + name + "_Order")).FromTable(N(name)).ForeignColumns(N("DepartmentId"), N("WorkOrderId")).ToTable(N("WorkOrders")).PrimaryColumns(N("DepartmentId"), N("Id"));
			}
			Create.Table(N("ReadinessProBillingAccounts"))
				.WithColumn(N("DepartmentId")).AsInt32().NotNullable().PrimaryKey()
				.WithColumn(N("Provider")).AsString(20).NotNullable()
				.WithColumn(N("CustomerId")).AsString(100).NotNullable()
				.WithColumn(N("PlanAddonId")).AsString(36).NotNullable()
				.WithColumn(N("PriceId")).AsString(100).NotNullable()
				.WithColumn(N("SubscriptionId")).AsString(100).Nullable()
				.WithColumn(N("CheckoutId")).AsString(100).Nullable()
				.WithColumn(N("CheckoutUrl")).AsString(2048).Nullable()
				.WithColumn(N("CheckoutExpiresOn")).AsDateTime2().Nullable()
				.WithColumn(N("CheckoutAttempt")).AsString(36).Nullable()
				.WithColumn(N("UpdatedOn")).AsDateTime2().NotNullable();
			Create.Table(N("WorkOrderNotifications"))
				.WithColumn(N("DepartmentId")).AsInt32().NotNullable().PrimaryKey()
				.WithColumn(N("EventId")).AsString(36).NotNullable().PrimaryKey()
				.WithColumn(N("UserId")).AsString(128).NotNullable().PrimaryKey()
				.WithColumn(N("WorkOrderId")).AsInt32().NotNullable()
				.WithColumn(N("State")).AsInt32().NotNullable()
				.WithColumn(N("LeaseOwner")).AsString(36).Nullable()
				.WithColumn(N("LeaseExpiresOn")).AsDateTime2().Nullable()
				.WithColumn(N("UpdatedOn")).AsDateTime2().NotNullable();
			Create.ForeignKey(N("FK_WorkOrderNotifications_Order")).FromTable(N("WorkOrderNotifications")).ForeignColumns(N("DepartmentId"), N("WorkOrderId")).ToTable(N("WorkOrders")).PrimaryColumns(N("DepartmentId"), N("Id"));
			Index("WorkOrders", "Number", true, "DepartmentId", "NumberYear", "NumberSequence");
			Index("WorkOrders", "Request", true, "DepartmentId", "RequestId");
			Index("WorkOrders", "StatusPriority", false, "DepartmentId", "Status", "Priority", "Id");
			Index("WorkOrders", "Assignee", false, "DepartmentId", "AssignedToUserId", "Status");
			Index("WorkOrders", "Role", false, "DepartmentId", "AssignedToRoleId", "Status");
			Index("WorkOrders", "Unit", false, "DepartmentId", "TargetUnitId", "Id");
			Index("WorkOrders", "Group", false, "DepartmentId", "TargetGroupId", "Id");
			Index("WorkOrders", "Asset", false, "DepartmentId", "InventoryAssetId", "Id");
			Index("WorkOrders", "Source", false, "DepartmentId", "SourceOccurrenceId", "SourceChecklistItemId");
			Create.ForeignKey(N("FK_WorkOrders_Duplicate")).FromTable(N("WorkOrders")).ForeignColumns(N("DepartmentId"), N("DuplicateOfId")).ToTable(N("WorkOrders")).PrimaryColumns(N("DepartmentId"), N("Id"));
		}
		private void Index(string table, string suffix, bool unique, params string[] columns)
		{
			var index = Create.Index(N((unique ? "UX_" : "IX_") + table + "_" + suffix)).OnTable(N(table)).OnColumn(N(columns[0])).Ascending();
			for (var i = 1; i < columns.Length; i++) index = index.OnColumn(N(columns[i])).Ascending();
			if (unique) index.WithOptions().Unique();
		}
		public override void Down()
		{
			// Never discard maintenance evidence or encrypted envelopes during a schema rollback.
			Execute.Sql("IF EXISTS (SELECT 1 FROM WorkOrders) OR EXISTS (SELECT 1 FROM ReadinessProBillingAccounts) THROW 51000, 'Work-order evidence must be exported and removed through the authorized retention process before rollback.', 1;");
			Delete.ForeignKey(N("FK_WorkOrders_Duplicate")).OnTable(N("WorkOrders"));
			foreach (var table in new[] { "ReadinessProBillingAccounts", "WorkOrderNotifications", "WorkOrderFiles", "WorkOrderParts", "WorkOrderLabors", "WorkOrderActivities", "WorkOrders" }) Delete.Table(N(table));
		}
	}
}
