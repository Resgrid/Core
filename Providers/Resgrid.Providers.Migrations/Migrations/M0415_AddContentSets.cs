using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 10 A3 (Protocols P5 regional publisher/subscriber, Training T5 shared course sets): ContentSets (a publisher's set),
	/// ContentSetItems (its protocols, courses and trainings with their lock mode), ContentSetSubscriptions (a linked department's
	/// subscription over a Department Link; one open subscription per set and department), ContentSetCopies (the subscriber's local copy of
	/// each item and its local changes), ContentSetUpdates (the subscriber's pending-update inbox) and ContentSetProposals (changes proposed
	/// upstream). No foreign keys: ContentSetsDepartmentCleanup removes the rows of either party. Placeholder number (renumbered at merge).
	/// Guarded; Down drops the tables.
	/// </summary>
	[Migration(415)]
	public class M0415_AddContentSets : Migration
	{
		private void Index(string table, string name, string columns, bool unique = false, string where = null) =>
			Execute.Sql($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}' AND object_id = OBJECT_ID('dbo.{table}')) " +
				$"CREATE {(unique ? "UNIQUE " : string.Empty)}INDEX [{name}] ON [dbo].[{table}] ({columns}){(where == null ? string.Empty : " WHERE " + where)};");

		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.ContentSets', 'U') IS NULL
CREATE TABLE [dbo].[ContentSets] (
	[ContentSetId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ContentSets] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[Kind] int NOT NULL,
	[Name] nvarchar(200) NOT NULL,
	[Description] nvarchar(max) NULL,
	[LockedDeferDays] int NOT NULL CONSTRAINT [DF_ContentSets_LockedDeferDays] DEFAULT 30,
	[IsArchived] bit NOT NULL CONSTRAINT [DF_ContentSets_IsArchived] DEFAULT 0,
	[CreatedOn] datetime2 NOT NULL,
	[CreatedByUserId] nvarchar(128) NULL,
	[UpdatedOn] datetime2 NULL,
	[UpdatedByUserId] nvarchar(128) NULL
);");
			Index("ContentSets", "IX_ContentSets_Department", "[DepartmentId], [Kind], [IsArchived]");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ContentSetItems', 'U') IS NULL
CREATE TABLE [dbo].[ContentSetItems] (
	[ContentSetItemId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ContentSetItems] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[ContentSetId] int NOT NULL,
	[ItemKind] int NOT NULL,
	[ItemId] int NOT NULL,
	[LockMode] int NOT NULL,
	[CustomizableBlockKeys] nvarchar(2000) NULL,
	[SortOrder] int NOT NULL CONSTRAINT [DF_ContentSetItems_SortOrder] DEFAULT 0,
	[AddedOn] datetime2 NOT NULL,
	[AddedByUserId] nvarchar(128) NULL,
	[RemovedOn] datetime2 NULL,
	[RemovedByUserId] nvarchar(128) NULL
);");
			Index("ContentSetItems", "IX_ContentSetItems_Set", "[ContentSetId], [RemovedOn]");
			Index("ContentSetItems", "IX_ContentSetItems_Item", "[DepartmentId], [ItemKind], [ItemId]");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ContentSetSubscriptions', 'U') IS NULL
CREATE TABLE [dbo].[ContentSetSubscriptions] (
	[ContentSetSubscriptionId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ContentSetSubscriptions] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[ContentSetId] int NOT NULL,
	[PublisherDepartmentId] int NOT NULL,
	[DepartmentLinkId] int NOT NULL,
	[Status] int NOT NULL,
	[AutoAccept] bit NOT NULL CONSTRAINT [DF_ContentSetSubscriptions_AutoAccept] DEFAULT 0,
	[SubscribedOn] datetime2 NOT NULL,
	[SubscribedByUserId] nvarchar(128) NULL,
	[SuspendedOn] datetime2 NULL,
	[EndedOn] datetime2 NULL,
	[EndedByUserId] nvarchar(128) NULL,
	[EndReason] int NOT NULL CONSTRAINT [DF_ContentSetSubscriptions_EndReason] DEFAULT 0
);");
			Index("ContentSetSubscriptions", "IX_ContentSetSubscriptions_Department", "[DepartmentId], [Status]");
			Index("ContentSetSubscriptions", "IX_ContentSetSubscriptions_Set", "[ContentSetId], [Status]");
			Index("ContentSetSubscriptions", "IX_ContentSetSubscriptions_Publisher", "[PublisherDepartmentId], [Status]");
			// One open (active or suspended) subscription per set and subscriber; two admins subscribing at once cannot both win.
			Index("ContentSetSubscriptions", "UX_ContentSetSubscriptions_Open", "[ContentSetId], [DepartmentId]", true, "[Status] <> 2");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ContentSetCopies', 'U') IS NULL
CREATE TABLE [dbo].[ContentSetCopies] (
	[ContentSetCopyId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ContentSetCopies] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[ContentSetSubscriptionId] int NOT NULL,
	[ContentSetItemId] int NOT NULL,
	[PublisherDepartmentId] int NOT NULL,
	[ItemKind] int NOT NULL,
	[SourceItemId] int NOT NULL,
	[LocalItemId] int NOT NULL,
	[SourceVersionId] int NOT NULL,
	[SourceVersionLabel] nvarchar(50) NULL,
	[LocalVersionId] int NULL,
	[LocalCustomizationJson] nvarchar(max) NULL,
	[CreatedOn] datetime2 NOT NULL,
	[UpdatedOn] datetime2 NULL,
	[DetachedOn] datetime2 NULL
);");
			Index("ContentSetCopies", "IX_ContentSetCopies_Local", "[DepartmentId], [ItemKind], [LocalItemId]");
			Index("ContentSetCopies", "IX_ContentSetCopies_Subscription", "[ContentSetSubscriptionId], [ContentSetItemId]");
			Index("ContentSetCopies", "IX_ContentSetCopies_Publisher", "[PublisherDepartmentId]");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ContentSetUpdates', 'U') IS NULL
CREATE TABLE [dbo].[ContentSetUpdates] (
	[ContentSetUpdateId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ContentSetUpdates] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[ContentSetSubscriptionId] int NOT NULL,
	[ContentSetItemId] int NOT NULL,
	[ContentSetCopyId] int NULL,
	[PublisherDepartmentId] int NOT NULL,
	[UpdateKind] int NOT NULL,
	[ItemKind] int NOT NULL,
	[SourceItemId] int NOT NULL,
	[SourceVersionId] int NOT NULL,
	[SourceVersionLabel] nvarchar(50) NULL,
	[Title] nvarchar(250) NULL,
	[ChangeSummary] nvarchar(max) NULL,
	[SourceEffectiveOn] datetime2 NOT NULL,
	[DeadlineOn] datetime2 NULL,
	[Status] int NOT NULL,
	[ApplyOn] datetime2 NULL,
	[DeferredUntil] datetime2 NULL,
	[DecidedOn] datetime2 NULL,
	[DecidedByUserId] nvarchar(128) NULL,
	[DecidedAutomatically] bit NOT NULL CONSTRAINT [DF_ContentSetUpdates_DecidedAutomatically] DEFAULT 0,
	[AppliedOn] datetime2 NULL,
	[LocalVersionId] int NULL,
	[FailureReason] nvarchar(500) NULL,
	[CreatedOn] datetime2 NOT NULL,
	[ClaimedOn] datetime2 NULL
);");
			Index("ContentSetUpdates", "IX_ContentSetUpdates_Department", "[DepartmentId], [Status]");
			Index("ContentSetUpdates", "IX_ContentSetUpdates_Due", "[Status], [ApplyOn]");
			Index("ContentSetUpdates", "IX_ContentSetUpdates_Subscription", "[ContentSetSubscriptionId], [ContentSetItemId]");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ContentSetProposals', 'U') IS NULL
CREATE TABLE [dbo].[ContentSetProposals] (
	[ContentSetProposalId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ContentSetProposals] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[PublisherDepartmentId] int NOT NULL,
	[ContentSetId] int NOT NULL,
	[ContentSetItemId] int NOT NULL,
	[ItemKind] int NOT NULL,
	[SourceItemId] int NOT NULL,
	[SourceVersionId] int NULL,
	[Anchor] nvarchar(128) NULL,
	[Body] nvarchar(max) NOT NULL,
	[AuthorUserId] nvarchar(128) NULL,
	[AuthorName] nvarchar(200) NULL,
	[SubscriberName] nvarchar(200) NULL,
	[Status] int NOT NULL,
	[ResponseBody] nvarchar(max) NULL,
	[RespondedByUserId] nvarchar(128) NULL,
	[RespondedOn] datetime2 NULL,
	[PublisherCommentId] int NULL,
	[CreatedOn] datetime2 NOT NULL
);");
			Index("ContentSetProposals", "IX_ContentSetProposals_Publisher", "[PublisherDepartmentId], [Status]");
			Index("ContentSetProposals", "IX_ContentSetProposals_Department", "[DepartmentId], [Status]");
		}

		public override void Down()
		{
			foreach (var table in new[] { "ContentSetProposals", "ContentSetUpdates", "ContentSetCopies", "ContentSetSubscriptions", "ContentSetItems", "ContentSets" })
				Execute.Sql($"IF OBJECT_ID('dbo.{table}', 'U') IS NOT NULL DROP TABLE [dbo].[{table}];");
		}
	}
}
