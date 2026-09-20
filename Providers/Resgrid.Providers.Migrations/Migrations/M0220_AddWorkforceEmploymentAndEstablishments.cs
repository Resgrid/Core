using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase E (E2): employer identity, affiliated entities, establishments, labor contractors, workers, employment periods and job assignments. Identifiers and addresses are ADP catalog 28 columns (text, envelope-ready) with IsProtected/ProtectedCatalogVersion markers. Registry M0220. Guarded for safe retry.
	/// </summary>
	[Migration(220)]
	public class M0220_AddWorkforceEmploymentAndEstablishments : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("WorkforceEmployerProfiles").Exists())
			{
				Create.Table("WorkforceEmployerProfiles")
					.WithColumn("WorkforceEmployerProfileId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("LegalName").AsString(250).Nullable()
					.WithColumn("Fein").AsString(int.MaxValue).Nullable()
					.WithColumn("Sein").AsString(int.MaxValue).Nullable()
					.WithColumn("SosNumber").AsString(int.MaxValue).Nullable()
					.WithColumn("Naics").AsString(6).Nullable()
					.WithColumn("EddAddress").AsString(int.MaxValue).Nullable()
					.WithColumn("HeadquartersAddress").AsString(int.MaxValue).Nullable()
					.WithColumn("IsIntegratedEnterprise").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("FilingContactName").AsString(int.MaxValue).Nullable()
					.WithColumn("FilingContactEmail").AsString(int.MaxValue).Nullable()
					.WithColumn("FilingContactPhone").AsString(int.MaxValue).Nullable()
					.WithColumn("CoverageStatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UsEmployeeCount").AsInt32().Nullable()
					.WithColumn("CaliforniaEmployeeCount").AsInt32().Nullable()
					.WithColumn("EffectiveOn").AsDateTime2().Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_WorkforceEmployerProfiles_Department").OnTable("WorkforceEmployerProfiles").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending();
			}
			if (!Schema.Table("WorkforceAffiliatedEntities").Exists())
			{
				Create.Table("WorkforceAffiliatedEntities")
					.WithColumn("WorkforceAffiliatedEntityId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("WorkforceEmployerProfileId").AsString(36).Nullable()
					.WithColumn("LegalName").AsString(250).Nullable()
					.WithColumn("Fein").AsString(int.MaxValue).Nullable()
					.WithColumn("Sein").AsString(int.MaxValue).Nullable()
					.WithColumn("SosNumber").AsString(int.MaxValue).Nullable()
					.WithColumn("HeadquartersAddress").AsString(int.MaxValue).Nullable()
					.WithColumn("EffectiveOn").AsDateTime2().Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_WorkforceAffiliatedEntities_Department").OnTable("WorkforceAffiliatedEntities").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending();
			}
			if (!Schema.Table("WorkforceEstablishments").Exists())
			{
				Create.Table("WorkforceEstablishments")
					.WithColumn("WorkforceEstablishmentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("WorkforceAffiliatedEntityId").AsString(36).Nullable()
					.WithColumn("Code").AsString(50).Nullable()
					.WithColumn("Name").AsString(250).Nullable()
					.WithColumn("PhysicalAddress").AsString(int.MaxValue).Nullable()
					.WithColumn("City").AsString(100).Nullable()
					.WithColumn("StateCode").AsString(2).Nullable()
					.WithColumn("PostalCode").AsString(10).Nullable()
					.WithColumn("Naics").AsString(6).Nullable()
					.WithColumn("MajorActivity").AsString(250).Nullable()
					.WithColumn("IsHeadquarters").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("WasFiledPriorYear").AsBoolean().Nullable()
					.WithColumn("ActiveFrom").AsDateTime2().Nullable()
					.WithColumn("ActiveTo").AsDateTime2().Nullable()
					.WithColumn("TimeZoneId").AsString(100).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_WorkforceEstablishments_Department").OnTable("WorkforceEstablishments").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_WorkforceEstablishments_Code' AND object_id = OBJECT_ID('WorkforceEstablishments')) CREATE UNIQUE INDEX [UX_WorkforceEstablishments_Code] ON [WorkforceEstablishments] ([DepartmentId], [Code]) WHERE [IsDeleted] = 0;");
			}
			if (!Schema.Table("WorkforceLaborContractors").Exists())
			{
				Create.Table("WorkforceLaborContractors")
					.WithColumn("WorkforceLaborContractorId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("LegalName").AsString(250).Nullable()
					.WithColumn("OwnershipName").AsString(250).Nullable()
					.WithColumn("Dba").AsString(250).Nullable()
					.WithColumn("Fein").AsString(int.MaxValue).Nullable()
					.WithColumn("IdentifierType").AsString(20).Nullable()
					.WithColumn("ContactDetails").AsString(int.MaxValue).Nullable()
					.WithColumn("RelationshipStartOn").AsDateTime2().Nullable()
					.WithColumn("RelationshipEndOn").AsDateTime2().Nullable()
					.WithColumn("Provenance").AsString(500).Nullable()
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_WorkforceLaborContractors_Department").OnTable("WorkforceLaborContractors").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending();
			}
			if (!Schema.Table("WorkforceWorkers").Exists())
			{
				Create.Table("WorkforceWorkers")
					.WithColumn("WorkforceWorkerId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).Nullable()
					.WithColumn("ExternalWorkerKey").AsString(int.MaxValue).Nullable()
					.WithColumn("DisplayLabel").AsString(int.MaxValue).Nullable()
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable();
				Create.Index("IX_WorkforceWorkers_Department").OnTable("WorkforceWorkers").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending();
				Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_WorkforceWorkers_User' AND object_id = OBJECT_ID('WorkforceWorkers')) CREATE UNIQUE INDEX [UX_WorkforceWorkers_User] ON [WorkforceWorkers] ([DepartmentId], [UserId]) WHERE [UserId] IS NOT NULL AND [IsDeleted] = 0;");
			}
			if (!Schema.Table("WorkforceEmployments").Exists())
			{
				Create.Table("WorkforceEmployments")
					.WithColumn("WorkforceEmploymentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("WorkforceWorkerId").AsString(36).Nullable()
					.WithColumn("WorkforceAffiliatedEntityId").AsString(36).Nullable()
					.WithColumn("WorkforceLaborContractorId").AsString(36).Nullable()
					.WithColumn("WorkerKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StartOn").AsDateTime2().NotNullable()
					.WithColumn("EndOn").AsDateTime2().Nullable()
					.WithColumn("EmploymentType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ExemptionStatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DefaultEstablishmentId").AsString(36).Nullable()
					.WithColumn("CaliforniaEmployeeBasis").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PersonnelRoleId").AsInt32().Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Create.Index("IX_WorkforceEmployments_Worker").OnTable("WorkforceEmployments").OnColumn("WorkforceWorkerId").Ascending().OnColumn("StartOn").Ascending();
				Create.Index("IX_WorkforceEmployments_Department").OnTable("WorkforceEmployments").OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending();
			}
			if (!Schema.Table("WorkforceJobAssignments").Exists())
			{
				Create.Table("WorkforceJobAssignments")
					.WithColumn("WorkforceJobAssignmentId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("WorkforceEmploymentId").AsString(36).Nullable()
					.WithColumn("EffectiveOn").AsDateTime2().NotNullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("WorkforceEstablishmentId").AsString(36).Nullable()
					.WithColumn("JobTitle").AsString(250).Nullable()
					.WithColumn("SocCode").AsString(20).Nullable()
					.WithColumn("SocVersion").AsString(20).Nullable()
					.WithColumn("CaPayDataProfileCode").AsString(50).Nullable()
					.WithColumn("JobCategoryCode").AsString(10).Nullable()
					.WithColumn("CalOesMarsAuthorityProfileCode").AsString(50).Nullable()
					.WithColumn("CalOesMarsClassificationCode").AsString(100).Nullable()
					.WithColumn("MappingProvenance").AsString(500).Nullable()
					.WithColumn("WorkMode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("WorkCountry").AsString(2).Nullable()
					.WithColumn("WorkSubdivision").AsString(10).Nullable()
					.WithColumn("RowVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();
				Create.Index("IX_WorkforceJobAssignments_Employment").OnTable("WorkforceJobAssignments").OnColumn("WorkforceEmploymentId").Ascending().OnColumn("EffectiveOn").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("WorkforceJobAssignments").Exists()) Delete.Table("WorkforceJobAssignments");
			if (Schema.Table("WorkforceEmployments").Exists()) Delete.Table("WorkforceEmployments");
			if (Schema.Table("WorkforceWorkers").Exists()) Delete.Table("WorkforceWorkers");
			if (Schema.Table("WorkforceLaborContractors").Exists()) Delete.Table("WorkforceLaborContractors");
			if (Schema.Table("WorkforceEstablishments").Exists()) Delete.Table("WorkforceEstablishments");
			if (Schema.Table("WorkforceAffiliatedEntities").Exists()) Delete.Table("WorkforceAffiliatedEntities");
			if (Schema.Table("WorkforceEmployerProfiles").Exists()) Delete.Table("WorkforceEmployerProfiles");
		}
	}
}
