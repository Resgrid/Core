using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Contact site attachments (Contacts plan Phase A, A1; registry M0184, next physical number under the
	/// no-gaps rule): typed blob rows (document, pre-plan, floor plan, site photo, site drawing) mirroring
	/// CallAttachments, including the ADP IsProtected row marker (name, file name and bytes are catalog v12 fields).
	/// List queries read metadata only. Guarded for safe retry.
	/// </summary>
	[Migration(184)]
	public class M0184_AddContactAttachments : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ContactAttachments").Exists())
			{
				Create.Table("ContactAttachments")
					.WithColumn("ContactAttachmentId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("ContactId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ContactAttachmentType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name").AsString(500).Nullable()
					.WithColumn("FileName").AsString(500).Nullable()
					.WithColumn("FileType").AsString(200).Nullable()
					.WithColumn("Size").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Data").AsBinary(int.MaxValue).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false);

				Create.Index("IX_ContactAttachments_Contact").OnTable("ContactAttachments")
					.OnColumn("DepartmentId").Ascending().OnColumn("ContactId").Ascending().OnColumn("IsDeleted").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("ContactAttachments").Exists())
				Delete.Table("ContactAttachments");
		}
	}
}
