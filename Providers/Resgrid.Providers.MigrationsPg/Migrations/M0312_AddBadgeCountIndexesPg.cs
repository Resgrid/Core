using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Indexes behind the menu and tab badge counts (v4 Counts/GetBadgeCounts); the PostgreSQL twin of
	/// M0312_AddBadgeCountIndexes. messagerecipients had no index on userid, so the unread inbox count (and the inbox
	/// list) scanned the table; weatheralerts was indexed on departmentid alone, so counting a department's active
	/// Extreme and Severe alerts read every alert it ever had; shiftdays, shiftsignups, shiftsignuptradeusers and the trade
	/// target column had none, so the member's shift and trade counts scanned them.
	///
	/// TransactionBehavior.None because CREATE INDEX CONCURRENTLY cannot run inside a transaction. A concurrent build
	/// that failed part-way leaves an invalid index behind, which IF NOT EXISTS would then keep; those are dropped first
	/// so a re-run builds them again.
	/// </summary>
	[Migration(312, TransactionBehavior.None)]
	public class M0312_AddBadgeCountIndexesPg : Migration
	{
		public override void Up()
		{
			RemoveInvalidIndexes();

			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_messagerecipients_userid_isdeleted_readon ON messagerecipients (userid, isdeleted, readon, messageid);");
			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_weatheralerts_departmentid_status_severity ON weatheralerts (departmentid, status, severity, expiresutc);");
			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_shiftdays_shiftid_day ON shiftdays (shiftid, day);");
			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_shiftsignups_shiftid_shiftday_userid ON shiftsignups (shiftid, shiftday, userid);");
			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_shiftsignuptradeusers_userid ON shiftsignuptradeusers (userid, shiftsignuptradeid);");
			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_shiftsignuptrades_targetshiftsignupid ON shiftsignuptrades (targetshiftsignupid) WHERE targetshiftsignupid IS NOT NULL;");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX CONCURRENTLY IF EXISTS ix_shiftsignuptrades_targetshiftsignupid;");
			Execute.Sql("DROP INDEX CONCURRENTLY IF EXISTS ix_shiftsignuptradeusers_userid;");
			Execute.Sql("DROP INDEX CONCURRENTLY IF EXISTS ix_shiftsignups_shiftid_shiftday_userid;");
			Execute.Sql("DROP INDEX CONCURRENTLY IF EXISTS ix_shiftdays_shiftid_day;");
			Execute.Sql("DROP INDEX CONCURRENTLY IF EXISTS ix_weatheralerts_departmentid_status_severity;");
			Execute.Sql("DROP INDEX CONCURRENTLY IF EXISTS ix_messagerecipients_userid_isdeleted_readon;");
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
							'ix_messagerecipients_userid_isdeleted_readon',
							'ix_weatheralerts_departmentid_status_severity',
							'ix_shiftdays_shiftid_day',
							'ix_shiftsignups_shiftid_shiftday_userid',
							'ix_shiftsignuptradeusers_userid',
							'ix_shiftsignuptrades_targetshiftsignupid')
						AND NOT i.indisvalid
					LOOP
						EXECUTE format('DROP INDEX %I.%I', invalid_index.schema_name, invalid_index.index_name);
					END LOOP;
				END $$;");
		}
	}
}
