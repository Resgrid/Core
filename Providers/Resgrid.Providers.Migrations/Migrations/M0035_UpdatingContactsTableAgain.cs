using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(35)]
	public class M0035_UpdatingContactsTableAgain : Migration
	{
		public override void Up()
		{
			Alter.Table("Contacts").AlterColumn("FirstName").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("MiddleName").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("LastName").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("OtherName").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("CompanyName").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("Email").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("Website").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("Twitter").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("Facebook").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("LinkedIn").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("Instagram").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("Threads").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("Bluesky").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("Mastodon").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("LocationGpsCoordinates").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("EntranceGpsCoordinates").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("ExitGpsCoordinates").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("LocationGeofence").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("CountryIssuedIdNumber").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("CountryIdName").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("StateIdNumber").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("StateIdName").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("StateIdCountryName").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("Description").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("OtherInfo").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("HomePhoneNumber").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("CellPhoneNumber").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("FaxPhoneNumber").AsString(Int32.MaxValue).Nullable();
			Alter.Table("Contacts").AlterColumn("OfficePhoneNumber").AsString(Int32.MaxValue).Nullable();
			Alter.Table("ContactNoteTypes").AlterColumn("Name").AsString(Int32.MaxValue).Nullable();
			Alter.Table("ContactNotes").AlterColumn("Note").AsString(Int32.MaxValue).Nullable();
			Alter.Table("ContactAssociations").AlterColumn("Note").AsString(Int32.MaxValue).Nullable();
			Alter.Table("ContactCategories").AlterColumn("Name").AsString(Int32.MaxValue).Nullable();
			Alter.Table("ContactCategories").AlterColumn("Description").AsString(Int32.MaxValue).Nullable();

		}

		public override void Down()
		{

		}
	}
}
