using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Passkey plan Phase 4, slice 37 (section 12.5.2): whether a brokered round trip came from a shared installation, so its
	/// callback can require the provider's own sign-in to be fresh and the next operator cannot ride on the last one's
	/// provider session. Columns are checked before each change, so the isolated database tests can apply this over just
	/// the tables they make.
	/// </summary>
	[Migration(257)]
	public class M0257_AddSsoTransactionSharedInstallation : Migration
	{
		public override void Up()
		{
			if (Schema.Table("SsoLoginTransactions").Exists() && !Schema.Table("SsoLoginTransactions").Column("SharedInstallation").Exists())
				Alter.Table("SsoLoginTransactions").AddColumn("SharedInstallation").AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			if (Schema.Table("SsoLoginTransactions").Exists() && Schema.Table("SsoLoginTransactions").Column("SharedInstallation").Exists())
				Delete.Column("SharedInstallation").FromTable("SsoLoginTransactions");
		}
	}
}
