using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// e-Signature S4: MutualAidAgreements, the agreement document a department's approval chain signs (and the completed chain
	/// seals). The partner department's countersignature across a Department Link is owed (the sharing helper is not on this branch).
	/// No foreign keys: UnlinkedDepartmentCleanup removes the rows. Guarded; Down drops the table.
	/// </summary>
	[Migration(386)]
	public class M0386_AddMutualAidAgreements : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.MutualAidAgreements', 'U') IS NULL
CREATE TABLE [dbo].[MutualAidAgreements] (
	[MutualAidAgreementId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MutualAidAgreements] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[Title] nvarchar(200) NOT NULL,
	[DepartmentLinkId] int NULL,
	[PartnerDepartmentId] int NULL,
	[PartnerName] nvarchar(200) NULL,
	[Terms] nvarchar(max) NULL,
	[EffectiveOn] datetime2 NULL,
	[ExpiresOn] datetime2 NULL,
	[Status] int NOT NULL,
	[IsDeleted] bit NOT NULL CONSTRAINT [DF_MutualAidAgreements_IsDeleted] DEFAULT 0,
	[CreatedByUserId] nvarchar(128) NULL,
	[CreatedOn] datetime2 NOT NULL,
	[UpdatedByUserId] nvarchar(128) NULL,
	[UpdatedOn] datetime2 NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MutualAidAgreements_Department' AND object_id = OBJECT_ID('dbo.MutualAidAgreements')) " +
				"CREATE INDEX [IX_MutualAidAgreements_Department] ON [dbo].[MutualAidAgreements] ([DepartmentId], [IsDeleted]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.MutualAidAgreements', 'U') IS NOT NULL DROP TABLE [dbo].[MutualAidAgreements];");
		}
	}
}
