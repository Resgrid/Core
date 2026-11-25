using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0340_AddContentAudienceAndMemberLeave: contentaudiencechanges (worker 83's membership-change queue),
	/// memberleaveperiods (R-C2 leave of absence) and memberpinneditems (My Training pins). No foreign keys.
	/// </summary>
	[Migration(340)]
	public class M0340_AddContentAudienceAndMemberLeavePg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("contentaudiencechanges").Exists())
			{
				Create.Table("contentaudiencechanges")
					.WithColumn("contentaudiencechangeid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").Nullable()
					.WithColumn("source").AsInt32().NotNullable()
					.WithColumn("queuedon").AsDateTime2().NotNullable()
					.WithColumn("processedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_contentaudiencechanges_processedon ON contentaudiencechanges (processedon, contentaudiencechangeid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_contentaudiencechanges_departmentid ON contentaudiencechanges (departmentid);");
			}

			if (!Schema.Table("memberleaveperiods").Exists())
			{
				Create.Table("memberleaveperiods")
					.WithColumn("memberleaveperiodid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("startson").AsDateTime2().NotNullable()
					.WithColumn("endson").AsDateTime2().NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("note").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_memberleaveperiods_department_user ON memberleaveperiods (departmentid, userid);");
			}

			if (!Schema.Table("memberpinneditems").Exists())
			{
				Create.Table("memberpinneditems")
					.WithColumn("memberpinneditemid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("kind").AsInt32().NotNullable()
					.WithColumn("refid").AsCustom("citext").NotNullable()
					.WithColumn("pinnedon").AsDateTime2().NotNullable();

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_memberpinneditems_department_user_kind_ref ON memberpinneditems (departmentid, userid, kind, refid);");
			}
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS memberpinneditems;");
			Execute.Sql("DROP TABLE IF EXISTS memberleaveperiods;");
			Execute.Sql("DROP TABLE IF EXISTS contentaudiencechanges;");
		}
	}
}
