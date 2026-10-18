using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Managed activation and invitation sends (see the SQL Server M0279): importruns.request, the staff request as JSON (text,
	/// like the table's other JSON columns).
	/// </summary>
	[Migration(281)]
	public class M0281_AddImportRunRequestPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("importruns").Column("request").Exists())
				Alter.Table("importruns").AddColumn("request").AsCustom("text").Nullable();
		}

		public override void Down()
		{
			if (Schema.Table("importruns").Column("request").Exists())
				Delete.Column("request").FromTable("importruns");
		}
	}
}
