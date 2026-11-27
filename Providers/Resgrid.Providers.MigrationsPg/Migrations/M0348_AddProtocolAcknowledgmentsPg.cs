using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0348_AddProtocolAcknowledgments: the dispatchprotocols acknowledgment columns, protocolversions.quiztrainingid
	/// and protocolacknowledgments (unique per version and member; userid citext). Guarded for safe retry.
	/// </summary>
	[Migration(348)]
	public class M0348_AddProtocolAcknowledgmentsPg : Migration
	{
		private static readonly (string Name, string Definition)[] ProtocolColumns =
		{
			("ackduedays", "integer NULL"),
			("ackbundleorder", "integer NOT NULL DEFAULT 0"),
			("acktrainingid", "integer NULL"),
			("standardstags", "citext NULL")
		};

		public override void Up()
		{
			foreach (var (name, definition) in ProtocolColumns)
				Execute.Sql($"ALTER TABLE public.dispatchprotocols ADD COLUMN IF NOT EXISTS {name} {definition};");

			Execute.Sql("ALTER TABLE public.protocolversions ADD COLUMN IF NOT EXISTS quiztrainingid integer NULL;");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.protocolacknowledgments (
	protocolacknowledgmentid serial NOT NULL,
	departmentid integer NOT NULL,
	dispatchprotocolid integer NOT NULL,
	protocolversionid integer NOT NULL,
	userid citext NOT NULL,
	status integer NOT NULL,
	source integer NOT NULL,
	assignedon timestamp without time zone NOT NULL,
	dueon timestamp without time zone NULL,
	enteredaudienceon timestamp without time zone NULL,
	bundleorder integer NOT NULL DEFAULT 0,
	openedon timestamp without time zone NULL,
	signedon timestamp without time zone NULL,
	signaturerecordid integer NULL,
	completedon timestamp without time zone NULL,
	quiztrainingid integer NULL,
	carriedfromacknowledgmentid integer NULL,
	withdrawnon timestamp without time zone NULL,
	reminderstage integer NOT NULL DEFAULT 0,
	lastreminderon timestamp without time zone NULL,
	escalatedon timestamp without time zone NULL,
	viaoffline boolean NOT NULL DEFAULT false,
	CONSTRAINT pk_protocolacknowledgments PRIMARY KEY (protocolacknowledgmentid),
	CONSTRAINT fk_protocolacknowledgments_protocolversions FOREIGN KEY (protocolversionid) REFERENCES public.protocolversions (protocolversionid) ON DELETE NO ACTION,
	CONSTRAINT fk_protocolacknowledgments_dispatchprotocols FOREIGN KEY (dispatchprotocolid) REFERENCES public.dispatchprotocols (dispatchprotocolid) ON DELETE NO ACTION
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_protocolacknowledgments_version_user ON public.protocolacknowledgments (protocolversionid, userid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolacknowledgments_department_user ON public.protocolacknowledgments (departmentid, userid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolacknowledgments_protocol ON public.protocolacknowledgments (dispatchprotocolid, status);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolacknowledgments_status_dueon ON public.protocolacknowledgments (status, dueon);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.protocolacknowledgments;");
			Execute.Sql("ALTER TABLE public.protocolversions DROP COLUMN IF EXISTS quiztrainingid;");
			for (var i = ProtocolColumns.Length - 1; i >= 0; i--)
				Execute.Sql($"ALTER TABLE public.dispatchprotocols DROP COLUMN IF EXISTS {ProtocolColumns[i].Name};");
		}
	}
}
