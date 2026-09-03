using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Department Profile elevation (RMS plan section 4.10.1, registry M0172). DepartmentProfileMedia holds the
	/// uploaded logo and its server-generated renditions (PrintHeader, EmailMasthead, Thumbnail) under one
	/// per-department MediaKey for the anonymous masthead endpoint. DepartmentProfiles gains
	/// UseDepartmentBrandingInEmails. The legacy DepartmentProfile.Logo bytes are copied once into a PrimaryLogo
	/// row (renditions are generated on first read) and the column is never written again. Existence-guarded.
	/// </summary>
	[Migration(172)]
	public class M0172_AddDepartmentProfileMediaAndBranding : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("DepartmentProfileMedia").Exists())
			{
				Create.Table("DepartmentProfileMedia")
					.WithColumn("DepartmentProfileMediaId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable()
					.WithColumn("ContentType").AsString(100).Nullable()
					.WithColumn("Width").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Height").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ByteSize").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("Checksum").AsString(128).Nullable()
					.WithColumn("Data").AsBinary(int.MaxValue).Nullable()
					.WithColumn("UploadedByUserId").AsString(128).Nullable()
					.WithColumn("UploadedOn").AsDateTime2().NotNullable()
					.WithColumn("MediaKey").AsString(64).NotNullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_DepartmentProfileMedia_Department_Kind ON DepartmentProfileMedia (DepartmentId, Kind);");
				Create.Index("IX_DepartmentProfileMedia_MediaKey_Kind").OnTable("DepartmentProfileMedia")
					.OnColumn("MediaKey").Ascending().OnColumn("Kind").Ascending();
			}

			if (Schema.Table("DepartmentProfiles").Exists() && !Schema.Table("DepartmentProfiles").Column("UseDepartmentBrandingInEmails").Exists())
			{
				Alter.Table("DepartmentProfiles").AddColumn("UseDepartmentBrandingInEmails").AsBoolean().NotNullable().WithDefaultValue(false);
			}

			if (Schema.Table("DepartmentProfiles").Exists() && Schema.Table("DepartmentProfiles").Column("Logo").Exists())
			{
				// One-time copy of a populated legacy logo. Content type and dimensions are unknown here; the
				// service decodes the bytes on first read, generates the renditions and records the real values.
				Execute.Sql(@"INSERT INTO DepartmentProfileMedia (DepartmentProfileMediaId, DepartmentId, ProtectionId, Kind, ContentType, Width, Height, ByteSize, Checksum, Data, UploadedByUserId, UploadedOn, MediaKey, CreatedOn, ModifiedOn, RowVersion)
SELECT LOWER(CONVERT(varchar(36), NEWID())), p.DepartmentId, LOWER(CONVERT(varchar(36), NEWID())), 1, 'application/octet-stream', 0, 0, DATALENGTH(p.Logo), NULL, p.Logo, NULL, GETUTCDATE(), LOWER(REPLACE(CONVERT(varchar(36), NEWID()), '-', '')), GETUTCDATE(), GETUTCDATE(), 1
FROM DepartmentProfiles p
WHERE p.Logo IS NOT NULL AND DATALENGTH(p.Logo) > 0
  AND NOT EXISTS (SELECT 1 FROM DepartmentProfileMedia m WHERE m.DepartmentId = p.DepartmentId AND m.Kind = 1);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("DepartmentProfileMedia").Exists())
				Delete.Table("DepartmentProfileMedia");

			if (Schema.Table("DepartmentProfiles").Exists() && Schema.Table("DepartmentProfiles").Column("UseDepartmentBrandingInEmails").Exists())
				Delete.Column("UseDepartmentBrandingInEmails").FromTable("DepartmentProfiles");
		}
	}
}
