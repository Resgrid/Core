using System;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// ADP plan section 22.2 pre-enrollment capacity migration (SQL Server only — PostgreSQL uses
	/// unbounded citext everywhere, so it has a matching no-op migration for version parity).
	/// An AES-GCM envelope "rgdp:1:{v}:{base64(nonce|tag|ciphertext)}" needs roughly
	/// 1.4 × plaintext + 70 characters, so bounded caps like 150/4000 cannot hold envelopes of
	/// near-cap plaintext; every cataloged bounded string column goes to NVARCHAR(MAX).
	/// Widening NVARCHAR(n) to MAX is metadata-only (no data rewrite). Plaintext user-input
	/// validation constants and NOT NULL constraints are unchanged — capacity is the only change.
	/// None of these columns is an index key or INCLUDE (verified in plan section 22.4).
	/// </summary>
	[Migration(127)]
	public class M0127_WidenProtectedCandidateColumns : Migration
	{
		public override void Up()
		{
			Alter.Table("Calls").AlterColumn("DeletedReason").AsString(int.MaxValue).Nullable();
			Alter.Table("CallNotes").AlterColumn("FlaggedReason").AsString(int.MaxValue).Nullable();
			Alter.Table("CallAttachments").AlterColumn("FlaggedReason").AsString(int.MaxValue).Nullable();
			Alter.Table("CallAttachments").AlterColumn("Name").AsString(int.MaxValue).Nullable();
			Alter.Table("CallReferences").AlterColumn("Note").AsString(int.MaxValue).Nullable();
			Alter.Table("CallLogs").AlterColumn("Narrative").AsString(int.MaxValue).NotNullable();
			Alter.Table("Logs").AlterColumn("Narrative").AsString(int.MaxValue).NotNullable();
			Alter.Table("UnitLogs").AlterColumn("Narrative").AsString(int.MaxValue).NotNullable();
			Alter.Table("UserStates").AlterColumn("Note").AsString(int.MaxValue).Nullable();
			Alter.Table("Messages").AlterColumn("Subject").AsString(int.MaxValue).NotNullable();
			Alter.Table("Messages").AlterColumn("Body").AsString(int.MaxValue).NotNullable();

			// Linked addresses enter the catalog (section 5.1); Address1 is already MAX after M0085.
			Alter.Table("Addresses").AlterColumn("City").AsString(int.MaxValue).Nullable();
			Alter.Table("Addresses").AlterColumn("State").AsString(int.MaxValue).Nullable();
			Alter.Table("Addresses").AlterColumn("PostalCode").AsString(int.MaxValue).Nullable();
			Alter.Table("Addresses").AlterColumn("Country").AsString(int.MaxValue).Nullable();

			// Mailbox credentials get envelope-encrypted under the credential-hygiene item (22.1).
			Alter.Table("DistributionLists").AlterColumn("Username").AsString(int.MaxValue).Nullable();
			Alter.Table("DistributionLists").AlterColumn("Password").AsString(int.MaxValue).Nullable();

			// Cataloged with the section 5.3 moderation set.
			Alter.Table("ModerationRequests").AlterColumn("OriginalContentType").AsString(int.MaxValue).Nullable();
		}

		public override void Down()
		{
			// Narrowing back can truncate data and offers no safety benefit; the widened columns are
			// valid for plaintext-only departments. Intentionally irreversible.
		}
	}
}
