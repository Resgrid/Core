using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(30)]
	public class M0030_AddingDocumentCategories : Migration
	{
		public override void Up()
		{
			Create.Table("DocumentCategories")
			   .WithColumn("DocumentCategoryId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("Name").AsString(512).NotNullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedById").AsString(128).NotNullable();

			Create.ForeignKey("FK_DocumentCategories_Department")
				.FromTable("DocumentCategories").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");
		}

		public override void Down()
		{

		}
	}
}
