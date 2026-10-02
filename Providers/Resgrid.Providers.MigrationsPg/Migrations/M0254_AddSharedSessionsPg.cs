using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 13 (sections 5.5, 10.5 and 12.5): shared vehicle tablet and workstation sessions.
	///
	/// UserSessions gains the shared-session state: whether it is shared and why, the idle lock sign-in recorded, a lock
	/// version that every lock advances, the locked flag, when and why it last locked, and the last operator activity. The
	/// shift ceiling is the existing ExpiresOn. DepartmentSecurityPolicies gains the department's shared policy (idle lock
	/// 5 minutes, shift 12 hours, no app required), and UserTotpStates records whether the authenticator was set up in a
	/// shared session. Existing rows take the defaults: every existing session is personal and unlocked.
	/// </summary>
	[Migration(254)]
	public class M0254_AddSharedSessionsPg : Migration
	{
		public override void Up()
		{
			// Each table is checked as well as each column, so the isolated database tests can apply this over just the
			// tables they create; every real database has all three.
			void AddColumn(string table, string column, System.Action<string, string> add)
			{
				if (Schema.Table(table).Exists() && !Schema.Table(table).Column(column).Exists())
					add(table, column);
			}

			AddColumn("usersessions", "sharedmode", (t, c) => Alter.Table(t).AddColumn(c).AsBoolean().NotNullable().WithDefaultValue(false));
			AddColumn("usersessions", "sharedmodesource", (t, c) => Alter.Table(t).AddColumn(c).AsInt32().NotNullable().WithDefaultValue(0));
			AddColumn("usersessions", "sharedidlelockminutes", (t, c) => Alter.Table(t).AddColumn(c).AsInt32().Nullable());
			AddColumn("usersessions", "lockversion", (t, c) => Alter.Table(t).AddColumn(c).AsInt64().NotNullable().WithDefaultValue(0L));
			AddColumn("usersessions", "islocked", (t, c) => Alter.Table(t).AddColumn(c).AsBoolean().NotNullable().WithDefaultValue(false));
			AddColumn("usersessions", "lockedonutc", (t, c) => Alter.Table(t).AddColumn(c).AsDateTime2().Nullable());
			AddColumn("usersessions", "lockreason", (t, c) => Alter.Table(t).AddColumn(c).AsInt32().Nullable());
			AddColumn("usersessions", "lastoperatoractivityon", (t, c) => Alter.Table(t).AddColumn(c).AsDateTime2().Nullable());

			AddColumn("departmentsecuritypolicies", "sharedidlelockminutes", (t, c) => Alter.Table(t).AddColumn(c).AsInt32().NotNullable().WithDefaultValue(5));
			AddColumn("departmentsecuritypolicies", "sharedshifthours", (t, c) => Alter.Table(t).AddColumn(c).AsInt32().NotNullable().WithDefaultValue(12));
			AddColumn("departmentsecuritypolicies", "sharedmoderequiredapps", (t, c) => Alter.Table(t).AddColumn(c).AsInt32().NotNullable().WithDefaultValue(0));

			AddColumn("usertotpstates", "enrolledinsharedmode", (t, c) => Alter.Table(t).AddColumn(c).AsBoolean().NotNullable().WithDefaultValue(false));
		}

		public override void Down()
		{
			void Drop(string table, params string[] columns)
			{
				foreach (var column in columns)
					if (Schema.Table(table).Exists() && Schema.Table(table).Column(column).Exists())
						Delete.Column(column).FromTable(table);
			}

			Drop("usertotpstates", "enrolledinsharedmode");
			Drop("departmentsecuritypolicies", "sharedidlelockminutes", "sharedshifthours", "sharedmoderequiredapps");
			Drop("usersessions", "sharedmode", "sharedmodesource", "sharedidlelockminutes", "lockversion", "islocked", "lockedonutc", "lockreason",
				"lastoperatoractivityon");
		}
	}
}
