using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 6 (section 10.1): which second factors a department accepts, next to RequireMfa and
	/// RequireSso, and the MFA policy version the server advances whenever the sign-in method rules change. Existing rows
	/// take the defaults, which is also how a department with no row behaves.
	/// </summary>
	[Migration(247)]
	public class M0247_AddSecurityPolicyMfaSwitches : Migration
	{
		public override void Up()
		{
			void AddFlag(string column, bool defaultValue)
			{
				if (!Schema.Table("DepartmentSecurityPolicies").Column(column).Exists())
					Alter.Table("DepartmentSecurityPolicies").AddColumn(column).AsBoolean().NotNullable().WithDefaultValue(defaultValue);
			}

			AddFlag("AllowPasskeysForLoginMfa", true);
			AddFlag("AllowPasskeysForAdp", true);
			AddFlag("AllowFederatedMfaForLoginMfa", false);
			AddFlag("AllowFederatedMfaForAdp", false);
			AddFlag("AllowResponderApproval", true);
			AddFlag("AcceptRecentLoginMfaForAdp", true);
			AddFlag("AcceptRecentUnlockMfaForAdp", true);

			if (!Schema.Table("DepartmentSecurityPolicies").Column("MfaPolicyVersion").Exists())
				Alter.Table("DepartmentSecurityPolicies").AddColumn("MfaPolicyVersion").AsInt64().NotNullable().WithDefaultValue(0L);
		}

		public override void Down()
		{
			foreach (var column in new[] { "AllowPasskeysForLoginMfa", "AllowPasskeysForAdp", "AllowFederatedMfaForLoginMfa", "AllowFederatedMfaForAdp",
				"AllowResponderApproval", "AcceptRecentLoginMfaForAdp", "AcceptRecentUnlockMfaForAdp", "MfaPolicyVersion" })
				if (Schema.Table("DepartmentSecurityPolicies").Column(column).Exists())
					Delete.Column(column).FromTable("DepartmentSecurityPolicies");
		}
	}
}
