using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(2)]
	public class M0002_AddingContacts : Migration
	{
		public override void Up()
		{
			Create.Table("Contacts")
			   .WithColumn("ContactId").AsInt32().NotNullable().PrimaryKey().Identity()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("ContactTypeId").AsInt32()
			   .WithColumn("Name").AsString().NotNullable()
			   .WithColumn("PhoneNumber").AsString()
			   .WithColumn("FaxNumber").AsString()
			   .WithColumn("Email").AsString()
			   .WithColumn("Twitter").AsString()
			   .WithColumn("Facebook").AsString()
			   .WithColumn("Notes").AsString()
			   .WithColumn("Address").AsString()
			   .WithColumn("City").AsString()
			   .WithColumn("State").AsString()
			   .WithColumn("PostalCode").AsString()
			   .WithColumn("Country").AsString()
			   .WithColumn("Location").AsString();


			Create.ForeignKey("FK_Conacts_Departments")
				.FromTable("Contacts").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");
		}

		public override void Down()
		{
			Delete.Table("Contacts");
		}
	}
}
