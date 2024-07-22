using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(31)]
	public class M0031_AddingNoteCategories : Migration
	{
		public override void Up()
		{
			Create.Table("NoteCategories")
			   .WithColumn("NoteCategoryId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("Name").AsString(512).NotNullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedById").AsString(128).NotNullable();

			Create.ForeignKey("FK_NoteCategories_Department")
				.FromTable("NoteCategories").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");
		}

		public override void Down()
		{

		}
	}
}
