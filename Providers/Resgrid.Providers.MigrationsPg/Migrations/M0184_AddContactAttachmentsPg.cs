using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Contact site attachments (Contacts plan Phase A, A1; registry M0184, next physical number under the
	/// no-gaps rule): typed blob rows (document, pre-plan, floor plan, site photo, site drawing) mirroring
	/// CallAttachments, including the ADP IsProtected row marker (name, file name and bytes are catalog v12 fields).
	/// List queries read metadata only. Guarded for safe retry.
	/// </summary>
	[Migration(184)]
	public class M0184_AddContactAttachmentsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("contactattachments").Exists())
			{
				Create.Table("contactattachments")
					.WithColumn("contactattachmentid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("contactid").AsString(128).NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("contactattachmenttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("name").AsString(500).Nullable()
					.WithColumn("filename").AsString(500).Nullable()
					.WithColumn("filetype").AsString(200).Nullable()
					.WithColumn("size").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("data").AsCustom("bytea").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_contactattachments_contact ON contactattachments (departmentid, contactid, isdeleted);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("contactattachments").Exists())
				Delete.Table("contactattachments");
		}
	}
}
