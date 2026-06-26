using System;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Widens the Addresses columns. Address1 (street address) was nvarchar(200); long addresses saved via Contacts
	/// edit (and Stations/Groups/Home) hit SQL error 8152 "String or binary data would be truncated" in
	/// RepositoryBase.UpdateAsync (AddressService.SaveAddressAsync). Street address is free-text and goes to max; the
	/// shorter fields are padded generously.
	/// </summary>
	[Migration(85)]
	public class M0085_WidenAddressColumns : Migration
	{
		public override void Up()
		{
			if (Schema.Table("Addresses").Exists())
			{
				// Keep these columns Nullable: the Addresses table predates the migration system and they were never
				// enforced NOT NULL, so legacy rows may hold NULLs. Enforcing NOT NULL here would fail the migration on
				// those rows. ([Required] on the Address model already prevents new null saves at the app layer.) This
				// migration's job is only to widen the columns to stop the 8152 "would be truncated" errors.
				Alter.Table("Addresses").AlterColumn("Address1").AsString(Int32.MaxValue).Nullable();
				Alter.Table("Addresses").AlterColumn("City").AsString(200).Nullable();
				Alter.Table("Addresses").AlterColumn("State").AsString(100).Nullable();
				Alter.Table("Addresses").AlterColumn("PostalCode").AsString(100).Nullable();
				Alter.Table("Addresses").AlterColumn("Country").AsString(100).Nullable();
			}
		}

		public override void Down()
		{
			// No-op: narrowing back could truncate existing data.
		}
	}
}
