using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0363_AddProtocolAttachmentSha256: dispatchprotocolattachments.sha256 (exact varchar, a hash), existing rows
	/// hashed by the database (sha256(bytea), PostgreSQL 11+). Guarded for safe retry.
	/// </summary>
	[Migration(363)]
	public class M0363_AddProtocolAttachmentSha256Pg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE public.dispatchprotocolattachments ADD COLUMN IF NOT EXISTS sha256 varchar(64) NULL;");
			Execute.Sql("UPDATE public.dispatchprotocolattachments SET sha256 = encode(sha256(data), 'hex') WHERE sha256 IS NULL AND data IS NOT NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE public.dispatchprotocolattachments DROP COLUMN IF EXISTS sha256;");
		}
	}
}
