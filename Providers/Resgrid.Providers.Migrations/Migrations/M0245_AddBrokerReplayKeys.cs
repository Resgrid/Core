using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 3: the shared, single-use record of Protected Data Broker request ids and session
	/// assertion ids, replacing the broker's per-process replay cache. Keys are SHA-256 digests; no request content,
	/// token, or user identifier is stored.
	/// </summary>
	[Migration(245)]
	public class M0245_AddBrokerReplayKeys : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("BrokerReplayKeys").Exists())
				Create.Table("BrokerReplayKeys")
					.WithColumn("ReplayKey").AsAnsiString(64).PrimaryKey().NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable()
					.WithColumn("CreatedOnUtc").AsDateTime().NotNullable()
					.WithColumn("ExpiresOnUtc").AsDateTime().NotNullable();

			if (!Schema.Table("BrokerReplayKeys").Index("IX_BrokerReplayKeys_Expires").Exists())
				Create.Index("IX_BrokerReplayKeys_Expires").OnTable("BrokerReplayKeys")
					.OnColumn("ExpiresOnUtc").Ascending();
		}

		public override void Down() => throw new System.NotSupportedException("Replay records are security state; they expire on their own.");
	}
}
