using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Postgres twin of M0135_PrepareCertificationsForProtection. The capacity half is a no-op here:
	/// PostgreSQL stores these columns as unbounded citext, so an rgdp envelope always fits. Only the
	/// row marker is added.
	/// </summary>
	[Migration(135)]
	public class M0135_PrepareCertificationsForProtectionPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("personnelcertifications").Column("isprotected").Exists())
				Alter.Table("personnelcertifications")
					.AddColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			if (Schema.Table("personnelcertifications").Column("isprotected").Exists())
				Delete.Column("isprotected").FromTable("personnelcertifications");
		}
	}
}
