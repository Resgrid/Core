using System;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// ADP plan section 22.3 companion-column (Appendix B) pattern, wave 2: MessageRecipients and
	/// UnitStates coordinates/telemetry. When a department is protected AND these families enter the
	/// protected-field catalog, the typed column is nulled and Protected{Name}Envelope carries the
	/// rgdp value; IsProtected marks the row. Purely additive and INERT today — catalog v1 does not
	/// include these tables, so no engine touches the new columns until the catalog-v2 bindings ship
	/// (section 5.2 decision: unit telemetry is protected when linked to a protected call).
	/// </summary>
	[Migration(129)]
	public class M0129_AddAdpCompanionColumnsWave2 : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("MessageRecipients").Column("IsProtected").Exists())
				Alter.Table("MessageRecipients")
					.AddColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.AddColumn("ProtectedLatitudeEnvelope").AsString(int.MaxValue).Nullable()
					.AddColumn("ProtectedLongitudeEnvelope").AsString(int.MaxValue).Nullable();

			if (!Schema.Table("UnitStates").Column("IsProtected").Exists())
				Alter.Table("UnitStates")
					.AddColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.AddColumn("ProtectedLatitudeEnvelope").AsString(int.MaxValue).Nullable()
					.AddColumn("ProtectedLongitudeEnvelope").AsString(int.MaxValue).Nullable()
					.AddColumn("ProtectedAccuracyEnvelope").AsString(int.MaxValue).Nullable()
					.AddColumn("ProtectedAltitudeEnvelope").AsString(int.MaxValue).Nullable()
					.AddColumn("ProtectedAltitudeAccuracyEnvelope").AsString(int.MaxValue).Nullable()
					.AddColumn("ProtectedSpeedEnvelope").AsString(int.MaxValue).Nullable()
					.AddColumn("ProtectedHeadingEnvelope").AsString(int.MaxValue).Nullable();
		}

		public override void Down()
		{
			// Only safe while every department is Disabled; dropping the envelope columns of a
			// protected department destroys its coordinate/telemetry ciphertext.
			if (Schema.Table("MessageRecipients").Column("IsProtected").Exists())
			{
				Delete.Column("ProtectedLongitudeEnvelope").FromTable("MessageRecipients");
				Delete.Column("ProtectedLatitudeEnvelope").FromTable("MessageRecipients");
				Delete.Column("IsProtected").FromTable("MessageRecipients");
			}

			if (Schema.Table("UnitStates").Column("IsProtected").Exists())
			{
				Delete.Column("ProtectedHeadingEnvelope").FromTable("UnitStates");
				Delete.Column("ProtectedSpeedEnvelope").FromTable("UnitStates");
				Delete.Column("ProtectedAltitudeAccuracyEnvelope").FromTable("UnitStates");
				Delete.Column("ProtectedAltitudeEnvelope").FromTable("UnitStates");
				Delete.Column("ProtectedLongitudeEnvelope").FromTable("UnitStates");
				Delete.Column("ProtectedLatitudeEnvelope").FromTable("UnitStates");
				Delete.Column("IsProtected").FromTable("UnitStates");
			}
		}
	}
}
