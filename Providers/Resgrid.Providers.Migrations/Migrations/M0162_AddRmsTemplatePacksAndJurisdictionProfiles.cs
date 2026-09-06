using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records (RMS-1C) product template packs and locked jurisdiction profiles (plan section 4.1, registry M0162): product-scope rows (DepartmentId 0) mirrored from the code catalog so departments see provenance, review dates, locales, units, currency and deprecation; a pack update never mutates a department clone.
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(162)]
	public class M0162_AddRmsTemplatePacksAndJurisdictionProfiles : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsTemplatePackVersions").Exists())
			{
				Create.Table("RmsTemplatePackVersions")
					.WithColumn("RmsTemplatePackVersionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("PackKey").AsString(200).NotNullable()
					.WithColumn("Version").AsInt32().NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Category").AsString(200).Nullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("IsPreview").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DefinitionKeys").AsString(int.MaxValue).Nullable()
					.WithColumn("SupportedProfiles").AsString(400).Nullable()
					.WithColumn("SupportedLocales").AsString(400).Nullable()
					.WithColumn("ReleaseNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("SourceProvenanceJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ReviewedOn").AsDateTime2().Nullable()
					.WithColumn("ArtifactStatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ContentChecksum").AsString(128).Nullable()
					.WithColumn("IsDeprecated").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DeprecatedByPackKey").AsString(200).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsTemplatePackVersions_Key_Version ON RmsTemplatePackVersions (DepartmentId, PackKey, Version);");
			}

			if (!Schema.Table("RmsJurisdictionProfileVersions").Exists())
			{
				Create.Table("RmsJurisdictionProfileVersions")
					.WithColumn("RmsJurisdictionProfileVersionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("ProfileKey").AsString(64).NotNullable()
					.WithColumn("Version").AsInt32().NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Country").AsString(64).Nullable()
					.WithColumn("Subdivision").AsString(64).Nullable()
					.WithColumn("AgencyScope").AsString(200).Nullable()
					.WithColumn("DefaultLocale").AsString(64).Nullable()
					.WithColumn("SupportedLocales").AsString(400).Nullable()
					.WithColumn("MeasurementSystem").AsString(64).Nullable()
					.WithColumn("CurrencyCode").AsString(64).Nullable()
					.WithColumn("DefaultTimeZone").AsString(128).Nullable()
					.WithColumn("TerminologyJson").AsString(int.MaxValue).Nullable()
					.WithColumn("StandardsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ClassificationDefault").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RetentionYearsDefault").AsInt32().Nullable()
					.WithColumn("RequiredSections").AsString(int.MaxValue).Nullable()
					.WithColumn("ArtifactStatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ReviewedOn").AsDateTime2().Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsJurisdictionProfileVersions_Key_Version ON RmsJurisdictionProfileVersions (DepartmentId, ProfileKey, Version);");
			}

		}

		public override void Down()
		{
			if (Schema.Table("RmsJurisdictionProfileVersions").Exists())
				Delete.Table("RmsJurisdictionProfileVersions");
			if (Schema.Table("RmsTemplatePackVersions").Exists())
				Delete.Table("RmsTemplatePackVersions");
		}
	}
}
