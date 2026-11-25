using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T3 learning paths: LmsPaths (department paths, and the Academy role paths as system paths with DepartmentId 0 and a
	/// unique SystemKey) and LmsPathSteps (position, target, prerequisite positions, elective group). LmsPathAssignments gains LmsPathId,
	/// CompletedOn, PersonnelCertificationId and LmsRequirementId: A1's ordered-list assignments are linked to their system path by the
	/// seeder (PathKey → SystemKey) without re-assignment, and progress keeps coming from the lesson enrollments. Guarded for safe retry.
	/// </summary>
	[Migration(384)]
	public class M0384_AddLmsPaths : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsPaths', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsPaths] (
		[LmsPathId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[SystemKey] nvarchar(100) NULL,
		[Name] nvarchar(200) NOT NULL,
		[Description] nvarchar(max) NULL,
		[IssuesCertificationTypeId] int NULL,
		[IsDeleted] bit NOT NULL CONSTRAINT [DF_LmsPaths_IsDeleted] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsPaths] PRIMARY KEY CLUSTERED ([LmsPathId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsPaths_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsPaths'))
	CREATE NONCLUSTERED INDEX [IX_LmsPaths_DepartmentId] ON [dbo].[LmsPaths] ([DepartmentId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsPaths_SystemKey' AND object_id = OBJECT_ID('dbo.LmsPaths'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsPaths_SystemKey] ON [dbo].[LmsPaths] ([SystemKey]) WHERE [SystemKey] IS NOT NULL;");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsPathSteps', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsPathSteps] (
		[LmsPathStepId] int IDENTITY(1,1) NOT NULL,
		[LmsPathId] int NOT NULL,
		[DepartmentId] int NOT NULL,
		[SortOrder] int NOT NULL,
		[Kind] int NOT NULL CONSTRAINT [DF_LmsPathSteps_Kind] DEFAULT (0),
		[TargetRefId] int NOT NULL,
		[TargetSystemKey] nvarchar(100) NULL,
		[PrerequisiteOrders] nvarchar(400) NULL,
		[ElectiveGroup] int NULL,
		[ElectiveRequired] int NOT NULL CONSTRAINT [DF_LmsPathSteps_ElectiveRequired] DEFAULT (0),
		CONSTRAINT [PK_LmsPathSteps] PRIMARY KEY CLUSTERED ([LmsPathStepId] ASC),
		CONSTRAINT [FK_LmsPathSteps_LmsPaths] FOREIGN KEY ([LmsPathId]) REFERENCES [dbo].[LmsPaths] ([LmsPathId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsPathSteps_Path' AND object_id = OBJECT_ID('dbo.LmsPathSteps'))
	CREATE NONCLUSTERED INDEX [IX_LmsPathSteps_Path] ON [dbo].[LmsPathSteps] ([LmsPathId], [SortOrder]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsPathSteps_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsPathSteps'))
	CREATE NONCLUSTERED INDEX [IX_LmsPathSteps_DepartmentId] ON [dbo].[LmsPathSteps] ([DepartmentId]);");

			Execute.Sql(@"
IF COL_LENGTH('dbo.LmsPathAssignments', 'LmsPathId') IS NULL
	ALTER TABLE [dbo].[LmsPathAssignments] ADD [LmsPathId] int NULL;");

			Execute.Sql(@"
IF COL_LENGTH('dbo.LmsPathAssignments', 'CompletedOn') IS NULL
	ALTER TABLE [dbo].[LmsPathAssignments] ADD [CompletedOn] datetime2 NULL;");

			Execute.Sql(@"
IF COL_LENGTH('dbo.LmsPathAssignments', 'PersonnelCertificationId') IS NULL
	ALTER TABLE [dbo].[LmsPathAssignments] ADD [PersonnelCertificationId] int NULL;");

			Execute.Sql(@"
IF COL_LENGTH('dbo.LmsPathAssignments', 'LmsRequirementId') IS NULL
	ALTER TABLE [dbo].[LmsPathAssignments] ADD [LmsRequirementId] int NULL;");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsPathAssignments_Path' AND object_id = OBJECT_ID('dbo.LmsPathAssignments'))
	CREATE NONCLUSTERED INDEX [IX_LmsPathAssignments_Path] ON [dbo].[LmsPathAssignments] ([LmsPathId]) INCLUDE ([DepartmentId], [UserId], [CompletedOn], [RemovedOn]);");
		}

		public override void Down()
		{
			Execute.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsPathAssignments_Path' AND object_id = OBJECT_ID('dbo.LmsPathAssignments'))
	DROP INDEX [IX_LmsPathAssignments_Path] ON [dbo].[LmsPathAssignments];");
			Execute.Sql("IF COL_LENGTH('dbo.LmsPathAssignments', 'LmsRequirementId') IS NOT NULL ALTER TABLE [dbo].[LmsPathAssignments] DROP COLUMN [LmsRequirementId];");
			Execute.Sql("IF COL_LENGTH('dbo.LmsPathAssignments', 'PersonnelCertificationId') IS NOT NULL ALTER TABLE [dbo].[LmsPathAssignments] DROP COLUMN [PersonnelCertificationId];");
			Execute.Sql("IF COL_LENGTH('dbo.LmsPathAssignments', 'CompletedOn') IS NOT NULL ALTER TABLE [dbo].[LmsPathAssignments] DROP COLUMN [CompletedOn];");
			Execute.Sql("IF COL_LENGTH('dbo.LmsPathAssignments', 'LmsPathId') IS NOT NULL ALTER TABLE [dbo].[LmsPathAssignments] DROP COLUMN [LmsPathId];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsPathSteps', 'U') IS NOT NULL DROP TABLE [dbo].[LmsPathSteps];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsPaths', 'U') IS NOT NULL DROP TABLE [dbo].[LmsPaths];");
		}
	}
}
