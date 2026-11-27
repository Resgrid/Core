using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0342_AddProtocolLibraryAndVersions: the dispatchprotocols library columns, protocolcategories,
	/// protocolversions, protocolversionapprovals, protocolaudiences, protocolcomments and callprotocols.protocolversionid.
	/// Text columns are citext; content hashes stay exact varchar(64). Guarded for safe retry.
	/// </summary>
	[Migration(342)]
	public class M0342_AddProtocolLibraryAndVersionsPg : Migration
	{
		private static readonly (string Name, string Definition)[] ProtocolColumns =
		{
			("kind", "integer NOT NULL DEFAULT 0"),
			("categoryid", "integer NULL"),
			("number", "citext NULL"),
			("tags", "citext NULL"),
			("owneruserid", "citext NULL"),
			("status", "integer NOT NULL DEFAULT 0"),
			("currentversionid", "integer NULL"),
			("reviewintervaldays", "integer NULL"),
			("nextreviewon", "timestamp without time zone NULL"),
			("lastreviewedon", "timestamp without time zone NULL"),
			("reviewreminderstage", "integer NOT NULL DEFAULT 0"),
			("rowversion", "bigint NOT NULL DEFAULT 0"),
			("excludefromai", "boolean NOT NULL DEFAULT false"),
			("islicensedcontent", "boolean NOT NULL DEFAULT false"),
			("legalhold", "boolean NOT NULL DEFAULT false")
		};

		public override void Up()
		{
			foreach (var (name, definition) in ProtocolColumns)
				Execute.Sql($"ALTER TABLE public.dispatchprotocols ADD COLUMN IF NOT EXISTS {name} {definition};");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatchprotocols_status_nextreviewon ON public.dispatchprotocols (status, nextreviewon);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.protocolcategories (
	protocolcategoryid serial NOT NULL,
	departmentid integer NOT NULL,
	parentcategoryid integer NULL,
	name citext NOT NULL,
	kind integer NULL,
	sortorder integer NOT NULL DEFAULT 0,
	approvalstepsjson text NULL,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	CONSTRAINT pk_protocolcategories PRIMARY KEY (protocolcategoryid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolcategories_departmentid ON public.protocolcategories (departmentid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.protocolversions (
	protocolversionid serial NOT NULL,
	departmentid integer NOT NULL,
	dispatchprotocolid integer NOT NULL,
	major integer NOT NULL DEFAULT 0,
	minor integer NOT NULL DEFAULT 0,
	status integer NOT NULL,
	effectiveon timestamp without time zone NULL,
	expireson timestamp without time zone NULL,
	contentjson text NULL,
	contenthtml text NULL,
	contenthash varchar(64) NULL,
	changesummary citext NULL,
	requiresacknowledgment boolean NOT NULL DEFAULT false,
	locale citext NULL,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	submittedon timestamp without time zone NULL,
	submittedbyuserid citext NULL,
	approvedon timestamp without time zone NULL,
	approvedbyuserid citext NULL,
	publishedon timestamp without time zone NULL,
	publishedbyuserid citext NULL,
	supersededon timestamp without time zone NULL,
	retiredon timestamp without time zone NULL,
	rowversion bigint NOT NULL DEFAULT 0,
	CONSTRAINT pk_protocolversions PRIMARY KEY (protocolversionid),
	CONSTRAINT fk_protocolversions_dispatchprotocols FOREIGN KEY (dispatchprotocolid) REFERENCES public.dispatchprotocols (dispatchprotocolid) ON DELETE NO ACTION
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolversions_protocol_status ON public.protocolversions (dispatchprotocolid, status);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolversions_departmentid ON public.protocolversions (departmentid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolversions_protocol_hash ON public.protocolversions (dispatchprotocolid, contenthash);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolversions_status_effectiveon ON public.protocolversions (status, effectiveon);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolversions_status_expireson ON public.protocolversions (status, expireson);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.protocolversionapprovals (
	protocolversionapprovalid serial NOT NULL,
	departmentid integer NOT NULL,
	protocolversionid integer NOT NULL,
	round integer NOT NULL DEFAULT 1,
	steporder integer NOT NULL,
	approveruserid citext NULL,
	approverroleid integer NULL,
	decision integer NOT NULL DEFAULT 0,
	decidedbyuserid citext NULL,
	comment citext NULL,
	decidedon timestamp without time zone NULL,
	signaturerecordid integer NULL,
	createdon timestamp without time zone NOT NULL,
	CONSTRAINT pk_protocolversionapprovals PRIMARY KEY (protocolversionapprovalid),
	CONSTRAINT fk_protocolversionapprovals_protocolversions FOREIGN KEY (protocolversionid) REFERENCES public.protocolversions (protocolversionid) ON DELETE NO ACTION
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolversionapprovals_protocolversionid ON public.protocolversionapprovals (protocolversionid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolversionapprovals_department_decision ON public.protocolversionapprovals (departmentid, decision);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.protocolaudiences (
	protocolaudienceid serial NOT NULL,
	departmentid integer NOT NULL,
	dispatchprotocolid integer NOT NULL,
	kind integer NOT NULL,
	refid citext NULL,
	CONSTRAINT pk_protocolaudiences PRIMARY KEY (protocolaudienceid),
	CONSTRAINT fk_protocolaudiences_dispatchprotocols FOREIGN KEY (dispatchprotocolid) REFERENCES public.dispatchprotocols (dispatchprotocolid) ON DELETE NO ACTION
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolaudiences_dispatchprotocolid ON public.protocolaudiences (dispatchprotocolid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolaudiences_departmentid ON public.protocolaudiences (departmentid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.protocolcomments (
	protocolcommentid serial NOT NULL,
	departmentid integer NOT NULL,
	dispatchprotocolid integer NOT NULL,
	protocolversionid integer NULL,
	parentcommentid integer NULL,
	kind integer NOT NULL,
	anchor citext NULL,
	body citext NOT NULL,
	authoruserid citext NOT NULL,
	createdon timestamp without time zone NOT NULL,
	resolvedon timestamp without time zone NULL,
	resolvedbyuserid citext NULL,
	CONSTRAINT pk_protocolcomments PRIMARY KEY (protocolcommentid),
	CONSTRAINT fk_protocolcomments_dispatchprotocols FOREIGN KEY (dispatchprotocolid) REFERENCES public.dispatchprotocols (dispatchprotocolid) ON DELETE NO ACTION
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolcomments_dispatchprotocolid ON public.protocolcomments (dispatchprotocolid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolcomments_protocolversionid ON public.protocolcomments (protocolversionid);");

			Execute.Sql("ALTER TABLE public.callprotocols ADD COLUMN IF NOT EXISTS protocolversionid integer NULL;");
			Execute.Sql(@"
DO $$
BEGIN
	IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_callprotocols_protocolversions' AND conrelid = 'public.callprotocols'::regclass) THEN
		ALTER TABLE public.callprotocols ADD CONSTRAINT fk_callprotocols_protocolversions
			FOREIGN KEY (protocolversionid) REFERENCES public.protocolversions (protocolversionid) ON DELETE NO ACTION;
	END IF;
END $$;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_callprotocols_protocolversionid ON public.callprotocols (protocolversionid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS public.ix_callprotocols_protocolversionid;");
			Execute.Sql("ALTER TABLE public.callprotocols DROP CONSTRAINT IF EXISTS fk_callprotocols_protocolversions;");
			Execute.Sql("ALTER TABLE public.callprotocols DROP COLUMN IF EXISTS protocolversionid;");

			Execute.Sql("DROP TABLE IF EXISTS public.protocolcomments;");
			Execute.Sql("DROP TABLE IF EXISTS public.protocolaudiences;");
			Execute.Sql("DROP TABLE IF EXISTS public.protocolversionapprovals;");
			Execute.Sql("DROP TABLE IF EXISTS public.protocolversions;");
			Execute.Sql("DROP TABLE IF EXISTS public.protocolcategories;");

			Execute.Sql("DROP INDEX IF EXISTS public.ix_dispatchprotocols_status_nextreviewon;");
			for (var i = ProtocolColumns.Length - 1; i >= 0; i--)
				Execute.Sql($"ALTER TABLE public.dispatchprotocols DROP COLUMN IF EXISTS {ProtocolColumns[i].Name};");
		}
	}
}
