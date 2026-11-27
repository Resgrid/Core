using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Protocols P0 (D5, D22, R-P2):
	/// <list type="bullet">
	/// <item>DispatchProtocolSnapshots: a protocol's text as dispatched, stored once per (DispatchProtocolId, ContentHash) with
	/// the time a call first used it, however many calls cite it. CallProtocols.ContentHash (M0336) points at it. Foreign keys
	/// to the protocol and the department, without cascade (department deletion clears it before the protocols).</item>
	/// <item>A protocol is on a call at most once: the duplicates the old New Call form could store (one row as active, one as
	/// pending) are removed first, keeping the earliest row of each pair, then a unique index on (CallId, DispatchProtocolId)
	/// stops new ones. The removed duplicates are not restored by Down.</item>
	/// </list>
	/// Guarded for safe retry.
	/// </summary>
	[Migration(337)]
	public class M0337_ProtocolSnapshotsAndUniqueCallProtocols : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.DispatchProtocolSnapshots', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[DispatchProtocolSnapshots] (
		[DispatchProtocolSnapshotId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[DispatchProtocolId] int NOT NULL,
		[ContentHash] nvarchar(64) NOT NULL,
		[ProtocolText] nvarchar(max) NULL,
		[FirstUsedOn] datetime2 NOT NULL,
		CONSTRAINT [PK_DispatchProtocolSnapshots] PRIMARY KEY CLUSTERED ([DispatchProtocolSnapshotId] ASC),
		CONSTRAINT [FK_DispatchProtocolSnapshots_DispatchProtocols] FOREIGN KEY ([DispatchProtocolId]) REFERENCES [dbo].[DispatchProtocols] ([DispatchProtocolId]),
		CONSTRAINT [FK_DispatchProtocolSnapshots_Departments] FOREIGN KEY ([DepartmentId]) REFERENCES [dbo].[Departments] ([DepartmentId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DispatchProtocolSnapshots_Protocol_Hash' AND object_id = OBJECT_ID('dbo.DispatchProtocolSnapshots'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_DispatchProtocolSnapshots_Protocol_Hash] ON [dbo].[DispatchProtocolSnapshots] ([DispatchProtocolId], [ContentHash]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatchProtocolSnapshots_DepartmentId' AND object_id = OBJECT_ID('dbo.DispatchProtocolSnapshots'))
	CREATE NONCLUSTERED INDEX [IX_DispatchProtocolSnapshots_DepartmentId] ON [dbo].[DispatchProtocolSnapshots] ([DepartmentId]);");

			Execute.Sql(@"
;WITH ranked AS (
	SELECT [CallProtocolId], ROW_NUMBER() OVER (PARTITION BY [CallId], [DispatchProtocolId] ORDER BY [CallProtocolId]) AS rn
		FROM [dbo].[CallProtocols]
)
DELETE FROM ranked WHERE rn > 1;");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_CallProtocols_CallId_DispatchProtocolId' AND object_id = OBJECT_ID('dbo.CallProtocols'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_CallProtocols_CallId_DispatchProtocolId] ON [dbo].[CallProtocols] ([CallId], [DispatchProtocolId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_CallProtocols_CallId_DispatchProtocolId' AND object_id = OBJECT_ID('dbo.CallProtocols')) DROP INDEX [UX_CallProtocols_CallId_DispatchProtocolId] ON [dbo].[CallProtocols];");
			Execute.Sql("IF OBJECT_ID('dbo.DispatchProtocolSnapshots', 'U') IS NOT NULL DROP TABLE [dbo].[DispatchProtocolSnapshots];");
		}
	}
}
