using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Passkey plan Phase 4, slice 34 (section 7.9): what a sign-in's first factor established about its installation, so
	/// the member's Responder, asked to approve it, sees a shared workstation's sign-in as shared and with the station's
	/// label, as it already does for a locked shared session. Display only: the session still decides for itself. Columns
	/// are checked before each change, so the isolated database tests can apply this over just the tables they make.
	/// </summary>
	[Migration(256)]
	public class M0256_AddLoginTransactionInstallation : Migration
	{
		public override void Up()
		{
			if (Schema.Table("MfaLoginTransactions").Exists() && !Schema.Table("MfaLoginTransactions").Column("SharedMode").Exists())
				Alter.Table("MfaLoginTransactions").AddColumn("SharedMode").AsBoolean().NotNullable().WithDefaultValue(false);
			if (Schema.Table("MfaLoginTransactions").Exists() && !Schema.Table("MfaLoginTransactions").Column("InstallationLabel").Exists())
				Alter.Table("MfaLoginTransactions").AddColumn("InstallationLabel").AsString(256).Nullable();
		}

		public override void Down()
		{
			if (Schema.Table("MfaLoginTransactions").Exists() && Schema.Table("MfaLoginTransactions").Column("InstallationLabel").Exists())
				Delete.Column("InstallationLabel").FromTable("MfaLoginTransactions");
			if (Schema.Table("MfaLoginTransactions").Exists() && Schema.Table("MfaLoginTransactions").Column("SharedMode").Exists())
				Delete.Column("SharedMode").FromTable("MfaLoginTransactions");
		}
	}
}
