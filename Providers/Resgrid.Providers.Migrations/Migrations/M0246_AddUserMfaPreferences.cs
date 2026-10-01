using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 5: each user's last successful MFA method, shown as the default choice on any
	/// installation (plan section 7.5 rule 5). Non-secret account metadata.
	/// </summary>
	[Migration(246)]
	public class M0246_AddUserMfaPreferences : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("UserMfaPreferences").Exists())
				Create.Table("UserMfaPreferences")
					.WithColumn("UserId").AsString(128).PrimaryKey().NotNullable()
					.WithColumn("PreferredMethod").AsInt32().NotNullable()
					.WithColumn("UpdatedOnUtc").AsDateTime().NotNullable();
		}

		public override void Down() => Delete.Table("UserMfaPreferences");
	}
}
