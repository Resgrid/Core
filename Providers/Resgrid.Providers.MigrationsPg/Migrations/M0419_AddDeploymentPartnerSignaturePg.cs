using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0419_AddDeploymentPartnerSignature: rmsexternalorders.partnerdepartmentlinkid / partnerdepartmentid and
	/// rmsexternalorderfills.partnerdepartmentid / partnersignstatus / partnersignrequestid / partnersignrequestedon / partnersignedon /
	/// partnersignedbyuserid, plus ix_rmsexternalorderfills_partnersign. Placeholder number. Guarded for safe retry.
	/// </summary>
	[Migration(419)]
	public class M0419_AddDeploymentPartnerSignaturePg : Migration
	{
		private static readonly (string Table, string Name, string Type)[] Columns =
		{
			("rmsexternalorders", "partnerdepartmentlinkid", "integer NULL"),
			("rmsexternalorders", "partnerdepartmentid", "integer NULL"),
			("rmsexternalorderfills", "partnerdepartmentid", "integer NULL"),
			("rmsexternalorderfills", "partnersignstatus", "integer NOT NULL DEFAULT 0"),
			("rmsexternalorderfills", "partnersignrequestid", "integer NULL"),
			("rmsexternalorderfills", "partnersignrequestedon", "timestamp NULL"),
			("rmsexternalorderfills", "partnersignedon", "timestamp NULL"),
			("rmsexternalorderfills", "partnersignedbyuserid", "citext NULL")
		};

		public override void Up()
		{
			foreach (var (table, name, type) in Columns)
				Execute.Sql($"ALTER TABLE IF EXISTS public.{table} ADD COLUMN IF NOT EXISTS {name} {type};");
			Execute.Sql(@"DO $$ BEGIN
IF to_regclass('public.rmsexternalorderfills') IS NOT NULL THEN
	CREATE INDEX IF NOT EXISTS ix_rmsexternalorderfills_partnersign ON rmsexternalorderfills (partnerdepartmentid, partnersignstatus);
END IF;
END $$;");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS ix_rmsexternalorderfills_partnersign;");
			foreach (var (table, name, _) in Columns)
				Execute.Sql($"ALTER TABLE IF EXISTS public.{table} DROP COLUMN IF EXISTS {name};");
		}
	}
}
