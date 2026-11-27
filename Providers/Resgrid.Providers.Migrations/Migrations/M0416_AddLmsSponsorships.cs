using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 10 A3 (Training T5 item 2a, R-L3, §25.4a items 8 and 8b): LmsSponsorships, a publisher paying for a linked department's LMS
	/// seats (the sponsored department's LMS-enabled members count in the sponsor's packs; it runs the sponsor's course sets only). One
	/// active sponsorship per sponsor and department. No foreign keys: ContentSetsDepartmentCleanup removes the rows of either party.
	/// Placeholder number (renumbered at merge). Guarded; Down drops the table.
	/// </summary>
	[Migration(416)]
	public class M0416_AddLmsSponsorships : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsSponsorships', 'U') IS NULL
CREATE TABLE [dbo].[LmsSponsorships] (
	[LmsSponsorshipId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LmsSponsorships] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[SponsorDepartmentId] int NOT NULL,
	[DepartmentLinkId] int NOT NULL,
	[Status] int NOT NULL,
	[SeatLimit] int NULL,
	[StartedOn] datetime2 NOT NULL,
	[StartedByUserId] nvarchar(128) NULL,
	[EndedOn] datetime2 NULL,
	[EndedByUserId] nvarchar(128) NULL,
	[EndReason] int NOT NULL CONSTRAINT [DF_LmsSponsorships_EndReason] DEFAULT 0
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSponsorships_Department' AND object_id = OBJECT_ID('dbo.LmsSponsorships')) " +
				"CREATE INDEX [IX_LmsSponsorships_Department] ON [dbo].[LmsSponsorships] ([DepartmentId], [Status]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSponsorships_Sponsor' AND object_id = OBJECT_ID('dbo.LmsSponsorships')) " +
				"CREATE INDEX [IX_LmsSponsorships_Sponsor] ON [dbo].[LmsSponsorships] ([SponsorDepartmentId], [Status]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsSponsorships_Active' AND object_id = OBJECT_ID('dbo.LmsSponsorships')) " +
				"CREATE UNIQUE INDEX [UX_LmsSponsorships_Active] ON [dbo].[LmsSponsorships] ([SponsorDepartmentId], [DepartmentId]) WHERE [Status] = 0;");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsSponsorships', 'U') IS NOT NULL DROP TABLE [dbo].[LmsSponsorships];");
		}
	}
}
