using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Work assignments on Records (RMS plan section 5.2 RmsRecordWorkAssignment, RMS-1D, registry M0179): an
	/// optional person / unit / group / command-role / dispatch-role assignment with purpose, due, acknowledged,
	/// completed and cancelled state, safe source context and the client origin. Guarded for safe retry.
	/// </summary>
	[Migration(179)]
	public class M0179_AddRmsRecordWorkAssignments : Migration
	{
		public override void Up()
		{
			if (Schema.Table("RmsRecordWorkAssignments").Exists())
				return;

			Create.Table("RmsRecordWorkAssignments")
				.WithColumn("RmsRecordWorkAssignmentId").AsString(36).NotNullable().PrimaryKey()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("ProtectionId").AsString(36).Nullable()
				.WithColumn("RecordId").AsString(36).NotNullable()
				.WithColumn("AssigneeKind").AsInt32().NotNullable()
				.WithColumn("AssigneeUserId").AsString(128).Nullable()
				.WithColumn("AssigneeUnitId").AsInt32().Nullable()
				.WithColumn("AssigneeGroupId").AsInt32().Nullable()
				.WithColumn("AssigneeRole").AsString(100).Nullable()
				.WithColumn("Purpose").AsString(32).NotNullable()
				.WithColumn("Note").AsString(1000).Nullable()
				.WithColumn("SourceContextJson").AsString(1000).Nullable()
				.WithColumn("DueOn").AsDateTime2().Nullable()
				.WithColumn("State").AsInt32().NotNullable()
				.WithColumn("AcknowledgedOn").AsDateTime2().Nullable()
				.WithColumn("AcknowledgedByUserId").AsString(128).Nullable()
				.WithColumn("CompletedOn").AsDateTime2().Nullable()
				.WithColumn("CompletedByUserId").AsString(128).Nullable()
				.WithColumn("CancelledOn").AsDateTime2().Nullable()
				.WithColumn("CancelledByUserId").AsString(128).Nullable()
				.WithColumn("CancelReason").AsString(500).Nullable()
				.WithColumn("OriginClient").AsInt32().NotNullable().WithDefaultValue(1)
				.WithColumn("CreatedOn").AsDateTime2().NotNullable()
				.WithColumn("CreatedByUserId").AsString(128).Nullable()
				.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
				.WithColumn("ModifiedByUserId").AsString(128).Nullable()
				.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1)
				.WithColumn("DeletedOn").AsDateTime2().Nullable();

			Create.Index("IX_RmsRecordWorkAssignments_Record").OnTable("RmsRecordWorkAssignments")
				.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
			Create.Index("IX_RmsRecordWorkAssignments_Person").OnTable("RmsRecordWorkAssignments")
				.OnColumn("DepartmentId").Ascending().OnColumn("AssigneeUserId").Ascending().OnColumn("State").Ascending();
			Create.Index("IX_RmsRecordWorkAssignments_Unit").OnTable("RmsRecordWorkAssignments")
				.OnColumn("DepartmentId").Ascending().OnColumn("AssigneeUnitId").Ascending().OnColumn("State").Ascending();
			Create.Index("IX_RmsRecordWorkAssignments_Modified").OnTable("RmsRecordWorkAssignments")
				.OnColumn("DepartmentId").Ascending().OnColumn("ModifiedOn").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("RmsRecordWorkAssignments").Exists())
				Delete.Table("RmsRecordWorkAssignments");
		}
	}
}
