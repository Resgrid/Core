using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0398_AddLmsCourseStudio: lmscourseorigins (one per course) and lmsflashcards (member ids citext). Existence-guarded.
	/// </summary>
	[Migration(398)]
	public class M0398_AddLmsCourseStudioPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("lmscourseorigins").Exists())
			{
				Create.Table("lmscourseorigins")
					.WithColumn("lmscourseoriginid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("lmscourseid").AsInt32().NotNullable()
					.WithColumn("lmscourseversionid").AsInt32().NotNullable()
					.WithColumn("originkind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sourcekey").AsString(200).Nullable()
					.WithColumn("sourcename").AsString(250).Nullable()
					.WithColumn("sourceversion").AsString(50).Nullable()
					.WithColumn("sourceurl").AsString(500).Nullable()
					.WithColumn("license").AsString(100).Nullable()
					.WithColumn("attribution").AsString(1000).Nullable()
					.WithColumn("exchangeid").AsString(64).Nullable()
					.WithColumn("citationsjson").AsCustom("text").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmscourseorigins_course ON lmscourseorigins (departmentid, lmscourseid);");

			if (!Schema.Table("lmsflashcards").Exists())
			{
				Create.Table("lmsflashcards")
					.WithColumn("lmsflashcardid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("lmscourseid").AsInt32().NotNullable()
					.WithColumn("sourcekind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sourceitemid").AsInt32().NotNullable()
					.WithColumn("sourceversionid").AsInt32().NotNullable()
					.WithColumn("sourceversionlabel").AsString(50).Nullable()
					.WithColumn("sourceanchor").AsString(128).Nullable()
					.WithColumn("sourceheading").AsString(500).Nullable()
					.WithColumn("sourcetitle").AsString(250).Nullable()
					.WithColumn("front").AsString(300).NotNullable()
					.WithColumn("back").AsString(600).NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("flagreason").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("flaggedon").AsDateTime2().Nullable()
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("exchangeid").AsString(64).Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("reviewedon").AsDateTime2().Nullable()
					.WithColumn("reviewedbyuserid").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsflashcards_course ON lmsflashcards (departmentid, lmscourseid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsflashcards_source ON lmsflashcards (departmentid, sourcekind, sourceitemid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS lmsflashcards;");
			Execute.Sql("DROP TABLE IF EXISTS lmscourseorigins;");
		}
	}
}
