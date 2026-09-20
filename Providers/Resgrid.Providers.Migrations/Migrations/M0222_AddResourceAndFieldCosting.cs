using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase E (E2): resource cost profiles and components, usage entries and the internal field-cost runs / lines. Personnel line detail is an ADP catalog 28 column. Registry M0222. Guarded for safe retry.
	/// </summary>
	[Migration(222)]
	public class M0222_AddResourceAndFieldCosting : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ResourceCostProfiles").Exists())
			{
				Create.Table("ResourceCostProfiles")
					.WithColumn("ResourceCostProfileId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("SubjectType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UnitId").AsInt32().Nullable()
					.WithColumn("InventoryAssetId").AsString(36).Nullable()
					.WithColumn("ExternalResourceKey").AsString(100).Nullable()
					.WithColumn("Name").AsString(250).Nullable()
					.WithColumn("EffectiveOn").AsDateTime2().NotNullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("Currency").AsString(3).Nullable()
					.WithColumn("AcquisitionCost").AsDecimal(18,2).Nullable()
					.WithColumn("AcquisitionDate").AsDateTime2().Nullable()
					.WithColumn("InServiceDate").AsDateTime2().Nullable()
					.WithColumn("SalvageValue").AsDecimal(18,2).Nullable()
					.WithColumn("DepreciationMethod").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AllocationBasis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UsefulLifeQuantity").AsDecimal(18,2).Nullable()
					.WithColumn("UsefulLifeMonths").AsInt32().Nullable()
					.WithColumn("ExpectedAnnualUtilization").AsDecimal(18,2).Nullable()
					.WithColumn("Source").AsString(100).Nullable()
					.WithColumn("IsApproved").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ApprovedByUserId").AsString(128).Nullable()
					.WithColumn("ApprovedOn").AsDateTime2().Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Create.Index("IX_ResourceCostProfiles_Department").OnTable("ResourceCostProfiles").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending();
				Create.Index("IX_ResourceCostProfiles_Unit").OnTable("ResourceCostProfiles").OnColumn("UnitId").Ascending();
			}
			if (!Schema.Table("ResourceCostComponents").Exists())
			{
				Create.Table("ResourceCostComponents")
					.WithColumn("ResourceCostComponentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ResourceCostProfileId").AsString(36).Nullable()
					.WithColumn("EffectiveOn").AsDateTime2().Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("Category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Basis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Rate").AsDecimal(18,4).Nullable()
					.WithColumn("ConsumptionQuantity").AsDecimal(18,4).Nullable()
					.WithColumn("ConsumptionUnit").AsString(20).Nullable()
					.WithColumn("UnitPrice").AsDecimal(18,4).Nullable()
					.WithColumn("Source").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceWindowStart").AsDateTime2().Nullable()
					.WithColumn("SourceWindowEnd").AsDateTime2().Nullable()
					.WithColumn("SourceMeterStart").AsDecimal(18,2).Nullable()
					.WithColumn("SourceMeterEnd").AsDecimal(18,2).Nullable()
					.WithColumn("IsApproved").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Create.Index("IX_ResourceCostComponents_Profile").OnTable("ResourceCostComponents").OnColumn("ResourceCostProfileId").Ascending();
			}
			if (!Schema.Table("ResourceUsageEntries").Exists())
			{
				Create.Table("ResourceUsageEntries")
					.WithColumn("ResourceUsageEntryId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("SubjectType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UnitId").AsInt32().Nullable()
					.WithColumn("InventoryAssetId").AsString(36).Nullable()
					.WithColumn("ExternalResourceKey").AsString(100).Nullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("DeploymentId").AsString(36).Nullable()
					.WithColumn("DeploymentTimeReportId").AsString(36).Nullable()
					.WithColumn("UsageDate").AsDateTime2().NotNullable()
					.WithColumn("Phase").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StartOdometer").AsDecimal(18,2).Nullable()
					.WithColumn("EndOdometer").AsDecimal(18,2).Nullable()
					.WithColumn("DistanceUnit").AsString(5).Nullable()
					.WithColumn("OriginalDistance").AsDecimal(18,2).Nullable()
					.WithColumn("CanonicalDistanceMiles").AsDecimal(18,2).Nullable()
					.WithColumn("StartEngineMeter").AsDecimal(18,2).Nullable()
					.WithColumn("EndEngineMeter").AsDecimal(18,2).Nullable()
					.WithColumn("EngineHours").AsDecimal(18,2).Nullable()
					.WithColumn("OperatingHours").AsDecimal(18,2).Nullable()
					.WithColumn("IdleHours").AsDecimal(18,2).Nullable()
					.WithColumn("DeployedDays").AsDecimal(9,2).Nullable()
					.WithColumn("StandbyDays").AsDecimal(9,2).Nullable()
					.WithColumn("FuelQuantity").AsDecimal(18,2).Nullable()
					.WithColumn("FuelUnit").AsString(10).Nullable()
					.WithColumn("FuelActualCost").AsDecimal(18,2).Nullable()
					.WithColumn("Source").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ExternalId").AsString(200).Nullable()
					.WithColumn("IsApproved").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("NeedsReview").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ReviewReason").AsString(500).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Create.Index("IX_ResourceUsageEntries_Deployment").OnTable("ResourceUsageEntries").OnColumn("DeploymentId").Ascending();
				Create.Index("IX_ResourceUsageEntries_Call").OnTable("ResourceUsageEntries").OnColumn("CallId").Ascending();
				Create.Index("IX_ResourceUsageEntries_Unit").OnTable("ResourceUsageEntries").OnColumn("UnitId").Ascending().OnColumn("UsageDate").Ascending();
			}
			if (!Schema.Table("FieldCostRuns").Exists())
			{
				Create.Table("FieldCostRuns")
					.WithColumn("FieldCostRunId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ContextType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("BidId").AsString(36).Nullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("DeploymentId").AsString(36).Nullable()
					.WithColumn("RunType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ThroughDate").AsDateTime2().Nullable()
					.WithColumn("Currency").AsString(3).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("InputVersions").AsString(int.MaxValue).Nullable()
					.WithColumn("RevenueSource").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RevenueAmount").AsDecimal(18,2).Nullable()
					.WithColumn("RevenueSourceId").AsString(36).Nullable()
					.WithColumn("RevenueSourceVersion").AsString(50).Nullable()
					.WithColumn("PersonnelTotal").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("ResourceTotal").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("ConsumableTotal").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("ExpenseTotal").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("OverheadTotal").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("TotalLoadedCost").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("ContributionMargin").AsDecimal(18,2).Nullable()
					.WithColumn("ContributionMarginPercent").AsDecimal(9,4).Nullable()
					.WithColumn("BreakEvenRevenue").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("MissingInputCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SupersedesRunId").AsString(36).Nullable()
					.WithColumn("FrozenByUserId").AsString(128).Nullable()
					.WithColumn("FrozenOn").AsDateTime2().Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Create.Index("IX_FieldCostRuns_Department").OnTable("FieldCostRuns").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending().OnColumn("Status").Ascending();
				Create.Index("IX_FieldCostRuns_Deployment").OnTable("FieldCostRuns").OnColumn("DeploymentId").Ascending();
				Create.Index("IX_FieldCostRuns_Bid").OnTable("FieldCostRuns").OnColumn("BidId").Ascending();
				Create.Index("IX_FieldCostRuns_Call").OnTable("FieldCostRuns").OnColumn("CallId").Ascending();
			}
			if (!Schema.Table("FieldCostLines").Exists())
			{
				Create.Table("FieldCostLines")
					.WithColumn("FieldCostLineId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("FieldCostRunId").AsString(36).Nullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("LineDate").AsDateTime2().Nullable()
					.WithColumn("Category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubjectType").AsString(30).Nullable()
					.WithColumn("SubjectId").AsString(128).Nullable()
					.WithColumn("SubjectLabel").AsString(250).Nullable()
					.WithColumn("Component").AsString(100).Nullable()
					.WithColumn("Quantity").AsDecimal(18,4).NotNullable().WithDefaultValue(0)
					.WithColumn("Unit").AsString(20).Nullable()
					.WithColumn("Rate").AsDecimal(18,4).Nullable()
					.WithColumn("Amount").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("ProtectedDetailJson").AsString(int.MaxValue).Nullable()
					.WithColumn("SourceType").AsString(50).Nullable()
					.WithColumn("SourceId").AsString(128).Nullable()
					.WithColumn("IsEstimated").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("IsFallback").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ReviewReason").AsString(250).Nullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_FieldCostLines_Run").OnTable("FieldCostLines").OnColumn("FieldCostRunId").Ascending().OnColumn("SortOrder").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("FieldCostLines").Exists()) Delete.Table("FieldCostLines");
			if (Schema.Table("FieldCostRuns").Exists()) Delete.Table("FieldCostRuns");
			if (Schema.Table("ResourceUsageEntries").Exists()) Delete.Table("ResourceUsageEntries");
			if (Schema.Table("ResourceCostComponents").Exists()) Delete.Table("ResourceCostComponents");
			if (Schema.Table("ResourceCostProfiles").Exists()) Delete.Table("ResourceCostProfiles");
		}
	}
}
