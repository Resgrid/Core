using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(11)]
	public class M0011_AddingAutofills : Migration
	{
		public override void Up()
		{
			Create.Table("Autofills")
			   .WithColumn("AutofillId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().Nullable()
			   .WithColumn("Type").AsInt32().NotNullable()
			   .WithColumn("Sort").AsInt32().NotNullable()
			   .WithColumn("Name").AsString(256)
			   .WithColumn("Data").AsString()
			   .WithColumn("AddedByUserId").AsString(128).Nullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable();
		}

		public override void Down()
		{

		}
	}
}
