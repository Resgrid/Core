using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0399_AddProtocolFlashcards: protocolflashcards (member ids citext), indexed by department and protocol. Existence-guarded.
	/// </summary>
	[Migration(399)]
	public class M0399_AddProtocolFlashcardsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("protocolflashcards").Exists())
			{
				Create.Table("protocolflashcards")
					.WithColumn("protocolflashcardid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("dispatchprotocolid").AsInt32().NotNullable()
					.WithColumn("protocolversionid").AsInt32().NotNullable()
					.WithColumn("sourceversionlabel").AsString(50).Nullable()
					.WithColumn("sourceanchor").AsString(128).Nullable()
					.WithColumn("sourceheading").AsString(500).Nullable()
					.WithColumn("front").AsString(300).NotNullable()
					.WithColumn("back").AsString(600).NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("flagreason").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("flaggedon").AsDateTime2().Nullable()
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("exchangeid").AsString(64).Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("reviewedon").AsDateTime2().Nullable()
					.WithColumn("reviewedbyuserid").AsCustom("citext").Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_protocolflashcards_protocol ON protocolflashcards (departmentid, dispatchprotocolid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS protocolflashcards;");
		}
	}
}
