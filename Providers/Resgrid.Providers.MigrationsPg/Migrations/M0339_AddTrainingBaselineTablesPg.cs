using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0339_AddTrainingBaselineTables: trainingquizattempts, trainingaudiences,
	/// trainingcompletionhistories and trainingquestionflags, each cascading from trainings (baseline tables, no LMS keys).
	/// </summary>
	[Migration(339)]
	public class M0339_AddTrainingBaselineTablesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("trainingquizattempts").Exists())
			{
				Create.Table("trainingquizattempts")
					.WithColumn("trainingquizattemptid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("trainingid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("attemptnumber").AsInt32().NotNullable()
					.WithColumn("cycle").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("submittedon").AsDateTime2().NotNullable()
					.WithColumn("quizrevisionhash").AsString(64).NotNullable()
					.WithColumn("snapshot").AsCustom("text").Nullable()
					.WithColumn("questioncount").AsInt32().NotNullable()
					.WithColumn("correctcount").AsInt32().NotNullable()
					.WithColumn("score").AsDouble().NotNullable()
					.WithColumn("minimumscore").AsDouble().NotNullable()
					.WithColumn("passed").AsBoolean().NotNullable();

				Create.ForeignKey("fk_trainingquizattempts_trainings").FromTable("trainingquizattempts").ForeignColumn("trainingid")
					.ToTable("trainings").PrimaryColumn("trainingid").OnDelete(Rule.Cascade);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_trainingquizattempts_department_training_user ON trainingquizattempts (departmentid, trainingid, userid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_trainingquizattempts_department_user ON trainingquizattempts (departmentid, userid);");
			}

			if (!Schema.Table("trainingaudiences").Exists())
			{
				Create.Table("trainingaudiences")
					.WithColumn("trainingaudienceid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("trainingid").AsInt32().NotNullable()
					.WithColumn("kind").AsInt32().NotNullable()
					.WithColumn("refid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable();

				Create.ForeignKey("fk_trainingaudiences_trainings").FromTable("trainingaudiences").ForeignColumn("trainingid")
					.ToTable("trainings").PrimaryColumn("trainingid").OnDelete(Rule.Cascade);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_trainingaudiences_department_training ON trainingaudiences (departmentid, trainingid);");
			}

			if (!Schema.Table("trainingcompletionhistories").Exists())
			{
				Create.Table("trainingcompletionhistories")
					.WithColumn("trainingcompletionhistoryid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("trainingid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("cycle").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("assignedon").AsDateTime2().Nullable()
					.WithColumn("dueon").AsDateTime2().Nullable()
					.WithColumn("viewedon").AsDateTime2().Nullable()
					.WithColumn("completedon").AsDateTime2().Nullable()
					.WithColumn("score").AsDouble().NotNullable().WithDefaultValue(0)
					.WithColumn("passed").AsBoolean().Nullable()
					.WithColumn("reason").AsInt32().NotNullable()
					.WithColumn("recordedon").AsDateTime2().NotNullable()
					.WithColumn("recordedbyuserid").AsCustom("citext").Nullable();

				Create.ForeignKey("fk_trainingcompletionhistories_trainings").FromTable("trainingcompletionhistories").ForeignColumn("trainingid")
					.ToTable("trainings").PrimaryColumn("trainingid").OnDelete(Rule.Cascade);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_trainingcompletionhistories_department_training ON trainingcompletionhistories (departmentid, trainingid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_trainingcompletionhistories_department_user ON trainingcompletionhistories (departmentid, userid);");
			}

			if (!Schema.Table("trainingquestionflags").Exists())
			{
				Create.Table("trainingquestionflags")
					.WithColumn("trainingquestionflagid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("trainingid").AsInt32().NotNullable()
					.WithColumn("trainingquestionid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("comment").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("resolvedon").AsDateTime2().Nullable()
					.WithColumn("resolvedbyuserid").AsCustom("citext").Nullable();

				Create.ForeignKey("fk_trainingquestionflags_trainings").FromTable("trainingquestionflags").ForeignColumn("trainingid")
					.ToTable("trainings").PrimaryColumn("trainingid").OnDelete(Rule.Cascade);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_trainingquestionflags_department_training ON trainingquestionflags (departmentid, trainingid);");
			}
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS trainingquestionflags;");
			Execute.Sql("DROP TABLE IF EXISTS trainingcompletionhistories;");
			Execute.Sql("DROP TABLE IF EXISTS trainingaudiences;");
			Execute.Sql("DROP TABLE IF EXISTS trainingquizattempts;");
		}
	}
}
