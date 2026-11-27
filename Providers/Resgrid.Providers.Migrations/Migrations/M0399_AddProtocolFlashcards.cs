using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 7A (Enhanced AI on protocols, product decision 2026-11-25): ProtocolFlashcards, a protocol's accepted practice cards (Enhanced AI only,
	/// no LMS), each with the protocol version and the section anchor it was drafted from, flagged for review when a newer version goes into
	/// force. Department-owned with no foreign keys; ProtocolDepartmentCleanup and UnlinkedDepartmentCleanup clear it. Existence-guarded.
	/// </summary>
	[Migration(399)]
	public class M0399_AddProtocolFlashcards : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ProtocolFlashcards").Exists())
			{
				Create.Table("ProtocolFlashcards")
					.WithColumn("ProtocolFlashcardId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DispatchProtocolId").AsInt32().NotNullable()
					.WithColumn("ProtocolVersionId").AsInt32().NotNullable()
					.WithColumn("SourceVersionLabel").AsString(50).Nullable()
					.WithColumn("SourceAnchor").AsString(128).Nullable()
					.WithColumn("SourceHeading").AsString(500).Nullable()
					.WithColumn("Front").AsString(300).NotNullable()
					.WithColumn("Back").AsString(600).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("FlagReason").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("FlaggedOn").AsDateTime2().Nullable()
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ExchangeId").AsString(64).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ReviewedOn").AsDateTime2().Nullable()
					.WithColumn("ReviewedByUserId").AsString(128).Nullable();
			}

			// The author's list, the member's deck and the supersede hook's one update: every card of a department's protocol.
			if (!Schema.Table("ProtocolFlashcards").Index("IX_ProtocolFlashcards_Protocol").Exists())
				Create.Index("IX_ProtocolFlashcards_Protocol").OnTable("ProtocolFlashcards")
					.OnColumn("DepartmentId").Ascending().OnColumn("DispatchProtocolId").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("ProtocolFlashcards").Exists())
				Delete.Table("ProtocolFlashcards");
		}
	}
}
