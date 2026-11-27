using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0403_AddCallProtocolStepNotes: callprotocolstepnotes (user id citext), indexed by department and call protocol, with the
	/// partial unique index on (callprotocolid, clientstepid). Existence-guarded.
	/// </summary>
	[Migration(403)]
	public class M0403_AddCallProtocolStepNotesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("callprotocolstepnotes").Exists())
			{
				Create.Table("callprotocolstepnotes")
					.WithColumn("callprotocolstepnoteid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().NotNullable()
					.WithColumn("callprotocolid").AsInt32().NotNullable()
					.WithColumn("stepid").AsString(128).NotNullable()
					.WithColumn("clientstepid").AsString(64).Nullable()
					.WithColumn("userid").AsCustom("citext").Nullable()
					.WithColumn("note").AsCustom("text").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("createdon").AsDateTime2().NotNullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_callprotocolstepnotes_callprotocol ON callprotocolstepnotes (departmentid, callprotocolid);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_callprotocolstepnotes_clientstep ON callprotocolstepnotes (callprotocolid, clientstepid) WHERE clientstepid IS NOT NULL;");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS callprotocolstepnotes;");
		}
	}
}
