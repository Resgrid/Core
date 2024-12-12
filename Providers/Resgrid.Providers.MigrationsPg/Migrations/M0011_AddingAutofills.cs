using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(11)]
	public class M0011_AddingAutofills : Migration
	{
		public override void Up()
		{
			Create.Table("Autofills")
			   .WithColumn("AutofillId").AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().Nullable()
			   .WithColumn("Type").AsInt32().NotNullable()
			   .WithColumn("Sort").AsInt32().NotNullable()
			   .WithColumn("Name").AsCustom("citext")
			   .WithColumn("Data").AsCustom("citext")
			   .WithColumn("AddedByUserId").AsCustom("citext").Nullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable();
		}

		public override void Down()
		{

		}
	}
}
