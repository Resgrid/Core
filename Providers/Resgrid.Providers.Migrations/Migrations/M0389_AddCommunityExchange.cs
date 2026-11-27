using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// The community exchange (Protocols P5 / Training T5, decision 7c, free): ExchangeListings (a department's stripped, self-contained copy
	/// of a protocol, training, skill sheet or lesson plan, its licence and attribution name, the scrubber's review flags, the Resgrid review
	/// and the rating and import counters), ExchangeRatings (one per member and listing), ExchangeComments (plain text, hidden never deleted)
	/// and ExchangeImports (what each import created). No foreign keys: a department's own rows and every row on its listings go with the
	/// department (UnlinkedDepartmentCleanup on both databases). Guarded for safe retry.
	/// </summary>
	[Migration(389)]
	public class M0389_AddCommunityExchange : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.ExchangeListings', 'U') IS NULL
CREATE TABLE [dbo].[ExchangeListings] (
	[ExchangeListingId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ExchangeListings] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[ItemKind] int NOT NULL,
	[SourceRef] nvarchar(100) NULL,
	[Title] nvarchar(200) NOT NULL,
	[Summary] nvarchar(2000) NULL,
	[Tags] nvarchar(500) NULL,
	[VersionLabel] nvarchar(50) NULL,
	[PayloadJson] nvarchar(max) NOT NULL,
	[PayloadHash] varchar(64) NOT NULL,
	[License] nvarchar(50) NOT NULL,
	[AttributionName] nvarchar(200) NULL,
	[ReviewFlagsJson] nvarchar(max) NULL,
	[Status] int NOT NULL CONSTRAINT [DF_ExchangeListings_Status] DEFAULT (0),
	[SubmittedOn] datetime2 NOT NULL,
	[SubmittedByUserId] nvarchar(128) NULL,
	[AttestationVersion] nvarchar(20) NULL,
	[ReviewedOn] datetime2 NULL,
	[ReviewedByUserId] nvarchar(128) NULL,
	[ReviewNote] nvarchar(2000) NULL,
	[ListedOn] datetime2 NULL,
	[WithdrawnOn] datetime2 NULL,
	[RatingCount] int NOT NULL CONSTRAINT [DF_ExchangeListings_RatingCount] DEFAULT (0),
	[RatingTotal] int NOT NULL CONSTRAINT [DF_ExchangeListings_RatingTotal] DEFAULT (0),
	[ImportCount] int NOT NULL CONSTRAINT [DF_ExchangeListings_ImportCount] DEFAULT (0)
);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ExchangeListings_Browse' AND object_id = OBJECT_ID('dbo.ExchangeListings'))
	CREATE NONCLUSTERED INDEX [IX_ExchangeListings_Browse] ON [dbo].[ExchangeListings] ([Status], [ItemKind], [ListedOn]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ExchangeListings_Department' AND object_id = OBJECT_ID('dbo.ExchangeListings'))
	CREATE NONCLUSTERED INDEX [IX_ExchangeListings_Department] ON [dbo].[ExchangeListings] ([DepartmentId], [SubmittedOn]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ExchangeRatings', 'U') IS NULL
CREATE TABLE [dbo].[ExchangeRatings] (
	[ExchangeRatingId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ExchangeRatings] PRIMARY KEY,
	[ExchangeListingId] int NOT NULL,
	[DepartmentId] int NOT NULL,
	[UserId] nvarchar(128) NOT NULL,
	[Stars] int NOT NULL,
	[CreatedOn] datetime2 NOT NULL,
	[UpdatedOn] datetime2 NULL
);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ExchangeRatings_ListingUser' AND object_id = OBJECT_ID('dbo.ExchangeRatings'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_ExchangeRatings_ListingUser] ON [dbo].[ExchangeRatings] ([ExchangeListingId], [UserId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ExchangeComments', 'U') IS NULL
CREATE TABLE [dbo].[ExchangeComments] (
	[ExchangeCommentId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ExchangeComments] PRIMARY KEY,
	[ExchangeListingId] int NOT NULL,
	[DepartmentId] int NOT NULL,
	[UserId] nvarchar(128) NULL,
	[AuthorName] nvarchar(200) NULL,
	[Body] nvarchar(2000) NOT NULL,
	[CreatedOn] datetime2 NOT NULL,
	[HiddenOn] datetime2 NULL,
	[HiddenByUserId] nvarchar(128) NULL
);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ExchangeComments_Listing' AND object_id = OBJECT_ID('dbo.ExchangeComments'))
	CREATE NONCLUSTERED INDEX [IX_ExchangeComments_Listing] ON [dbo].[ExchangeComments] ([ExchangeListingId], [CreatedOn]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ExchangeImports', 'U') IS NULL
CREATE TABLE [dbo].[ExchangeImports] (
	[ExchangeImportId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ExchangeImports] PRIMARY KEY,
	[ExchangeListingId] int NOT NULL,
	[DepartmentId] int NOT NULL,
	[UserId] nvarchar(128) NULL,
	[ItemKind] int NOT NULL,
	[CreatedItemId] int NOT NULL,
	[ImportedOn] datetime2 NOT NULL
);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ExchangeImports_Department' AND object_id = OBJECT_ID('dbo.ExchangeImports'))
	CREATE NONCLUSTERED INDEX [IX_ExchangeImports_Department] ON [dbo].[ExchangeImports] ([DepartmentId], [ExchangeListingId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.ExchangeImports', 'U') IS NOT NULL DROP TABLE [dbo].[ExchangeImports];");
			Execute.Sql("IF OBJECT_ID('dbo.ExchangeComments', 'U') IS NOT NULL DROP TABLE [dbo].[ExchangeComments];");
			Execute.Sql("IF OBJECT_ID('dbo.ExchangeRatings', 'U') IS NOT NULL DROP TABLE [dbo].[ExchangeRatings];");
			Execute.Sql("IF OBJECT_ID('dbo.ExchangeListings', 'U') IS NOT NULL DROP TABLE [dbo].[ExchangeListings];");
		}
	}
}
