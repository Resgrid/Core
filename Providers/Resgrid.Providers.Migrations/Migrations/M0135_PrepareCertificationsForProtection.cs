using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Brings PersonnelCertifications into the ADP schema (plan section 5.1, Personnel family:
	/// "license/certification numbers and documents"). The table predates this repository's
	/// migrations, so it was never covered by M0127's capacity pass or M0128's row markers.
	///
	/// Two changes: every cataloged string column goes to NVARCHAR(MAX), because an AES-GCM envelope
	/// needs roughly 1.4 x plaintext + 70 characters and a bounded column cannot hold one for
	/// near-cap plaintext; and IsProtected marks a row whose values carry envelopes. Widening
	/// NVARCHAR(n) to MAX is metadata-only on SQL Server. Nullability is preserved exactly — Name is
	/// required on the entity, the rest are not — so this changes capacity and nothing else.
	///
	/// Additive and inert while no department is enrolled: the columns simply hold what they held.
	/// </summary>
	[Migration(135)]
	public class M0135_PrepareCertificationsForProtection : Migration
	{
		public override void Up()
		{
			Alter.Table("PersonnelCertifications").AlterColumn("Name").AsString(int.MaxValue).NotNullable();
			Alter.Table("PersonnelCertifications").AlterColumn("Number").AsString(int.MaxValue).Nullable();
			Alter.Table("PersonnelCertifications").AlterColumn("Type").AsString(int.MaxValue).Nullable();
			Alter.Table("PersonnelCertifications").AlterColumn("Area").AsString(int.MaxValue).Nullable();
			Alter.Table("PersonnelCertifications").AlterColumn("IssuedBy").AsString(int.MaxValue).Nullable();
			Alter.Table("PersonnelCertifications").AlterColumn("Filename").AsString(int.MaxValue).Nullable();

			if (!Schema.Table("PersonnelCertifications").Column("IsProtected").Exists())
				Alter.Table("PersonnelCertifications")
					.AddColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			// The marker only: narrowing the widened columns would truncate envelopes, and for a
			// protected department those columns are the only copy of the data.
			if (Schema.Table("PersonnelCertifications").Column("IsProtected").Exists())
				Delete.Column("IsProtected").FromTable("PersonnelCertifications");
		}
	}
}
