using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0393_AddLmsLtiAndXapiOutbox: LTI tools, platform keys and AGS tokens, and the xAPI outbox, in lower case. Client ids,
	/// key ids, token hashes and jtis stay exact; member ids and names are citext. Guarded for safe retry.
	/// </summary>
	[Migration(393)]
	public class M0393_AddLmsLtiAndXapiOutboxPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsltitools (
	lmsltitoolid serial NOT NULL,
	departmentid integer NOT NULL,
	name citext NULL,
	clientid varchar(64) NOT NULL,
	deploymentid varchar(64) NULL,
	oidcloginurl text NULL,
	launchurl text NULL,
	redirecturis text NULL,
	jwksurl text NULL,
	publicjwksjson text NULL,
	customparameters text NULL,
	allowgradepassback boolean NOT NULL DEFAULT false,
	sharename boolean NOT NULL DEFAULT false,
	shareemail boolean NOT NULL DEFAULT false,
	isactive boolean NOT NULL DEFAULT true,
	isdeleted boolean NOT NULL DEFAULT false,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	CONSTRAINT pk_lmsltitools PRIMARY KEY (lmsltitoolid)
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsltitools_clientid ON public.lmsltitools (clientid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsltitools_department ON public.lmsltitools (departmentid, isdeleted);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsltiplatformkeys (
	lmsltiplatformkeyid serial NOT NULL,
	departmentid integer NOT NULL,
	kid varchar(64) NOT NULL,
	publicjwkjson text NULL,
	privatekeyencrypted text NULL,
	status integer NOT NULL DEFAULT 0,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	retiredon timestamp without time zone NULL,
	CONSTRAINT pk_lmsltiplatformkeys PRIMARY KEY (lmsltiplatformkeyid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsltiplatformkeys_department ON public.lmsltiplatformkeys (departmentid, status);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsltiaccesstokens (
	lmsltiaccesstokenid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsltitoolid integer NOT NULL,
	tokenhash varchar(64) NOT NULL,
	scopes text NULL,
	assertionjti varchar(200) NOT NULL,
	createdon timestamp without time zone NOT NULL,
	expireson timestamp without time zone NOT NULL,
	CONSTRAINT pk_lmsltiaccesstokens PRIMARY KEY (lmsltiaccesstokenid)
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsltiaccesstokens_token ON public.lmsltiaccesstokens (tokenhash);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsltiaccesstokens_jti ON public.lmsltiaccesstokens (lmsltitoolid, assertionjti);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsltiaccesstokens_expires ON public.lmsltiaccesstokens (expireson);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsxapioutbox (
	lmsxapioutboxitemid serial NOT NULL,
	departmentid integer NOT NULL,
	statementid varchar(36) NOT NULL,
	userid citext NULL,
	verbid text NULL,
	statementjson text NULL,
	status integer NOT NULL DEFAULT 0,
	attempts integer NOT NULL DEFAULT 0,
	nextattempton timestamp without time zone NOT NULL,
	claimedon timestamp without time zone NULL,
	lasterror text NULL,
	createdon timestamp without time zone NOT NULL,
	deliveredon timestamp without time zone NULL,
	CONSTRAINT pk_lmsxapioutbox PRIMARY KEY (lmsxapioutboxitemid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsxapioutbox_due ON public.lmsxapioutbox (status, nextattempton) WHERE status IN (0, 1);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsxapioutbox_department ON public.lmsxapioutbox (departmentid, status);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmsxapioutbox;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsltiaccesstokens;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsltiplatformkeys;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsltitools;");
		}
	}
}
