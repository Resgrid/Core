using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Call intake channels, dedicated department numbers, Enhanced AI voice intake and destination advice
	/// (call-intake-and-ai-destinations-plan.md). Tables: IntakeChannels (a department's SMS / email / chat / webhook / voice
	/// addresses whose messages become pending calls; one live channel per transport + address), IntakeMessages (per-message
	/// idempotency claim and audit; the text lives on the call), DepartmentPhoneNumbers (numbers bought from Twilio for the
	/// Dedicated Number add-on), VoiceIntakeSessions (one per phone call, keyed by CallSid), AiDestinationAdvices (model advice per
	/// call, never applied automatically), AiDestinationReviews (debounced review requests, one open per call, worker 81) and
	/// DedicatedNumberBillingAccounts (mirror of AiBillingAccounts). Seeds the PlanAddons row for PlanAddonTypes 7 (USD 15/month; live
	/// Stripe price price_0UduJSqJFDZJcnkV8cIpUYFE; the test-mode price is still empty, so test-mode checkout fails closed) and five flags, all off: Intake.Channels,
	/// Intake.DedicatedNumber, and Ai.DestinationAdvice / Ai.Intake / Ai.VoiceIntake with a prerequisite on Ai.Enhanced. No foreign
	/// keys; UnlinkedDepartmentCleanup removes the rows. Existence-guarded for safe retry.
	/// </summary>
	[Migration(323)]
	public class M0323_AddIntakeChannelsAndDedicatedNumbers : Migration
	{
		private const string DedicatedNumberAddonId = "3b1c7f0e-5d2a-4c86-9a4e-7f2d1e6b8c40";

		/// <summary>Live Stripe price of the add-on ("Private Inbound Dispatch Number", product prod_VfEnZg5YGLGGMp), set 2026-11-20.</summary>
		private const string StripePriceId = "price_0UduJSqJFDZJcnkV8cIpUYFE";

		private static readonly (string Key, string Name, string Description, string Category, bool AiChild)[] Flags =
		{
			("Intake.Channels", "Intake channels", "SMS, email, chat and webhook messages become pending calls. Operator rollout. Seeded off.", "Intake", false),
			("Intake.DedicatedNumber", "Private Inbound Dispatch Number add-on", "Private Inbound Dispatch Number add-on (the department's own phone number): card and checkout (PlanAddonTypes 7). Seeded off.", "Intake", false),
			("Ai.DestinationAdvice", "AI destination advice", "Enhanced AI: destination advice from the call nature, note and call notes, with change suggestions. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off.", "AI", true),
			("Ai.Intake", "AI intake", "Enhanced AI: intake messages saved as pending calls are filled in by the model. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off.", "AI", true),
			("Ai.VoiceIntake", "AI voice intake", "Enhanced AI: guided voice intake on the private inbound dispatch number creates pending calls. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off.", "AI", true)
		};

		public override void Up()
		{
			if (!Schema.Table("IntakeChannels").Exists())
			{
				Create.Table("IntakeChannels")
					.WithColumn("IntakeChannelId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(100).Nullable()
					.WithColumn("ChannelType").AsInt32().NotNullable()
					.WithColumn("Address").AsString(256).Nullable()
					.WithColumn("ProcessingMode").AsInt32().NotNullable()
					.WithColumn("TemplateFormat").AsInt32().Nullable()
					.WithColumn("AllowedSenders").AsString(int.MaxValue).Nullable()
					.WithColumn("DefaultPriority").AsInt32().Nullable()
					.WithColumn("DefaultCallTypeId").AsInt32().Nullable()
					.WithColumn("SendAcknowledgement").AsBoolean().NotNullable()
					.WithColumn("WebhookSecretProtected").AsString(int.MaxValue).Nullable()
					.WithColumn("IsEnabled").AsBoolean().NotNullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable();
			}

			Index("UX_IntakeChannels_Address", "IntakeChannels", "CREATE UNIQUE INDEX [UX_IntakeChannels_Address] ON [IntakeChannels] ([ChannelType], [Address]) WHERE [IsDeleted] = 0 AND [Address] IS NOT NULL;");
			Index("IX_IntakeChannels_Department", "IntakeChannels", "CREATE INDEX [IX_IntakeChannels_Department] ON [IntakeChannels] ([DepartmentId]);");

			if (!Schema.Table("IntakeMessages").Exists())
			{
				Create.Table("IntakeMessages")
					.WithColumn("IntakeMessageId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IntakeChannelId").AsString(36).Nullable()
					.WithColumn("ChannelType").AsInt32().NotNullable()
					.WithColumn("ProviderMessageId").AsString(256).Nullable()
					.WithColumn("Sender").AsString(256).Nullable()
					.WithColumn("State").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("Reason").AsString(1000).Nullable()
					.WithColumn("ReceivedOn").AsDateTime2().NotNullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable();
			}

			Index("UX_IntakeMessages_Provider", "IntakeMessages", "CREATE UNIQUE INDEX [UX_IntakeMessages_Provider] ON [IntakeMessages] ([IntakeChannelId], [ProviderMessageId]) WHERE [IntakeChannelId] IS NOT NULL AND [ProviderMessageId] IS NOT NULL;");
			Index("IX_IntakeMessages_Usage", "IntakeMessages", "CREATE INDEX [IX_IntakeMessages_Usage] ON [IntakeMessages] ([DepartmentId], [ChannelType], [ReceivedOn]);");
			Index("IX_IntakeMessages_Received", "IntakeMessages", "CREATE INDEX [IX_IntakeMessages_Received] ON [IntakeMessages] ([ReceivedOn]);");

			if (!Schema.Table("DepartmentPhoneNumbers").Exists())
			{
				Create.Table("DepartmentPhoneNumbers")
					.WithColumn("DepartmentPhoneNumberId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("PhoneNumber").AsString(32).NotNullable()
					.WithColumn("Provider").AsString(20).Nullable()
					.WithColumn("ProviderNumberId").AsString(64).Nullable()
					.WithColumn("CountryCode").AsString(2).Nullable()
					.WithColumn("SmsCapable").AsBoolean().NotNullable()
					.WithColumn("VoiceCapable").AsBoolean().NotNullable()
					.WithColumn("Status").AsInt32().NotNullable()
					.WithColumn("SmsIntakeEnabled").AsBoolean().NotNullable()
					.WithColumn("VoiceIntakeEnabled").AsBoolean().NotNullable()
					.WithColumn("ProvisionedByUserId").AsString(128).Nullable()
					.WithColumn("ProvisionedOn").AsDateTime2().Nullable()
					.WithColumn("ReleaseAfter").AsDateTime2().Nullable()
					.WithColumn("ReleasedOn").AsDateTime2().Nullable()
					.WithColumn("LastError").AsString(1000).Nullable()
					.WithColumn("AddonInactiveSince").AsDateTime2().Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable();
			}

			// Added to the unapplied migration on 2026-11-19 (worker 80 renewal delay); a dev database that already ran it gets the column here.
			// Schema reads see the database before this migration's queued expressions run, so a table created above is skipped here.
			if (Schema.Table("DepartmentPhoneNumbers").Exists() && !Schema.Table("DepartmentPhoneNumbers").Column("AddonInactiveSince").Exists())
				Alter.Table("DepartmentPhoneNumbers").AddColumn("AddonInactiveSince").AsDateTime2().Nullable();

			// A number belongs to one department while it is provisioning, active or held after lapsing.
			Index("UX_DepartmentPhoneNumbers_Number", "DepartmentPhoneNumbers", "CREATE UNIQUE INDEX [UX_DepartmentPhoneNumbers_Number] ON [DepartmentPhoneNumbers] ([PhoneNumber]) WHERE [Status] IN (1, 2, 3);");
			Index("IX_DepartmentPhoneNumbers_Department", "DepartmentPhoneNumbers", "CREATE INDEX [IX_DepartmentPhoneNumbers_Department] ON [DepartmentPhoneNumbers] ([DepartmentId], [Status]);");

			if (!Schema.Table("VoiceIntakeSessions").Exists())
			{
				Create.Table("VoiceIntakeSessions")
					.WithColumn("VoiceIntakeSessionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProviderCallId").AsString(64).NotNullable()
					.WithColumn("FromNumber").AsString(32).Nullable()
					.WithColumn("ToNumber").AsString(32).Nullable()
					.WithColumn("Step").AsInt32().NotNullable()
					.WithColumn("AnswersJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Retries").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("StartedOn").AsDateTime2().NotNullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable();
			}

			Index("UX_VoiceIntakeSessions_Call", "VoiceIntakeSessions", "CREATE UNIQUE INDEX [UX_VoiceIntakeSessions_Call] ON [VoiceIntakeSessions] ([ProviderCallId]);");
			Index("IX_VoiceIntakeSessions_Usage", "VoiceIntakeSessions", "CREATE INDEX [IX_VoiceIntakeSessions_Usage] ON [VoiceIntakeSessions] ([DepartmentId], [StartedOn]);");

			if (!Schema.Table("AiDestinationAdvices").Exists())
			{
				Create.Table("AiDestinationAdvices")
					.WithColumn("AiDestinationAdviceId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("CurrentPoiId").AsInt32().Nullable()
					.WithColumn("RecommendedPoiId").AsInt32().Nullable()
					.WithColumn("RequiredCapabilityIds").AsString(int.MaxValue).Nullable()
					.WithColumn("Rationale").AsString(2000).Nullable()
					.WithColumn("Confidence").AsDouble().NotNullable()
					.WithColumn("IsChange").AsBoolean().NotNullable()
					.WithColumn("ReviewTrigger").AsInt32().NotNullable()
					.WithColumn("State").AsInt32().NotNullable()
					.WithColumn("InputDigest").AsString(64).Nullable()
					.WithColumn("PromptVersion").AsString(64).Nullable()
					.WithColumn("ModelRevision").AsString(100).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ResolvedByUserId").AsString(128).Nullable()
					.WithColumn("ResolvedOn").AsDateTime2().Nullable();
			}

			Index("IX_AiDestinationAdvices_Call", "AiDestinationAdvices", "CREATE INDEX [IX_AiDestinationAdvices_Call] ON [AiDestinationAdvices] ([CallId], [State]);");
			Index("IX_AiDestinationAdvices_Department", "AiDestinationAdvices", "CREATE INDEX [IX_AiDestinationAdvices_Department] ON [AiDestinationAdvices] ([DepartmentId]);");

			if (!Schema.Table("AiDestinationReviews").Exists())
			{
				Create.Table("AiDestinationReviews")
					.WithColumn("AiDestinationReviewId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("ReviewTrigger").AsInt32().NotNullable()
					.WithColumn("RequestedOn").AsDateTime2().NotNullable()
					.WithColumn("ClaimedOn").AsDateTime2().Nullable()
					.WithColumn("ProcessedOn").AsDateTime2().Nullable()
					.WithColumn("Outcome").AsString(200).Nullable();
			}

			// One waiting review per call: later triggers move RequestedOn instead of adding rows.
			Index("UX_AiDestinationReviews_Open", "AiDestinationReviews", "CREATE UNIQUE INDEX [UX_AiDestinationReviews_Open] ON [AiDestinationReviews] ([CallId]) WHERE [ProcessedOn] IS NULL;");
			Index("IX_AiDestinationReviews_Due", "AiDestinationReviews", "CREATE INDEX [IX_AiDestinationReviews_Due] ON [AiDestinationReviews] ([ProcessedOn], [RequestedOn]);");
			Index("IX_AiDestinationReviews_Department", "AiDestinationReviews", "CREATE INDEX [IX_AiDestinationReviews_Department] ON [AiDestinationReviews] ([DepartmentId]);");

			if (!Schema.Table("DedicatedNumberBillingAccounts").Exists())
			{
				Create.Table("DedicatedNumberBillingAccounts")
					.WithColumn("DepartmentId").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("Provider").AsString(20).NotNullable()
					.WithColumn("CustomerId").AsString(100).NotNullable()
					.WithColumn("PlanAddonId").AsString(36).NotNullable()
					.WithColumn("PriceId").AsString(100).NotNullable()
					.WithColumn("SubscriptionId").AsString(100).Nullable()
					.WithColumn("CheckoutId").AsString(100).Nullable()
					.WithColumn("CheckoutUrl").AsString(2048).Nullable()
					.WithColumn("CheckoutExpiresOn").AsDateTime2().Nullable()
					.WithColumn("CheckoutAttempt").AsString(36).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable();
			}

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [PlanAddons] WHERE [PlanAddonId] = '" + DedicatedNumberAddonId + "' OR [AddonType] = 7) " +
				"INSERT INTO [PlanAddons] ([PlanAddonId], [AddonType], [Cost], [ExternalId], [TestExternalId]) " +
				"VALUES ('" + DedicatedNumberAddonId + "', 7, 15, '" + StripePriceId + "', '');");

			foreach (var flag in Flags)
			{
				Execute.Sql(
					"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = '" + flag.Key + "') " +
					"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
					"VALUES ('" + flag.Key + "', '" + flag.Name + "', '" + flag.Description + "', '" + flag.Category + "', 0);");

				if (!flag.AiChild)
					continue;

				Execute.Sql(
					"IF NOT EXISTS (SELECT 1 FROM [FeatureFlagPrerequisites] p " +
					"  JOIN [FeatureFlags] f ON f.[FeatureFlagId] = p.[FeatureFlagId] " +
					"  JOIN [FeatureFlags] r ON r.[FeatureFlagId] = p.[RequiredFeatureFlagId] " +
					"  WHERE f.[FlagKey] = '" + flag.Key + "' AND r.[FlagKey] = 'Ai.Enhanced') " +
					"INSERT INTO [FeatureFlagPrerequisites] ([FeatureFlagId], [RequiredFeatureFlagId], [RequiredValue]) " +
					"SELECT f.[FeatureFlagId], r.[FeatureFlagId], NULL FROM [FeatureFlags] f CROSS JOIN [FeatureFlags] r " +
					"WHERE f.[FlagKey] = '" + flag.Key + "' AND r.[FlagKey] = 'Ai.Enhanced';");
			}
		}

		public override void Down()
		{
			// Operator-owned rows (add-on catalog, rollout flags) are never removed on rollback.
			foreach (var table in new[] { "DedicatedNumberBillingAccounts", "AiDestinationReviews", "AiDestinationAdvices", "VoiceIntakeSessions",
						 "DepartmentPhoneNumbers", "IntakeMessages", "IntakeChannels" })
			{
				if (Schema.Table(table).Exists())
					Delete.Table(table);
			}
		}

		private void Index(string name, string table, string createSql)
		{
			Execute.Sql($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}' AND object_id = OBJECT_ID('{table}')) " + createSql);
		}
	}
}
