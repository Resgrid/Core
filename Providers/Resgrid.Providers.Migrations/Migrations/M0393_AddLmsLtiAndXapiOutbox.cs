using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T5 standards (round 6C): LTI 1.3 tool registrations (LmsLtiTools, client id unique), the department's RS256 platform keys
	/// (LmsLtiPlatformKeys, private half encrypted for the department), AGS access tokens (LmsLtiAccessTokens: hashed, unique per tool and
	/// assertion jti so an assertion cannot be replayed) and the xAPI outbox worker 98 delivers (LmsXapiOutbox). No foreign keys;
	/// LmsStandardsDepartmentCleanup removes the rows. Existence-guarded for safe retry; Down drops the tables.
	/// </summary>
	[Migration(393)]
	public class M0393_AddLmsLtiAndXapiOutbox : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsLtiTools', 'U') IS NULL
CREATE TABLE [dbo].[LmsLtiTools] (
	[LmsLtiToolId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LmsLtiTools] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[Name] nvarchar(200) NULL,
	[ClientId] varchar(64) NOT NULL,
	[DeploymentId] varchar(64) NULL,
	[OidcLoginUrl] nvarchar(2000) NULL,
	[LaunchUrl] nvarchar(2000) NULL,
	[RedirectUris] nvarchar(max) NULL,
	[JwksUrl] nvarchar(2000) NULL,
	[PublicJwksJson] nvarchar(max) NULL,
	[CustomParameters] nvarchar(4000) NULL,
	[AllowGradePassback] bit NOT NULL CONSTRAINT [DF_LmsLtiTools_AllowGradePassback] DEFAULT 0,
	[ShareName] bit NOT NULL CONSTRAINT [DF_LmsLtiTools_ShareName] DEFAULT 0,
	[ShareEmail] bit NOT NULL CONSTRAINT [DF_LmsLtiTools_ShareEmail] DEFAULT 0,
	[IsActive] bit NOT NULL CONSTRAINT [DF_LmsLtiTools_IsActive] DEFAULT 1,
	[IsDeleted] bit NOT NULL CONSTRAINT [DF_LmsLtiTools_IsDeleted] DEFAULT 0,
	[CreatedOn] datetime2 NOT NULL,
	[CreatedByUserId] nvarchar(128) NULL,
	[UpdatedOn] datetime2 NULL,
	[UpdatedByUserId] nvarchar(128) NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsLtiTools_ClientId' AND object_id = OBJECT_ID('dbo.LmsLtiTools')) " +
				"CREATE UNIQUE INDEX [UX_LmsLtiTools_ClientId] ON [dbo].[LmsLtiTools] ([ClientId]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLtiTools_Department' AND object_id = OBJECT_ID('dbo.LmsLtiTools')) " +
				"CREATE INDEX [IX_LmsLtiTools_Department] ON [dbo].[LmsLtiTools] ([DepartmentId], [IsDeleted]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsLtiPlatformKeys', 'U') IS NULL
CREATE TABLE [dbo].[LmsLtiPlatformKeys] (
	[LmsLtiPlatformKeyId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LmsLtiPlatformKeys] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[Kid] varchar(64) NOT NULL,
	[PublicJwkJson] nvarchar(max) NULL,
	[PrivateKeyEncrypted] nvarchar(max) NULL,
	[Status] int NOT NULL CONSTRAINT [DF_LmsLtiPlatformKeys_Status] DEFAULT 0,
	[CreatedOn] datetime2 NOT NULL,
	[CreatedByUserId] nvarchar(128) NULL,
	[RetiredOn] datetime2 NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLtiPlatformKeys_Department' AND object_id = OBJECT_ID('dbo.LmsLtiPlatformKeys')) " +
				"CREATE INDEX [IX_LmsLtiPlatformKeys_Department] ON [dbo].[LmsLtiPlatformKeys] ([DepartmentId], [Status]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsLtiAccessTokens', 'U') IS NULL
CREATE TABLE [dbo].[LmsLtiAccessTokens] (
	[LmsLtiAccessTokenId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LmsLtiAccessTokens] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[LmsLtiToolId] int NOT NULL,
	[TokenHash] varchar(64) NOT NULL,
	[Scopes] nvarchar(500) NULL,
	[AssertionJti] nvarchar(200) NOT NULL,
	[CreatedOn] datetime2 NOT NULL,
	[ExpiresOn] datetime2 NOT NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsLtiAccessTokens_Token' AND object_id = OBJECT_ID('dbo.LmsLtiAccessTokens')) " +
				"CREATE UNIQUE INDEX [UX_LmsLtiAccessTokens_Token] ON [dbo].[LmsLtiAccessTokens] ([TokenHash]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsLtiAccessTokens_Jti' AND object_id = OBJECT_ID('dbo.LmsLtiAccessTokens')) " +
				"CREATE UNIQUE INDEX [UX_LmsLtiAccessTokens_Jti] ON [dbo].[LmsLtiAccessTokens] ([LmsLtiToolId], [AssertionJti]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLtiAccessTokens_Expires' AND object_id = OBJECT_ID('dbo.LmsLtiAccessTokens')) " +
				"CREATE INDEX [IX_LmsLtiAccessTokens_Expires] ON [dbo].[LmsLtiAccessTokens] ([ExpiresOn]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsXapiOutbox', 'U') IS NULL
CREATE TABLE [dbo].[LmsXapiOutbox] (
	[LmsXapiOutboxItemId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LmsXapiOutbox] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[StatementId] varchar(36) NOT NULL,
	[UserId] nvarchar(128) NULL,
	[VerbId] nvarchar(250) NULL,
	[StatementJson] nvarchar(max) NULL,
	[Status] int NOT NULL CONSTRAINT [DF_LmsXapiOutbox_Status] DEFAULT 0,
	[Attempts] int NOT NULL CONSTRAINT [DF_LmsXapiOutbox_Attempts] DEFAULT 0,
	[NextAttemptOn] datetime2 NOT NULL,
	[ClaimedOn] datetime2 NULL,
	[LastError] nvarchar(500) NULL,
	[CreatedOn] datetime2 NOT NULL,
	[DeliveredOn] datetime2 NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsXapiOutbox_Due' AND object_id = OBJECT_ID('dbo.LmsXapiOutbox')) " +
				"CREATE INDEX [IX_LmsXapiOutbox_Due] ON [dbo].[LmsXapiOutbox] ([Status], [NextAttemptOn]) WHERE [Status] IN (0, 1);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsXapiOutbox_Department' AND object_id = OBJECT_ID('dbo.LmsXapiOutbox')) " +
				"CREATE INDEX [IX_LmsXapiOutbox_Department] ON [dbo].[LmsXapiOutbox] ([DepartmentId], [Status]);");
		}

		public override void Down()
		{
			foreach (var table in new[] { "LmsXapiOutbox", "LmsLtiAccessTokens", "LmsLtiPlatformKeys", "LmsLtiTools" })
				Execute.Sql($"IF OBJECT_ID('dbo.{table}', 'U') IS NOT NULL DROP TABLE [dbo].[{table}];");
		}
	}
}
