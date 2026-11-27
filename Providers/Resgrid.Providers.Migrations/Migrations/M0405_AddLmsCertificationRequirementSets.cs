using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 9A (LMS add-on): certification requirement sets. LmsCertificationRequirementSets (one per certification type: active, issue to
	/// non-holders, renew holders, due-soon days, member notices), LmsCertificationRequirementItems (the items: an LMS course, a baseline
	/// training, CE hours of a category, a course set / learning path, passed skill evaluations; a supervised practical counts only a
	/// completion an instructor recorded) and LmsProgramStates.RequirementSetSweepLocalDate (worker 94's day claim for the step). No foreign
	/// keys (§17.3). Existence-guarded for safe retry; Down removes exactly what Up added.
	/// </summary>
	[Migration(405)]
	public class M0405_AddLmsCertificationRequirementSets : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCertificationRequirementSets', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCertificationRequirementSets] (
		[LmsCertificationRequirementSetId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[DepartmentCertificationTypeId] int NOT NULL,
		[IsActive] bit NOT NULL CONSTRAINT [DF_LmsCertificationRequirementSets_IsActive] DEFAULT (1),
		[IssueToNonHolders] bit NOT NULL CONSTRAINT [DF_LmsCertificationRequirementSets_IssueToNonHolders] DEFAULT (1),
		[RenewHolders] bit NOT NULL CONSTRAINT [DF_LmsCertificationRequirementSets_RenewHolders] DEFAULT (1),
		[DueSoonDays] int NOT NULL CONSTRAINT [DF_LmsCertificationRequirementSets_DueSoonDays] DEFAULT (30),
		[NotifyMembers] bit NOT NULL CONSTRAINT [DF_LmsCertificationRequirementSets_NotifyMembers] DEFAULT (1),
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsCertificationRequirementSets] PRIMARY KEY CLUSTERED ([LmsCertificationRequirementSetId] ASC)
	);
END");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsCertificationRequirementSets_Type' AND object_id = OBJECT_ID('dbo.LmsCertificationRequirementSets'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsCertificationRequirementSets_Type] ON [dbo].[LmsCertificationRequirementSets] ([DepartmentId], [DepartmentCertificationTypeId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCertificationRequirementItems', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCertificationRequirementItems] (
		[LmsCertificationRequirementItemId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsCertificationRequirementSetId] int NOT NULL,
		[Kind] int NOT NULL,
		[TargetRefId] int NULL,
		[TargetCode] nvarchar(100) NULL,
		[RequiredHours] decimal(9,2) NULL,
		[RequiredCount] int NULL,
		[RequireRecordedByInstructor] bit NOT NULL CONSTRAINT [DF_LmsCertificationRequirementItems_RequireRecordedByInstructor] DEFAULT (0),
		[Label] nvarchar(200) NULL,
		[SortOrder] int NOT NULL CONSTRAINT [DF_LmsCertificationRequirementItems_SortOrder] DEFAULT (0),
		CONSTRAINT [PK_LmsCertificationRequirementItems] PRIMARY KEY CLUSTERED ([LmsCertificationRequirementItemId] ASC)
	);
END");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCertificationRequirementItems_Set' AND object_id = OBJECT_ID('dbo.LmsCertificationRequirementItems'))
	CREATE NONCLUSTERED INDEX [IX_LmsCertificationRequirementItems_Set] ON [dbo].[LmsCertificationRequirementItems] ([DepartmentId], [LmsCertificationRequirementSetId]);");

			Execute.Sql("IF OBJECT_ID('dbo.LmsProgramStates', 'U') IS NOT NULL AND COL_LENGTH('dbo.LmsProgramStates', 'RequirementSetSweepLocalDate') IS NULL ALTER TABLE [dbo].[LmsProgramStates] ADD [RequirementSetSweepLocalDate] datetime2 NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('dbo.LmsProgramStates', 'RequirementSetSweepLocalDate') IS NOT NULL ALTER TABLE [dbo].[LmsProgramStates] DROP COLUMN [RequirementSetSweepLocalDate];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsCertificationRequirementItems', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCertificationRequirementItems];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsCertificationRequirementSets', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCertificationRequirementSets];");
		}
	}
}
