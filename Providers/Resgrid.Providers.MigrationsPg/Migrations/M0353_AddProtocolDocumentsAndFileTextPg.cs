using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0353_AddProtocolDocumentsAndFileText: protocolversions.contentkind, protocolversionfiles,
	/// protocolfiletexts, protocolfiletextpages and documents.movedtoprotocolid / movedon. Text columns are citext; the SHA-256
	/// stays exact varchar(64); extracted page text is text. Guarded for safe retry.
	/// </summary>
	[Migration(353)]
	public class M0353_AddProtocolDocumentsAndFileTextPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE public.protocolversions ADD COLUMN IF NOT EXISTS contentkind integer NOT NULL DEFAULT 0;");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.protocolversionfiles (
	protocolversionfileid serial NOT NULL,
	departmentid integer NOT NULL,
	dispatchprotocolid integer NOT NULL,
	protocolversionid integer NOT NULL,
	filename citext NOT NULL,
	contenttype citext NOT NULL,
	size bigint NOT NULL,
	sha256 varchar(64) NOT NULL,
	data bytea NOT NULL,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	CONSTRAINT pk_protocolversionfiles PRIMARY KEY (protocolversionfileid),
	CONSTRAINT fk_protocolversionfiles_protocolversions FOREIGN KEY (protocolversionid) REFERENCES public.protocolversions (protocolversionid) ON DELETE NO ACTION
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_protocolversionfiles_protocolversionid ON public.protocolversionfiles (protocolversionid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolversionfiles_departmentid ON public.protocolversionfiles (departmentid, dispatchprotocolid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.protocolfiletexts (
	protocolfiletextid serial NOT NULL,
	departmentid integer NOT NULL,
	dispatchprotocolid integer NOT NULL,
	sourcekind integer NOT NULL,
	sourceid integer NOT NULL,
	protocolversionid integer NULL,
	status integer NOT NULL,
	reason citext NULL,
	pagecount integer NOT NULL DEFAULT 0,
	charcount integer NOT NULL DEFAULT 0,
	attempts integer NOT NULL DEFAULT 0,
	queuedon timestamp without time zone NOT NULL,
	extractedon timestamp without time zone NULL,
	CONSTRAINT pk_protocolfiletexts PRIMARY KEY (protocolfiletextid),
	CONSTRAINT fk_protocolfiletexts_dispatchprotocols FOREIGN KEY (dispatchprotocolid) REFERENCES public.dispatchprotocols (dispatchprotocolid) ON DELETE NO ACTION
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_protocolfiletexts_source ON public.protocolfiletexts (sourcekind, sourceid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolfiletexts_protocol ON public.protocolfiletexts (departmentid, dispatchprotocolid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolfiletexts_status_queuedon ON public.protocolfiletexts (status, queuedon);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.protocolfiletextpages (
	protocolfiletextpageid serial NOT NULL,
	protocolfiletextid integer NOT NULL,
	departmentid integer NOT NULL,
	pagenumber integer NOT NULL,
	text text NULL,
	CONSTRAINT pk_protocolfiletextpages PRIMARY KEY (protocolfiletextpageid),
	CONSTRAINT fk_protocolfiletextpages_protocolfiletexts FOREIGN KEY (protocolfiletextid) REFERENCES public.protocolfiletexts (protocolfiletextid) ON DELETE NO ACTION
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolfiletextpages_text_page ON public.protocolfiletextpages (protocolfiletextid, pagenumber);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolfiletextpages_departmentid ON public.protocolfiletextpages (departmentid);");

			// Guarded on the table too: a schema without documents (a module test fixture) still migrates.
			Execute.Sql("ALTER TABLE IF EXISTS public.documents ADD COLUMN IF NOT EXISTS movedtoprotocolid integer NULL;");
			Execute.Sql("ALTER TABLE IF EXISTS public.documents ADD COLUMN IF NOT EXISTS movedon timestamp without time zone NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.documents DROP COLUMN IF EXISTS movedon;");
			Execute.Sql("ALTER TABLE IF EXISTS public.documents DROP COLUMN IF EXISTS movedtoprotocolid;");

			Execute.Sql("DROP TABLE IF EXISTS public.protocolfiletextpages;");
			Execute.Sql("DROP TABLE IF EXISTS public.protocolfiletexts;");
			Execute.Sql("DROP TABLE IF EXISTS public.protocolversionfiles;");

			Execute.Sql("ALTER TABLE public.protocolversions DROP COLUMN IF EXISTS contentkind;");
		}
	}
}
