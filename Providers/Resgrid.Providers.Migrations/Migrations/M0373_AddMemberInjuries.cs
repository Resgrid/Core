using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Member wellness WL1 (§13b): injuries with the OSHA 300/301 facts (RmsMemberInjuries) and their duty status history
	/// (RmsInjuryDutyPeriods), the source of the OSHA day counts. Case numbers are unique per department and OSHA year. No foreign keys:
	/// RmsDepartmentCleanup purges both on department deletion (a legal hold defers it). Protected columns (ADP catalog 38) are
	/// nvarchar(max). Guarded for a safe retry; Down drops both tables.
	/// </summary>
	[Migration(373)]
	public class M0373_AddMemberInjuries : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsMemberInjuries").Exists())
			{
				Create.Table("RmsMemberInjuries")
					.WithColumn("RmsMemberInjuryId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("ExposureIncidentId").AsString(36).Nullable()
					.WithColumn("OccurredOn").AsDateTime2().NotNullable()
					.WithColumn("ReportedOn").AsDateTime2().NotNullable()
					.WithColumn("ReportedByUserId").AsString(128).Nullable()
					.WithColumn("OshaYear").AsInt32().NotNullable()
					.WithColumn("OshaCaseNumber").AsInt32().NotNullable()
					.WithColumn("Location").AsString(200).Nullable()
					.WithColumn("JobTitle").AsString(100).Nullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DutyStatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DutyStatusSince").AsDateTime2().Nullable()
					.WithColumn("ExpectedReturnOn").AsDateTime2().Nullable()
					.WithColumn("ReturnReviewNoticeOn").AsDateTime2().Nullable()
					.WithColumn("ReturnedToDutyOn").AsDateTime2().Nullable()
					.WithColumn("ClearedOn").AsDateTime2().Nullable()
					.WithColumn("ClearedByUserId").AsString(128).Nullable()
					.WithColumn("IsRecordable").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("OshaInjuryType").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsPrivacyCase").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("TreatedInEmergencyRoom").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("HospitalizedOvernight").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DiedOn").AsDateTime2().Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("InjuryDetails").AsString(int.MaxValue).Nullable()
					.WithColumn("Restrictions").AsString(int.MaxValue).Nullable()
					.WithColumn("TreatmentDetails").AsString(int.MaxValue).Nullable()
					.WithColumn("ClearanceNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("VoidedOn").AsDateTime2().Nullable()
					.WithColumn("VoidedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1);
			}

			if (!Schema.Table("RmsMemberInjuries").Index("IX_RmsMemberInjuries_User").Exists())
				Create.Index("IX_RmsMemberInjuries_User").OnTable("RmsMemberInjuries")
					.OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().OnColumn("OccurredOn").Ascending();
			if (!Schema.Table("RmsMemberInjuries").Index("UX_RmsMemberInjuries_Case").Exists())
				Create.Index("UX_RmsMemberInjuries_Case").OnTable("RmsMemberInjuries")
					.OnColumn("DepartmentId").Ascending().OnColumn("OshaYear").Ascending().OnColumn("OshaCaseNumber").Ascending()
					.WithOptions().Unique();
			if (!Schema.Table("RmsMemberInjuries").Index("IX_RmsMemberInjuries_Review").Exists())
				Create.Index("IX_RmsMemberInjuries_Review").OnTable("RmsMemberInjuries")
					.OnColumn("State").Ascending().OnColumn("ExpectedReturnOn").Ascending();
			if (!Schema.Table("RmsMemberInjuries").Index("IX_RmsMemberInjuries_Year").Exists())
				Create.Index("IX_RmsMemberInjuries_Year").OnTable("RmsMemberInjuries")
					.OnColumn("OshaYear").Ascending().OnColumn("DepartmentId").Ascending();

			if (!Schema.Table("RmsInjuryDutyPeriods").Exists())
			{
				Create.Table("RmsInjuryDutyPeriods")
					.WithColumn("RmsInjuryDutyPeriodId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("InjuryId").AsString(36).NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("DutyStatus").AsInt32().NotNullable()
					.WithColumn("StartsOn").AsDateTime2().NotNullable()
					.WithColumn("EndsOn").AsDateTime2().Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable();
			}

			if (!Schema.Table("RmsInjuryDutyPeriods").Index("IX_RmsInjuryDutyPeriods_Injury").Exists())
				Create.Index("IX_RmsInjuryDutyPeriods_Injury").OnTable("RmsInjuryDutyPeriods")
					.OnColumn("DepartmentId").Ascending().OnColumn("InjuryId").Ascending().OnColumn("StartsOn").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("RmsInjuryDutyPeriods").Exists())
				Delete.Table("RmsInjuryDutyPeriods");
			if (Schema.Table("RmsMemberInjuries").Exists())
				Delete.Table("RmsMemberInjuries");
		}
	}
}
