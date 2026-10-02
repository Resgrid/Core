using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 6 (section 10.1): which second factors a department accepts, next to RequireMfa and
	/// RequireSso, and the MFA policy version the server advances whenever the sign-in method rules change. Existing rows
	/// take the defaults, which is also how a department with no row behaves.
	/// </summary>
	[Migration(247)]
	public class M0247_AddSecurityPolicyMfaSwitchesPg : Migration
	{
		public override void Up()
		{
			void AddFlag(string column, bool defaultValue)
			{
				if (!Schema.Table("departmentsecuritypolicies").Column(column).Exists())
					Alter.Table("departmentsecuritypolicies").AddColumn(column).AsBoolean().NotNullable().WithDefaultValue(defaultValue);
			}

			AddFlag("allowpasskeysforloginmfa", true);
			AddFlag("allowpasskeysforadp", true);
			AddFlag("allowfederatedmfaforloginmfa", false);
			AddFlag("allowfederatedmfaforadp", false);
			AddFlag("allowresponderapproval", true);
			AddFlag("acceptrecentloginmfaforadp", true);
			AddFlag("acceptrecentunlockmfaforadp", true);

			if (!Schema.Table("departmentsecuritypolicies").Column("mfapolicyversion").Exists())
				Alter.Table("departmentsecuritypolicies").AddColumn("mfapolicyversion").AsInt64().NotNullable().WithDefaultValue(0L);
		}

		public override void Down()
		{
			foreach (var column in new[] { "allowpasskeysforloginmfa", "allowpasskeysforadp", "allowfederatedmfaforloginmfa", "allowfederatedmfaforadp",
				"allowresponderapproval", "acceptrecentloginmfaforadp", "acceptrecentunlockmfaforadp", "mfapolicyversion" })
				if (Schema.Table("departmentsecuritypolicies").Column(column).Exists())
					Delete.Column(column).FromTable("departmentsecuritypolicies");
		}
	}
}
