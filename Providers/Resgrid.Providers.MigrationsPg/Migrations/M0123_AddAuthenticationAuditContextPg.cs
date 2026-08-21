using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	// Authentication investigations use target-user equality with the same time range and
	// newest-first ordering as the existing audit queries. Build the supporting partial index
	// concurrently so historical rows without target context are excluded without blocking writes.
	[Migration(123, TransactionBehavior.None)]
	public class M0123_AddAuthenticationAuditContextPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("systemaudits").Column("targetuserid").Exists())
				Alter.Table("systemaudits").AddColumn("targetuserid").AsCustom("citext").Nullable();
			if (!Schema.Table("systemaudits").Column("sessionid").Exists())
				Alter.Table("systemaudits").AddColumn("sessionid").AsCustom("citext").Nullable();
			if (!Schema.Table("systemaudits").Column("correlationid").Exists())
				Alter.Table("systemaudits").AddColumn("correlationid").AsCustom("citext").Nullable();

			RemoveInvalidTargetUserIndex();

			// Expected query shape:
			// WHERE targetuserid = @TargetUserId AND loggedon >= @StartDate AND loggedon < @EndDate
			// ORDER BY loggedon DESC, systemauditid DESC.
			Execute.Sql(@"CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_systemaudits_targetuserid_loggedon
				ON systemaudits (targetuserid, loggedon DESC, systemauditid DESC)
				WHERE targetuserid IS NOT NULL;");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX CONCURRENTLY IF EXISTS ix_systemaudits_targetuserid_loggedon;");

			if (Schema.Table("systemaudits").Column("correlationid").Exists())
				Delete.Column("correlationid").FromTable("systemaudits");
			if (Schema.Table("systemaudits").Column("sessionid").Exists())
				Delete.Column("sessionid").FromTable("systemaudits");
			if (Schema.Table("systemaudits").Column("targetuserid").Exists())
				Delete.Column("targetuserid").FromTable("systemaudits");
		}

		private void RemoveInvalidTargetUserIndex()
		{
			// An interrupted concurrent build leaves an invalid catalog entry that IF NOT EXISTS
			// would otherwise mistake for a usable index.
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
						AND c.relname = 'ix_systemaudits_targetuserid_loggedon'
						AND NOT i.indisvalid
					LOOP
						EXECUTE format('DROP INDEX %I.%I', invalid_index.schema_name, invalid_index.index_name);
					END LOOP;
				END $$;");
		}
	}
}
