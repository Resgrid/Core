using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	// CREATE INDEX CONCURRENTLY cannot run inside a transaction. Each self-committing statement is
	// existence-guarded, and invalid indexes from an interrupted build are removed before retry.
	[Migration(122, TransactionBehavior.None)]
	public class M0122_AddUserExternalIdentityLinksPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("userexternalidentitylinks").Exists())
			{
				Create.Table("userexternalidentitylinks")
					.WithColumn("userexternalidentitylinkid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("departmentmemberid").AsInt32().NotNullable()
					.WithColumn("departmentssoconfigid").AsCustom("citext").NotNullable()
					.WithColumn("providertype").AsInt32().NotNullable()
					.WithColumn("issuer").AsCustom("citext").NotNullable()
					.WithColumn("externalsubject").AsCustom("citext").NotNullable()
					.WithColumn("linkmethod").AsInt32().NotNullable()
					.WithColumn("emailatlink").AsCustom("citext").Nullable()
					.WithColumn("isemailexternallymanaged").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("linkedon").AsDateTime2().NotNullable()
					.WithColumn("lastloginon").AsDateTime2().Nullable()
					.WithColumn("unlinkedon").AsDateTime2().Nullable()
					.WithColumn("unlinkedbyuserid").AsCustom("citext").Nullable();
			}

			RemoveInvalidIndexes();

			// Use raw IF NOT EXISTS statements so they execute after invalid-index cleanup. FluentMigrator
			// evaluates Schema.Index.Exists while collecting expressions, before the cleanup SQL runs.
			Execute.Sql("CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_userexternalidentitylinks_config_subject ON userexternalidentitylinks (departmentssoconfigid, externalsubject);");
			Execute.Sql("CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_userexternalidentitylinks_user_config ON userexternalidentitylinks (userid, departmentssoconfigid);");
			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_userexternalidentitylinks_department_member ON userexternalidentitylinks (departmentid, departmentmemberid);");
		}

		public override void Down()
		{
			// PostgreSQL has no concurrent DROP TABLE. Rollback requires ACCESS EXCLUSIVE and must
			// be scheduled for a maintenance window when identity-link writes are still possible.
			if (Schema.Table("userexternalidentitylinks").Exists())
				Delete.Table("userexternalidentitylinks");
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
							'ux_userexternalidentitylinks_config_subject',
							'ux_userexternalidentitylinks_user_config',
							'ix_userexternalidentitylinks_department_member')
						AND NOT i.indisvalid
					LOOP
						EXECUTE format('DROP INDEX %I.%I', invalid_index.schema_name, invalid_index.index_name);
					END LOOP;
				END $$;");
		}
	}
}
