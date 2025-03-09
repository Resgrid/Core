using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(11)]
	public class M0011_AddingAutofills : Migration
	{
		public override void Up()
		{
			Create.Table("Autofills".ToLower())
			   .WithColumn("AutofillId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().Nullable()
			   .WithColumn("Type".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Sort".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Name".ToLower()).AsCustom("citext")
			   .WithColumn("Data".ToLower()).AsCustom("citext")
			   .WithColumn("AddedByUserId".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("AddedOn".ToLower()).AsDateTime2().NotNullable();
		}

		public override void Down()
		{

		}
	}
}
