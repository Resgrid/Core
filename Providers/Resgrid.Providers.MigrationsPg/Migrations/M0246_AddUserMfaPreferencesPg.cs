using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 5: each user's last successful MFA method, shown as the default choice on any
	/// installation (plan section 7.5 rule 5). Non-secret account metadata.
	/// </summary>
	[Migration(246)]
	public class M0246_AddUserMfaPreferencesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("usermfapreferences").Exists())
				Create.Table("usermfapreferences")
					.WithColumn("userid").AsString(128).PrimaryKey().NotNullable()
					.WithColumn("preferredmethod").AsInt32().NotNullable()
					.WithColumn("updatedonutc").AsDateTime().NotNullable();
		}

		public override void Down() => Delete.Table("usermfapreferences");
	}
}
