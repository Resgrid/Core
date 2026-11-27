using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0362_AddProtocolVersionTranslations: protocolversiontranslations (unique per version and language; locale and
	/// user ids citext, hashes exact). Guarded for safe retry.
	/// </summary>
	[Migration(362)]
	public class M0362_AddProtocolVersionTranslationsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.protocolversiontranslations (
	protocolversiontranslationid serial NOT NULL,
	departmentid integer NOT NULL,
	dispatchprotocolid integer NOT NULL,
	protocolversionid integer NOT NULL,
	locale citext NOT NULL,
	status integer NOT NULL,
	contentjson text NULL,
	contenthtml text NULL,
	contenthash varchar(64) NULL,
	sourcecontenthash varchar(64) NULL,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	approvedon timestamp without time zone NULL,
	approvedbyuserid citext NULL,
	signaturerecordid integer NULL,
	withdrawnon timestamp without time zone NULL,
	withdrawnbyuserid citext NULL,
	rowversion bigint NOT NULL DEFAULT 0,
	CONSTRAINT pk_protocolversiontranslations PRIMARY KEY (protocolversiontranslationid),
	CONSTRAINT fk_protocolversiontranslations_protocolversions FOREIGN KEY (protocolversionid) REFERENCES public.protocolversions (protocolversionid) ON DELETE NO ACTION
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_protocolversiontranslations_version_locale ON public.protocolversiontranslations (protocolversionid, locale);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolversiontranslations_department_status ON public.protocolversiontranslations (departmentid, status);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.protocolversiontranslations;");
		}
	}
}
