using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase E (E2): separately stored demographic responses, CRD report runs, immutable employee snapshots and aggregate rows, and short-lived encrypted export artifacts (ADP catalog 28). Registry M0223. Guarded for safe retry.
	/// </summary>
	[Migration(223)]
	public class M0223_AddCaliforniaPayDataReporting : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("PayDataReportingDemographics").Exists())
			{
				Create.Table("PayDataReportingDemographics")
					.WithColumn("PayDataReportingDemographicId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("WorkforceWorkerId").AsString(36).Nullable()
					.WithColumn("EffectiveOn").AsDateTime2().NotNullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("HispanicLatino").AsString(int.MaxValue).Nullable()
					.WithColumn("RaceEthnicityCodes").AsString(int.MaxValue).Nullable()
					.WithColumn("SexCode").AsString(int.MaxValue).Nullable()
					.WithColumn("DeclinedRaceEthnicity").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DeclinedSex").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CollectionSource").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CollectedOn").AsDateTime2().Nullable()
					.WithColumn("CollectedByUserId").AsString(128).Nullable()
					.WithColumn("ReviewedOn").AsDateTime2().Nullable()
					.WithColumn("ReviewedByUserId").AsString(128).Nullable()
					.WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_PayDataReportingDemographics_Worker").OnTable("PayDataReportingDemographics").OnColumn("WorkforceWorkerId").Ascending().OnColumn("EffectiveOn").Ascending();
			}
			if (!Schema.Table("PayDataReportRuns").Exists())
			{
				Create.Table("PayDataReportRuns")
					.WithColumn("PayDataReportRunId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ReportType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ReportingYear").AsInt32().NotNullable()
					.WithColumn("SchemaProfileCode").AsString(50).Nullable()
					.WithColumn("SchemaProfileHash").AsString(128).Nullable()
					.WithColumn("SnapshotStart").AsDateTime2().NotNullable()
					.WithColumn("SnapshotEnd").AsDateTime2().NotNullable()
					.WithColumn("EmployerSnapshotJson").AsString(int.MaxValue).Nullable()
					.WithColumn("SourceCutoff").AsDateTime2().Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("EmployeeCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RowCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ExceptionCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("WarningCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ValidationSummaryJson").AsString(int.MaxValue).Nullable()
					.WithColumn("RunRemarks").AsString(int.MaxValue).Nullable()
					.WithColumn("SupersedesRunId").AsString(36).Nullable()
					.WithColumn("ReviewedByUserId").AsString(128).Nullable()
					.WithColumn("ReviewedOn").AsDateTime2().Nullable()
					.WithColumn("FrozenByUserId").AsString(128).Nullable()
					.WithColumn("FrozenOn").AsDateTime2().Nullable()
					.WithColumn("ExportedByUserId").AsString(128).Nullable()
					.WithColumn("ExportedOn").AsDateTime2().Nullable()
					.WithColumn("CertifiedByUserId").AsString(128).Nullable()
					.WithColumn("CertifiedOn").AsDateTime2().Nullable()
					.WithColumn("CertificationReference").AsString(200).Nullable()
					.WithColumn("CertifiedArtifactChecksum").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_PayDataReportRuns_Department").OnTable("PayDataReportRuns").OnColumn("DepartmentId").Ascending().OnColumn("ReportingYear").Ascending().OnColumn("ReportType").Ascending().OnColumn("IsDeleted").Ascending();
			}
			if (!Schema.Table("PayDataReportEmployeeSnapshots").Exists())
			{
				Create.Table("PayDataReportEmployeeSnapshots")
					.WithColumn("PayDataReportEmployeeSnapshotId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("PayDataReportRunId").AsString(36).Nullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("WorkforceWorkerId").AsString(36).Nullable()
					.WithColumn("WorkforceEmploymentId").AsString(36).Nullable()
					.WithColumn("WorkforceEstablishmentId").AsString(36).Nullable()
					.WithColumn("WorkforceLaborContractorId").AsString(36).Nullable()
					.WithColumn("JobCategoryCode").AsString(10).Nullable()
					.WithColumn("DemographicCode").AsString(int.MaxValue).Nullable()
					.WithColumn("PayBandCode").AsString(10).Nullable()
					.WithColumn("ExemptionCode").AsString(10).Nullable()
					.WithColumn("EmploymentTypeCode").AsString(10).Nullable()
					.WithColumn("WorkMode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AnnualEarnings").AsString(int.MaxValue).Nullable()
					.WithColumn("EarningsSource").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AnnualHours").AsDecimal(12,2).NotNullable().WithDefaultValue(0)
					.WithColumn("AnnualWeeks").AsDecimal(9,2).NotNullable().WithDefaultValue(0)
					.WithColumn("HourlyRate").AsString(int.MaxValue).Nullable()
					.WithColumn("IsIncluded").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("ExceptionCodesCsv").AsString(500).Nullable()
					.WithColumn("OverrideReason").AsString(500).Nullable()
					.WithColumn("OverrideByUserId").AsString(128).Nullable()
					.WithColumn("SourceVersions").AsString(int.MaxValue).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_PayDataReportEmployeeSnapshots_Run").OnTable("PayDataReportEmployeeSnapshots").OnColumn("PayDataReportRunId").Ascending();
			}
			if (!Schema.Table("PayDataReportRows").Exists())
			{
				Create.Table("PayDataReportRows")
					.WithColumn("PayDataReportRowId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("PayDataReportRunId").AsString(36).Nullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("WorkforceEstablishmentId").AsString(36).Nullable()
					.WithColumn("WorkforceLaborContractorId").AsString(36).Nullable()
					.WithColumn("JobCategoryCode").AsString(10).Nullable()
					.WithColumn("DemographicCode").AsString(int.MaxValue).Nullable()
					.WithColumn("PayBandCode").AsString(10).Nullable()
					.WithColumn("ExemptionCode").AsString(10).Nullable()
					.WithColumn("EmploymentTypeCode").AsString(10).Nullable()
					.WithColumn("EmployeeCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AnnualHours").AsDecimal(12,2).NotNullable().WithDefaultValue(0)
					.WithColumn("AnnualWeeks").AsDecimal(9,2).NotNullable().WithDefaultValue(0)
					.WithColumn("MeanHourlyRate").AsString(int.MaxValue).Nullable()
					.WithColumn("MedianHourlyRate").AsString(int.MaxValue).Nullable()
					.WithColumn("NonRemoteCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RemoteWithinCaliforniaCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RemoteOutsideCaliforniaCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RowRemarks").AsString(int.MaxValue).Nullable()
					.WithColumn("ContributingSnapshotIdsCsv").AsString(int.MaxValue).Nullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_PayDataReportRows_Run").OnTable("PayDataReportRows").OnColumn("PayDataReportRunId").Ascending().OnColumn("SortOrder").Ascending();
			}
			if (!Schema.Table("PayDataExportArtifacts").Exists())
			{
				Create.Table("PayDataExportArtifacts")
					.WithColumn("PayDataExportArtifactId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("PayDataReportRunId").AsString(36).Nullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("SchemaProfileCode").AsString(50).Nullable()
					.WithColumn("SchemaProfileHash").AsString(128).Nullable()
					.WithColumn("Format").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("FileName").AsString(250).Nullable()
					.WithColumn("Checksum").AsString(128).Nullable()
					.WithColumn("Data").AsBinary(int.MaxValue).Nullable()
					.WithColumn("Size").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ExpiresOn").AsDateTime2().NotNullable()
					.WithColumn("PurgedOn").AsDateTime2().Nullable()
					.WithColumn("ExportedByUserId").AsString(128).Nullable()
					.WithColumn("DownloadCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LastDownloadedOn").AsDateTime2().Nullable()
					.WithColumn("LastDownloadedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_PayDataExportArtifacts_Run").OnTable("PayDataExportArtifacts").OnColumn("PayDataReportRunId").Ascending();
				Create.Index("IX_PayDataExportArtifacts_Expiry").OnTable("PayDataExportArtifacts").OnColumn("ExpiresOn").Ascending().OnColumn("PurgedOn").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("PayDataExportArtifacts").Exists()) Delete.Table("PayDataExportArtifacts");
			if (Schema.Table("PayDataReportRows").Exists()) Delete.Table("PayDataReportRows");
			if (Schema.Table("PayDataReportEmployeeSnapshots").Exists()) Delete.Table("PayDataReportEmployeeSnapshots");
			if (Schema.Table("PayDataReportRuns").Exists()) Delete.Table("PayDataReportRuns");
			if (Schema.Table("PayDataReportingDemographics").Exists()) Delete.Table("PayDataReportingDemographics");
		}
	}
}
