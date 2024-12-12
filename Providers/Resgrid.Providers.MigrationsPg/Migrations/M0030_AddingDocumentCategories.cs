using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(30)]
	public class M0030_AddingDocumentCategories : Migration
	{
		public override void Up()
		{
			Create.Table("DocumentCategories")
			   .WithColumn("DocumentCategoryId").AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("Name").AsCustom("citext").NotNullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedById").AsCustom("citext").NotNullable();

			Create.ForeignKey("FK_DocumentCategories_Department")
				.FromTable("DocumentCategories").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");
		}

		public override void Down()
		{

		}
	}
}
