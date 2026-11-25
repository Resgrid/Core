using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T3 learning requirements (LMS): LmsRequirements (what, how often, due days, grace, onboarding auto-assign, the certification
	/// type a requirement backs for training-backed enforcement), LmsRequirementAudiences (the shared ContentAudienceKinds rules) and
	/// LmsRequirementMembers (one row per member under a requirement: the audience-entry anchor, exemption and extension, the last computed
	/// state and the notices claimed; one open row per member and requirement through a filtered unique index). No foreign key to baseline
	/// tables (§17.3). Guarded for safe retry.
	/// </summary>
	[Migration(383)]
	public class M0383_AddLmsRequirements : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsRequirements', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsRequirements] (
		[LmsRequirementId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[Name] nvarchar(200) NOT NULL,
		[Description] nvarchar(max) NULL,
		[Kind] int NOT NULL CONSTRAINT [DF_LmsRequirements_Kind] DEFAULT (0),
		[TargetRefId] int NULL,
		[TargetCode] nvarchar(100) NULL,
		[RequiredHours] decimal(9,2) NULL,
		[RequiredCount] int NULL,
		[Recurrence] int NOT NULL CONSTRAINT [DF_LmsRequirements_Recurrence] DEFAULT (0),
		[IntervalMonths] int NULL,
		[DueDays] int NOT NULL CONSTRAINT [DF_LmsRequirements_DueDays] DEFAULT (30),
		[GraceDays] int NOT NULL CONSTRAINT [DF_LmsRequirements_GraceDays] DEFAULT (0),
		[AtRiskDays] int NOT NULL CONSTRAINT [DF_LmsRequirements_AtRiskDays] DEFAULT (14),
		[NewMembersOnly] bit NOT NULL CONSTRAINT [DF_LmsRequirements_NewMembersOnly] DEFAULT (0),
		[AutoAssign] bit NOT NULL CONSTRAINT [DF_LmsRequirements_AutoAssign] DEFAULT (0),
		[IsOnboarding] bit NOT NULL CONSTRAINT [DF_LmsRequirements_IsOnboarding] DEFAULT (0),
		[BacksCertificationTypeId] int NULL,
		[EnforcementEnabled] bit NOT NULL CONSTRAINT [DF_LmsRequirements_EnforcementEnabled] DEFAULT (0),
		[AlertSupervisors] bit NOT NULL CONSTRAINT [DF_LmsRequirements_AlertSupervisors] DEFAULT (0),
		[AlertUserIds] nvarchar(max) NULL,
		[AlertOnCompletion] bit NOT NULL CONSTRAINT [DF_LmsRequirements_AlertOnCompletion] DEFAULT (0),
		[IsActive] bit NOT NULL CONSTRAINT [DF_LmsRequirements_IsActive] DEFAULT (1),
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		[DeletedOn] datetime2 NULL,
		[DeletedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsRequirements] PRIMARY KEY CLUSTERED ([LmsRequirementId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsRequirements_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsRequirements'))
	CREATE NONCLUSTERED INDEX [IX_LmsRequirements_DepartmentId] ON [dbo].[LmsRequirements] ([DepartmentId]) INCLUDE ([IsActive], [DeletedOn]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsRequirementAudiences', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsRequirementAudiences] (
		[LmsRequirementAudienceId] int IDENTITY(1,1) NOT NULL,
		[LmsRequirementId] int NOT NULL,
		[DepartmentId] int NOT NULL,
		[Kind] int NOT NULL,
		[RefId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsRequirementAudiences] PRIMARY KEY CLUSTERED ([LmsRequirementAudienceId] ASC),
		CONSTRAINT [FK_LmsRequirementAudiences_LmsRequirements] FOREIGN KEY ([LmsRequirementId]) REFERENCES [dbo].[LmsRequirements] ([LmsRequirementId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsRequirementAudiences_Requirement' AND object_id = OBJECT_ID('dbo.LmsRequirementAudiences'))
	CREATE NONCLUSTERED INDEX [IX_LmsRequirementAudiences_Requirement] ON [dbo].[LmsRequirementAudiences] ([LmsRequirementId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsRequirementAudiences_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsRequirementAudiences'))
	CREATE NONCLUSTERED INDEX [IX_LmsRequirementAudiences_DepartmentId] ON [dbo].[LmsRequirementAudiences] ([DepartmentId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsRequirementMembers', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsRequirementMembers] (
		[LmsRequirementMemberId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsRequirementId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[EnteredOn] datetime2 NOT NULL,
		[LeftOn] datetime2 NULL,
		[NotRequired] bit NOT NULL CONSTRAINT [DF_LmsRequirementMembers_NotRequired] DEFAULT (0),
		[State] int NOT NULL CONSTRAINT [DF_LmsRequirementMembers_State] DEFAULT (1),
		[StateChangedOn] datetime2 NULL,
		[DueOn] datetime2 NULL,
		[LastCompletedOn] datetime2 NULL,
		[ExemptOn] datetime2 NULL,
		[ExemptUntil] datetime2 NULL,
		[ExemptReason] nvarchar(500) NULL,
		[ExemptByUserId] nvarchar(128) NULL,
		[ExtendedDueOn] datetime2 NULL,
		[ExtendedByUserId] nvarchar(128) NULL,
		[ExtensionReason] nvarchar(500) NULL,
		[AtRiskNotifiedFor] datetime2 NULL,
		[OverdueNotifiedFor] datetime2 NULL,
		[AssignedForDueOn] datetime2 NULL,
		CONSTRAINT [PK_LmsRequirementMembers] PRIMARY KEY CLUSTERED ([LmsRequirementMemberId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsRequirementMembers_Department_Requirement' AND object_id = OBJECT_ID('dbo.LmsRequirementMembers'))
	CREATE NONCLUSTERED INDEX [IX_LmsRequirementMembers_Department_Requirement] ON [dbo].[LmsRequirementMembers] ([DepartmentId], [LmsRequirementId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsRequirementMembers_Department_User' AND object_id = OBJECT_ID('dbo.LmsRequirementMembers'))
	CREATE NONCLUSTERED INDEX [IX_LmsRequirementMembers_Department_User] ON [dbo].[LmsRequirementMembers] ([DepartmentId], [UserId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsRequirementMembers_Open' AND object_id = OBJECT_ID('dbo.LmsRequirementMembers'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsRequirementMembers_Open] ON [dbo].[LmsRequirementMembers] ([LmsRequirementId], [UserId]) WHERE [LeftOn] IS NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsRequirementMembers', 'U') IS NOT NULL DROP TABLE [dbo].[LmsRequirementMembers];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsRequirementAudiences', 'U') IS NOT NULL DROP TABLE [dbo].[LmsRequirementAudiences];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsRequirements', 'U') IS NOT NULL DROP TABLE [dbo].[LmsRequirements];");
		}
	}
}
