using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Passkey plan Phase 1, slice 3: the shared, single-use record of Protected Data Broker request ids and session
	/// assertion ids, replacing the broker's per-process replay cache. Keys are SHA-256 digests; no request content,
	/// token, or user identifier is stored.
	/// </summary>
	[Migration(245)]
	public class M0245_AddBrokerReplayKeysPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("brokerreplaykeys").Exists())
				Create.Table("brokerreplaykeys")
					.WithColumn("replaykey").AsString(64).PrimaryKey().NotNullable()
					.WithColumn("kind").AsInt32().NotNullable()
					.WithColumn("createdonutc").AsDateTime().NotNullable()
					.WithColumn("expiresonutc").AsDateTime().NotNullable();

			if (!Schema.Table("brokerreplaykeys").Index("ix_brokerreplaykeys_expires").Exists())
				Create.Index("ix_brokerreplaykeys_expires").OnTable("brokerreplaykeys")
					.OnColumn("expiresonutc").Ascending();
		}

		public override void Down() => throw new System.NotSupportedException("Replay records are security state; they expire on their own.");
	}
}
