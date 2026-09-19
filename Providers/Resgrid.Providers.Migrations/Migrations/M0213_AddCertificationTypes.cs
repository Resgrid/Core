using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase D (D2): turns the existing DepartmentCertificationTypes
	/// catalog into a typed catalog (additive columns; Code backfilled from Type and made unique per department while
	/// not deleted), adds role certification requirements, per-department certification settings and unit-scoped
	/// certification records, and adds the typed-record columns to PersonnelCertifications (backfilling the type
	/// where the legacy Type text exactly matches a catalog row of the same department; protected rows carry
	/// envelopes and are left untyped). No feature flag and no add-on: EnforcementMode Off plus additive columns is
	/// zero behaviour change until a department opts in. ADP catalog 27 (UnitCertifications) is registered in code
	/// with this migration. Registry M0213. Guarded for safe retry.
	/// </summary>
	[Migration(213)]
	public class M0213_AddCertificationTypes : Migration
	{
		public override void Up()
		{
			// ---- Catalog: DepartmentCertificationTypes (additive) --------------------------------------------
			var types = "DepartmentCertificationTypes";
			if (!Schema.Table(types).Column("Code").Exists())
				Alter.Table(types).AddColumn("Code").AsString(50).Nullable();
			if (!Schema.Table(types).Column("Category").Exists())
				Alter.Table(types).AddColumn("Category").AsInt32().NotNullable().WithDefaultValue(10);
			if (!Schema.Table(types).Column("AppliesTo").Exists())
				Alter.Table(types).AddColumn("AppliesTo").AsInt32().NotNullable().WithDefaultValue(0);
			if (!Schema.Table(types).Column("Description").Exists())
				Alter.Table(types).AddColumn("Description").AsString(int.MaxValue).Nullable();
			if (!Schema.Table(types).Column("IssuingAuthority").Exists())
				Alter.Table(types).AddColumn("IssuingAuthority").AsString(200).Nullable();
			if (!Schema.Table(types).Column("DefaultValidityMonths").Exists())
				Alter.Table(types).AddColumn("DefaultValidityMonths").AsInt32().Nullable();
			if (!Schema.Table(types).Column("NeverExpires").Exists())
				Alter.Table(types).AddColumn("NeverExpires").AsBoolean().NotNullable().WithDefaultValue(false);
			if (!Schema.Table(types).Column("RenewalCreditHoursRequired").Exists())
				Alter.Table(types).AddColumn("RenewalCreditHoursRequired").AsDecimal(9, 2).Nullable();
			if (!Schema.Table(types).Column("RequiresVerification").Exists())
				Alter.Table(types).AddColumn("RequiresVerification").AsBoolean().NotNullable().WithDefaultValue(false);
			if (!Schema.Table(types).Column("IsActive").Exists())
				Alter.Table(types).AddColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true);
			if (!Schema.Table(types).Column("IsDeleted").Exists())
				Alter.Table(types).AddColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false);
			if (!Schema.Table(types).Column("AddedOn").Exists())
				Alter.Table(types).AddColumn("AddedOn").AsDateTime2().Nullable();
			if (!Schema.Table(types).Column("AddedByUserId").Exists())
				Alter.Table(types).AddColumn("AddedByUserId").AsString(128).Nullable();
			if (!Schema.Table(types).Column("EditedOn").Exists())
				Alter.Table(types).AddColumn("EditedOn").AsDateTime2().Nullable();
			if (!Schema.Table(types).Column("EditedByUserId").Exists())
				Alter.Table(types).AddColumn("EditedByUserId").AsString(128).Nullable();

			// Code backfill: the legacy Type text, trimmed to the column; duplicates within a department get the row id
			// appended so the filtered unique index can be created. Existing rows keep working as free-text matches.
			Execute.Sql("UPDATE [DepartmentCertificationTypes] SET [Code] = LEFT(LTRIM(RTRIM([Type])), 50) WHERE [Code] IS NULL OR LTRIM(RTRIM([Code])) = '';");
			Execute.Sql("UPDATE [DepartmentCertificationTypes] SET [Code] = 'TYPE-' + CAST([DepartmentCertificationTypeId] AS nvarchar(20)) WHERE [Code] IS NULL OR LTRIM(RTRIM([Code])) = '';");
			Execute.Sql(
				";WITH d AS (SELECT [DepartmentCertificationTypeId], [Code], ROW_NUMBER() OVER (PARTITION BY [DepartmentId], [Code] ORDER BY [DepartmentCertificationTypeId]) AS rn FROM [DepartmentCertificationTypes]) " +
				"UPDATE d SET [Code] = LEFT([Code], 38) + '-' + CAST([DepartmentCertificationTypeId] AS nvarchar(11)) WHERE rn > 1;");
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DepartmentCertificationTypes_Code' AND object_id = OBJECT_ID('DepartmentCertificationTypes')) " +
				"CREATE UNIQUE INDEX [UX_DepartmentCertificationTypes_Code] ON [DepartmentCertificationTypes] ([DepartmentId], [Code]) WHERE [IsDeleted] = 0;");

			// ---- Role requirements ------------------------------------------------------------------------------
			if (!Schema.Table("PersonnelRoleCertificationRequirements").Exists())
			{
				Create.Table("PersonnelRoleCertificationRequirements")
					.WithColumn("PersonnelRoleCertificationRequirementId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("PersonnelRoleId").AsInt32().NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DepartmentCertificationTypeId").AsInt32().NotNullable()
					.WithColumn("IsMandatory").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("AnyOfGroup").AsInt32().Nullable()
					.WithColumn("AllowTrainee").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("GraceDaysOverride").AsInt32().Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable();

				Create.Index("IX_PersonnelRoleCertificationRequirements_Role").OnTable("PersonnelRoleCertificationRequirements")
					.OnColumn("PersonnelRoleId").Ascending();
				Create.Index("IX_PersonnelRoleCertificationRequirements_Department").OnTable("PersonnelRoleCertificationRequirements")
					.OnColumn("DepartmentId").Ascending();
				Create.Index("IX_PersonnelRoleCertificationRequirements_Type").OnTable("PersonnelRoleCertificationRequirements")
					.OnColumn("DepartmentCertificationTypeId").Ascending();
				Create.ForeignKey("FK_PersonnelRoleCertificationRequirements_Role").FromTable("PersonnelRoleCertificationRequirements").ForeignColumn("PersonnelRoleId")
					.ToTable("PersonnelRoles").PrimaryColumn("PersonnelRoleId");
				Create.ForeignKey("FK_PersonnelRoleCertificationRequirements_Type").FromTable("PersonnelRoleCertificationRequirements").ForeignColumn("DepartmentCertificationTypeId")
					.ToTable("DepartmentCertificationTypes").PrimaryColumn("DepartmentCertificationTypeId");
			}

			// ---- Department settings ----------------------------------------------------------------------------
			if (!Schema.Table("DepartmentCertificationSettings").Exists())
			{
				Create.Table("DepartmentCertificationSettings")
					.WithColumn("DepartmentId").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("EnforcementMode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RoleRemovalGraceDays").AsInt32().NotNullable().WithDefaultValue(30)
					.WithColumn("NotifyLeadDaysCsv").AsString(100).NotNullable().WithDefaultValue("60,30,14,7,1")
					.WithColumn("NotifyCertificationHolder").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("TreatPendingVerificationAsValid").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("SendAdminDigest").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable()
					.WithColumn("LastSweepLocalDate").AsDateTime2().Nullable();
			}
			// The worker's per-department sweep claim (one sweep per department-local day, survives a repeated or skipped hour).
			// Schema checks run when Up() is evaluated while Create.Table above is deferred, so a fresh database takes the
			// column from the create and only a database that already carries the table gets the alter.
			if (Schema.Table("DepartmentCertificationSettings").Exists() && !Schema.Table("DepartmentCertificationSettings").Column("LastSweepLocalDate").Exists())
				Alter.Table("DepartmentCertificationSettings").AddColumn("LastSweepLocalDate").AsDateTime2().Nullable();

			// ---- Unit-scoped records ----------------------------------------------------------------------------
			if (!Schema.Table("UnitCertifications").Exists())
			{
				Create.Table("UnitCertifications")
					.WithColumn("UnitCertificationId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("UnitId").AsInt32().NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DepartmentCertificationTypeId").AsInt32().NotNullable()
					.WithColumn("Number").AsString(int.MaxValue).Nullable()
					.WithColumn("IssuedBy").AsString(int.MaxValue).Nullable()
					.WithColumn("IssuedOn").AsDateTime2().Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StatusChangedOn").AsDateTime2().Nullable()
					.WithColumn("StatusChangedByUserId").AsString(128).Nullable()
					.WithColumn("StatusReason").AsString(500).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
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

				Create.Index("IX_UnitCertifications_Unit").OnTable("UnitCertifications")
					.OnColumn("UnitId").Ascending().OnColumn("IsDeleted").Ascending();
				Create.Index("IX_UnitCertifications_Department").OnTable("UnitCertifications")
					.OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending().OnColumn("ExpiresOn").Ascending();
				Create.Index("IX_UnitCertifications_Type").OnTable("UnitCertifications")
					.OnColumn("DepartmentCertificationTypeId").Ascending();
				Create.ForeignKey("FK_UnitCertifications_Unit").FromTable("UnitCertifications").ForeignColumn("UnitId")
					.ToTable("Units").PrimaryColumn("UnitId");
				Create.ForeignKey("FK_UnitCertifications_Type").FromTable("UnitCertifications").ForeignColumn("DepartmentCertificationTypeId")
					.ToTable("DepartmentCertificationTypes").PrimaryColumn("DepartmentCertificationTypeId");
			}

			// ---- PersonnelCertifications (additive typed-record columns) ------------------------------------------
			var certs = "PersonnelCertifications";
			if (!Schema.Table(certs).Column("DepartmentCertificationTypeId").Exists())
				Alter.Table(certs).AddColumn("DepartmentCertificationTypeId").AsInt32().Nullable();
			if (!Schema.Table(certs).Column("Status").Exists())
				Alter.Table(certs).AddColumn("Status").AsInt32().NotNullable().WithDefaultValue(0);
			if (!Schema.Table(certs).Column("StatusChangedOn").Exists())
				Alter.Table(certs).AddColumn("StatusChangedOn").AsDateTime2().Nullable();
			if (!Schema.Table(certs).Column("StatusChangedByUserId").Exists())
				Alter.Table(certs).AddColumn("StatusChangedByUserId").AsString(128).Nullable();
			if (!Schema.Table(certs).Column("StatusReason").Exists())
				Alter.Table(certs).AddColumn("StatusReason").AsString(500).Nullable();
			if (!Schema.Table(certs).Column("VerifiedByUserId").Exists())
				Alter.Table(certs).AddColumn("VerifiedByUserId").AsString(128).Nullable();
			if (!Schema.Table(certs).Column("VerifiedOn").Exists())
				Alter.Table(certs).AddColumn("VerifiedOn").AsDateTime2().Nullable();
			if (!Schema.Table(certs).Column("IsDeleted").Exists())
				Alter.Table(certs).AddColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false);

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PersonnelCertifications_Type' AND object_id = OBJECT_ID('PersonnelCertifications')) " +
				"CREATE INDEX [IX_PersonnelCertifications_Type] ON [PersonnelCertifications] ([DepartmentCertificationTypeId], [IsDeleted]);");
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PersonnelCertifications_Department_Expiry' AND object_id = OBJECT_ID('PersonnelCertifications')) " +
				"CREATE INDEX [IX_PersonnelCertifications_Department_Expiry] ON [PersonnelCertifications] ([DepartmentId], [IsDeleted], [ExpiresOn]);");

			// Type backfill: exact legacy text match within the department; enveloped (protected) rows cannot match and stay untyped.
			Execute.Sql(
				"UPDATE c SET c.[DepartmentCertificationTypeId] = t.[DepartmentCertificationTypeId] " +
				"FROM [PersonnelCertifications] c JOIN [DepartmentCertificationTypes] t ON t.[DepartmentId] = c.[DepartmentId] AND t.[Type] = c.[Type] AND t.[IsDeleted] = 0 " +
				"WHERE c.[DepartmentCertificationTypeId] IS NULL AND c.[IsProtected] = 0 AND c.[Type] IS NOT NULL AND c.[Type] <> '';");
		}

		public override void Down()
		{
			if (Schema.Table("UnitCertifications").Exists())
				Delete.Table("UnitCertifications");
			if (Schema.Table("DepartmentCertificationSettings").Exists())
				Delete.Table("DepartmentCertificationSettings");
			if (Schema.Table("PersonnelRoleCertificationRequirements").Exists())
				Delete.Table("PersonnelRoleCertificationRequirements");

			foreach (var column in new[] { "DepartmentCertificationTypeId", "Status", "StatusChangedOn", "StatusChangedByUserId", "StatusReason", "VerifiedByUserId", "VerifiedOn", "IsDeleted" })
				if (Schema.Table("PersonnelCertifications").Column(column).Exists())
					Delete.Column(column).FromTable("PersonnelCertifications");

			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DepartmentCertificationTypes_Code' AND object_id = OBJECT_ID('DepartmentCertificationTypes')) DROP INDEX [UX_DepartmentCertificationTypes_Code] ON [DepartmentCertificationTypes];");
			foreach (var column in new[] { "Code", "Category", "AppliesTo", "Description", "IssuingAuthority", "DefaultValidityMonths", "NeverExpires", "RenewalCreditHoursRequired", "RequiresVerification", "IsActive", "IsDeleted", "AddedOn", "AddedByUserId", "EditedOn", "EditedByUserId" })
				if (Schema.Table("DepartmentCertificationTypes").Column(column).Exists())
					Delete.Column(column).FromTable("DepartmentCertificationTypes");
		}
	}
}
