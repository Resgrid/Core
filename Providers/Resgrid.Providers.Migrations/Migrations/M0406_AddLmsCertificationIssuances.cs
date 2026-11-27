using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 9A (LMS add-on): the certification issuance ledger. LmsCertificationIssuances: one row per automatic issue or renewal (an LMS
	/// course or path completion, a completed requirement set, a cycle's renewal hours) with the record's state before, the state it set,
	/// the learning records behind it and, when a void took it back, who, when and why. A void of a basis record re-evaluates the row. No
	/// foreign keys (§17.3). Existence-guarded for safe retry; Down removes exactly what Up added.
	/// </summary>
	[Migration(406)]
	public class M0406_AddLmsCertificationIssuances : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCertificationIssuances', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCertificationIssuances] (
		[LmsCertificationIssuanceId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[PersonnelCertificationId] int NOT NULL,
		[DepartmentCertificationTypeId] int NOT NULL,
		[Source] int NOT NULL,
		[SourceRefId] nvarchar(128) NULL,
		[Action] int NOT NULL,
		[PreviousStatus] int NULL,
		[PreviousExpiresOn] datetime2 NULL,
		[PreviousRecievedOn] datetime2 NULL,
		[NewStatus] int NOT NULL,
		[NewExpiresOn] datetime2 NULL,
		[BasisRecordIds] nvarchar(2000) NULL,
		[CompletedOn] datetime2 NOT NULL,
		[IssuedOn] datetime2 NOT NULL,
		[IssuedByUserId] nvarchar(128) NULL,
		[State] int NOT NULL CONSTRAINT [DF_LmsCertificationIssuances_State] DEFAULT (0),
		[RolledBackOn] datetime2 NULL,
		[RolledBackByUserId] nvarchar(128) NULL,
		[RollbackReason] nvarchar(500) NULL,
		CONSTRAINT [PK_LmsCertificationIssuances] PRIMARY KEY CLUSTERED ([LmsCertificationIssuanceId] ASC)
	);
END");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCertificationIssuances_User' AND object_id = OBJECT_ID('dbo.LmsCertificationIssuances'))
	CREATE NONCLUSTERED INDEX [IX_LmsCertificationIssuances_User] ON [dbo].[LmsCertificationIssuances] ([DepartmentId], [UserId], [State]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCertificationIssuances_Certification' AND object_id = OBJECT_ID('dbo.LmsCertificationIssuances'))
	CREATE NONCLUSTERED INDEX [IX_LmsCertificationIssuances_Certification] ON [dbo].[LmsCertificationIssuances] ([DepartmentId], [PersonnelCertificationId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsCertificationIssuances', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCertificationIssuances];");
		}
	}
}
