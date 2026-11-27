using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Protocols P1, the library and lifecycle (the shared content core, plan §12.1):
	/// <list type="bullet">
	/// <item>DispatchProtocols gains Kind (0 Dispatch for every existing protocol), CategoryId, Number, Tags, OwnerUserId, Status,
	/// CurrentVersionId, the review cycle (ReviewIntervalDays, NextReviewOn, LastReviewedOn, ReviewReminderStage), RowVersion
	/// for safe co-editing, and the ExcludeFromAi, IsLicensedContent and LegalHold flags.</item>
	/// <item>ProtocolCategories (nested, per kind, ordered, optional approval steps), ProtocolVersions (immutable once out of
	/// draft; ContentJson blocks, sanitized ContentHtml and its SHA-256 ContentHash), ProtocolVersionApprovals (steps with their
	/// L2 SignatureRecordId), ProtocolAudiences (the shared audience kinds) and ProtocolComments (anchored review comments and
	/// member suggestions).</item>
	/// <item>CallProtocols.ProtocolVersionId: the version a call cites (M0343 fills it for existing rows).</item>
	/// </list>
	/// Versions, approvals, audiences and comments reference DispatchProtocols without cascade; ProtocolDepartmentCleanup removes
	/// them children first. Guarded for safe retry.
	/// </summary>
	[Migration(342)]
	public class M0342_AddProtocolLibraryAndVersions : Migration
	{
		private static readonly (string Name, string Definition)[] ProtocolColumns =
		{
			("Kind", "int NOT NULL CONSTRAINT [DF_DispatchProtocols_Kind] DEFAULT (0)"),
			("CategoryId", "int NULL"),
			("Number", "nvarchar(32) NULL"),
			("Tags", "nvarchar(500) NULL"),
			("OwnerUserId", "nvarchar(128) NULL"),
			("Status", "int NOT NULL CONSTRAINT [DF_DispatchProtocols_Status] DEFAULT (0)"),
			("CurrentVersionId", "int NULL"),
			("ReviewIntervalDays", "int NULL"),
			("NextReviewOn", "datetime2 NULL"),
			("LastReviewedOn", "datetime2 NULL"),
			("ReviewReminderStage", "int NOT NULL CONSTRAINT [DF_DispatchProtocols_ReviewReminderStage] DEFAULT (0)"),
			("RowVersion", "bigint NOT NULL CONSTRAINT [DF_DispatchProtocols_RowVersion] DEFAULT (0)"),
			("ExcludeFromAi", "bit NOT NULL CONSTRAINT [DF_DispatchProtocols_ExcludeFromAi] DEFAULT (0)"),
			("IsLicensedContent", "bit NOT NULL CONSTRAINT [DF_DispatchProtocols_IsLicensedContent] DEFAULT (0)"),
			("LegalHold", "bit NOT NULL CONSTRAINT [DF_DispatchProtocols_LegalHold] DEFAULT (0)")
		};

		public override void Up()
		{
			foreach (var (name, definition) in ProtocolColumns)
				Execute.Sql($"IF COL_LENGTH('dbo.DispatchProtocols', '{name}') IS NULL ALTER TABLE [dbo].[DispatchProtocols] ADD [{name}] {definition};");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatchProtocols_Status_NextReviewOn' AND object_id = OBJECT_ID('dbo.DispatchProtocols'))
	CREATE NONCLUSTERED INDEX [IX_DispatchProtocols_Status_NextReviewOn] ON [dbo].[DispatchProtocols] ([Status], [NextReviewOn]) INCLUDE ([DepartmentId], [ReviewReminderStage], [IsDeleted]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ProtocolCategories', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[ProtocolCategories] (
		[ProtocolCategoryId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[ParentCategoryId] int NULL,
		[Name] nvarchar(100) NOT NULL,
		[Kind] int NULL,
		[SortOrder] int NOT NULL CONSTRAINT [DF_ProtocolCategories_SortOrder] DEFAULT (0),
		[ApprovalStepsJson] nvarchar(max) NULL,
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_ProtocolCategories] PRIMARY KEY CLUSTERED ([ProtocolCategoryId] ASC)
	);
	CREATE NONCLUSTERED INDEX [IX_ProtocolCategories_DepartmentId] ON [dbo].[ProtocolCategories] ([DepartmentId]);
END");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ProtocolVersions', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[ProtocolVersions] (
		[ProtocolVersionId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[DispatchProtocolId] int NOT NULL,
		[Major] int NOT NULL CONSTRAINT [DF_ProtocolVersions_Major] DEFAULT (0),
		[Minor] int NOT NULL CONSTRAINT [DF_ProtocolVersions_Minor] DEFAULT (0),
		[Status] int NOT NULL,
		[EffectiveOn] datetime2 NULL,
		[ExpiresOn] datetime2 NULL,
		[ContentJson] nvarchar(max) NULL,
		[ContentHtml] nvarchar(max) NULL,
		[ContentHash] nvarchar(64) NULL,
		[ChangeSummary] nvarchar(2000) NULL,
		[RequiresAcknowledgment] bit NOT NULL CONSTRAINT [DF_ProtocolVersions_RequiresAcknowledgment] DEFAULT (0),
		[Locale] nvarchar(16) NULL,
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		[SubmittedOn] datetime2 NULL,
		[SubmittedByUserId] nvarchar(128) NULL,
		[ApprovedOn] datetime2 NULL,
		[ApprovedByUserId] nvarchar(128) NULL,
		[PublishedOn] datetime2 NULL,
		[PublishedByUserId] nvarchar(128) NULL,
		[SupersededOn] datetime2 NULL,
		[RetiredOn] datetime2 NULL,
		[RowVersion] bigint NOT NULL CONSTRAINT [DF_ProtocolVersions_RowVersion] DEFAULT (0),
		CONSTRAINT [PK_ProtocolVersions] PRIMARY KEY CLUSTERED ([ProtocolVersionId] ASC),
		CONSTRAINT [FK_ProtocolVersions_DispatchProtocols] FOREIGN KEY ([DispatchProtocolId]) REFERENCES [dbo].[DispatchProtocols] ([DispatchProtocolId])
	);
	CREATE NONCLUSTERED INDEX [IX_ProtocolVersions_Protocol_Status] ON [dbo].[ProtocolVersions] ([DispatchProtocolId], [Status]);
	CREATE NONCLUSTERED INDEX [IX_ProtocolVersions_DepartmentId] ON [dbo].[ProtocolVersions] ([DepartmentId]);
	CREATE NONCLUSTERED INDEX [IX_ProtocolVersions_Protocol_Hash] ON [dbo].[ProtocolVersions] ([DispatchProtocolId], [ContentHash]);
	CREATE NONCLUSTERED INDEX [IX_ProtocolVersions_Status_EffectiveOn] ON [dbo].[ProtocolVersions] ([Status], [EffectiveOn]);
	CREATE NONCLUSTERED INDEX [IX_ProtocolVersions_Status_ExpiresOn] ON [dbo].[ProtocolVersions] ([Status], [ExpiresOn]);
END");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ProtocolVersionApprovals', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[ProtocolVersionApprovals] (
		[ProtocolVersionApprovalId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[ProtocolVersionId] int NOT NULL,
		[Round] int NOT NULL CONSTRAINT [DF_ProtocolVersionApprovals_Round] DEFAULT (1),
		[StepOrder] int NOT NULL,
		[ApproverUserId] nvarchar(128) NULL,
		[ApproverRoleId] int NULL,
		[Decision] int NOT NULL CONSTRAINT [DF_ProtocolVersionApprovals_Decision] DEFAULT (0),
		[DecidedByUserId] nvarchar(128) NULL,
		[Comment] nvarchar(2000) NULL,
		[DecidedOn] datetime2 NULL,
		[SignatureRecordId] int NULL,
		[CreatedOn] datetime2 NOT NULL,
		CONSTRAINT [PK_ProtocolVersionApprovals] PRIMARY KEY CLUSTERED ([ProtocolVersionApprovalId] ASC),
		CONSTRAINT [FK_ProtocolVersionApprovals_ProtocolVersions] FOREIGN KEY ([ProtocolVersionId]) REFERENCES [dbo].[ProtocolVersions] ([ProtocolVersionId])
	);
	CREATE NONCLUSTERED INDEX [IX_ProtocolVersionApprovals_ProtocolVersionId] ON [dbo].[ProtocolVersionApprovals] ([ProtocolVersionId]);
	CREATE NONCLUSTERED INDEX [IX_ProtocolVersionApprovals_Department_Decision] ON [dbo].[ProtocolVersionApprovals] ([DepartmentId], [Decision]);
END");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ProtocolAudiences', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[ProtocolAudiences] (
		[ProtocolAudienceId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[DispatchProtocolId] int NOT NULL,
		[Kind] int NOT NULL,
		[RefId] nvarchar(128) NULL,
		CONSTRAINT [PK_ProtocolAudiences] PRIMARY KEY CLUSTERED ([ProtocolAudienceId] ASC),
		CONSTRAINT [FK_ProtocolAudiences_DispatchProtocols] FOREIGN KEY ([DispatchProtocolId]) REFERENCES [dbo].[DispatchProtocols] ([DispatchProtocolId])
	);
	CREATE NONCLUSTERED INDEX [IX_ProtocolAudiences_DispatchProtocolId] ON [dbo].[ProtocolAudiences] ([DispatchProtocolId]);
	CREATE NONCLUSTERED INDEX [IX_ProtocolAudiences_DepartmentId] ON [dbo].[ProtocolAudiences] ([DepartmentId]);
END");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ProtocolComments', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[ProtocolComments] (
		[ProtocolCommentId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[DispatchProtocolId] int NOT NULL,
		[ProtocolVersionId] int NULL,
		[ParentCommentId] int NULL,
		[Kind] int NOT NULL,
		[Anchor] nvarchar(128) NULL,
		[Body] nvarchar(4000) NOT NULL,
		[AuthorUserId] nvarchar(128) NOT NULL,
		[CreatedOn] datetime2 NOT NULL,
		[ResolvedOn] datetime2 NULL,
		[ResolvedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_ProtocolComments] PRIMARY KEY CLUSTERED ([ProtocolCommentId] ASC),
		CONSTRAINT [FK_ProtocolComments_DispatchProtocols] FOREIGN KEY ([DispatchProtocolId]) REFERENCES [dbo].[DispatchProtocols] ([DispatchProtocolId])
	);
	CREATE NONCLUSTERED INDEX [IX_ProtocolComments_DispatchProtocolId] ON [dbo].[ProtocolComments] ([DispatchProtocolId]);
	CREATE NONCLUSTERED INDEX [IX_ProtocolComments_ProtocolVersionId] ON [dbo].[ProtocolComments] ([ProtocolVersionId]);
END");

			Execute.Sql("IF COL_LENGTH('dbo.CallProtocols', 'ProtocolVersionId') IS NULL ALTER TABLE [dbo].[CallProtocols] ADD [ProtocolVersionId] int NULL;");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_CallProtocols_ProtocolVersions' AND parent_object_id = OBJECT_ID('dbo.CallProtocols'))
	ALTER TABLE [dbo].[CallProtocols] ADD CONSTRAINT [FK_CallProtocols_ProtocolVersions] FOREIGN KEY ([ProtocolVersionId]) REFERENCES [dbo].[ProtocolVersions] ([ProtocolVersionId]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CallProtocols_ProtocolVersionId' AND object_id = OBJECT_ID('dbo.CallProtocols'))
	CREATE NONCLUSTERED INDEX [IX_CallProtocols_ProtocolVersionId] ON [dbo].[CallProtocols] ([ProtocolVersionId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CallProtocols_ProtocolVersionId' AND object_id = OBJECT_ID('dbo.CallProtocols')) DROP INDEX [IX_CallProtocols_ProtocolVersionId] ON [dbo].[CallProtocols];");
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_CallProtocols_ProtocolVersions' AND parent_object_id = OBJECT_ID('dbo.CallProtocols')) ALTER TABLE [dbo].[CallProtocols] DROP CONSTRAINT [FK_CallProtocols_ProtocolVersions];");
			Execute.Sql("IF COL_LENGTH('dbo.CallProtocols', 'ProtocolVersionId') IS NOT NULL ALTER TABLE [dbo].[CallProtocols] DROP COLUMN [ProtocolVersionId];");

			Execute.Sql("IF OBJECT_ID('dbo.ProtocolComments', 'U') IS NOT NULL DROP TABLE [dbo].[ProtocolComments];");
			Execute.Sql("IF OBJECT_ID('dbo.ProtocolAudiences', 'U') IS NOT NULL DROP TABLE [dbo].[ProtocolAudiences];");
			Execute.Sql("IF OBJECT_ID('dbo.ProtocolVersionApprovals', 'U') IS NOT NULL DROP TABLE [dbo].[ProtocolVersionApprovals];");
			Execute.Sql("IF OBJECT_ID('dbo.ProtocolVersions', 'U') IS NOT NULL DROP TABLE [dbo].[ProtocolVersions];");
			Execute.Sql("IF OBJECT_ID('dbo.ProtocolCategories', 'U') IS NOT NULL DROP TABLE [dbo].[ProtocolCategories];");

			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatchProtocols_Status_NextReviewOn' AND object_id = OBJECT_ID('dbo.DispatchProtocols')) DROP INDEX [IX_DispatchProtocols_Status_NextReviewOn] ON [dbo].[DispatchProtocols];");

			for (var i = ProtocolColumns.Length - 1; i >= 0; i--)
			{
				var name = ProtocolColumns[i].Name;
				Execute.Sql($@"
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_DispatchProtocols_{name}' AND parent_object_id = OBJECT_ID('dbo.DispatchProtocols'))
	ALTER TABLE [dbo].[DispatchProtocols] DROP CONSTRAINT [DF_DispatchProtocols_{name}];
IF COL_LENGTH('dbo.DispatchProtocols', '{name}') IS NOT NULL ALTER TABLE [dbo].[DispatchProtocols] DROP COLUMN [{name}];");
			}
		}
	}
}
