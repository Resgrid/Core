using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Protocols P5, public pages and attribution. ProtocolPublicLinks holds the opt-in public URLs (a protocol's version in force, a pinned
	/// version, or a category as a set) with their unguessable token (binary collation: tokens are case-sensitive), embed settings and
	/// revocation. ContentAttributions keeps the source, licence and source version of content that came from the template library, the
	/// community exchange or an attributed file, one row per subject (a protocol version, a training, a skill sheet, a lesson plan). No foreign
	/// keys: department deletion clears both (UnlinkedDepartmentCleanup on both databases). Guarded for safe retry.
	/// </summary>
	[Migration(388)]
	public class M0388_AddProtocolPublicPagesAndAttributions : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.ProtocolPublicLinks', 'U') IS NULL
CREATE TABLE [dbo].[ProtocolPublicLinks] (
	[ProtocolPublicLinkId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ProtocolPublicLinks] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[Kind] int NOT NULL CONSTRAINT [DF_ProtocolPublicLinks_Kind] DEFAULT (0),
	[DispatchProtocolId] int NULL,
	[ProtocolCategoryId] int NULL,
	[ProtocolVersionId] int NULL,
	[Token] varchar(32) COLLATE Latin1_General_BIN2 NOT NULL,
	[EmbedEnabled] bit NOT NULL CONSTRAINT [DF_ProtocolPublicLinks_EmbedEnabled] DEFAULT (0),
	[EmbedOrigins] nvarchar(1000) NULL,
	[CreatedOn] datetime2 NOT NULL,
	[CreatedByUserId] nvarchar(128) NULL,
	[RevokedOn] datetime2 NULL,
	[RevokedByUserId] nvarchar(128) NULL
);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ProtocolPublicLinks_Token' AND object_id = OBJECT_ID('dbo.ProtocolPublicLinks'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_ProtocolPublicLinks_Token] ON [dbo].[ProtocolPublicLinks] ([Token]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProtocolPublicLinks_Department' AND object_id = OBJECT_ID('dbo.ProtocolPublicLinks'))
	CREATE NONCLUSTERED INDEX [IX_ProtocolPublicLinks_Department] ON [dbo].[ProtocolPublicLinks] ([DepartmentId], [RevokedOn]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ContentAttributions', 'U') IS NULL
CREATE TABLE [dbo].[ContentAttributions] (
	[ContentAttributionId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ContentAttributions] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[SubjectKind] int NOT NULL,
	[SubjectId] int NOT NULL,
	[SourceKind] int NOT NULL CONSTRAINT [DF_ContentAttributions_SourceKind] DEFAULT (0),
	[SourceRef] nvarchar(100) NULL,
	[SourceName] nvarchar(200) NULL,
	[Author] nvarchar(200) NULL,
	[License] nvarchar(50) NULL,
	[LicenseUrl] nvarchar(300) NULL,
	[SourceVersion] nvarchar(50) NULL,
	[CreatedOn] datetime2 NOT NULL,
	[CreatedByUserId] nvarchar(128) NULL
);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ContentAttributions_Subject' AND object_id = OBJECT_ID('dbo.ContentAttributions'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_ContentAttributions_Subject] ON [dbo].[ContentAttributions] ([DepartmentId], [SubjectKind], [SubjectId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.ContentAttributions', 'U') IS NOT NULL DROP TABLE [dbo].[ContentAttributions];");
			Execute.Sql("IF OBJECT_ID('dbo.ProtocolPublicLinks', 'U') IS NOT NULL DROP TABLE [dbo].[ProtocolPublicLinks];");
		}
	}
}
