using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase C (C1, decision 39): the free deployment finance wrapper around a Call — deployments (with the RMS external-order soft link and finance mode), roster units/personnel/equipment, pre-numbered daily time reports with multi-span time entries, expenses, attachments and the report number sequence. External orders and fills are the RMS-owned RmsExternalOrders/RmsExternalOrderFills; no order tables here. Registry M0218. Guarded for safe retry.
	/// </summary>
	[Migration(218)]
	public class M0218_AddDeploymentsAndTimeTracking : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("Deployments").Exists())
			{
				Create.Table("Deployments")
					.WithColumn("DeploymentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("RmsExternalOrderId").AsString(36).Nullable()
					.WithColumn("FinanceMode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("BidId").AsString(36).Nullable()
					.WithColumn("ServiceContractId").AsString(36).Nullable()
					.WithColumn("RateScheduleId").AsString(36).Nullable()
					.WithColumn("ContactId").AsString(128).Nullable()
					.WithColumn("Name").AsString(250).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IncidentNumber").AsString(100).Nullable()
					.WithColumn("ServiceRequestNumber").AsString(100).Nullable()
					.WithColumn("ResourceOrderNumber").AsString(100).Nullable()
					.WithColumn("RequestNumber").AsString(100).Nullable()
					.WithColumn("CostCode").AsString(100).Nullable()
					.WithColumn("PointOfHire").AsString(250).Nullable()
					.WithColumn("StartOn").AsDateTime2().Nullable()
					.WithColumn("EndOn").AsDateTime2().Nullable()
					.WithColumn("MaxDays").AsInt32().Nullable()
					.WithColumn("OutOfProvince").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("TravelViaAir").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("HomeCountry").AsString(2).Nullable()
					.WithColumn("HostCountry").AsString(2).Nullable()
					.WithColumn("HomeSubdivision").AsString(10).Nullable()
					.WithColumn("HostSubdivision").AsString(10).Nullable()
					.WithColumn("LocalTimeZoneId").AsString(100).Nullable()
					.WithColumn("Locale").AsString(20).Nullable()
					.WithColumn("MeasurementSystem").AsString(10).Nullable()
					.WithColumn("Currency").AsString(3).Nullable()
					.WithColumn("DiscountPercent").AsDecimal(9,4).Nullable()
					.WithColumn("StatusChangedOn").AsDateTime2().Nullable()
					.WithColumn("CalendarItemId").AsInt32().Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_Deployments_Department").OnTable("Deployments").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending().OnColumn("Status").Ascending();
				Create.Index("IX_Deployments_ExternalOrder").OnTable("Deployments").OnColumn("RmsExternalOrderId").Ascending();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Deployments_Call' AND object_id = OBJECT_ID('Deployments')) CREATE UNIQUE INDEX [UX_Deployments_Call] ON [Deployments] ([CallId]) WHERE [CallId] IS NOT NULL AND [IsDeleted] = 0;");
			}
			if (!Schema.Table("DeploymentUnits").Exists())
			{
				Create.Table("DeploymentUnits")
					.WithColumn("DeploymentUnitId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DeploymentId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UnitId").AsInt32().NotNullable()
					.WithColumn("RateScheduleEntryId").AsString(36).Nullable()
					.WithColumn("CallSign").AsString(100).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("RemovedOn").AsDateTime2().Nullable();
				Create.Index("IX_DeploymentUnits_Deployment").OnTable("DeploymentUnits").OnColumn("DeploymentId").Ascending();
				Create.Index("IX_DeploymentUnits_Unit").OnTable("DeploymentUnits").OnColumn("UnitId").Ascending();
				Create.ForeignKey("FK_DeploymentUnits_Deployment").FromTable("DeploymentUnits").ForeignColumn("DeploymentId").ToTable("Deployments").PrimaryColumn("DeploymentId");
			}
			if (!Schema.Table("DeploymentPersonnel").Exists())
			{
				Create.Table("DeploymentPersonnel")
					.WithColumn("DeploymentPersonnelId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DeploymentId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DeploymentUnitId").AsString(36).Nullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("UnitRoleId").AsInt32().Nullable()
					.WithColumn("RateScheduleEntryId").AsString(36).Nullable()
					.WithColumn("CertificationCode").AsString(50).Nullable()
					.WithColumn("PremiumIdsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("CallSign").AsString(100).Nullable()
					.WithColumn("RmsExternalOrderFillId").AsString(36).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("RemovedOn").AsDateTime2().Nullable();
				Create.Index("IX_DeploymentPersonnel_Deployment").OnTable("DeploymentPersonnel").OnColumn("DeploymentId").Ascending();
				Create.Index("IX_DeploymentPersonnel_User").OnTable("DeploymentPersonnel").OnColumn("UserId").Ascending().OnColumn("RemovedOn").Ascending();
				Create.ForeignKey("FK_DeploymentPersonnel_Deployment").FromTable("DeploymentPersonnel").ForeignColumn("DeploymentId").ToTable("Deployments").PrimaryColumn("DeploymentId");
			}
			if (!Schema.Table("DeploymentEquipment").Exists())
			{
				Create.Table("DeploymentEquipment")
					.WithColumn("DeploymentEquipmentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DeploymentId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DeploymentUnitId").AsString(36).Nullable()
					.WithColumn("InventoryAssetId").AsString(36).Nullable()
					.WithColumn("InventoryItemId").AsString(36).Nullable()
					.WithColumn("FreeTextName").AsString(250).Nullable()
					.WithColumn("RateScheduleEntryId").AsString(36).Nullable()
					.WithColumn("IssuedOn").AsDateTime2().Nullable()
					.WithColumn("ReturnedOn").AsDateTime2().Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable();
				Create.Index("IX_DeploymentEquipment_Deployment").OnTable("DeploymentEquipment").OnColumn("DeploymentId").Ascending();
				Create.ForeignKey("FK_DeploymentEquipment_Deployment").FromTable("DeploymentEquipment").ForeignColumn("DeploymentId").ToTable("Deployments").PrimaryColumn("DeploymentId");
			}
			if (!Schema.Table("DeploymentTimeReports").Exists())
			{
				Create.Table("DeploymentTimeReports")
					.WithColumn("DeploymentTimeReportId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DeploymentId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ReportNumber").AsInt32().NotNullable()
					.WithColumn("ReportDate").AsDate().NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IncidentNumber").AsString(100).Nullable()
					.WithColumn("ResourceOrderNumber").AsString(100).Nullable()
					.WithColumn("RequestNumber").AsString(100).Nullable()
					.WithColumn("CostCode").AsString(100).Nullable()
					.WithColumn("PointOfHire").AsString(250).Nullable()
					.WithColumn("NoClear8").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("UnsafeConditionsStandDown").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ContractorSignedByUserId").AsString(128).Nullable()
					.WithColumn("ContractorSignedOn").AsDateTime2().Nullable()
					.WithColumn("CustomerSignerName").AsString(int.MaxValue).Nullable()
					.WithColumn("CustomerSignedOn").AsDateTime2().Nullable()
					.WithColumn("SubmittedByUserId").AsString(128).Nullable()
					.WithColumn("SubmittedOn").AsDateTime2().Nullable()
					.WithColumn("ApprovedByUserId").AsString(128).Nullable()
					.WithColumn("ApprovedOn").AsDateTime2().Nullable()
					.WithColumn("InvoiceId").AsString(36).Nullable()
					.WithColumn("RmsExternalOrderFillId").AsString(36).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_DeploymentTimeReports_Deployment").OnTable("DeploymentTimeReports").OnColumn("DeploymentId").Ascending().OnColumn("IsDeleted").Ascending().OnColumn("ReportDate").Ascending();
				Create.Index("IX_DeploymentTimeReports_Department").OnTable("DeploymentTimeReports").OnColumn("DepartmentId").Ascending().OnColumn("Status").Ascending();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DeploymentTimeReports_Number' AND object_id = OBJECT_ID('DeploymentTimeReports')) CREATE UNIQUE INDEX [UX_DeploymentTimeReports_Number] ON [DeploymentTimeReports] ([DepartmentId], [ReportNumber]);");
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DeploymentTimeReports_Date' AND object_id = OBJECT_ID('DeploymentTimeReports')) CREATE UNIQUE INDEX [UX_DeploymentTimeReports_Date] ON [DeploymentTimeReports] ([DeploymentId], [ReportDate]) WHERE [IsDeleted] = 0 AND [Status] <> 4;");
				Create.ForeignKey("FK_DeploymentTimeReports_Deployment").FromTable("DeploymentTimeReports").ForeignColumn("DeploymentId").ToTable("Deployments").PrimaryColumn("DeploymentId");
			}
			if (!Schema.Table("DeploymentTimeEntries").Exists())
			{
				Create.Table("DeploymentTimeEntries")
					.WithColumn("DeploymentTimeEntryId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DeploymentTimeReportId").AsString(36).NotNullable()
					.WithColumn("DeploymentId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("SubjectType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DeploymentPersonnelId").AsString(36).Nullable()
					.WithColumn("DeploymentUnitId").AsString(36).Nullable()
					.WithColumn("DeploymentEquipmentId").AsString(36).Nullable()
					.WithColumn("EntryType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StartTime").AsDateTime2().NotNullable()
					.WithColumn("EndTime").AsDateTime2().NotNullable()
					.WithColumn("PaidBreakMinutes").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UnpaidBreakMinutes").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CrewSizeSnapshot").AsInt32().Nullable()
					.WithColumn("CertificationCode").AsString(50).Nullable()
					.WithColumn("MileageKm").AsDecimal(9,2).Nullable()
					.WithColumn("FuelDeductionLitres").AsDecimal(9,2).Nullable()
					.WithColumn("AgencySuppliedMeals").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AgencySuppliedAccommodation").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0);
				Create.Index("IX_DeploymentTimeEntries_Report").OnTable("DeploymentTimeEntries").OnColumn("DeploymentTimeReportId").Ascending().OnColumn("SortOrder").Ascending();
				Create.Index("IX_DeploymentTimeEntries_Deployment").OnTable("DeploymentTimeEntries").OnColumn("DeploymentId").Ascending();
				Create.ForeignKey("FK_DeploymentTimeEntries_Report").FromTable("DeploymentTimeEntries").ForeignColumn("DeploymentTimeReportId").ToTable("DeploymentTimeReports").PrimaryColumn("DeploymentTimeReportId");
			}
			if (!Schema.Table("DeploymentExpenses").Exists())
			{
				Create.Table("DeploymentExpenses")
					.WithColumn("DeploymentExpenseId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DeploymentId").AsString(36).NotNullable()
					.WithColumn("DeploymentTimeReportId").AsString(36).Nullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ExpenseDate").AsDateTime2().NotNullable()
					.WithColumn("ExpenseType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MealCode").AsString(10).Nullable()
					.WithColumn("City").AsString(200).Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("Amount").AsDecimal(18,2).NotNullable().WithDefaultValue(0)
					.WithColumn("Currency").AsString(3).Nullable()
					.WithColumn("PreApproved").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Billable").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("ReceiptAttachmentId").AsInt32().Nullable()
					.WithColumn("RmsExternalOrderFillId").AsString(36).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_DeploymentExpenses_Deployment").OnTable("DeploymentExpenses").OnColumn("DeploymentId").Ascending().OnColumn("IsDeleted").Ascending().OnColumn("ExpenseDate").Ascending();
				Create.ForeignKey("FK_DeploymentExpenses_Deployment").FromTable("DeploymentExpenses").ForeignColumn("DeploymentId").ToTable("Deployments").PrimaryColumn("DeploymentId");
			}
			if (!Schema.Table("DeploymentAttachments").Exists())
			{
				Create.Table("DeploymentAttachments")
					.WithColumn("DeploymentAttachmentId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DeploymentId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("AttachmentType").AsInt32().NotNullable().WithDefaultValue(5)
					.WithColumn("Name").AsString(250).Nullable()
					.WithColumn("FileName").AsString(int.MaxValue).Nullable()
					.WithColumn("FileType").AsString(200).Nullable()
					.WithColumn("FileSize").AsInt32().Nullable()
					.WithColumn("Data").AsBinary(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_DeploymentAttachments_Deployment").OnTable("DeploymentAttachments").OnColumn("DeploymentId").Ascending().OnColumn("IsDeleted").Ascending();
				Create.ForeignKey("FK_DeploymentAttachments_Deployment").FromTable("DeploymentAttachments").ForeignColumn("DeploymentId").ToTable("Deployments").PrimaryColumn("DeploymentId");
			}
			if (!Schema.Table("TimeReportNumberSequences").Exists())
			{
				Create.Table("TimeReportNumberSequences")
					.WithColumn("DepartmentId").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("NextReportNumber").AsInt32().NotNullable().WithDefaultValue(1);
			}
		}

		public override void Down()
		{
			if (Schema.Table("TimeReportNumberSequences").Exists()) Delete.Table("TimeReportNumberSequences");
			if (Schema.Table("DeploymentAttachments").Exists()) Delete.Table("DeploymentAttachments");
			if (Schema.Table("DeploymentExpenses").Exists()) Delete.Table("DeploymentExpenses");
			if (Schema.Table("DeploymentTimeEntries").Exists()) Delete.Table("DeploymentTimeEntries");
			if (Schema.Table("DeploymentTimeReports").Exists()) Delete.Table("DeploymentTimeReports");
			if (Schema.Table("DeploymentEquipment").Exists()) Delete.Table("DeploymentEquipment");
			if (Schema.Table("DeploymentPersonnel").Exists()) Delete.Table("DeploymentPersonnel");
			if (Schema.Table("DeploymentUnits").Exists()) Delete.Table("DeploymentUnits");
			if (Schema.Table("Deployments").Exists()) Delete.Table("Deployments");
		}
	}
}
