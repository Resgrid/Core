using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase C (C1): contractor rate schedules with typed entries (per-certification, crew size families, vehicle, equipment, service), explicit rate bands (standby, deployment, overtime thresholds, daily tiers, out-of-province, mileage, per-diem) and stacking premiums. Rates are explicit numbers (decision 16); thresholds are data, not code. Nothing here is under Advanced Data Protection (decision 44; marker columns dropped before release). Registry M0215. Guarded for safe retry.
	/// </summary>
	[Migration(215)]
	public class M0215_AddRateSchedules : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RateSchedules").Exists())
			{
				Create.Table("RateSchedules")
					.WithColumn("RateScheduleId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("Currency").AsString(3).NotNullable().WithDefaultValue("USD")
					.WithColumn("EffectiveOn").AsDateTime2().Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("PolicyJson").AsString(int.MaxValue).Nullable()
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Create.Index("IX_RateSchedules_Department").OnTable("RateSchedules").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending();
			}
			if (!Schema.Table("RateScheduleEntries").Exists())
			{
				Create.Table("RateScheduleEntries")
					.WithColumn("RateScheduleEntryId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("RateScheduleId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("EntryType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Code").AsString(50).Nullable()
					.WithColumn("GroupKey").AsString(50).Nullable()
					.WithColumn("CrewSize").AsInt32().Nullable()
					.WithColumn("CertificationCode").AsString(50).Nullable()
					.WithColumn("UnitTypeId").AsInt32().Nullable()
					.WithColumn("InventoryItemId").AsString(36).Nullable()
					.WithColumn("InventoryCategoryId").AsString(36).Nullable()
					.WithColumn("BillingBasis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RequiredCertificationsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Create.Index("IX_RateScheduleEntries_Schedule").OnTable("RateScheduleEntries").OnColumn("RateScheduleId").Ascending().OnColumn("IsDeleted").Ascending().OnColumn("SortOrder").Ascending();
				Create.Index("IX_RateScheduleEntries_Group").OnTable("RateScheduleEntries").OnColumn("RateScheduleId").Ascending().OnColumn("GroupKey").Ascending();
				Create.ForeignKey("FK_RateScheduleEntries_Schedule").FromTable("RateScheduleEntries").ForeignColumn("RateScheduleId").ToTable("RateSchedules").PrimaryColumn("RateScheduleId");
			}
			if (!Schema.Table("RateScheduleEntryBands").Exists())
			{
				Create.Table("RateScheduleEntryBands")
					.WithColumn("RateScheduleEntryBandId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("RateScheduleEntryId").AsString(36).NotNullable()
					.WithColumn("BandType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Rate").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("ThresholdStartHours").AsDecimal(9,2).Nullable()
					.WithColumn("ThresholdEndHours").AsDecimal(9,2).Nullable()
					.WithColumn("DailyTierMinHours").AsDecimal(9,2).Nullable()
					.WithColumn("DailyTierMaxHours").AsDecimal(9,2).Nullable()
					.WithColumn("FreeUnitsPerDay").AsDecimal(9,2).Nullable()
					.WithColumn("RequiresAirTravel").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("MealCode").AsString(10).Nullable()
					.WithColumn("Label").AsString(200).Nullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0);
				Create.Index("IX_RateScheduleEntryBands_Entry").OnTable("RateScheduleEntryBands").OnColumn("RateScheduleEntryId").Ascending().OnColumn("SortOrder").Ascending();
				Create.ForeignKey("FK_RateScheduleEntryBands_Entry").FromTable("RateScheduleEntryBands").ForeignColumn("RateScheduleEntryId").ToTable("RateScheduleEntries").PrimaryColumn("RateScheduleEntryId");
			}
			if (!Schema.Table("RatePremiums").Exists())
			{
				Create.Table("RatePremiums")
					.WithColumn("RatePremiumId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("RateScheduleId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Code").AsString(50).Nullable()
					.WithColumn("StandbyAdder").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("DeploymentAdder").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("Overtime1Adder").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("Overtime2Adder").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Create.Index("IX_RatePremiums_Schedule").OnTable("RatePremiums").OnColumn("RateScheduleId").Ascending().OnColumn("IsDeleted").Ascending();
				Create.ForeignKey("FK_RatePremiums_Schedule").FromTable("RatePremiums").ForeignColumn("RateScheduleId").ToTable("RateSchedules").PrimaryColumn("RateScheduleId");
			}
		}

		public override void Down()
		{
			if (Schema.Table("RatePremiums").Exists()) Delete.Table("RatePremiums");
			if (Schema.Table("RateScheduleEntryBands").Exists()) Delete.Table("RateScheduleEntryBands");
			if (Schema.Table("RateScheduleEntries").Exists()) Delete.Table("RateScheduleEntries");
			if (Schema.Table("RateSchedules").Exists()) Delete.Table("RateSchedules");
		}
	}
}
