using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 5F (protocols-training-knowledge-plan §17.2, P2 LMS parts and the A2 release rule): LmsProtocolLearnings (a protocol's linked
	/// question bank, whether its acknowledgment needs a bank quiz, micro-learning on or off; unique per department and protocol),
	/// LmsProtocolQuizAttempts (server-side attempt snapshots), LmsMicroLearningItems (one weekly question per member, unique per department,
	/// member and ISO week), LmsMicroLearningRuns (worker 86's weekly clock per department) and LmsRequirements.AcademyUpdatePolicy (what a
	/// major Academy update of a required lesson does: 1 notify by default). LMS tables carry DepartmentId and no foreign keys (§17.3);
	/// LmsDepartmentCleanup clears them. Existence-guarded.
	/// </summary>
	[Migration(387)]
	public class M0387_AddLmsProtocolLearning : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("LmsProtocolLearnings").Exists())
			{
				Create.Table("LmsProtocolLearnings")
					.WithColumn("LmsProtocolLearningId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DispatchProtocolId").AsInt32().NotNullable()
					.WithColumn("LmsQuestionBankId").AsInt32().Nullable()
					.WithColumn("QuizRequired").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("QuestionCount").AsInt32().NotNullable().WithDefaultValue(5)
					.WithColumn("PassMark").AsInt32().NotNullable().WithDefaultValue(80)
					.WithColumn("Tags").AsString(500).Nullable()
					.WithColumn("MicroLearning").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable();
			}

			if (!Schema.Table("LmsProtocolLearnings").Index("UX_LmsProtocolLearnings_Protocol").Exists())
				Create.Index("UX_LmsProtocolLearnings_Protocol").OnTable("LmsProtocolLearnings")
					.OnColumn("DepartmentId").Ascending().OnColumn("DispatchProtocolId").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("LmsProtocolQuizAttempts").Exists())
			{
				Create.Table("LmsProtocolQuizAttempts")
					.WithColumn("LmsProtocolQuizAttemptId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DispatchProtocolId").AsInt32().NotNullable()
					.WithColumn("ProtocolVersionId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("LmsQuestionBankId").AsInt32().NotNullable()
					.WithColumn("AttemptNumber").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StartedOn").AsDateTime2().NotNullable()
					.WithColumn("SubmittedOn").AsDateTime2().Nullable()
					.WithColumn("Score").AsDecimal(9, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("MaxScore").AsDecimal(9, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Percent").AsDecimal(5, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("Passed").AsBoolean().Nullable()
					.WithColumn("PassMark").AsInt32().NotNullable().WithDefaultValue(80)
					.WithColumn("RevisionHash").AsString(64).Nullable()
					.WithColumn("SnapshotJson").AsString(int.MaxValue).Nullable()
					.WithColumn("AnswersJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ResultsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(0);
			}

			if (!Schema.Table("LmsProtocolQuizAttempts").Index("IX_LmsProtocolQuizAttempts_Member_Version").Exists())
				Create.Index("IX_LmsProtocolQuizAttempts_Member_Version").OnTable("LmsProtocolQuizAttempts")
					.OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().OnColumn("ProtocolVersionId").Ascending();

			if (!Schema.Table("LmsMicroLearningItems").Exists())
			{
				Create.Table("LmsMicroLearningItems")
					.WithColumn("LmsMicroLearningItemId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("DispatchProtocolId").AsInt32().NotNullable()
					.WithColumn("LmsQuestionId").AsInt32().NotNullable()
					.WithColumn("WeekKey").AsString(10).NotNullable()
					.WithColumn("SnapshotJson").AsString(int.MaxValue).Nullable()
					.WithColumn("SentOn").AsDateTime2().NotNullable()
					.WithColumn("AnsweredOn").AsDateTime2().Nullable()
					.WithColumn("AnswersJson").AsString(4000).Nullable()
					.WithColumn("Correct").AsBoolean().Nullable();
			}

			if (!Schema.Table("LmsMicroLearningItems").Index("UX_LmsMicroLearningItems_Member_Week").Exists())
				Create.Index("UX_LmsMicroLearningItems_Member_Week").OnTable("LmsMicroLearningItems")
					.OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().OnColumn("WeekKey").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("LmsMicroLearningRuns").Exists())
			{
				Create.Table("LmsMicroLearningRuns")
					.WithColumn("LmsMicroLearningRunId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("NextRunOn").AsDateTime2().NotNullable()
					.WithColumn("LastRunOn").AsDateTime2().Nullable()
					.WithColumn("LastWeekKey").AsString(10).Nullable();
			}

			if (!Schema.Table("LmsMicroLearningRuns").Index("UX_LmsMicroLearningRuns_Department").Exists())
				Create.Index("UX_LmsMicroLearningRuns_Department").OnTable("LmsMicroLearningRuns")
					.OnColumn("DepartmentId").Ascending()
					.WithOptions().Unique();

			if (Schema.Table("LmsRequirements").Exists() && !Schema.Table("LmsRequirements").Column("AcademyUpdatePolicy").Exists())
				Alter.Table("LmsRequirements").AddColumn("AcademyUpdatePolicy").AsInt32().NotNullable().WithDefaultValue(1);
		}

		public override void Down()
		{
			if (Schema.Table("LmsRequirements").Exists() && Schema.Table("LmsRequirements").Column("AcademyUpdatePolicy").Exists())
			{
				// The column's default constraint has a generated name; drop it before the column (SQL Server).
				Execute.Sql(@"DECLARE @df sysname = (SELECT d.name FROM sys.default_constraints d JOIN sys.columns c ON c.default_object_id = d.object_id
WHERE d.parent_object_id = OBJECT_ID('LmsRequirements') AND c.name = 'AcademyUpdatePolicy');
IF @df IS NOT NULL EXEC('ALTER TABLE LmsRequirements DROP CONSTRAINT [' + @df + ']');");
				Delete.Column("AcademyUpdatePolicy").FromTable("LmsRequirements");
			}
			if (Schema.Table("LmsMicroLearningRuns").Exists())
				Delete.Table("LmsMicroLearningRuns");
			if (Schema.Table("LmsMicroLearningItems").Exists())
				Delete.Table("LmsMicroLearningItems");
			if (Schema.Table("LmsProtocolQuizAttempts").Exists())
				Delete.Table("LmsProtocolQuizAttempts");
			if (Schema.Table("LmsProtocolLearnings").Exists())
				Delete.Table("LmsProtocolLearnings");
		}
	}
}
