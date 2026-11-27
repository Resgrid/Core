using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T0 baseline tables. All are baseline (free) and reference Trainings only, never an LMS table (§17.3). Each
	/// cascades from Trainings, which is deleted outright only when nobody completed it (archive otherwise, T-D21); department
	/// deletion clears them first (UnlinkedDepartmentCleanup).
	/// - TrainingQuizAttempts (R-T7): every submitted attempt with the quiz revision hash and a snapshot of the questions,
	///   options, answer key and chosen answers (R-T6); score and pass result stored, never recomputed (T-D20).
	/// - TrainingAudiences (T-D20): dynamic assignment rules, kind (ContentAudienceKinds) + ref id.
	/// - TrainingCompletionHistories: completions moved off live rows by a repeat, a material edit or a reset, for T1's
	///   transcript backfill.
	/// - TrainingQuestionFlags: "flag this question" feedback. No foreign key to TrainingQuestions (SQL Server refuses a
	///   second cascade path); the service removes a deleted question's flags.
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(339)]
	public class M0339_AddTrainingBaselineTables : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("TrainingQuizAttempts").Exists())
			{
				Create.Table("TrainingQuizAttempts")
					.WithColumn("TrainingQuizAttemptId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("TrainingId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("AttemptNumber").AsInt32().NotNullable()
					.WithColumn("Cycle").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubmittedOn").AsDateTime2().NotNullable()
					.WithColumn("QuizRevisionHash").AsString(64).NotNullable()
					.WithColumn("Snapshot").AsString(int.MaxValue).Nullable()
					.WithColumn("QuestionCount").AsInt32().NotNullable()
					.WithColumn("CorrectCount").AsInt32().NotNullable()
					.WithColumn("Score").AsDouble().NotNullable()
					.WithColumn("MinimumScore").AsDouble().NotNullable()
					.WithColumn("Passed").AsBoolean().NotNullable();

				Create.ForeignKey("FK_TrainingQuizAttempts_Trainings").FromTable("TrainingQuizAttempts").ForeignColumn("TrainingId")
					.ToTable("Trainings").PrimaryColumn("TrainingId").OnDelete(Rule.Cascade);

				Create.Index("IX_TrainingQuizAttempts_Department_Training_User").OnTable("TrainingQuizAttempts")
					.OnColumn("DepartmentId").Ascending().OnColumn("TrainingId").Ascending().OnColumn("UserId").Ascending();
				Create.Index("IX_TrainingQuizAttempts_Department_User").OnTable("TrainingQuizAttempts")
					.OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending();
			}

			if (!Schema.Table("TrainingAudiences").Exists())
			{
				Create.Table("TrainingAudiences")
					.WithColumn("TrainingAudienceId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("TrainingId").AsInt32().NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable()
					.WithColumn("RefId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable();

				Create.ForeignKey("FK_TrainingAudiences_Trainings").FromTable("TrainingAudiences").ForeignColumn("TrainingId")
					.ToTable("Trainings").PrimaryColumn("TrainingId").OnDelete(Rule.Cascade);

				Create.Index("IX_TrainingAudiences_Department_Training").OnTable("TrainingAudiences")
					.OnColumn("DepartmentId").Ascending().OnColumn("TrainingId").Ascending();
			}

			if (!Schema.Table("TrainingCompletionHistories").Exists())
			{
				Create.Table("TrainingCompletionHistories")
					.WithColumn("TrainingCompletionHistoryId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("TrainingId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("Cycle").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AssignedOn").AsDateTime2().Nullable()
					.WithColumn("DueOn").AsDateTime2().Nullable()
					.WithColumn("ViewedOn").AsDateTime2().Nullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable()
					.WithColumn("Score").AsDouble().NotNullable().WithDefaultValue(0)
					.WithColumn("Passed").AsBoolean().Nullable()
					.WithColumn("Reason").AsInt32().NotNullable()
					.WithColumn("RecordedOn").AsDateTime2().NotNullable()
					.WithColumn("RecordedByUserId").AsString(128).Nullable();

				Create.ForeignKey("FK_TrainingCompletionHistories_Trainings").FromTable("TrainingCompletionHistories").ForeignColumn("TrainingId")
					.ToTable("Trainings").PrimaryColumn("TrainingId").OnDelete(Rule.Cascade);

				Create.Index("IX_TrainingCompletionHistories_Department_Training").OnTable("TrainingCompletionHistories")
					.OnColumn("DepartmentId").Ascending().OnColumn("TrainingId").Ascending();
				Create.Index("IX_TrainingCompletionHistories_Department_User").OnTable("TrainingCompletionHistories")
					.OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending();
			}

			if (!Schema.Table("TrainingQuestionFlags").Exists())
			{
				Create.Table("TrainingQuestionFlags")
					.WithColumn("TrainingQuestionFlagId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("TrainingId").AsInt32().NotNullable()
					.WithColumn("TrainingQuestionId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("Comment").AsString(1000).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ResolvedOn").AsDateTime2().Nullable()
					.WithColumn("ResolvedByUserId").AsString(128).Nullable();

				Create.ForeignKey("FK_TrainingQuestionFlags_Trainings").FromTable("TrainingQuestionFlags").ForeignColumn("TrainingId")
					.ToTable("Trainings").PrimaryColumn("TrainingId").OnDelete(Rule.Cascade);

				Create.Index("IX_TrainingQuestionFlags_Department_Training").OnTable("TrainingQuestionFlags")
					.OnColumn("DepartmentId").Ascending().OnColumn("TrainingId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("TrainingQuestionFlags").Exists())
				Delete.Table("TrainingQuestionFlags");
			if (Schema.Table("TrainingCompletionHistories").Exists())
				Delete.Table("TrainingCompletionHistories");
			if (Schema.Table("TrainingAudiences").Exists())
				Delete.Table("TrainingAudiences");
			if (Schema.Table("TrainingQuizAttempts").Exists())
				Delete.Table("TrainingQuizAttempts");
		}
	}
}
