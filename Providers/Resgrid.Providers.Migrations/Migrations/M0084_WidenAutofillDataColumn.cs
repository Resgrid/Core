using System;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Widens Autofills.Data (the call-note / autofill template body) from the M0011 default nvarchar(255) to
	/// nvarchar(max). Users saving a call-note template longer than 255 chars via POST /User/Templates/EditCallNote
	/// hit SQL error 8152 "String or binary data would be truncated" in RepositoryBase.UpdateAsync.
	/// </summary>
	[Migration(84)]
	public class M0084_WidenAutofillDataColumn : Migration
	{
		public override void Up()
		{
			if (Schema.Table("Autofills").Exists() && Schema.Table("Autofills").Column("Data").Exists())
			{
				Alter.Table("Autofills").AlterColumn("Data").AsString(Int32.MaxValue).Nullable();
			}
		}

		public override void Down()
		{
			// No-op: narrowing back to nvarchar(255) could truncate existing data.
		}
	}
}
