using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T3 training-backed enforcement (R-L11) and worker 93's clock: LmsRequirementEnforcements (each action with the
	/// certification record's previous status and expiry, the requirement and the learning record that caused it, the roles that required
	/// the certification, and its undo; one open action per requirement and member) and LmsRequirementDepartmentStates (the claimed
	/// department-local day of the daily pass and the last blast-radius refusal). Guarded for safe retry.
	/// </summary>
	[Migration(385)]
	public class M0385_AddLmsRequirementEnforcement : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsRequirementEnforcements', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsRequirementEnforcements] (
		[LmsRequirementEnforcementId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsRequirementId] int NOT NULL,
		[LmsRequirementMemberId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[PersonnelCertificationId] int NOT NULL,
		[DepartmentCertificationTypeId] int NOT NULL,
		[PreviousStatus] int NOT NULL,
		[PreviousExpiresOn] datetime2 NULL,
		[CauseLearningRecordId] int NULL,
		[DueOn] datetime2 NOT NULL,
		[ActedOn] datetime2 NOT NULL,
		[RolesJson] nvarchar(max) NULL,
		[UndoneOn] datetime2 NULL,
		[UndoReason] int NULL,
		[RestoredRolesJson] nvarchar(max) NULL,
		CONSTRAINT [PK_LmsRequirementEnforcements] PRIMARY KEY CLUSTERED ([LmsRequirementEnforcementId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsRequirementEnforcements_Department_User' AND object_id = OBJECT_ID('dbo.LmsRequirementEnforcements'))
	CREATE NONCLUSTERED INDEX [IX_LmsRequirementEnforcements_Department_User] ON [dbo].[LmsRequirementEnforcements] ([DepartmentId], [UserId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsRequirementEnforcements_Open' AND object_id = OBJECT_ID('dbo.LmsRequirementEnforcements'))
	CREATE NONCLUSTERED INDEX [IX_LmsRequirementEnforcements_Open] ON [dbo].[LmsRequirementEnforcements] ([DepartmentId]) WHERE [UndoneOn] IS NULL;");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsRequirementEnforcements_OpenAction' AND object_id = OBJECT_ID('dbo.LmsRequirementEnforcements'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsRequirementEnforcements_OpenAction] ON [dbo].[LmsRequirementEnforcements] ([LmsRequirementId], [UserId]) WHERE [UndoneOn] IS NULL;");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsRequirementDepartmentStates', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsRequirementDepartmentStates] (
		[LmsRequirementDepartmentStateId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LastSweepLocalDate] datetime2 NULL,
		[LastSweepOn] datetime2 NULL,
		[LastRefusedOn] datetime2 NULL,
		CONSTRAINT [PK_LmsRequirementDepartmentStates] PRIMARY KEY CLUSTERED ([LmsRequirementDepartmentStateId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsRequirementDepartmentStates_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsRequirementDepartmentStates'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsRequirementDepartmentStates_DepartmentId] ON [dbo].[LmsRequirementDepartmentStates] ([DepartmentId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsRequirementDepartmentStates', 'U') IS NOT NULL DROP TABLE [dbo].[LmsRequirementDepartmentStates];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsRequirementEnforcements', 'U') IS NOT NULL DROP TABLE [dbo].[LmsRequirementEnforcements];");
		}
	}
}
