using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Passkey plan Phase 4, slice 34 (section 7.9): what a sign-in's first factor established about its installation, so
	/// the member's Responder, asked to approve it, sees a shared workstation's sign-in as shared and with the station's
	/// label, as it already does for a locked shared session. Display only: the session still decides for itself. Columns
	/// are checked before each change, so the isolated database tests can apply this over just the tables they make.
	/// </summary>
	[Migration(256)]
	public class M0256_AddLoginTransactionInstallationPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("mfalogintransactions").Exists() && !Schema.Table("mfalogintransactions").Column("sharedmode").Exists())
				Alter.Table("mfalogintransactions").AddColumn("sharedmode").AsBoolean().NotNullable().WithDefaultValue(false);
			if (Schema.Table("mfalogintransactions").Exists() && !Schema.Table("mfalogintransactions").Column("installationlabel").Exists())
				Alter.Table("mfalogintransactions").AddColumn("installationlabel").AsString(256).Nullable();
		}

		public override void Down()
		{
			if (Schema.Table("mfalogintransactions").Exists() && Schema.Table("mfalogintransactions").Column("installationlabel").Exists())
				Delete.Column("installationlabel").FromTable("mfalogintransactions");
			if (Schema.Table("mfalogintransactions").Exists() && Schema.Table("mfalogintransactions").Column("sharedmode").Exists())
				Delete.Column("sharedmode").FromTable("mfalogintransactions");
		}
	}
}
