using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Civilian and responder casualties, rescues, and property exposures (RMS plan section 4.2, registry M0168,
	/// package RMS-3): RmsCasualtyRescues and RmsExposures.
	/// <para>
	/// Both are restricted classes. Demographics, the injury detail and the vehicle/person identity require
	/// RecordRestricted_View wherever they are rendered, both carry the inert Protected Data envelope of plan
	/// section 5.9.1, and their retention default is permanent (plan section 4.9) — so nothing here is ever
	/// purged by the retention sweep without an explicit department override.
	/// </para>
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(168)]
	public class M0168_AddRmsCasualtyRescueExposure : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsCasualtyRescues").Exists())
			{
				Create.Table("RmsCasualtyRescues")
					.WithColumn("RmsCasualtyRescueId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("Kind").AsInt32().NotNullable()
					.WithColumn("PersonType").AsString(10).NotNullable()
					.WithColumn("PersonnelUserId").AsString(128).Nullable()
					.WithColumn("Rank").AsString(255).Nullable()
					.WithColumn("YearsOfService").AsDecimal(6, 2).Nullable()
					.WithColumn("JobClassification").AsString(50).Nullable()
					.WithColumn("BirthMonthYear").AsString(7).Nullable()
					.WithColumn("Gender").AsString(50).Nullable()
					.WithColumn("Race").AsString(50).Nullable()
					.WithColumn("WasInjured").AsBoolean().Nullable()
					.WithColumn("CasualtyCause").AsString(100).Nullable()
					.WithColumn("CasualtyAction").AsString(100).Nullable()
					.WithColumn("CasualtyTimeline").AsString(100).Nullable()
					.WithColumn("DutyType").AsString(100).Nullable()
					.WithColumn("PpeCsv").AsString(500).Nullable()
					.WithColumn("InjuryDetailJson").AsString(int.MaxValue).Nullable()
					.WithColumn("WasFatal").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RescueType").AsString(100).Nullable()
					.WithColumn("RescueActionsCsv").AsString(500).Nullable()
					.WithColumn("RescueImpedimentsCsv").AsString(500).Nullable()
					.WithColumn("RescueMode").AsString(100).Nullable()
					.WithColumn("RescuePath").AsString(100).Nullable()
					.WithColumn("RescueElevation").AsString(100).Nullable()
					.WithColumn("PresenceKnown").AsString(100).Nullable()
					.WithColumn("DetailJson").AsString(int.MaxValue).Nullable()
					.WithColumn("OccurredOn").AsDateTime2().Nullable()
					.WithColumn("ProtectedEnvelope").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsCasualtyRescues_Department_Record_Revision").OnTable("RmsCasualtyRescues")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("RevisionId").Ascending();
				// Responder injury reporting: a member's own casualty history across incidents.
				Create.Index("IX_RmsCasualtyRescues_Department_Personnel").OnTable("RmsCasualtyRescues")
					.OnColumn("DepartmentId").Ascending().OnColumn("PersonnelUserId").Ascending();
			}

			if (!Schema.Table("RmsExposures").Exists())
			{
				Create.Table("RmsExposures")
					.WithColumn("RmsExposureId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("LocationKind").AsString(20).Nullable()
					.WithColumn("ItemType").AsString(50).Nullable()
					.WithColumn("DamageType").AsString(50).Nullable()
					.WithColumn("LocationUse").AsString(150).Nullable()
					.WithColumn("PeoplePresent").AsBoolean().Nullable()
					.WithColumn("DisplacementCount").AsInt32().Nullable()
					.WithColumn("DisplacementCausesCsv").AsString(500).Nullable()
					.WithColumn("AddressText").AsString(500).Nullable()
					.WithColumn("Street").AsString(200).Nullable()
					.WithColumn("Municipality").AsString(150).Nullable()
					.WithColumn("State").AsString(10).Nullable()
					.WithColumn("PostalCode").AsString(20).Nullable()
					.WithColumn("Latitude").AsDecimal(12, 8).Nullable()
					.WithColumn("Longitude").AsDecimal(12, 8).Nullable()
					.WithColumn("EstimatedValue").AsDecimal(18, 2).Nullable()
					.WithColumn("EstimatedLoss").AsDecimal(18, 2).Nullable()
					.WithColumn("CurrencyCode").AsString(3).Nullable()
					.WithColumn("DetailJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ProtectedEnvelope").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsExposures_Department_Record_Revision").OnTable("RmsExposures")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("RevisionId").Ascending();
			}
		}

		public override void Down()
		{
			foreach (var table in new[] { "RmsExposures", "RmsCasualtyRescues" })
			{
				if (Schema.Table(table).Exists())
					Delete.Table(table);
			}
		}
	}
}
