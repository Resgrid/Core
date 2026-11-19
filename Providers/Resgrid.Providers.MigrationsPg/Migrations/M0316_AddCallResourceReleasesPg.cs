using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Releasing a unit or a member from a call that stays open; see the SQL Server M0316_AddCallResourceReleases. citext like
	/// the other id and user-id columns; no foreign keys.
	/// </summary>
	[Migration(316)]
	public class M0316_AddCallResourceReleasesPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE calldispatchunits ADD COLUMN IF NOT EXISTS releasedon timestamp without time zone NULL;");
			Execute.Sql("ALTER TABLE calldispatchunits ADD COLUMN IF NOT EXISTS releasedbyuserid citext NULL;");

			if (!Schema.Table("callpersonnelreleases").Exists())
			{
				Create.Table("callpersonnelreleases")
					.WithColumn("callpersonnelreleaseid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("releasedon").AsDateTime2().NotNullable()
					.WithColumn("releasedbyuserid").AsCustom("citext").Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_callpersonnelreleases_department ON callpersonnelreleases (departmentid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_callpersonnelreleases_call_user ON callpersonnelreleases (callid, userid);");
			}
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS callpersonnelreleases;");
			Execute.Sql("ALTER TABLE calldispatchunits DROP COLUMN IF EXISTS releasedbyuserid;");
			Execute.Sql("ALTER TABLE calldispatchunits DROP COLUMN IF EXISTS releasedon;");
		}
	}
}
