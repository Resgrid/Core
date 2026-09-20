using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase E (E2): compensation profiles, pay / employer-cost components, work entries and annual pay facts. Monetary values are ADP catalog 28 text columns. Registry M0221. Guarded for safe retry.
	/// </summary>
	[Migration(221)]
	public class M0221_AddWorkforceCompensationAndAnnualFacts : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("EmployeeCompensationProfiles").Exists())
			{
				Create.Table("EmployeeCompensationProfiles")
					.WithColumn("EmployeeCompensationProfileId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Scope").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("WorkforceEmploymentId").AsString(36).Nullable()
					.WithColumn("PersonnelRoleId").AsInt32().Nullable()
					.WithColumn("EffectiveOn").AsDateTime2().NotNullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("Currency").AsString(3).Nullable()
					.WithColumn("PayBasis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("BaseAmount").AsString(int.MaxValue).Nullable()
					.WithColumn("RegularHourlyEquivalent").AsString(int.MaxValue).Nullable()
					.WithColumn("StandardHoursPerDay").AsDecimal(9,2).Nullable()
					.WithColumn("StandardHoursPerWeek").AsDecimal(9,2).Nullable()
					.WithColumn("StandardHoursPerYear").AsDecimal(9,2).Nullable()
					.WithColumn("RateMultipliersJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Source").AsString(100).Nullable()
					.WithColumn("ImportBatchId").AsString(36).Nullable()
					.WithColumn("SourceChecksum").AsString(128).Nullable()
					.WithColumn("IsApproved").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ApprovedByUserId").AsString(128).Nullable()
					.WithColumn("ApprovedOn").AsDateTime2().Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_EmployeeCompensationProfiles_Employment").OnTable("EmployeeCompensationProfiles").OnColumn("WorkforceEmploymentId").Ascending().OnColumn("EffectiveOn").Ascending();
				Create.Index("IX_EmployeeCompensationProfiles_Department").OnTable("EmployeeCompensationProfiles").OnColumn("DepartmentId").Ascending().OnColumn("Scope").Ascending().OnColumn("IsDeleted").Ascending();
			}
			if (!Schema.Table("EmployeePayComponents").Exists())
			{
				Create.Table("EmployeePayComponents")
					.WithColumn("EmployeePayComponentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("EmployeeCompensationProfileId").AsString(36).Nullable()
					.WithColumn("EffectiveOn").AsDateTime2().Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("Category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name").AsString(100).Nullable()
					.WithColumn("Basis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Amount").AsString(int.MaxValue).Nullable()
					.WithColumn("EligiblePayCodesCsv").AsString(100).Nullable()
					.WithColumn("PaidForEachOvertimeHour").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("SourceAgreement").AsString(250).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_EmployeePayComponents_Profile").OnTable("EmployeePayComponents").OnColumn("EmployeeCompensationProfileId").Ascending();
			}
			if (!Schema.Table("EmployeeCostComponents").Exists())
			{
				Create.Table("EmployeeCostComponents")
					.WithColumn("EmployeeCostComponentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("EmployeeCompensationProfileId").AsString(36).Nullable()
					.WithColumn("EffectiveOn").AsDateTime2().Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("Category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name").AsString(100).Nullable()
					.WithColumn("Basis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RateAmount").AsString(int.MaxValue).Nullable()
					.WithColumn("EligiblePayCodesCsv").AsString(100).Nullable()
					.WithColumn("Cap").AsString(int.MaxValue).Nullable()
					.WithColumn("Source").AsString(250).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_EmployeeCostComponents_Profile").OnTable("EmployeeCostComponents").OnColumn("EmployeeCompensationProfileId").Ascending();
			}
			if (!Schema.Table("WorkforceWorkEntries").Exists())
			{
				Create.Table("WorkforceWorkEntries")
					.WithColumn("WorkforceWorkEntryId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("WorkforceWorkerId").AsString(36).Nullable()
					.WithColumn("WorkforceEmploymentId").AsString(36).Nullable()
					.WithColumn("WorkDate").AsDateTime2().NotNullable()
					.WithColumn("StartTime").AsDateTime2().Nullable()
					.WithColumn("EndTime").AsDateTime2().Nullable()
					.WithColumn("Hours").AsDecimal(9,2).NotNullable().WithDefaultValue(0)
					.WithColumn("HoursType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("WorkforceEstablishmentId").AsString(36).Nullable()
					.WithColumn("WorkCountry").AsString(2).Nullable()
					.WithColumn("WorkSubdivision").AsString(10).Nullable()
					.WithColumn("WorkMode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("DeploymentId").AsString(36).Nullable()
					.WithColumn("DeploymentTimeReportId").AsString(36).Nullable()
					.WithColumn("ApprovedPayrollCost").AsString(int.MaxValue).Nullable()
					.WithColumn("ExternalSource").AsString(100).Nullable()
					.WithColumn("ExternalId").AsString(200).Nullable()
					.WithColumn("ImportBatchId").AsString(36).Nullable()
					.WithColumn("IsApproved").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsReconciled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_WorkforceWorkEntries_Worker").OnTable("WorkforceWorkEntries").OnColumn("WorkforceWorkerId").Ascending().OnColumn("WorkDate").Ascending();
				Create.Index("IX_WorkforceWorkEntries_Deployment").OnTable("WorkforceWorkEntries").OnColumn("DeploymentId").Ascending();
				Create.Index("IX_WorkforceWorkEntries_Call").OnTable("WorkforceWorkEntries").OnColumn("CallId").Ascending();
			}
			if (!Schema.Table("WorkforceAnnualPayFacts").Exists())
			{
				Create.Table("WorkforceAnnualPayFacts")
					.WithColumn("WorkforceAnnualPayFactId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("WorkforceEmploymentId").AsString(36).Nullable()
					.WithColumn("ReportingYear").AsInt32().NotNullable()
					.WithColumn("ReportType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ClientAllocationKey").AsString(100).Nullable()
					.WithColumn("W2Box5").AsString(int.MaxValue).Nullable()
					.WithColumn("W2Box1").AsString(int.MaxValue).Nullable()
					.WithColumn("EarningsUsed").AsString(int.MaxValue).Nullable()
					.WithColumn("EarningsSource").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ActualWorkedHours").AsDecimal(12,2).Nullable()
					.WithColumn("PaidLeaveHours").AsDecimal(12,2).Nullable()
					.WithColumn("ReportableHours").AsDecimal(12,2).Nullable()
					.WithColumn("DaysWorked").AsInt32().Nullable()
					.WithColumn("WeeksWorked").AsDecimal(9,2).Nullable()
					.WithColumn("ExemptProxyMethod").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ProxyAverageHoursPerDay").AsDecimal(9,2).Nullable()
					.WithColumn("ClientAllocatedEarnings").AsString(int.MaxValue).Nullable()
					.WithColumn("ClientAllocatedHours").AsDecimal(12,2).Nullable()
					.WithColumn("ClientAllocatedWeeks").AsDecimal(9,2).Nullable()
					.WithColumn("Source").AsString(100).Nullable()
					.WithColumn("ImportBatchId").AsString(36).Nullable()
					.WithColumn("SourceChecksum").AsString(128).Nullable()
					.WithColumn("IsReconciled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsApproved").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("SupersedesFactId").AsString(36).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_WorkforceAnnualPayFacts_Employment").OnTable("WorkforceAnnualPayFacts").OnColumn("WorkforceEmploymentId").Ascending().OnColumn("ReportingYear").Ascending();
				Create.Index("IX_WorkforceAnnualPayFacts_Department").OnTable("WorkforceAnnualPayFacts").OnColumn("DepartmentId").Ascending().OnColumn("ReportingYear").Ascending().OnColumn("ReportType").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("WorkforceAnnualPayFacts").Exists()) Delete.Table("WorkforceAnnualPayFacts");
			if (Schema.Table("WorkforceWorkEntries").Exists()) Delete.Table("WorkforceWorkEntries");
			if (Schema.Table("EmployeeCostComponents").Exists()) Delete.Table("EmployeeCostComponents");
			if (Schema.Table("EmployeePayComponents").Exists()) Delete.Table("EmployeePayComponents");
			if (Schema.Table("EmployeeCompensationProfiles").Exists()) Delete.Table("EmployeeCompensationProfiles");
		}
	}
}
