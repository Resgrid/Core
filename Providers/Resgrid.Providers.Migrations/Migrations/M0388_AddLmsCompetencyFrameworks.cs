using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T3 competency frameworks (§13 T3 item 3b, R-C7; LMS add-on): LmsCompetencyFrameworks (applied from Resgrid's catalog of
	/// structure and public identifiers only, imported from CASE 1.1, or custom), LmsCompetencyItems (levels, chapters, JPRs, positions, skills;
	/// parent links) and LmsCompetencyAlignments (an item tagged on a course, training, category or certification type; unique per
	/// subject). No foreign keys (§17.3); UnlinkedDepartmentCleanup removes the rows. Existence-guarded for safe retry.
	/// </summary>
	[Migration(388)]
	public class M0388_AddLmsCompetencyFrameworks : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCompetencyFrameworks', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCompetencyFrameworks] (
		[LmsCompetencyFrameworkId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[CatalogKey] nvarchar(100) NULL,
		[Name] nvarchar(250) NOT NULL,
		[Publisher] nvarchar(250) NULL,
		[Edition] nvarchar(100) NULL,
		[Description] nvarchar(2000) NULL,
		[Source] int NOT NULL CONSTRAINT [DF_LmsCompetencyFrameworks_Source] DEFAULT (0),
		[CaseDocumentId] nvarchar(200) NULL,
		[CaseUri] nvarchar(1000) NULL,
		[EvidenceMaxAgeMonths] int NOT NULL CONSTRAINT [DF_LmsCompetencyFrameworks_EvidenceMaxAgeMonths] DEFAULT (0),
		[IsDeleted] bit NOT NULL CONSTRAINT [DF_LmsCompetencyFrameworks_IsDeleted] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsCompetencyFrameworks] PRIMARY KEY CLUSTERED ([LmsCompetencyFrameworkId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCompetencyFrameworks_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsCompetencyFrameworks'))
	CREATE NONCLUSTERED INDEX [IX_LmsCompetencyFrameworks_DepartmentId] ON [dbo].[LmsCompetencyFrameworks] ([DepartmentId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCompetencyItems', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCompetencyItems] (
		[LmsCompetencyItemId] int IDENTITY(1,1) NOT NULL,
		[LmsCompetencyFrameworkId] int NOT NULL,
		[DepartmentId] int NOT NULL,
		[ParentItemId] int NULL,
		[Identifier] nvarchar(200) NULL,
		[HumanCodingScheme] nvarchar(100) NULL,
		[Label] nvarchar(500) NULL,
		[ItemType] nvarchar(60) NULL,
		[SortOrder] int NOT NULL CONSTRAINT [DF_LmsCompetencyItems_SortOrder] DEFAULT (0),
		[IsDeleted] bit NOT NULL CONSTRAINT [DF_LmsCompetencyItems_IsDeleted] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		CONSTRAINT [PK_LmsCompetencyItems] PRIMARY KEY CLUSTERED ([LmsCompetencyItemId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCompetencyItems_Framework' AND object_id = OBJECT_ID('dbo.LmsCompetencyItems'))
	CREATE NONCLUSTERED INDEX [IX_LmsCompetencyItems_Framework] ON [dbo].[LmsCompetencyItems] ([LmsCompetencyFrameworkId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCompetencyItems_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsCompetencyItems'))
	CREATE NONCLUSTERED INDEX [IX_LmsCompetencyItems_DepartmentId] ON [dbo].[LmsCompetencyItems] ([DepartmentId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCompetencyAlignments', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCompetencyAlignments] (
		[LmsCompetencyAlignmentId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsCompetencyItemId] int NOT NULL,
		[SubjectKind] int NOT NULL,
		[SubjectId] nvarchar(100) NOT NULL,
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsCompetencyAlignments] PRIMARY KEY CLUSTERED ([LmsCompetencyAlignmentId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsCompetencyAlignments_Item_Subject' AND object_id = OBJECT_ID('dbo.LmsCompetencyAlignments'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsCompetencyAlignments_Item_Subject] ON [dbo].[LmsCompetencyAlignments] ([DepartmentId], [LmsCompetencyItemId], [SubjectKind], [SubjectId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCompetencyAlignments_Subject' AND object_id = OBJECT_ID('dbo.LmsCompetencyAlignments'))
	CREATE NONCLUSTERED INDEX [IX_LmsCompetencyAlignments_Subject] ON [dbo].[LmsCompetencyAlignments] ([DepartmentId], [SubjectKind], [SubjectId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsCompetencyAlignments', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCompetencyAlignments];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsCompetencyItems', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCompetencyItems];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsCompetencyFrameworks', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCompetencyFrameworks];");
		}
	}
}
