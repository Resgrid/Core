using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T1 columns on existing tables:
	/// - Trainings: the foreign key T0's CategoryId column waited for (to TrainingCategories, set to null when a category row
	///   is removed; ids that point at no category are cleared first), and the Vector-parity fields MethodOfTraining,
	///   Objectives, Tags and ExternalCode (§13a).
	/// - CalendarItemTypes: CountsAsTraining and DefaultCategoryId (R-T9): check-in/out on items of such a type produces hours.
	/// - PersonnelCertificationCredits: LearningRecordId, the learning record a credit was derived from (no foreign key: the
	///   ledger has none; voiding the record removes the credit in the service).
	/// Guarded for safe retry. FluentMigrator evaluates Schema.*.Exists() before deferred expressions run, so every check
	/// here reads the state before this migration (pr-bot-review-triage note on M0081/M0082).
	/// </summary>
	[Migration(349)]
	public class M0349_TrainingT1Columns : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("Trainings").Column("MethodOfTraining").Exists())
				Alter.Table("Trainings").AddColumn("MethodOfTraining").AsInt32().Nullable();
			if (!Schema.Table("Trainings").Column("Objectives").Exists())
				Alter.Table("Trainings").AddColumn("Objectives").AsString(2000).Nullable();
			if (!Schema.Table("Trainings").Column("Tags").Exists())
				Alter.Table("Trainings").AddColumn("Tags").AsString(500).Nullable();
			if (!Schema.Table("Trainings").Column("ExternalCode").Exists())
				Alter.Table("Trainings").AddColumn("ExternalCode").AsString(64).Nullable();

			if (!Schema.Table("CalendarItemTypes").Column("CountsAsTraining").Exists())
				Alter.Table("CalendarItemTypes").AddColumn("CountsAsTraining").AsBoolean().NotNullable().WithDefaultValue(false);
			if (!Schema.Table("CalendarItemTypes").Column("DefaultCategoryId").Exists())
				Alter.Table("CalendarItemTypes").AddColumn("DefaultCategoryId").AsInt32().Nullable();

			if (!Schema.Table("PersonnelCertificationCredits").Column("LearningRecordId").Exists())
				Alter.Table("PersonnelCertificationCredits").AddColumn("LearningRecordId").AsInt32().Nullable();

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PersonnelCertificationCredits_LearningRecord' AND object_id = OBJECT_ID('dbo.PersonnelCertificationCredits')) " +
				"CREATE INDEX [IX_PersonnelCertificationCredits_LearningRecord] ON [dbo].[PersonnelCertificationCredits] ([LearningRecordId]) WHERE [LearningRecordId] IS NOT NULL;");

			// T0 stored a bare number in Trainings.CategoryId; anything that is not a category id would block the key.
			Execute.Sql(
				"IF OBJECT_ID('dbo.FK_Trainings_TrainingCategories', 'F') IS NULL " +
				"BEGIN " +
				"UPDATE [dbo].[Trainings] SET [CategoryId] = NULL WHERE [CategoryId] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[TrainingCategories] c WHERE c.[TrainingCategoryId] = [Trainings].[CategoryId] AND c.[DepartmentId] = [Trainings].[DepartmentId]); " +
				"ALTER TABLE [dbo].[Trainings] ADD CONSTRAINT [FK_Trainings_TrainingCategories] FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[TrainingCategories] ([TrainingCategoryId]) ON DELETE SET NULL; " +
				"END");

			Execute.Sql(
				"IF OBJECT_ID('dbo.FK_CalendarItemTypes_TrainingCategories', 'F') IS NULL " +
				"ALTER TABLE [dbo].[CalendarItemTypes] ADD CONSTRAINT [FK_CalendarItemTypes_TrainingCategories] FOREIGN KEY ([DefaultCategoryId]) REFERENCES [dbo].[TrainingCategories] ([TrainingCategoryId]) ON DELETE SET NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.FK_CalendarItemTypes_TrainingCategories', 'F') IS NOT NULL ALTER TABLE [dbo].[CalendarItemTypes] DROP CONSTRAINT [FK_CalendarItemTypes_TrainingCategories];");
			Execute.Sql("IF OBJECT_ID('dbo.FK_Trainings_TrainingCategories', 'F') IS NOT NULL ALTER TABLE [dbo].[Trainings] DROP CONSTRAINT [FK_Trainings_TrainingCategories];");
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PersonnelCertificationCredits_LearningRecord' AND object_id = OBJECT_ID('dbo.PersonnelCertificationCredits')) DROP INDEX [IX_PersonnelCertificationCredits_LearningRecord] ON [dbo].[PersonnelCertificationCredits];");

			if (Schema.Table("PersonnelCertificationCredits").Column("LearningRecordId").Exists())
				Delete.Column("LearningRecordId").FromTable("PersonnelCertificationCredits");
			if (Schema.Table("CalendarItemTypes").Column("DefaultCategoryId").Exists())
				Delete.Column("DefaultCategoryId").FromTable("CalendarItemTypes");
			if (Schema.Table("CalendarItemTypes").Column("CountsAsTraining").Exists())
				Delete.Column("CountsAsTraining").FromTable("CalendarItemTypes");
			if (Schema.Table("Trainings").Column("ExternalCode").Exists())
				Delete.Column("ExternalCode").FromTable("Trainings");
			if (Schema.Table("Trainings").Column("Tags").Exists())
				Delete.Column("Tags").FromTable("Trainings");
			if (Schema.Table("Trainings").Column("Objectives").Exists())
				Delete.Column("Objectives").FromTable("Trainings");
			if (Schema.Table("Trainings").Column("MethodOfTraining").Exists())
				Delete.Column("MethodOfTraining").FromTable("Trainings");
		}
	}
}
