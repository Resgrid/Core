using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
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
			   .WithColumn("Name").AsCustom("citext").NotNullable()
			   .WithColumn("PhoneNumber").AsCustom("citext")
			   .WithColumn("FaxNumber").AsCustom("citext")
			   .WithColumn("Email").AsCustom("citext")
			   .WithColumn("Twitter").AsCustom("citext")
			   .WithColumn("Facebook").AsCustom("citext")
			   .WithColumn("Notes").AsCustom("citext")
			   .WithColumn("Address").AsCustom("citext")
			   .WithColumn("City").AsCustom("citext")
			   .WithColumn("State").AsCustom("citext")
			   .WithColumn("PostalCode").AsCustom("citext")
			   .WithColumn("Country").AsCustom("citext")
			   .WithColumn("Location").AsCustom("citext");


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
