using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// e-Signature S2: SignatureRecords learns external (L4) signers (plan §21.3 "SignerUserId | ExternalSignerName"):
	/// SignerUserId becomes nullable, plus SignatureRequestId, ExternalSignerName, ExternalSignerContact (masked only),
	/// CapturedByUserId and ProviderEvidenceKey. New table SignatureCaptures holds in-person captures (drawn signature, printed
	/// name and witness, encrypted) with the revision hash each was bound to. Guarded for safe retry. Down removes external
	/// signature rows first (the S0 shape cannot hold a signature without a member), then the columns and the table.
	/// </summary>
	[Migration(358)]
	public class M0358_AddExternalSignatureColumns : Migration
	{
		private static readonly (string Column, string Definition)[] Columns =
		{
			("SignatureRequestId", "int NULL"),
			("ExternalSignerName", "nvarchar(200) NULL"),
			("ExternalSignerContact", "nvarchar(128) NULL"),
			("CapturedByUserId", "nvarchar(128) NULL"),
			("ProviderEvidenceKey", "nvarchar(512) NULL")
		};

		public override void Up()
		{
			foreach (var (column, definition) in Columns)
				Execute.Sql($"IF COL_LENGTH('dbo.SignatureRecords', '{column}') IS NULL ALTER TABLE [dbo].[SignatureRecords] ADD [{column}] {definition};");

			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SignatureRecords') AND name = 'SignerUserId' AND is_nullable = 0) " +
				"ALTER TABLE [dbo].[SignatureRecords] ALTER COLUMN [SignerUserId] nvarchar(128) NULL;");

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SignatureRecords_Request' AND object_id = OBJECT_ID('dbo.SignatureRecords')) " +
				"CREATE INDEX [IX_SignatureRecords_Request] ON [dbo].[SignatureRecords] ([DepartmentId], [SignatureRequestId]) WHERE [SignatureRequestId] IS NOT NULL;");

			Execute.Sql(@"
IF OBJECT_ID('dbo.SignatureCaptures', 'U') IS NULL
CREATE TABLE [dbo].[SignatureCaptures] (
	[SignatureCaptureId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SignatureCaptures] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[SignatureRequestId] int NOT NULL,
	[SignatureRequestSignerId] int NOT NULL,
	[SignatureRecordId] int NULL,
	[Method] int NOT NULL,
	[BoundRevisionSha256] varchar(64) NOT NULL,
	[SignatureImageEncrypted] nvarchar(max) NULL,
	[SignatureImageSha256] varchar(64) NULL,
	[PrintedNameEncrypted] nvarchar(1000) NULL,
	[WitnessNameEncrypted] nvarchar(1000) NULL,
	[WitnessUserId] nvarchar(128) NULL,
	[CapturedByUserId] nvarchar(128) NULL,
	[CapturedOn] datetime2 NOT NULL,
	[ReceivedOn] datetime2 NOT NULL,
	[DeviceInfo] nvarchar(256) NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SignatureCaptures_Request' AND object_id = OBJECT_ID('dbo.SignatureCaptures')) " +
				"CREATE INDEX [IX_SignatureCaptures_Request] ON [dbo].[SignatureCaptures] ([DepartmentId], [SignatureRequestId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.SignatureCaptures', 'U') IS NOT NULL DROP TABLE [dbo].[SignatureCaptures];");
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SignatureRecords_Request' AND object_id = OBJECT_ID('dbo.SignatureRecords')) DROP INDEX [IX_SignatureRecords_Request] ON [dbo].[SignatureRecords];");
			Execute.Sql("IF COL_LENGTH('dbo.SignatureRecords', 'SignerUserId') IS NOT NULL DELETE FROM [dbo].[SignatureRecords] WHERE [SignerUserId] IS NULL;");
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SignatureRecords') AND name = 'SignerUserId' AND is_nullable = 1) " +
				"ALTER TABLE [dbo].[SignatureRecords] ALTER COLUMN [SignerUserId] nvarchar(128) NOT NULL;");
			foreach (var (column, _) in Columns)
				Execute.Sql($"IF COL_LENGTH('dbo.SignatureRecords', '{column}') IS NOT NULL ALTER TABLE [dbo].[SignatureRecords] DROP COLUMN [{column}];");
		}
	}
}
