using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>Destination rules; see the SQL Server M0317_AddDestinationRules.</summary>
	[Migration(317)]
	public class M0317_AddDestinationRulesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("destinationrules").Exists())
			{
				Create.Table("destinationrules")
					.WithColumn("destinationruleid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("calltypeid").AsInt32().Nullable()
					.WithColumn("callpriority").AsInt32().Nullable()
					.WithColumn("unittypeid").AsInt32().Nullable()
					.WithColumn("poitypeids").AsCustom("citext").Nullable()
					.WithColumn("requiredcapabilityids").AsCustom("citext").Nullable()
					.WithColumn("preferredcapabilityids").AsCustom("citext").Nullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("updatedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_destinationrules_department ON destinationrules (departmentid);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("destinationrules").Exists())
				Delete.Table("destinationrules");
		}
	}
}
