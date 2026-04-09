using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(62)]
	public class M0062_AddingCommunicationTests : Migration
	{
		public override void Up()
		{
			Create.Table("CommunicationTests")
				.WithColumn("CommunicationTestId").AsGuid().NotNullable().PrimaryKey().WithDefault(SystemMethods.NewGuid)
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("Name").AsString(500).NotNullable()
				.WithColumn("Description").AsString(4000).Nullable()
				.WithColumn("ScheduleType").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("Sunday").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("Monday").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("Tuesday").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("Wednesday").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("Thursday").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("Friday").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("Saturday").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("DayOfMonth").AsInt32().Nullable()
				.WithColumn("Time").AsString(50).Nullable()
				.WithColumn("TestSms").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("TestEmail").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("TestVoice").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("TestPush").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("Active").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("CreatedByUserId").AsString(128).NotNullable()
				.WithColumn("CreatedOn").AsDateTime2().NotNullable()
				.WithColumn("UpdatedOn").AsDateTime2().Nullable()
				.WithColumn("ResponseWindowMinutes").AsInt32().NotNullable().WithDefaultValue(60);

			Create.ForeignKey("FK_CommunicationTests_Departments")
				.FromTable("CommunicationTests").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.Index("IX_CommunicationTests_DepartmentId")
				.OnTable("CommunicationTests")
				.OnColumn("DepartmentId").Ascending();

			Create.Table("CommunicationTestRuns")
				.WithColumn("CommunicationTestRunId").AsGuid().NotNullable().PrimaryKey().WithDefault(SystemMethods.NewGuid)
				.WithColumn("CommunicationTestId").AsGuid().NotNullable()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("InitiatedByUserId").AsString(128).Nullable()
				.WithColumn("StartedOn").AsDateTime2().NotNullable()
				.WithColumn("CompletedOn").AsDateTime2().Nullable()
				.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("RunCode").AsString(20).NotNullable()
				.WithColumn("TotalUsersTested").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("TotalResponses").AsInt32().NotNullable().WithDefaultValue(0);

			Create.ForeignKey("FK_CommunicationTestRuns_CommunicationTests")
				.FromTable("CommunicationTestRuns").ForeignColumn("CommunicationTestId")
				.ToTable("CommunicationTests").PrimaryColumn("CommunicationTestId");

			Create.Index("IX_CommunicationTestRuns_CommunicationTestId")
				.OnTable("CommunicationTestRuns")
				.OnColumn("CommunicationTestId").Ascending();

			Create.Index("IX_CommunicationTestRuns_DepartmentId")
				.OnTable("CommunicationTestRuns")
				.OnColumn("DepartmentId").Ascending();

			Create.Index("IX_CommunicationTestRuns_RunCode")
				.OnTable("CommunicationTestRuns")
				.OnColumn("RunCode").Ascending()
				.WithOptions().Unique();

			Create.Table("CommunicationTestResults")
				.WithColumn("CommunicationTestResultId").AsGuid().NotNullable().PrimaryKey().WithDefault(SystemMethods.NewGuid)
				.WithColumn("CommunicationTestRunId").AsGuid().NotNullable()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("UserId").AsString(128).NotNullable()
				.WithColumn("Channel").AsInt32().NotNullable()
				.WithColumn("ContactValue").AsString(500).Nullable()
				.WithColumn("ContactCarrier").AsString(200).Nullable()
				.WithColumn("VerificationStatus").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("SendAttempted").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("SendSucceeded").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("SentOn").AsDateTime2().Nullable()
				.WithColumn("Responded").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("RespondedOn").AsDateTime2().Nullable()
				.WithColumn("ResponseToken").AsString(128).Nullable();

			Create.ForeignKey("FK_CommunicationTestResults_CommunicationTestRuns")
				.FromTable("CommunicationTestResults").ForeignColumn("CommunicationTestRunId")
				.ToTable("CommunicationTestRuns").PrimaryColumn("CommunicationTestRunId");

			Create.Index("IX_CommunicationTestResults_CommunicationTestRunId")
				.OnTable("CommunicationTestResults")
				.OnColumn("CommunicationTestRunId").Ascending();

			Create.Index("IX_CommunicationTestResults_DepartmentId")
				.OnTable("CommunicationTestResults")
				.OnColumn("DepartmentId").Ascending();

			Create.Index("IX_CommunicationTestResults_ResponseToken")
				.OnTable("CommunicationTestResults")
				.OnColumn("ResponseToken").Ascending();
		}

		public override void Down()
		{
			Delete.Table("CommunicationTestResults");
			Delete.Table("CommunicationTestRuns");
			Delete.Table("CommunicationTests");
		}
	}
}
