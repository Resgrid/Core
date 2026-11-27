using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0375_AddAcademyGuidance: academyguidancestates (member ids citext) and academylessonupdates. Existence-guarded.</summary>
	[Migration(375)]
	public class M0375_AddAcademyGuidancePg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("academyguidancestates").Exists())
			{
				Create.Table("academyguidancestates")
					.WithColumn("academyguidancestateid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("guidancekey").AsString(100).NotNullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("revision").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("tasksdone").AsString(1000).Nullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_academyguidancestates_member_key ON academyguidancestates (departmentid, userid, guidancekey);");

			if (!Schema.Table("academylessonupdates").Exists())
			{
				Create.Table("academylessonupdates")
					.WithColumn("academylessonupdateid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("systemkey").AsString(100).NotNullable()
					.WithColumn("lmscourseid").AsInt32().NotNullable()
					.WithColumn("lmscourseversionid").AsInt32().NotNullable()
					.WithColumn("revision").AsInt32().NotNullable()
					.WithColumn("policy").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("reassignduedays").AsInt32().Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("appliedthroughdepartmentid").AsInt32().Nullable()
					.WithColumn("completedon").AsDateTime2().Nullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_academylessonupdates_course_revision ON academylessonupdates (lmscourseid, revision);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_academylessonupdates_pending ON academylessonupdates (completedon, academylessonupdateid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS academylessonupdates;");
			Execute.Sql("DROP TABLE IF EXISTS academyguidancestates;");
		}
	}
}
