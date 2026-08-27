using System;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// ADP plan section 22.3 companion-column (Appendix B) pattern for cataloged typed columns that
	/// cannot hold a text envelope: CallNotes and CallAttachments coordinates. When a department is
	/// protected, the decimal column is nulled and Protected{Name}Envelope carries the rgdp value;
	/// IsProtected marks the row. Purely additive and inert while no department is enrolled.
	/// (MessageRecipients/UnitStates coordinates follow when their families enter the catalog.)
	/// </summary>
	[Migration(128)]
	public class M0128_AddAdpCompanionColumns : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("CallNotes").Column("IsProtected").Exists())
				Alter.Table("CallNotes")
					.AddColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.AddColumn("ProtectedLatitudeEnvelope").AsString(int.MaxValue).Nullable()
					.AddColumn("ProtectedLongitudeEnvelope").AsString(int.MaxValue).Nullable();

			if (!Schema.Table("CallAttachments").Column("IsProtected").Exists())
				Alter.Table("CallAttachments")
					.AddColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.AddColumn("ProtectedLatitudeEnvelope").AsString(int.MaxValue).Nullable()
					.AddColumn("ProtectedLongitudeEnvelope").AsString(int.MaxValue).Nullable();
		}

		public override void Down()
		{
			// Only safe while every department is Disabled; dropping the envelope columns of a
			// protected department destroys its coordinate ciphertext.
			if (Schema.Table("CallNotes").Column("IsProtected").Exists())
			{
				Delete.Column("ProtectedLongitudeEnvelope").FromTable("CallNotes");
				Delete.Column("ProtectedLatitudeEnvelope").FromTable("CallNotes");
				Delete.Column("IsProtected").FromTable("CallNotes");
			}

			if (Schema.Table("CallAttachments").Column("IsProtected").Exists())
			{
				Delete.Column("ProtectedLongitudeEnvelope").FromTable("CallAttachments");
				Delete.Column("ProtectedLatitudeEnvelope").FromTable("CallAttachments");
				Delete.Column("IsProtected").FromTable("CallAttachments");
			}
		}
	}
}
