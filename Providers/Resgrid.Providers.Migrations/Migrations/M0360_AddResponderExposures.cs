using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Member wellness WL0 (protocols-training-knowledge-plan §13b; RMS plan §5.2 RmsResponderExposure): the per-member exposure and
	/// critical-incident log (RmsResponderExposures), post-call flags on calls (RmsCallExposureFlags) and at-risk screening flags
	/// (RmsResponderScreeningFlags). No foreign keys, like every Rms* table: RmsDepartmentCleanup finds them by name on department
	/// deletion (an active legal hold defers it). The protected columns (ADP catalog 37) are nvarchar(max) so an envelope fits.
	/// Guarded for a safe retry; Down drops the three tables.
	/// </summary>
	[Migration(360)]
	public class M0360_AddResponderExposures : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsResponderExposures").Exists())
			{
				Create.Table("RmsResponderExposures")
					.WithColumn("RmsResponderExposureId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("Category").AsInt32().NotNullable()
					.WithColumn("OccurredOn").AsDateTime2().NotNullable()
					.WithColumn("DurationMinutes").AsInt32().Nullable()
					.WithColumn("Status").AsInt32().NotNullable()
					.WithColumn("Source").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CallExposureFlagId").AsString(36).Nullable()
					.WithColumn("SuggestionBasis").AsString(200).Nullable()
					.WithColumn("Substance").AsString(int.MaxValue).Nullable()
					.WithColumn("Narrative").AsString(int.MaxValue).Nullable()
					.WithColumn("PpeWorn").AsString(int.MaxValue).Nullable()
					.WithColumn("Decontamination").AsString(int.MaxValue).Nullable()
					.WithColumn("HealthDetails").AsString(int.MaxValue).Nullable()
					.WithColumn("RemindMember").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RemindPeerTeam").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("NextAnniversaryOn").AsDateTime2().Nullable()
					.WithColumn("LastReminderSentOn").AsDateTime2().Nullable()
					.WithColumn("ConfirmedOn").AsDateTime2().Nullable()
					.WithColumn("ConfirmedByUserId").AsString(128).Nullable()
					.WithColumn("DismissedOn").AsDateTime2().Nullable()
					.WithColumn("DismissedByUserId").AsString(128).Nullable()
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

			if (!Schema.Table("RmsResponderExposures").Index("IX_RmsResponderExposures_User").Exists())
				Create.Index("IX_RmsResponderExposures_User").OnTable("RmsResponderExposures")
					.OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().OnColumn("OccurredOn").Ascending();
			if (!Schema.Table("RmsResponderExposures").Index("IX_RmsResponderExposures_Call").Exists())
				Create.Index("IX_RmsResponderExposures_Call").OnTable("RmsResponderExposures")
					.OnColumn("DepartmentId").Ascending().OnColumn("CallId").Ascending();
			if (!Schema.Table("RmsResponderExposures").Index("IX_RmsResponderExposures_Anniversary").Exists())
				Create.Index("IX_RmsResponderExposures_Anniversary").OnTable("RmsResponderExposures")
					.OnColumn("NextAnniversaryOn").Ascending().OnColumn("DepartmentId").Ascending();
			if (!Schema.Table("RmsResponderExposures").Index("IX_RmsResponderExposures_Occurred").Exists())
				Create.Index("IX_RmsResponderExposures_Occurred").OnTable("RmsResponderExposures")
					.OnColumn("OccurredOn").Ascending().OnColumn("DepartmentId").Ascending();

			if (!Schema.Table("RmsCallExposureFlags").Exists())
			{
				Create.Table("RmsCallExposureFlags")
					.WithColumn("RmsCallExposureFlagId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("Category").AsInt32().NotNullable()
					.WithColumn("FlaggedOn").AsDateTime2().NotNullable()
					.WithColumn("FlaggedByUserId").AsString(128).Nullable()
					.WithColumn("SuggestionsGeneratedOn").AsDateTime2().Nullable()
					.WithColumn("SuggestionCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ClearedOn").AsDateTime2().Nullable()
					.WithColumn("ClearedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1);
			}

			if (!Schema.Table("RmsCallExposureFlags").Index("UX_RmsCallExposureFlags_CallCategory").Exists())
				Create.Index("UX_RmsCallExposureFlags_CallCategory").OnTable("RmsCallExposureFlags")
					.OnColumn("DepartmentId").Ascending().OnColumn("CallId").Ascending().OnColumn("Category").Ascending()
					.WithOptions().Unique();
			if (!Schema.Table("RmsCallExposureFlags").Index("IX_RmsCallExposureFlags_Pending").Exists())
				Create.Index("IX_RmsCallExposureFlags_Pending").OnTable("RmsCallExposureFlags")
					.OnColumn("SuggestionsGeneratedOn").Ascending().OnColumn("FlaggedOn").Ascending();

			if (!Schema.Table("RmsResponderScreeningFlags").Exists())
			{
				Create.Table("RmsResponderScreeningFlags")
					.WithColumn("RmsResponderScreeningFlagId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("Category").AsInt32().Nullable()
					.WithColumn("ReviewOn").AsDateTime2().Nullable()
					.WithColumn("PlanNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("FlaggedOn").AsDateTime2().NotNullable()
					.WithColumn("FlaggedByUserId").AsString(128).Nullable()
					.WithColumn("ClearedOn").AsDateTime2().Nullable()
					.WithColumn("ClearedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1);
			}

			if (!Schema.Table("RmsResponderScreeningFlags").Index("IX_RmsResponderScreeningFlags_User").Exists())
				Create.Index("IX_RmsResponderScreeningFlags_User").OnTable("RmsResponderScreeningFlags")
					.OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("RmsResponderScreeningFlags").Exists())
				Delete.Table("RmsResponderScreeningFlags");
			if (Schema.Table("RmsCallExposureFlags").Exists())
				Delete.Table("RmsCallExposureFlags");
			if (Schema.Table("RmsResponderExposures").Exists())
				Delete.Table("RmsResponderExposures");
		}
	}
}
