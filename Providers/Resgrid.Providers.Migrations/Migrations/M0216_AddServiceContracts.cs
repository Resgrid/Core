using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase C (C1): customer service contracts with per-stage document requirements, and the department-level compliance document register (decision 24: COI, WCB/WorkSafe clearance, SAM/UEI, licences, CAGE, tax registration, bonds) with expiry lead days and a file blob. Registry M0216. Guarded for safe retry.
	/// </summary>
	[Migration(216)]
	public class M0216_AddServiceContracts : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ServiceContracts").Exists())
			{
				Create.Table("ServiceContracts")
					.WithColumn("ServiceContractId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ContactId").AsString(128).NotNullable()
					.WithColumn("CustomerBillingProfileId").AsString(36).Nullable()
					.WithColumn("ContractNumber").AsString(100).Nullable()
					.WithColumn("Name").AsString(250).NotNullable()
					.WithColumn("ContractType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StartOn").AsDateTime2().NotNullable()
					.WithColumn("EndOn").AsDateTime2().Nullable()
					.WithColumn("RateScheduleId").AsString(36).Nullable()
					.WithColumn("DiscountPercent").AsDecimal(9,4).Nullable()
					.WithColumn("TermsNetDays").AsInt32().Nullable()
					.WithColumn("InvoiceSubmissionEmail").AsString(500).Nullable()
					.WithColumn("MaxDeploymentDays").AsInt32().Nullable()
					.WithColumn("ResponseTimeMinutes").AsInt32().Nullable()
					.WithColumn("PointOfHire").AsString(250).Nullable()
					.WithColumn("DocumentTemplateKey").AsString(50).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_ServiceContracts_Department").OnTable("ServiceContracts").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending().OnColumn("Status").Ascending();
				Create.Index("IX_ServiceContracts_Contact").OnTable("ServiceContracts").OnColumn("ContactId").Ascending();
			}
			if (!Schema.Table("ServiceContractDocumentRequirements").Exists())
			{
				Create.Table("ServiceContractDocumentRequirements")
					.WithColumn("ServiceContractDocumentRequirementId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("ServiceContractId").AsString(36).NotNullable()
					.WithColumn("Name").AsString(250).NotNullable()
					.WithColumn("Stage").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ComplianceDocumentType").AsInt32().Nullable()
					.WithColumn("IsMandatory").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0);
				Create.Index("IX_ServiceContractDocumentRequirements_Contract").OnTable("ServiceContractDocumentRequirements").OnColumn("ServiceContractId").Ascending().OnColumn("SortOrder").Ascending();
				Create.ForeignKey("FK_ServiceContractDocumentRequirements_Contract").FromTable("ServiceContractDocumentRequirements").ForeignColumn("ServiceContractId").ToTable("ServiceContracts").PrimaryColumn("ServiceContractId");
			}
			if (!Schema.Table("DepartmentComplianceDocuments").Exists())
			{
				Create.Table("DepartmentComplianceDocuments")
					.WithColumn("DepartmentComplianceDocumentId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DocumentType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name").AsString(250).NotNullable()
					.WithColumn("DocumentNumber").AsString(int.MaxValue).Nullable()
					.WithColumn("Issuer").AsString(250).Nullable()
					.WithColumn("EffectiveOn").AsDateTime2().Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("AlertLeadDays").AsInt32().NotNullable().WithDefaultValue(30)
					.WithColumn("FileName").AsString(int.MaxValue).Nullable()
					.WithColumn("FileType").AsString(200).Nullable()
					.WithColumn("FileSize").AsInt32().Nullable()
					.WithColumn("Data").AsBinary(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_DepartmentComplianceDocuments_Department").OnTable("DepartmentComplianceDocuments").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending().OnColumn("ExpiresOn").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("DepartmentComplianceDocuments").Exists()) Delete.Table("DepartmentComplianceDocuments");
			if (Schema.Table("ServiceContractDocumentRequirements").Exists()) Delete.Table("ServiceContractDocumentRequirements");
			if (Schema.Table("ServiceContracts").Exists()) Delete.Table("ServiceContracts");
		}
	}
}
