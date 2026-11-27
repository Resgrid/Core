using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Member wellness WL1 (protocols-training-knowledge-plan §13b): BBP / infectious-disease exposure cases (RmsExposureIncidents), the
	/// designated-officer workflow with Ryan White notification tracking. The case points at the call and the WL0 exposure entry; it holds
	/// no patient identity. No foreign keys, like every Rms* table: RmsDepartmentCleanup finds it by name on department deletion (an active
	/// legal hold defers the deletion), as for WL0. Protected columns (ADP catalog 38) are nvarchar(max) so an envelope fits.
	/// Guarded for a safe retry; Down drops the table.
	/// </summary>
	[Migration(372)]
	public class M0372_AddExposureIncidents : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsExposureIncidents").Exists())
			{
				Create.Table("RmsExposureIncidents")
					.WithColumn("RmsExposureIncidentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("ResponderExposureId").AsString(36).Nullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("Route").AsInt32().NotNullable()
					.WithColumn("OccurredOn").AsDateTime2().NotNullable()
					.WithColumn("Stage").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ReportedOn").AsDateTime2().NotNullable()
					.WithColumn("ReportedByUserId").AsString(128).Nullable()
					.WithColumn("SourceFacility").AsString(200).Nullable()
					.WithColumn("SourceRequestedOn").AsDateTime2().Nullable()
					.WithColumn("NotificationRequestedOn").AsDateTime2().Nullable()
					.WithColumn("NotificationDueOn").AsDateTime2().Nullable()
					.WithColumn("NotificationReceivedOn").AsDateTime2().Nullable()
					.WithColumn("NotificationOverdueNoticeOn").AsDateTime2().Nullable()
					.WithColumn("FollowUpStartedOn").AsDateTime2().Nullable()
					.WithColumn("ClosedOn").AsDateTime2().Nullable()
					.WithColumn("ClosedByUserId").AsString(128).Nullable()
					.WithColumn("WithdrawnOn").AsDateTime2().Nullable()
					.WithColumn("WithdrawnByUserId").AsString(128).Nullable()
					.WithColumn("SourceFollowUpNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("NotificationResponse").AsString(int.MaxValue).Nullable()
					.WithColumn("MedicalNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("ClosureNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1);
			}

			if (!Schema.Table("RmsExposureIncidents").Index("IX_RmsExposureIncidents_User").Exists())
				Create.Index("IX_RmsExposureIncidents_User").OnTable("RmsExposureIncidents")
					.OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().OnColumn("OccurredOn").Ascending();
			if (!Schema.Table("RmsExposureIncidents").Index("IX_RmsExposureIncidents_Exposure").Exists())
				Create.Index("IX_RmsExposureIncidents_Exposure").OnTable("RmsExposureIncidents")
					.OnColumn("DepartmentId").Ascending().OnColumn("ResponderExposureId").Ascending();
			if (!Schema.Table("RmsExposureIncidents").Index("IX_RmsExposureIncidents_Overdue").Exists())
				Create.Index("IX_RmsExposureIncidents_Overdue").OnTable("RmsExposureIncidents")
					.OnColumn("Stage").Ascending().OnColumn("NotificationDueOn").Ascending();
			if (!Schema.Table("RmsExposureIncidents").Index("IX_RmsExposureIncidents_Occurred").Exists())
				Create.Index("IX_RmsExposureIncidents_Occurred").OnTable("RmsExposureIncidents")
					.OnColumn("OccurredOn").Ascending().OnColumn("DepartmentId").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("RmsExposureIncidents").Exists())
				Delete.Table("RmsExposureIncidents");
		}
	}
}
