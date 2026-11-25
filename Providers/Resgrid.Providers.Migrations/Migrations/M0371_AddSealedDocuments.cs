using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// e-Signature S1 (L3 sealing, plan §21.3): SealedDocuments (one row per document Resgrid sealed or stored as an e-sign
	/// provider delivered it: subject, revision hash, role, status Pending/Sealed/Failed/StoredAsIs, PAdES level, store key,
	/// SHA-256 of the stored bytes, signing certificate thumbprint, timestamp evidence, retry and re-timestamp schedule) and
	/// SealedDocumentInputs (the HTML or PDF a pending seal waits with; deleted once sealed). No foreign keys:
	/// UnlinkedDepartmentCleanup removes the department's rows. Guarded for safe retry.
	/// </summary>
	[Migration(371)]
	public class M0371_AddSealedDocuments : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.SealedDocuments', 'U') IS NULL
CREATE TABLE [dbo].[SealedDocuments] (
	[SealedDocumentId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SealedDocuments] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[SubjectKind] int NOT NULL,
	[SubjectId] nvarchar(128) NOT NULL,
	[RevisionId] nvarchar(128) NULL,
	[RevisionSha256] varchar(64) NOT NULL,
	[Role] int NOT NULL CONSTRAINT [DF_SealedDocuments_Role] DEFAULT 0,
	[SignatureRequestId] int NULL,
	[Title] nvarchar(256) NULL,
	[FileName] nvarchar(256) NULL,
	[Status] int NOT NULL,
	[PadesLevel] int NOT NULL CONSTRAINT [DF_SealedDocuments_PadesLevel] DEFAULT 0,
	[RetentionYears] int NULL,
	[AuditPageIncluded] bit NOT NULL CONSTRAINT [DF_SealedDocuments_AuditPageIncluded] DEFAULT 0,
	[StorageKey] nvarchar(512) NULL,
	[Sha256] varchar(64) NULL,
	[SizeBytes] bigint NULL,
	[SigningCertThumbprint] varchar(128) NULL,
	[TimestampToken] nvarchar(1000) NULL,
	[TimestampedOn] datetime2 NULL,
	[Attempts] int NOT NULL CONSTRAINT [DF_SealedDocuments_Attempts] DEFAULT 0,
	[NextAttemptOn] datetime2 NULL,
	[ClaimedOn] datetime2 NULL,
	[LastError] nvarchar(1000) NULL,
	[SealedOn] datetime2 NULL,
	[RetimestampDueOn] datetime2 NULL,
	[CreatedByUserId] nvarchar(128) NULL,
	[CreatedOn] datetime2 NOT NULL
);");

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SealedDocuments_Subject' AND object_id = OBJECT_ID('dbo.SealedDocuments')) " +
				"CREATE INDEX [IX_SealedDocuments_Subject] ON [dbo].[SealedDocuments] ([DepartmentId], [SubjectKind], [SubjectId], [Role]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SealedDocuments_Sha256' AND object_id = OBJECT_ID('dbo.SealedDocuments')) " +
				"CREATE INDEX [IX_SealedDocuments_Sha256] ON [dbo].[SealedDocuments] ([Sha256]) WHERE [Sha256] IS NOT NULL;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SealedDocuments_Pending' AND object_id = OBJECT_ID('dbo.SealedDocuments')) " +
				"CREATE INDEX [IX_SealedDocuments_Pending] ON [dbo].[SealedDocuments] ([NextAttemptOn]) WHERE [Status] = 0;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SealedDocuments_Retimestamp' AND object_id = OBJECT_ID('dbo.SealedDocuments')) " +
				"CREATE INDEX [IX_SealedDocuments_Retimestamp] ON [dbo].[SealedDocuments] ([RetimestampDueOn]) WHERE [RetimestampDueOn] IS NOT NULL;");

			Execute.Sql(@"
IF OBJECT_ID('dbo.SealedDocumentInputs', 'U') IS NULL
CREATE TABLE [dbo].[SealedDocumentInputs] (
	[SealedDocumentInputId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SealedDocumentInputs] PRIMARY KEY,
	[SealedDocumentId] int NOT NULL,
	[DepartmentId] int NOT NULL,
	[Html] nvarchar(max) NULL,
	[PdfBase64] nvarchar(max) NULL,
	[PageSize] nvarchar(16) NULL,
	[CreatedOn] datetime2 NOT NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SealedDocumentInputs_Document' AND object_id = OBJECT_ID('dbo.SealedDocumentInputs')) " +
				"CREATE UNIQUE INDEX [UX_SealedDocumentInputs_Document] ON [dbo].[SealedDocumentInputs] ([SealedDocumentId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.SealedDocumentInputs', 'U') IS NOT NULL DROP TABLE [dbo].[SealedDocumentInputs];");
			Execute.Sql("IF OBJECT_ID('dbo.SealedDocuments', 'U') IS NOT NULL DROP TABLE [dbo].[SealedDocuments];");
		}
	}
}
