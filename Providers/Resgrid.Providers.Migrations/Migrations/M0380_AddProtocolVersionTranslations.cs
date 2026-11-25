using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Protocols P3 content translations: ProtocolVersionTranslations, one locale variant per version (unique per version and language),
	/// holding the translated blocks, their rendered HTML and its own SHA-256 (ContentHash, which the translation's L2 approval binds to),
	/// and the source version's hash it was made from (SourceContentHash). The source version's hash and signatures never change.
	/// References ProtocolVersions without cascade; ProtocolDepartmentCleanup and the retention purge remove it before the version.
	/// Guarded for safe retry.
	/// </summary>
	[Migration(380)]
	public class M0380_AddProtocolVersionTranslations : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.ProtocolVersionTranslations', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[ProtocolVersionTranslations] (
		[ProtocolVersionTranslationId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[DispatchProtocolId] int NOT NULL,
		[ProtocolVersionId] int NOT NULL,
		[Locale] nvarchar(16) NOT NULL,
		[Status] int NOT NULL,
		[ContentJson] nvarchar(max) NULL,
		[ContentHtml] nvarchar(max) NULL,
		[ContentHash] nvarchar(64) NULL,
		[SourceContentHash] nvarchar(64) NULL,
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		[ApprovedOn] datetime2 NULL,
		[ApprovedByUserId] nvarchar(128) NULL,
		[SignatureRecordId] int NULL,
		[WithdrawnOn] datetime2 NULL,
		[WithdrawnByUserId] nvarchar(128) NULL,
		[RowVersion] bigint NOT NULL CONSTRAINT [DF_ProtocolVersionTranslations_RowVersion] DEFAULT (0),
		CONSTRAINT [PK_ProtocolVersionTranslations] PRIMARY KEY CLUSTERED ([ProtocolVersionTranslationId] ASC),
		CONSTRAINT [FK_ProtocolVersionTranslations_ProtocolVersions] FOREIGN KEY ([ProtocolVersionId]) REFERENCES [dbo].[ProtocolVersions] ([ProtocolVersionId])
	);
	CREATE UNIQUE NONCLUSTERED INDEX [UX_ProtocolVersionTranslations_Version_Locale] ON [dbo].[ProtocolVersionTranslations] ([ProtocolVersionId], [Locale]);
	CREATE NONCLUSTERED INDEX [IX_ProtocolVersionTranslations_Department_Status] ON [dbo].[ProtocolVersionTranslations] ([DepartmentId], [Status]) INCLUDE ([ProtocolVersionId], [Locale]);
END");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.ProtocolVersionTranslations', 'U') IS NOT NULL DROP TABLE [dbo].[ProtocolVersionTranslations];");
		}
	}
}
