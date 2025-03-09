using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(32)]
	public class M0032_AddingContacts : Migration
	{
		public override void Up()
		{
			//.ForeignKey("FK_Conacts_Departments");
			Delete.Table("Contacts");

			Create.Table("Contacts")
			   .WithColumn("ContactId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("ContactType").AsInt32()
			   .WithColumn("ContactCategoryId").AsString(128)
			   .WithColumn("FirstName").AsString()
			   .WithColumn("MiddleName").AsString()
			   .WithColumn("LastName").AsString()
			   .WithColumn("OtherName").AsString()
			   .WithColumn("CompanyName").AsString()
			   .WithColumn("Email").AsString()
			   .WithColumn("PhysicalAddressId").AsInt32()
			   .WithColumn("MailingAddressId").AsInt32()
			   .WithColumn("Website").AsString()
			   .WithColumn("Twitter").AsString()
			   .WithColumn("Facebook").AsString()
			   .WithColumn("LinkedIn").AsString()
			   .WithColumn("Instagram").AsString()
			   .WithColumn("Threads").AsString()
			   .WithColumn("Bluesky").AsString()
			   .WithColumn("Mastodon").AsString()
			   .WithColumn("LocationGpsCoordinates").AsString()
			   .WithColumn("EntranceGpsCoordinates").AsString()
			   .WithColumn("ExitGpsCoordinates").AsString()
			   .WithColumn("LocationGeofence").AsString()
			   .WithColumn("CountryIssuedIdNumber").AsString()
			   .WithColumn("CountryIdName").AsString()
			   .WithColumn("StateIdNumber").AsString()
			   .WithColumn("StateIdName").AsString()
			   .WithColumn("StateIdCountryName").AsString()
			   .WithColumn("Description").AsString()
			   .WithColumn("OtherInfo").AsString()
			   .WithColumn("HomePhoneNumber").AsString()
			   .WithColumn("CellPhoneNumber").AsString()
			   .WithColumn("FaxPhoneNumber").AsString()
			   .WithColumn("OfficePhoneNumber").AsString()
			   .WithColumn("Image").AsBinary()
			   .WithColumn("IsDeleted").AsBoolean()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedByUserId").AsString(128).NotNullable()
			   .WithColumn("EditedOn").AsDateTime2()
			   .WithColumn("EditedByUserId").AsString(128);

			Create.ForeignKey("FK_Contacts_Department")
				.FromTable("Contacts").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.ForeignKey("FK_Contacts_PhysicalAddress")
				.FromTable("Contacts").ForeignColumn("PhysicalAddressId")
				.ToTable("Addresses").PrimaryColumn("AddressId");

			Create.ForeignKey("FK_Contacts_MailingAddress")
				.FromTable("Contacts").ForeignColumn("MailingAddressId")
				.ToTable("Addresses").PrimaryColumn("AddressId");

			Create.Table("ContactNoteTypes")
			   .WithColumn("ContactNoteTypeId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("Name").AsString().NotNullable()
			   .WithColumn("Color").AsString()
			   .WithColumn("DefaultShouldAlert").AsBoolean()
			   .WithColumn("DefaultVisibility").AsInt32()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedByUserId").AsString(128).NotNullable()
			   .WithColumn("EditedOn").AsDateTime2()
			   .WithColumn("EditedByUserId").AsString(128);

			Create.ForeignKey("FK_ContactNoteTypes_Department")
				.FromTable("ContactNoteTypes").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.Table("ContactNotes")
			   .WithColumn("ContactNoteId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("ContactId").AsString(128).NotNullable()
			   .WithColumn("ContactNoteTypeId").AsString(128)
			   .WithColumn("Note").AsString().NotNullable()
			   .WithColumn("Color").AsString()
			   .WithColumn("ShouldAlert").AsBoolean()
			   .WithColumn("Visibility").AsInt32()
			   .WithColumn("ExpiresOn").AsDateTime2()
			   .WithColumn("IsDeleted").AsBoolean().NotNullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedByUserId").AsString(128).NotNullable()
			   .WithColumn("EditedOn").AsDateTime2()
			   .WithColumn("EditedByUserId").AsString(128);

			Create.ForeignKey("FK_ContactNotes_Department")
				.FromTable("ContactNotes").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.ForeignKey("FK_ContactNotes_Contacts")
				.FromTable("ContactNotes").ForeignColumn("ContactId")
				.ToTable("Contacts").PrimaryColumn("ContactId");

			Create.Table("ContactAssociations")
			   .WithColumn("ContactAssociationId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("SourceContactId").AsString(128).NotNullable()
			   .WithColumn("TargetContactId").AsString(128).NotNullable()
			   .WithColumn("Type").AsInt32()
			   .WithColumn("Note").AsString().Nullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedByUserId").AsString(128).NotNullable();

			Create.ForeignKey("FK_ContactAssociations_Department")
				.FromTable("ContactAssociations").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.ForeignKey("FK_ContactAssociations_SourceContactId")
				.FromTable("ContactAssociations").ForeignColumn("SourceContactId")
				.ToTable("Contacts").PrimaryColumn("ContactId");

			Create.ForeignKey("FK_ContactAssociations_TargetContactId")
				.FromTable("ContactAssociations").ForeignColumn("TargetContactId")
				.ToTable("Contacts").PrimaryColumn("ContactId");

			Create.Table("CallContacts")
			   .WithColumn("CallContactId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("CallId").AsInt32().NotNullable()
			   .WithColumn("ContactId").AsString(128).NotNullable()
			   .WithColumn("CallContactType").AsInt32();

			Create.ForeignKey("FK_CallContacts_Department")
				.FromTable("CallContacts").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.ForeignKey("FK_CallContacts_Contacts")
				.FromTable("Contacts").ForeignColumn("ContactId")
				.ToTable("Contacts").PrimaryColumn("ContactId");

			Create.Table("ContactCategories")
			   .WithColumn("ContactCategoryId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("Name").AsString().NotNullable()
			   .WithColumn("Description").AsString().Nullable()
			   .WithColumn("Color").AsString()
			   .WithColumn("DisplayOnMap").AsBoolean().NotNullable()
			   .WithColumn("MapIcon").AsInt32().Nullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedByUserId").AsString(128).NotNullable()
			   .WithColumn("EditedOn").AsDateTime2()
			   .WithColumn("EditedByUserId").AsString(128);

			Create.ForeignKey("FK_ContactCategories_Department")
				.FromTable("ContactCategories").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");
		}

		public override void Down()
		{

		}
	}
}
