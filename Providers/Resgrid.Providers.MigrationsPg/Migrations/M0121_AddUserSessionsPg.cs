using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	// CREATE INDEX CONCURRENTLY cannot run inside a transaction. Each self-committing statement is
	// existence-guarded, and invalid indexes from an interrupted build are removed before retry.
	[Migration(121, TransactionBehavior.None)]
	public class M0121_AddUserSessionsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("usersessions").Exists())
			{
				Create.Table("usersessions")
					.WithColumn("usersessionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("departmentid").AsInt32().Nullable()
					.WithColumn("authenticationgeneration").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("stateversion").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("clientapplication").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("clientinstanceidhash").AsCustom("citext").Nullable()
					.WithColumn("devicename").AsCustom("citext").Nullable()
					.WithColumn("devicetype").AsCustom("citext").Nullable()
					.WithColumn("operatingsystem").AsCustom("citext").Nullable()
					.WithColumn("browser").AsCustom("citext").Nullable()
					.WithColumn("applicationversion").AsCustom("citext").Nullable()
					.WithColumn("authenticationmethod").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("departmentssoconfigid").AsCustom("citext").Nullable()
					.WithColumn("openiddictauthorizationid").AsCustom("citext").Nullable()
					.WithColumn("webcookieticketkey").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("lastactiveon").AsDateTime2().NotNullable()
					.WithColumn("expireson").AsDateTime2().NotNullable()
					.WithColumn("firstipaddress").AsCustom("citext").Nullable()
					.WithColumn("lastipaddress").AsCustom("citext").Nullable()
					.WithColumn("lastcountry").AsCustom("citext").Nullable()
					.WithColumn("lastregion").AsCustom("citext").Nullable()
					.WithColumn("lastcity").AsCustom("citext").Nullable()
					.WithColumn("useragent").AsCustom("citext").Nullable()
					.WithColumn("islegacyadopted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("revokedon").AsDateTime2().Nullable()
					.WithColumn("revokedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("revocationreason").AsInt32().Nullable();
			}

			RemoveInvalidIndexes();

			// Use raw IF NOT EXISTS statements so they execute after invalid-index cleanup. FluentMigrator
			// evaluates Schema.Index.Exists while collecting expressions, before the cleanup SQL runs.
			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_usersessions_user_state_expiry_activity ON usersessions (userid, state, expireson, lastactiveon DESC);");
			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_usersessions_department_user_state ON usersessions (departmentid, userid, state);");
			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_usersessions_revoked_expiry ON usersessions (revokedon, expireson);");
			Execute.Sql("CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_usersessions_openiddictauthorizationid ON usersessions (openiddictauthorizationid) WHERE openiddictauthorizationid IS NOT NULL;");
		}

		public override void Down()
		{
			// PostgreSQL has no concurrent DROP TABLE. Rollback requires ACCESS EXCLUSIVE and must
			// be scheduled for a maintenance window when active sessions can still write this table.
			if (Schema.Table("usersessions").Exists())
				Delete.Table("usersessions");
		}

		private void RemoveInvalidIndexes()
		{
			Execute.Sql(@"
				DO $$
				DECLARE invalid_index record;
				BEGIN
					FOR invalid_index IN
						SELECT n.nspname AS schema_name, c.relname AS index_name
						FROM pg_class c
						JOIN pg_index i ON i.indexrelid = c.oid
						JOIN pg_namespace n ON n.oid = c.relnamespace
						WHERE n.nspname = current_schema()
						AND c.relname IN (
							'ix_usersessions_user_state_expiry_activity',
							'ix_usersessions_department_user_state',
							'ix_usersessions_revoked_expiry',
							'ux_usersessions_openiddictauthorizationid')
						AND NOT i.indisvalid
					LOOP
						EXECUTE format('DROP INDEX %I.%I', invalid_index.schema_name, invalid_index.index_name);
					END LOOP;
				END $$;");
		}
	}
}
