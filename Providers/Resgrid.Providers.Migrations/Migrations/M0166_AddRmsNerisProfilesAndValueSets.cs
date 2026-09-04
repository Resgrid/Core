using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// NERIS profiles, value sets and crosswalks (RMS plan section 5.5, registry M0166, RMS-2). RmsNerisProfiles:
	/// one per department — entity ID, environment, department-encrypted credential, pinned contract version.
	/// RmsNerisValueSets: global reference data seeded from the pinned snapshot, keyed by contract version.
	/// RmsNerisCrosswalks: department-owned Resgrid/CAD code to NERIS value mapping; the original code is always
	/// kept beside the mapped one.
	/// </summary>
	[Migration(166)]
	public class M0166_AddRmsNerisProfilesAndValueSets : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsNerisProfiles").Exists())
			{
				Create.Table("RmsNerisProfiles")
					.WithColumn("RmsNerisProfileId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("NerisEntityId").AsString(50).Nullable()
					.WithColumn("EntityName").AsString(200).Nullable()
					.WithColumn("Environment").AsString(20).NotNullable().WithDefaultValue("production")
					.WithColumn("BaseUrlOverride").AsString(400).Nullable()
					.WithColumn("GrantType").AsString(30).NotNullable().WithDefaultValue("client_credentials")
					.WithColumn("EncryptedCredentialJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ContractVersion").AsString(20).Nullable()
					.WithColumn("AutoSubmitOnFinalize").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("LastTokenIssuedOn").AsDateTime2().Nullable()
					.WithColumn("LastSuccessfulCallOn").AsDateTime2().Nullable()
					.WithColumn("LastError").AsString(int.MaxValue).Nullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsNerisProfiles_Department ON RmsNerisProfiles (DepartmentId);");
			}

			if (!Schema.Table("RmsNerisValueSets").Exists())
			{
				Create.Table("RmsNerisValueSets")
					.WithColumn("RmsNerisValueSetEntryId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("ContractVersion").AsString(20).NotNullable()
					.WithColumn("SetKey").AsString(60).NotNullable()
					.WithColumn("Code").AsString(300).NotNullable()
					.WithColumn("Label").AsString(300).Nullable()
					.WithColumn("ParentCode").AsString(300).Nullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsRetired").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable();

				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsNerisValueSets_Version_Set_Code ON RmsNerisValueSets (ContractVersion, SetKey, Code);");
			}

			if (!Schema.Table("RmsNerisCrosswalks").Exists())
			{
				Create.Table("RmsNerisCrosswalks")
					.WithColumn("RmsNerisCrosswalkId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("ContractVersion").AsString(20).NotNullable()
					.WithColumn("SetKey").AsString(60).NotNullable()
					.WithColumn("LocalSource").AsString(50).NotNullable()
					.WithColumn("LocalCode").AsString(200).NotNullable()
					.WithColumn("NerisCode").AsString(300).NotNullable()
					.WithColumn("IsDefault").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsNerisCrosswalks_Department_Version").OnTable("RmsNerisCrosswalks")
					.OnColumn("DepartmentId").Ascending().OnColumn("ContractVersion").Ascending();
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsNerisCrosswalks_Local ON RmsNerisCrosswalks (DepartmentId, ContractVersion, SetKey, LocalSource, LocalCode) WHERE DeletedOn IS NULL;");
			}
		}

		public override void Down()
		{
			foreach (var table in new[] { "RmsNerisCrosswalks", "RmsNerisValueSets", "RmsNerisProfiles" })
			{
				if (Schema.Table(table).Exists())
					Delete.Table(table);
			}
		}
	}
}
