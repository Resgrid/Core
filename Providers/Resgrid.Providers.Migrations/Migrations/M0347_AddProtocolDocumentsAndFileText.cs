using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Protocols P1 wave-2 remainder (R-P5, R-P6), document-backed protocols and file text:
	/// <list type="bullet">
	/// <item>ProtocolVersions.ContentKind (0 Html for every existing version, 1 Document).</item>
	/// <item>ProtocolVersionFiles: one primary PDF or DOCX per document-backed version, with its SHA-256 (the version's ContentHash).</item>
	/// <item>ProtocolFileTexts (one per file: a version's primary file or an attachment, with the extraction status and why there is no
	/// text) and ProtocolFileTextPages (page-numbered plain text, so the knowledge index can chunk by page).</item>
	/// <item>Documents.MovedToProtocolId and MovedOn: "Move to Protocols" leaves the document row as the redirect.</item>
	/// </list>
	/// The new tables reference ProtocolVersions, DispatchProtocols and ProtocolFileTexts without cascade; ProtocolDepartmentCleanup and
	/// the retention purge remove them children first. Guarded for safe retry.
	/// </summary>
	[Migration(347)]
	public class M0347_AddProtocolDocumentsAndFileText : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('dbo.ProtocolVersions', 'ContentKind') IS NULL ALTER TABLE [dbo].[ProtocolVersions] ADD [ContentKind] int NOT NULL CONSTRAINT [DF_ProtocolVersions_ContentKind] DEFAULT (0);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ProtocolVersionFiles', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[ProtocolVersionFiles] (
		[ProtocolVersionFileId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[DispatchProtocolId] int NOT NULL,
		[ProtocolVersionId] int NOT NULL,
		[FileName] nvarchar(256) NOT NULL,
		[ContentType] nvarchar(128) NOT NULL,
		[Size] bigint NOT NULL,
		[Sha256] nvarchar(64) NOT NULL,
		[Data] varbinary(max) NOT NULL,
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_ProtocolVersionFiles] PRIMARY KEY CLUSTERED ([ProtocolVersionFileId] ASC),
		CONSTRAINT [FK_ProtocolVersionFiles_ProtocolVersions] FOREIGN KEY ([ProtocolVersionId]) REFERENCES [dbo].[ProtocolVersions] ([ProtocolVersionId])
	);
	CREATE UNIQUE NONCLUSTERED INDEX [UX_ProtocolVersionFiles_ProtocolVersionId] ON [dbo].[ProtocolVersionFiles] ([ProtocolVersionId]);
	CREATE NONCLUSTERED INDEX [IX_ProtocolVersionFiles_DepartmentId] ON [dbo].[ProtocolVersionFiles] ([DepartmentId], [DispatchProtocolId]);
END");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ProtocolFileTexts', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[ProtocolFileTexts] (
		[ProtocolFileTextId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[DispatchProtocolId] int NOT NULL,
		[SourceKind] int NOT NULL,
		[SourceId] int NOT NULL,
		[ProtocolVersionId] int NULL,
		[Status] int NOT NULL,
		[Reason] nvarchar(64) NULL,
		[PageCount] int NOT NULL CONSTRAINT [DF_ProtocolFileTexts_PageCount] DEFAULT (0),
		[CharCount] int NOT NULL CONSTRAINT [DF_ProtocolFileTexts_CharCount] DEFAULT (0),
		[Attempts] int NOT NULL CONSTRAINT [DF_ProtocolFileTexts_Attempts] DEFAULT (0),
		[QueuedOn] datetime2 NOT NULL,
		[ExtractedOn] datetime2 NULL,
		CONSTRAINT [PK_ProtocolFileTexts] PRIMARY KEY CLUSTERED ([ProtocolFileTextId] ASC),
		CONSTRAINT [FK_ProtocolFileTexts_DispatchProtocols] FOREIGN KEY ([DispatchProtocolId]) REFERENCES [dbo].[DispatchProtocols] ([DispatchProtocolId])
	);
	CREATE UNIQUE NONCLUSTERED INDEX [UX_ProtocolFileTexts_Source] ON [dbo].[ProtocolFileTexts] ([SourceKind], [SourceId]);
	CREATE NONCLUSTERED INDEX [IX_ProtocolFileTexts_Protocol] ON [dbo].[ProtocolFileTexts] ([DepartmentId], [DispatchProtocolId]);
	CREATE NONCLUSTERED INDEX [IX_ProtocolFileTexts_Status_QueuedOn] ON [dbo].[ProtocolFileTexts] ([Status], [QueuedOn]);
END");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ProtocolFileTextPages', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[ProtocolFileTextPages] (
		[ProtocolFileTextPageId] int IDENTITY(1,1) NOT NULL,
		[ProtocolFileTextId] int NOT NULL,
		[DepartmentId] int NOT NULL,
		[PageNumber] int NOT NULL,
		[Text] nvarchar(max) NULL,
		CONSTRAINT [PK_ProtocolFileTextPages] PRIMARY KEY CLUSTERED ([ProtocolFileTextPageId] ASC),
		CONSTRAINT [FK_ProtocolFileTextPages_ProtocolFileTexts] FOREIGN KEY ([ProtocolFileTextId]) REFERENCES [dbo].[ProtocolFileTexts] ([ProtocolFileTextId])
	);
	CREATE NONCLUSTERED INDEX [IX_ProtocolFileTextPages_Text_Page] ON [dbo].[ProtocolFileTextPages] ([ProtocolFileTextId], [PageNumber]);
	CREATE NONCLUSTERED INDEX [IX_ProtocolFileTextPages_DepartmentId] ON [dbo].[ProtocolFileTextPages] ([DepartmentId]);
END");

			// Guarded on the table too: a schema without Documents (a module test fixture) still migrates.
			Execute.Sql("IF OBJECT_ID('dbo.Documents', 'U') IS NOT NULL AND COL_LENGTH('dbo.Documents', 'MovedToProtocolId') IS NULL ALTER TABLE [dbo].[Documents] ADD [MovedToProtocolId] int NULL;");
			Execute.Sql("IF OBJECT_ID('dbo.Documents', 'U') IS NOT NULL AND COL_LENGTH('dbo.Documents', 'MovedOn') IS NULL ALTER TABLE [dbo].[Documents] ADD [MovedOn] datetime2 NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('dbo.Documents', 'MovedOn') IS NOT NULL ALTER TABLE [dbo].[Documents] DROP COLUMN [MovedOn];");
			Execute.Sql("IF COL_LENGTH('dbo.Documents', 'MovedToProtocolId') IS NOT NULL ALTER TABLE [dbo].[Documents] DROP COLUMN [MovedToProtocolId];");

			Execute.Sql("IF OBJECT_ID('dbo.ProtocolFileTextPages', 'U') IS NOT NULL DROP TABLE [dbo].[ProtocolFileTextPages];");
			Execute.Sql("IF OBJECT_ID('dbo.ProtocolFileTexts', 'U') IS NOT NULL DROP TABLE [dbo].[ProtocolFileTexts];");
			Execute.Sql("IF OBJECT_ID('dbo.ProtocolVersionFiles', 'U') IS NOT NULL DROP TABLE [dbo].[ProtocolVersionFiles];");

			Execute.Sql(@"
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_ProtocolVersions_ContentKind' AND parent_object_id = OBJECT_ID('dbo.ProtocolVersions'))
	ALTER TABLE [dbo].[ProtocolVersions] DROP CONSTRAINT [DF_ProtocolVersions_ContentKind];
IF COL_LENGTH('dbo.ProtocolVersions', 'ContentKind') IS NOT NULL ALTER TABLE [dbo].[ProtocolVersions] DROP COLUMN [ContentKind];");
		}
	}
}
