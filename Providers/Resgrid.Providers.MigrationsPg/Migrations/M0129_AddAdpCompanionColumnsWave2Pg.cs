using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// ADP plan section 22.3 companion-column (Appendix B) pattern, wave 2: messagerecipients and
	/// unitstates coordinates/telemetry. When a department is protected AND these families enter the
	/// protected-field catalog, the typed column is nulled and protected{name}envelope carries the
	/// rgdp value; isprotected marks the row. Purely additive and INERT today — catalog v1 does not
	/// include these tables, so no engine touches the new columns until the catalog-v2 bindings ship.
	/// </summary>
	[Migration(129)]
	public class M0129_AddAdpCompanionColumnsWave2Pg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("messagerecipients").Column("isprotected").Exists())
				Alter.Table("messagerecipients")
					.AddColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.AddColumn("protectedlatitudeenvelope").AsCustom("citext").Nullable()
					.AddColumn("protectedlongitudeenvelope").AsCustom("citext").Nullable();

			if (!Schema.Table("unitstates").Column("isprotected").Exists())
				Alter.Table("unitstates")
					.AddColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.AddColumn("protectedlatitudeenvelope").AsCustom("citext").Nullable()
					.AddColumn("protectedlongitudeenvelope").AsCustom("citext").Nullable()
					.AddColumn("protectedaccuracyenvelope").AsCustom("citext").Nullable()
					.AddColumn("protectedaltitudeenvelope").AsCustom("citext").Nullable()
					.AddColumn("protectedaltitudeaccuracyenvelope").AsCustom("citext").Nullable()
					.AddColumn("protectedspeedenvelope").AsCustom("citext").Nullable()
					.AddColumn("protectedheadingenvelope").AsCustom("citext").Nullable();
		}

		public override void Down()
		{
			// Only safe while every department is Disabled; dropping the envelope columns of a
			// protected department destroys its coordinate/telemetry ciphertext.
			if (Schema.Table("messagerecipients").Column("isprotected").Exists())
			{
				Delete.Column("protectedlongitudeenvelope").FromTable("messagerecipients");
				Delete.Column("protectedlatitudeenvelope").FromTable("messagerecipients");
				Delete.Column("isprotected").FromTable("messagerecipients");
			}

			if (Schema.Table("unitstates").Column("isprotected").Exists())
			{
				Delete.Column("protectedheadingenvelope").FromTable("unitstates");
				Delete.Column("protectedspeedenvelope").FromTable("unitstates");
				Delete.Column("protectedaltitudeaccuracyenvelope").FromTable("unitstates");
				Delete.Column("protectedaltitudeenvelope").FromTable("unitstates");
				Delete.Column("protectedlongitudeenvelope").FromTable("unitstates");
				Delete.Column("protectedlatitudeenvelope").FromTable("unitstates");
				Delete.Column("isprotected").FromTable("unitstates");
			}
		}
	}
}
