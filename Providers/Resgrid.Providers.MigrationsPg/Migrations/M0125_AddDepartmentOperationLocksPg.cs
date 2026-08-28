using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Department operation lock — a department-wide mutation freeze (reads continue) held by the ADP
	/// migration worker during an active overnight migration window, designed as a general platform
	/// mechanism. The partial unique index enforces at most one active lock per department at the
	/// database, closing the acquire race. CREATE INDEX CONCURRENTLY cannot run inside a transaction;
	/// statements are existence-guarded and invalid indexes are removed before retry.
	/// </summary>
	[Migration(125, TransactionBehavior.None)]
	public class M0125_AddDepartmentOperationLocksPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("departmentoperationlocks").Exists())
				Create.Table("departmentoperationlocks")
					.WithColumn("departmentoperationlockid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("locktype").AsInt32().NotNullable()
					.WithColumn("reason").AsCustom("citext").Nullable()
					.WithColumn("correlationid").AsCustom("citext").Nullable()
					.WithColumn("appliedutc").AsDateTime2().NotNullable()
					.WithColumn("appliedbyidentity").AsCustom("citext").Nullable()
					.WithColumn("heartbeatutc").AsDateTime2().NotNullable()
					.WithColumn("expiresutc").AsDateTime2().NotNullable()
					.WithColumn("projectedendutc").AsDateTime2().Nullable()
					.WithColumn("releasedutc").AsDateTime2().Nullable()
					.WithColumn("releasedby").AsCustom("citext").Nullable()
					.WithColumn("releasekind").AsInt32().Nullable();

			RemoveInvalidIndexes();

			// At most one active lock per department, enforced at the database.
			Execute.Sql("CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_departmentoperationlocks_department_active ON departmentoperationlocks (departmentid) WHERE releasedutc IS NULL;");

			// Liveness sweep: find active locks whose safety valve has passed.
			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_departmentoperationlocks_released_expires ON departmentoperationlocks (releasedutc, expiresutc);");
		}

		public override void Down()
		{
			if (Schema.Table("departmentoperationlocks").Exists())
				Delete.Table("departmentoperationlocks");
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
							'ux_departmentoperationlocks_department_active',
							'ix_departmentoperationlocks_released_expires')
						AND NOT i.indisvalid
					LOOP
						EXECUTE format('DROP INDEX %I.%I', invalid_index.schema_name, invalid_index.index_name);
					END LOOP;
				END $$;");
		}
	}
}
