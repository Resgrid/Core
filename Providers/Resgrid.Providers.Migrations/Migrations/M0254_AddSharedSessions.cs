using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
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
	public class M0254_AddSharedSessions : Migration
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

			AddColumn("UserSessions", "SharedMode", (t, c) => Alter.Table(t).AddColumn(c).AsBoolean().NotNullable().WithDefaultValue(false));
			AddColumn("UserSessions", "SharedModeSource", (t, c) => Alter.Table(t).AddColumn(c).AsInt32().NotNullable().WithDefaultValue(0));
			AddColumn("UserSessions", "SharedIdleLockMinutes", (t, c) => Alter.Table(t).AddColumn(c).AsInt32().Nullable());
			AddColumn("UserSessions", "LockVersion", (t, c) => Alter.Table(t).AddColumn(c).AsInt64().NotNullable().WithDefaultValue(0L));
			AddColumn("UserSessions", "IsLocked", (t, c) => Alter.Table(t).AddColumn(c).AsBoolean().NotNullable().WithDefaultValue(false));
			AddColumn("UserSessions", "LockedOnUtc", (t, c) => Alter.Table(t).AddColumn(c).AsDateTime2().Nullable());
			AddColumn("UserSessions", "LockReason", (t, c) => Alter.Table(t).AddColumn(c).AsInt32().Nullable());
			AddColumn("UserSessions", "LastOperatorActivityOn", (t, c) => Alter.Table(t).AddColumn(c).AsDateTime2().Nullable());

			AddColumn("DepartmentSecurityPolicies", "SharedIdleLockMinutes", (t, c) => Alter.Table(t).AddColumn(c).AsInt32().NotNullable().WithDefaultValue(5));
			AddColumn("DepartmentSecurityPolicies", "SharedShiftHours", (t, c) => Alter.Table(t).AddColumn(c).AsInt32().NotNullable().WithDefaultValue(12));
			AddColumn("DepartmentSecurityPolicies", "SharedModeRequiredApps", (t, c) => Alter.Table(t).AddColumn(c).AsInt32().NotNullable().WithDefaultValue(0));

			AddColumn("UserTotpStates", "EnrolledInSharedMode", (t, c) => Alter.Table(t).AddColumn(c).AsBoolean().NotNullable().WithDefaultValue(false));
		}

		public override void Down()
		{
			void Drop(string table, params string[] columns)
			{
				foreach (var column in columns)
					if (Schema.Table(table).Exists() && Schema.Table(table).Column(column).Exists())
						Delete.Column(column).FromTable(table);
			}

			Drop("UserTotpStates", "EnrolledInSharedMode");
			Drop("DepartmentSecurityPolicies", "SharedIdleLockMinutes", "SharedShiftHours", "SharedModeRequiredApps");
			Drop("UserSessions", "SharedMode", "SharedModeSource", "SharedIdleLockMinutes", "LockVersion", "IsLocked", "LockedOnUtc", "LockReason",
				"LastOperatorActivityOn");
		}
	}
}
