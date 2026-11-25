using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// e-Signature S2: ESignProviderAccounts (one Documenso team per department; the team-scoped API token encrypted with the
	/// department key; unique per department and provider) and ESignWebhookEvents (webhook hints, idempotent by the provider's
	/// event id — the unique key is the claim's arbiter, the inbound-email-receipts pattern; only the envelope id is kept; no
	/// DepartmentId, purged by worker 90 after seven days). Guarded for safe retry.
	/// </summary>
	[Migration(374)]
	public class M0374_AddESignAccountsAndWebhookEvents : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.ESignProviderAccounts', 'U') IS NULL
CREATE TABLE [dbo].[ESignProviderAccounts] (
	[ESignProviderAccountId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ESignProviderAccounts] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[ProviderKind] int NOT NULL,
	[TeamId] nvarchar(128) NULL,
	[ApiTokenEncrypted] nvarchar(2000) NULL,
	[TokenHint] nvarchar(8) NULL,
	[IsActive] bit NOT NULL CONSTRAINT [DF_ESignProviderAccounts_IsActive] DEFAULT 1,
	[CreatedByUserId] nvarchar(128) NULL,
	[CreatedOn] datetime2 NOT NULL,
	[UpdatedByUserId] nvarchar(128) NULL,
	[UpdatedOn] datetime2 NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ESignProviderAccounts_Department' AND object_id = OBJECT_ID('dbo.ESignProviderAccounts')) " +
				"CREATE UNIQUE INDEX [UX_ESignProviderAccounts_Department] ON [dbo].[ESignProviderAccounts] ([DepartmentId], [ProviderKind]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ESignWebhookEvents', 'U') IS NULL
CREATE TABLE [dbo].[ESignWebhookEvents] (
	[ESignWebhookEventId] nvarchar(200) NOT NULL CONSTRAINT [PK_ESignWebhookEvents] PRIMARY KEY,
	[ProviderKind] int NOT NULL,
	[ProviderEventId] nvarchar(160) NOT NULL,
	[EventType] nvarchar(64) NULL,
	[EnvelopeId] nvarchar(128) NULL,
	[State] int NOT NULL,
	[ReceivedOn] datetime2 NOT NULL,
	[ClaimedOn] datetime2 NULL,
	[ProcessedOn] datetime2 NULL,
	[Attempts] int NOT NULL CONSTRAINT [DF_ESignWebhookEvents_Attempts] DEFAULT 0,
	[LastError] nvarchar(1000) NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ESignWebhookEvents_Due' AND object_id = OBJECT_ID('dbo.ESignWebhookEvents')) " +
				"CREATE INDEX [IX_ESignWebhookEvents_Due] ON [dbo].[ESignWebhookEvents] ([State], [ReceivedOn]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.ESignWebhookEvents', 'U') IS NOT NULL DROP TABLE [dbo].[ESignWebhookEvents];");
			Execute.Sql("IF OBJECT_ID('dbo.ESignProviderAccounts', 'U') IS NOT NULL DROP TABLE [dbo].[ESignProviderAccounts];");
		}
	}
}
