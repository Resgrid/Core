using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// e-Signature S2 (L4 external signers, plan §21.3): SignatureRequests (subject, the revision hash it binds to, channel
	/// remote/in-person, status Draft..Voided, expiry, provider envelope, statement, sealed evidence, reconcile schedule) and
	/// SignatureRequestSigners (order, role, the external signer's name with the e-mail and phone encrypted and a masked copy,
	/// auth method, status, the single-use link token / session / one-time code as SHA-256 hashes, consent version and time).
	/// No foreign keys: UnlinkedDepartmentCleanup removes signers, then requests. Guarded for safe retry.
	/// </summary>
	[Migration(372)]
	public class M0372_AddSignatureRequests : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.SignatureRequests', 'U') IS NULL
CREATE TABLE [dbo].[SignatureRequests] (
	[SignatureRequestId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SignatureRequests] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[SubjectKind] int NOT NULL,
	[SubjectId] nvarchar(128) NOT NULL,
	[RevisionId] nvarchar(128) NULL,
	[Title] nvarchar(256) NULL,
	[ArtifactSha256] varchar(64) NOT NULL,
	[ArtifactStorageKey] nvarchar(512) NULL,
	[RequiredLevel] int NOT NULL,
	[Ordered] bit NOT NULL CONSTRAINT [DF_SignatureRequests_Ordered] DEFAULT 0,
	[Status] int NOT NULL,
	[Channel] int NOT NULL,
	[ExpiresOn] datetime2 NOT NULL,
	[ProviderKind] int NOT NULL CONSTRAINT [DF_SignatureRequests_ProviderKind] DEFAULT 0,
	[ProviderEnvelopeId] nvarchar(128) NULL,
	[StatementVersion] nvarchar(64) NULL,
	[StatementText] nvarchar(2000) NULL,
	[CompletedOn] datetime2 NULL,
	[DeclinedOn] datetime2 NULL,
	[DeclineReason] nvarchar(1000) NULL,
	[ExpiredOn] datetime2 NULL,
	[VoidedOn] datetime2 NULL,
	[VoidedByUserId] nvarchar(128) NULL,
	[VoidReason] nvarchar(1000) NULL,
	[SealedDocumentId] int NULL,
	[ReconcileAfter] datetime2 NULL,
	[LastReconciledOn] datetime2 NULL,
	[ClaimedOn] datetime2 NULL,
	[CreatedByUserId] nvarchar(128) NULL,
	[CreatedOn] datetime2 NOT NULL,
	[UpdatedOn] datetime2 NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SignatureRequests_Subject' AND object_id = OBJECT_ID('dbo.SignatureRequests')) " +
				"CREATE INDEX [IX_SignatureRequests_Subject] ON [dbo].[SignatureRequests] ([DepartmentId], [SubjectKind], [SubjectId]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SignatureRequests_Envelope' AND object_id = OBJECT_ID('dbo.SignatureRequests')) " +
				"CREATE INDEX [IX_SignatureRequests_Envelope] ON [dbo].[SignatureRequests] ([ProviderKind], [ProviderEnvelopeId]) WHERE [ProviderEnvelopeId] IS NOT NULL;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SignatureRequests_Open' AND object_id = OBJECT_ID('dbo.SignatureRequests')) " +
				"CREATE INDEX [IX_SignatureRequests_Open] ON [dbo].[SignatureRequests] ([ExpiresOn], [ReconcileAfter]) WHERE [Status] IN (0, 1, 2);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.SignatureRequestSigners', 'U') IS NULL
CREATE TABLE [dbo].[SignatureRequestSigners] (
	[SignatureRequestSignerId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SignatureRequestSigners] PRIMARY KEY,
	[SignatureRequestId] int NOT NULL,
	[DepartmentId] int NOT NULL,
	[SignerOrder] int NOT NULL CONSTRAINT [DF_SignatureRequestSigners_SignerOrder] DEFAULT 1,
	[Role] int NOT NULL,
	[UserId] nvarchar(128) NULL,
	[ExternalName] nvarchar(200) NULL,
	[ExternalEmailEncrypted] nvarchar(1000) NULL,
	[ExternalPhoneEncrypted] nvarchar(1000) NULL,
	[ContactMasked] nvarchar(128) NULL,
	[AuthMethod] int NOT NULL,
	[Status] int NOT NULL,
	[SignedOn] datetime2 NULL,
	[DeclinedOn] datetime2 NULL,
	[ProviderRecipientId] nvarchar(128) NULL,
	[ProviderSigningTokenEncrypted] nvarchar(1000) NULL,
	[LinkTokenHash] varchar(64) NULL,
	[LinkTokenExpiresOn] datetime2 NULL,
	[LinkTokenUsedOn] datetime2 NULL,
	[LinkSentOn] datetime2 NULL,
	[SessionHash] varchar(64) NULL,
	[SessionExpiresOn] datetime2 NULL,
	[ConsentTextVersion] nvarchar(64) NULL,
	[ConsentedOn] datetime2 NULL,
	[ConsentIpAddress] nvarchar(64) NULL,
	[OtpChannel] int NULL,
	[OtpHash] varchar(64) NULL,
	[OtpExpiresOn] datetime2 NULL,
	[OtpAttempts] int NOT NULL CONSTRAINT [DF_SignatureRequestSigners_OtpAttempts] DEFAULT 0,
	[OtpSentOn] datetime2 NULL,
	[OtpVerifiedOn] datetime2 NULL,
	[SignatureRecordId] int NULL,
	[CreatedOn] datetime2 NOT NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SignatureRequestSigners_Request' AND object_id = OBJECT_ID('dbo.SignatureRequestSigners')) " +
				"CREATE INDEX [IX_SignatureRequestSigners_Request] ON [dbo].[SignatureRequestSigners] ([DepartmentId], [SignatureRequestId]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SignatureRequestSigners_LinkToken' AND object_id = OBJECT_ID('dbo.SignatureRequestSigners')) " +
				"CREATE UNIQUE INDEX [UX_SignatureRequestSigners_LinkToken] ON [dbo].[SignatureRequestSigners] ([LinkTokenHash]) WHERE [LinkTokenHash] IS NOT NULL;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SignatureRequestSigners_Session' AND object_id = OBJECT_ID('dbo.SignatureRequestSigners')) " +
				"CREATE UNIQUE INDEX [UX_SignatureRequestSigners_Session] ON [dbo].[SignatureRequestSigners] ([SessionHash]) WHERE [SessionHash] IS NOT NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.SignatureRequestSigners', 'U') IS NOT NULL DROP TABLE [dbo].[SignatureRequestSigners];");
			Execute.Sql("IF OBJECT_ID('dbo.SignatureRequests', 'U') IS NOT NULL DROP TABLE [dbo].[SignatureRequests];");
		}
	}
}
