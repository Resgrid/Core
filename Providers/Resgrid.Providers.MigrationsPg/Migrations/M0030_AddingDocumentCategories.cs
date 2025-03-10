using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(30)]
	public class M0030_AddingDocumentCategories : Migration
	{
		public override void Up()
		{
			Create.Table("DocumentCategories".ToLower())
			   .WithColumn("DocumentCategoryId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("AddedOn".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("AddedById".ToLower()).AsCustom("citext").NotNullable();

			Create.ForeignKey("FK_DocumentCategories_Department")
				.FromTable("DocumentCategories".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());
		}

		public override void Down()
		{

		}
	}
}
