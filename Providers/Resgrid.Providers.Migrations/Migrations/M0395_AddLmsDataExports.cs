using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T5 data exit (R-C12, round 6D): LmsDataExports, the department export jobs worker 99 builds into the LMS store (status, scope,
	/// expiry, object key, size, hash, counts, download audit fields), and LmsDataExportBlobs, the zip of an export on an install without the
	/// LMS store (bytes kept off the job row so lists never read them). The queue index covers the due-work query; the expiry index covers
	/// worker 99's purge. No foreign keys (§17.3); LmsDataExitDepartmentCleanup removes the rows. Existence-guarded for safe retry.
	/// </summary>
	[Migration(395)]
	public class M0395_AddLmsDataExports : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsDataExports', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsDataExports] (
		[LmsDataExportId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[Scope] int NOT NULL CONSTRAINT [DF_LmsDataExports_Scope] DEFAULT (0),
		[IncludeMedia] bit NOT NULL CONSTRAINT [DF_LmsDataExports_IncludeMedia] DEFAULT (0),
		[Status] int NOT NULL CONSTRAINT [DF_LmsDataExports_Status] DEFAULT (0),
		[RequestedByUserId] nvarchar(128) NOT NULL,
		[RequestedOn] datetime2 NOT NULL,
		[ClaimedOn] datetime2 NULL,
		[Attempts] int NOT NULL CONSTRAINT [DF_LmsDataExports_Attempts] DEFAULT (0),
		[CompletedOn] datetime2 NULL,
		[ExpiresOn] datetime2 NULL,
		[ObjectKey] nvarchar(500) NULL,
		[StoredInDatabase] bit NOT NULL CONSTRAINT [DF_LmsDataExports_StoredInDatabase] DEFAULT (0),
		[FileName] nvarchar(250) NULL,
		[SizeBytes] bigint NOT NULL CONSTRAINT [DF_LmsDataExports_SizeBytes] DEFAULT (0),
		[Sha256] nvarchar(64) NULL,
		[CountsJson] nvarchar(max) NULL,
		[Error] nvarchar(1000) NULL,
		[DownloadCount] int NOT NULL CONSTRAINT [DF_LmsDataExports_DownloadCount] DEFAULT (0),
		[LastDownloadedOn] datetime2 NULL,
		[PurgedOn] datetime2 NULL,
		CONSTRAINT [PK_LmsDataExports] PRIMARY KEY CLUSTERED ([LmsDataExportId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsDataExports_Department' AND object_id = OBJECT_ID('dbo.LmsDataExports'))
	CREATE NONCLUSTERED INDEX [IX_LmsDataExports_Department] ON [dbo].[LmsDataExports] ([DepartmentId], [RequestedOn]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsDataExports_Queue' AND object_id = OBJECT_ID('dbo.LmsDataExports'))
	CREATE NONCLUSTERED INDEX [IX_LmsDataExports_Queue] ON [dbo].[LmsDataExports] ([Status], [RequestedOn]) WHERE [Status] IN (0, 1);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsDataExports_Expiry' AND object_id = OBJECT_ID('dbo.LmsDataExports'))
	CREATE NONCLUSTERED INDEX [IX_LmsDataExports_Expiry] ON [dbo].[LmsDataExports] ([ExpiresOn]) WHERE [Status] = 2 AND [PurgedOn] IS NULL;");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsDataExportBlobs', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsDataExportBlobs] (
		[LmsDataExportBlobId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsDataExportId] int NOT NULL,
		[Data] varbinary(max) NULL,
		[CreatedOn] datetime2 NOT NULL,
		CONSTRAINT [PK_LmsDataExportBlobs] PRIMARY KEY CLUSTERED ([LmsDataExportBlobId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsDataExportBlobs_Export' AND object_id = OBJECT_ID('dbo.LmsDataExportBlobs'))
	CREATE NONCLUSTERED INDEX [IX_LmsDataExportBlobs_Export] ON [dbo].[LmsDataExportBlobs] ([DepartmentId], [LmsDataExportId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsDataExportBlobs', 'U') IS NOT NULL DROP TABLE [dbo].[LmsDataExportBlobs];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsDataExports', 'U') IS NOT NULL DROP TABLE [dbo].[LmsDataExports];");
		}
	}
}
