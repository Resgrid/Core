using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Protocols P5 analytics: ProtocolSearchMisses counts protocol searches that found nothing ("protocol gaps"), one row per department,
	/// surface (library search, Unified Search), UTC day and normalized query, never naming who searched. No foreign keys: department deletion
	/// clears it (UnlinkedDepartmentCleanup on both databases). Guarded for safe retry.
	/// </summary>
	[Migration(390)]
	public class M0390_AddProtocolSearchMisses : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.ProtocolSearchMisses', 'U') IS NULL
CREATE TABLE [dbo].[ProtocolSearchMisses] (
	[ProtocolSearchMissId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ProtocolSearchMisses] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[Surface] int NOT NULL CONSTRAINT [DF_ProtocolSearchMisses_Surface] DEFAULT (0),
	[Query] nvarchar(200) NOT NULL,
	[Day] datetime2 NOT NULL,
	[Count] int NOT NULL CONSTRAINT [DF_ProtocolSearchMisses_Count] DEFAULT (1),
	[LastSeenOn] datetime2 NOT NULL
);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ProtocolSearchMisses_Key' AND object_id = OBJECT_ID('dbo.ProtocolSearchMisses'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_ProtocolSearchMisses_Key] ON [dbo].[ProtocolSearchMisses] ([DepartmentId], [Surface], [Day], [Query]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.ProtocolSearchMisses', 'U') IS NOT NULL DROP TABLE [dbo].[ProtocolSearchMisses];");
		}
	}
}
