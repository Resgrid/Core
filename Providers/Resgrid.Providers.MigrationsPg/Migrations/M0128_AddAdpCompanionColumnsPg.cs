using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// ADP plan section 22.3 companion-column (Appendix B) pattern for cataloged typed columns that
	/// cannot hold a text envelope: callnotes and callattachments coordinates. When a department is
	/// protected, the decimal column is nulled and protected{name}envelope carries the rgdp value;
	/// isprotected marks the row. Purely additive and inert while no department is enrolled.
	/// </summary>
	[Migration(128)]
	public class M0128_AddAdpCompanionColumnsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("callnotes").Column("isprotected").Exists())
				Alter.Table("callnotes")
					.AddColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.AddColumn("protectedlatitudeenvelope").AsCustom("citext").Nullable()
					.AddColumn("protectedlongitudeenvelope").AsCustom("citext").Nullable();

			if (!Schema.Table("callattachments").Column("isprotected").Exists())
				Alter.Table("callattachments")
					.AddColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.AddColumn("protectedlatitudeenvelope").AsCustom("citext").Nullable()
					.AddColumn("protectedlongitudeenvelope").AsCustom("citext").Nullable();
		}

		public override void Down()
		{
			// Only safe while every department is Disabled; dropping the envelope columns of a
			// protected department destroys its coordinate ciphertext.
			if (Schema.Table("callnotes").Column("isprotected").Exists())
			{
				Delete.Column("protectedlongitudeenvelope").FromTable("callnotes");
				Delete.Column("protectedlatitudeenvelope").FromTable("callnotes");
				Delete.Column("isprotected").FromTable("callnotes");
			}

			if (Schema.Table("callattachments").Column("isprotected").Exists())
			{
				Delete.Column("protectedlongitudeenvelope").FromTable("callattachments");
				Delete.Column("protectedlatitudeenvelope").FromTable("callattachments");
				Delete.Column("isprotected").FromTable("callattachments");
			}
		}
	}
}
