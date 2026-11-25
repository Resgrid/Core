using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Protocols P3 offline: DispatchProtocolAttachments.Sha256, the lower-case hex SHA-256 of each attachment's bytes, so the v4 offline
	/// manifest can list files by hash without loading them. Existing rows are hashed here by the database (HASHBYTES SHA2_256 takes
	/// varbinary(max) from SQL Server 2016); a row written later without one is hashed on the next manifest read. Guarded for safe retry.
	/// </summary>
	[Migration(381)]
	public class M0381_AddProtocolAttachmentSha256 : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('dbo.DispatchProtocolAttachments', 'Sha256') IS NULL ALTER TABLE [dbo].[DispatchProtocolAttachments] ADD [Sha256] varchar(64) NULL;");
			// A separate batch: the column must exist when this statement is compiled.
			Execute.Sql(@"
EXEC sp_executesql N'UPDATE [dbo].[DispatchProtocolAttachments] SET [Sha256] = LOWER(CONVERT(varchar(64), HASHBYTES(''SHA2_256'', [Data]), 2))
WHERE [Sha256] IS NULL AND [Data] IS NOT NULL';");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('dbo.DispatchProtocolAttachments', 'Sha256') IS NOT NULL ALTER TABLE [dbo].[DispatchProtocolAttachments] DROP COLUMN [Sha256];");
		}
	}
}
