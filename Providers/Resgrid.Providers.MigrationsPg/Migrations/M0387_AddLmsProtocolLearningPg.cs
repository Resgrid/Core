using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0387_AddLmsProtocolLearning: lmsprotocollearnings, lmsprotocolquizattempts, lmsmicrolearningitems,
	/// lmsmicrolearningruns (member ids citext) and lmsrequirements.academyupdatepolicy. Existence-guarded.
	/// </summary>
	[Migration(387)]
	public class M0387_AddLmsProtocolLearningPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("lmsprotocollearnings").Exists())
			{
				Create.Table("lmsprotocollearnings")
					.WithColumn("lmsprotocollearningid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("dispatchprotocolid").AsInt32().NotNullable()
					.WithColumn("lmsquestionbankid").AsInt32().Nullable()
					.WithColumn("quizrequired").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("questioncount").AsInt32().NotNullable().WithDefaultValue(5)
					.WithColumn("passmark").AsInt32().NotNullable().WithDefaultValue(80)
					.WithColumn("tags").AsString(500).Nullable()
					.WithColumn("microlearning").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().Nullable()
					.WithColumn("updatedbyuserid").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsprotocollearnings_protocol ON lmsprotocollearnings (departmentid, dispatchprotocolid);");

			if (!Schema.Table("lmsprotocolquizattempts").Exists())
			{
				Create.Table("lmsprotocolquizattempts")
					.WithColumn("lmsprotocolquizattemptid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("dispatchprotocolid").AsInt32().NotNullable()
					.WithColumn("protocolversionid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("lmsquestionbankid").AsInt32().NotNullable()
					.WithColumn("attemptnumber").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("startedon").AsDateTime2().NotNullable()
					.WithColumn("submittedon").AsDateTime2().Nullable()
					.WithColumn("score").AsDecimal(9, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("maxscore").AsDecimal(9, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("percent").AsDecimal(5, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("passed").AsBoolean().Nullable()
					.WithColumn("passmark").AsInt32().NotNullable().WithDefaultValue(80)
					.WithColumn("revisionhash").AsString(64).Nullable()
					.WithColumn("snapshotjson").AsCustom("text").Nullable()
					.WithColumn("answersjson").AsCustom("text").Nullable()
					.WithColumn("resultsjson").AsCustom("text").Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(0);
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsprotocolquizattempts_member_version ON lmsprotocolquizattempts (departmentid, userid, protocolversionid);");

			if (!Schema.Table("lmsmicrolearningitems").Exists())
			{
				Create.Table("lmsmicrolearningitems")
					.WithColumn("lmsmicrolearningitemid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("dispatchprotocolid").AsInt32().NotNullable()
					.WithColumn("lmsquestionid").AsInt32().NotNullable()
					.WithColumn("weekkey").AsString(10).NotNullable()
					.WithColumn("snapshotjson").AsCustom("text").Nullable()
					.WithColumn("senton").AsDateTime2().NotNullable()
					.WithColumn("answeredon").AsDateTime2().Nullable()
					.WithColumn("answersjson").AsString(4000).Nullable()
					.WithColumn("correct").AsBoolean().Nullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsmicrolearningitems_member_week ON lmsmicrolearningitems (departmentid, userid, weekkey);");

			if (!Schema.Table("lmsmicrolearningruns").Exists())
			{
				Create.Table("lmsmicrolearningruns")
					.WithColumn("lmsmicrolearningrunid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("nextrunon").AsDateTime2().NotNullable()
					.WithColumn("lastrunon").AsDateTime2().Nullable()
					.WithColumn("lastweekkey").AsString(10).Nullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsmicrolearningruns_department ON lmsmicrolearningruns (departmentid);");

			Execute.Sql("ALTER TABLE IF EXISTS lmsrequirements ADD COLUMN IF NOT EXISTS academyupdatepolicy integer NOT NULL DEFAULT 1;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE IF EXISTS lmsrequirements DROP COLUMN IF EXISTS academyupdatepolicy;");
			Execute.Sql("DROP TABLE IF EXISTS lmsmicrolearningruns;");
			Execute.Sql("DROP TABLE IF EXISTS lmsmicrolearningitems;");
			Execute.Sql("DROP TABLE IF EXISTS lmsprotocolquizattempts;");
			Execute.Sql("DROP TABLE IF EXISTS lmsprotocollearnings;");
		}
	}
}
