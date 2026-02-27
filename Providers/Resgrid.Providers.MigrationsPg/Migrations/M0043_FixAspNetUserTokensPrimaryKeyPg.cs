using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL counterpart of M0043 — fixes the AspNetUserTokens primary key.
	/// The table was previously created with UserId as the sole PK column, which causes a
	/// duplicate-key violation when a second token (e.g. recoverycodes) is inserted for the
	/// same user alongside an existing authenticatorkey row.
	/// The correct PK is the composite (userid, loginprovider, name) per ASP.NET Core Identity.
	/// </summary>
	[Migration(43)]
	public class M0043_FixAspNetUserTokensPrimaryKeyPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
				DO $$
				DECLARE
					v_pk_name   TEXT;
					v_col_count INT;
				BEGIN
					-- Find the current PK constraint name on the table
					SELECT kc.constraint_name
					INTO   v_pk_name
					FROM   information_schema.table_constraints    tc
					JOIN   information_schema.key_column_usage     kc
					       ON kc.constraint_name = tc.constraint_name
					       AND kc.table_name     = tc.table_name
					WHERE  tc.table_name      = 'aspnetusertokens'
					AND    tc.constraint_type = 'PRIMARY KEY'
					LIMIT 1;

					IF v_pk_name IS NULL THEN
						-- No PK at all — just create the correct one
						ALTER TABLE aspnetusertokens
							ADD CONSTRAINT pk_aspnetusertokens
							PRIMARY KEY (userid, loginprovider, name);
						RETURN;
					END IF;

					-- Count how many columns are in that PK
					SELECT COUNT(*)
					INTO   v_col_count
					FROM   information_schema.key_column_usage
					WHERE  constraint_name = v_pk_name
					AND    table_name      = 'aspnetusertokens';

					IF v_col_count < 3 THEN
						-- Wrong PK — drop it and recreate as composite
						EXECUTE 'ALTER TABLE aspnetusertokens DROP CONSTRAINT ' || quote_ident(v_pk_name);
						ALTER TABLE aspnetusertokens
							ADD CONSTRAINT pk_aspnetusertokens
							PRIMARY KEY (userid, loginprovider, name);
					END IF;
				END;
				$$;
			");
		}

		public override void Down()
		{
			// Reverting to a single-column PK would corrupt data; intentionally a no-op.
		}
	}
}

