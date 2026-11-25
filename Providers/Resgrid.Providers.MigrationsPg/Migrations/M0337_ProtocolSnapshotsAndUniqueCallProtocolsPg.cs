using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0337: dispatchprotocolsnapshots (a protocol's text once per protocol and content hash, with the
	/// time a call first used it), then callprotocols duplicates removed (the earliest row of each call and protocol kept)
	/// and a unique index on (callid, dispatchprotocolid). The removed duplicates are not restored by Down. Guarded for safe retry.
	/// </summary>
	[Migration(337)]
	public class M0337_ProtocolSnapshotsAndUniqueCallProtocolsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.dispatchprotocolsnapshots (
	dispatchprotocolsnapshotid serial NOT NULL,
	departmentid integer NOT NULL,
	dispatchprotocolid integer NOT NULL,
	contenthash varchar(64) NOT NULL,
	protocoltext text NULL,
	firstusedon timestamp without time zone NOT NULL,
	CONSTRAINT pk_dispatchprotocolsnapshots PRIMARY KEY (dispatchprotocolsnapshotid),
	CONSTRAINT fk_dispatchprotocolsnapshots_dispatchprotocols FOREIGN KEY (dispatchprotocolid) REFERENCES public.dispatchprotocols (dispatchprotocolid) ON DELETE NO ACTION,
	CONSTRAINT fk_dispatchprotocolsnapshots_departments FOREIGN KEY (departmentid) REFERENCES public.departments (departmentid) ON DELETE NO ACTION
);");

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_dispatchprotocolsnapshots_protocol_hash ON public.dispatchprotocolsnapshots (dispatchprotocolid, contenthash);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_dispatchprotocolsnapshots_departmentid ON public.dispatchprotocolsnapshots (departmentid);");

			Execute.Sql(@"
DELETE FROM public.callprotocols a
	USING public.callprotocols b
	WHERE a.callid = b.callid
		AND a.dispatchprotocolid = b.dispatchprotocolid
		AND a.callprotocolid > b.callprotocolid;");

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_callprotocols_callid_dispatchprotocolid ON public.callprotocols (callid, dispatchprotocolid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS public.ux_callprotocols_callid_dispatchprotocolid;");
			Execute.Sql("DROP TABLE IF EXISTS public.dispatchprotocolsnapshots;");
		}
	}
}
