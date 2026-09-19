using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase B2 (B2.2): department payment-provider connections, hosted
	/// payment requests and the received-event ledger for collecting invoice payments through a department's own
	/// Stripe account. Seeds the operator cluster switch Payments.StripeConnect (off, permanent) and the department
	/// flag Invoicing.OnlinePayments (off) with prerequisite edges to Invoicing.CustomerInvoicing and
	/// Payments.StripeConnect. ADP catalog 26 is registered in code with this migration. The InvoicePayments and
	/// DepartmentBillingIdentities columns Phase B2 uses were created by M0209/M0210; the only change to them is
	/// the gateway index on InvoicePayments becoming unique (filtered to online rows) so a replayed or concurrent
	/// webhook delivery cannot record the same provider transaction twice. Registry M0212. Guarded for safe retry.
	/// </summary>
	[Migration(212)]
	public class M0212_AddOnlinePayments : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("DepartmentPaymentConnections").Exists())
			{
				Create.Table("DepartmentPaymentConnections")
					.WithColumn("DepartmentPaymentConnectionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Provider").AsInt32().NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Environment").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ExternalAccountId").AsString(200).NotNullable()
					.WithColumn("DisplayName").AsString(300).Nullable()
					.WithColumn("Country").AsString(2).Nullable()
					.WithColumn("DefaultCurrency").AsString(3).Nullable()
					.WithColumn("CapabilitiesJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ScopesCsv").AsString(200).Nullable()
					.WithColumn("AccessTokenCiphertext").AsString(int.MaxValue).Nullable()
					.WithColumn("RefreshTokenCiphertext").AsString(int.MaxValue).Nullable()
					.WithColumn("TokenExpiresOn").AsDateTime2().Nullable()
					.WithColumn("ConnectedOn").AsDateTime2().NotNullable()
					.WithColumn("ConnectedByUserId").AsString(128).Nullable()
					.WithColumn("DisconnectedOn").AsDateTime2().Nullable()
					.WithColumn("LastVerifiedOn").AsDateTime2().Nullable()
					.WithColumn("LastError").AsString(500).Nullable()
					.WithColumn("IsDefault").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("EditedByUserId").AsString(128).Nullable();

				Create.Index("IX_DepartmentPaymentConnections_Department").OnTable("DepartmentPaymentConnections")
					.OnColumn("DepartmentId").Ascending().OnColumn("IsDeleted").Ascending();
				Create.Index("IX_DepartmentPaymentConnections_Account").OnTable("DepartmentPaymentConnections")
					.OnColumn("Provider").Ascending().OnColumn("ExternalAccountId").Ascending();
				// One live connection per department, provider and environment (plan B2.2).
				Execute.Sql("CREATE UNIQUE INDEX [UX_DepartmentPaymentConnections_Live] ON [DepartmentPaymentConnections] ([DepartmentId], [Provider], [Environment]) WHERE [IsDeleted] = 0;");
			}

			if (!Schema.Table("InvoicePaymentRequests").Exists())
			{
				Create.Table("InvoicePaymentRequests")
					.WithColumn("InvoicePaymentRequestId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("InvoiceId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DepartmentPaymentConnectionId").AsString(36).Nullable()
					.WithColumn("Provider").AsInt32().NotNullable()
					.WithColumn("Amount").AsDecimal(18, 2).NotNullable()
					.WithColumn("Currency").AsString(3).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ExternalReference").AsString(200).Nullable()
					.WithColumn("PaymentIntentId").AsString(200).Nullable()
					.WithColumn("HostedUrl").AsString(2048).Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().NotNullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable()
					.WithColumn("InvoicePaymentId").AsString(36).Nullable()
					.WithColumn("Source").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("LastError").AsString(1000).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable();

				Create.Index("IX_InvoicePaymentRequests_Invoice").OnTable("InvoicePaymentRequests")
					.OnColumn("InvoiceId").Ascending().OnColumn("Status").Ascending();
				Create.Index("IX_InvoicePaymentRequests_Reference").OnTable("InvoicePaymentRequests")
					.OnColumn("Provider").Ascending().OnColumn("ExternalReference").Ascending();
				Create.Index("IX_InvoicePaymentRequests_Intent").OnTable("InvoicePaymentRequests")
					.OnColumn("Provider").Ascending().OnColumn("PaymentIntentId").Ascending();
				Create.Index("IX_InvoicePaymentRequests_Status_Added").OnTable("InvoicePaymentRequests")
					.OnColumn("Status").Ascending().OnColumn("AddedOn").Ascending();
				Create.Index("IX_InvoicePaymentRequests_Connection").OnTable("InvoicePaymentRequests")
					.OnColumn("DepartmentPaymentConnectionId").Ascending().OnColumn("Status").Ascending();
				// At most one non-terminal request per invoice (Created 0, Opened 1, Processing 2).
				Execute.Sql("CREATE UNIQUE INDEX [UX_InvoicePaymentRequests_Open] ON [InvoicePaymentRequests] ([InvoiceId]) WHERE [Status] IN (0, 1, 2);");
			}

			if (!Schema.Table("PaymentProviderEvents").Exists())
			{
				Create.Table("PaymentProviderEvents")
					.WithColumn("PaymentConnectEventId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("Provider").AsInt32().NotNullable()
					.WithColumn("ExternalEventId").AsString(200).NotNullable()
					.WithColumn("ExternalAccountId").AsString(200).Nullable()
					.WithColumn("EventType").AsString(100).Nullable()
					.WithColumn("DepartmentId").AsInt32().Nullable()
					.WithColumn("LiveMode").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ReceivedOn").AsDateTime2().NotNullable()
					.WithColumn("ProcessedOn").AsDateTime2().Nullable()
					.WithColumn("Outcome").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Error").AsString(1000).Nullable()
					.WithColumn("PayloadJson").AsString(int.MaxValue).Nullable();

				Create.Index("UX_PaymentProviderEvents_Event").OnTable("PaymentProviderEvents")
					.OnColumn("Provider").Ascending().OnColumn("ExternalEventId").Ascending().WithOptions().Unique();
				Create.Index("IX_PaymentProviderEvents_Account").OnTable("PaymentProviderEvents")
					.OnColumn("Provider").Ascending().OnColumn("ExternalAccountId").Ascending().OnColumn("ReceivedOn").Ascending();
				Create.Index("IX_PaymentProviderEvents_Received").OnTable("PaymentProviderEvents")
					.OnColumn("ReceivedOn").Ascending();
				Create.Index("IX_PaymentProviderEvents_Outcome").OnTable("PaymentProviderEvents")
					.OnColumn("Outcome").Ascending().OnColumn("ReceivedOn").Ascending();
				Create.Index("IX_PaymentProviderEvents_Department").OnTable("PaymentProviderEvents")
					.OnColumn("DepartmentId").Ascending();
			}

			// Idempotency of online payments is enforced by the database, not only by the pre-insert lookup: M0210's
			// non-unique gateway index is replaced by a filtered unique one (manual payments carry no transaction id).
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InvoicePayments_Gateway' AND object_id = OBJECT_ID('[InvoicePayments]')) DROP INDEX [IX_InvoicePayments_Gateway] ON [InvoicePayments];");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_InvoicePayments_Gateway' AND object_id = OBJECT_ID('[InvoicePayments]')) " +
				"CREATE UNIQUE INDEX [UX_InvoicePayments_Gateway] ON [InvoicePayments] ([Provider], [GatewayTransactionId]) WHERE [GatewayTransactionId] IS NOT NULL AND [Provider] IS NOT NULL;");

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Payments.StripeConnect') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally], [IsPermanent]) " +
				"VALUES ('Payments.StripeConnect', 'Stripe Connect payment collection', 'Operator per-cluster switch for departments connecting their own Stripe account to collect invoice payments (Workforce & Business Operations plan, Phase B2). Global state only; on in the US cluster at launch, off in the EU cluster. Seeded off.', 'Business', 0, 1);");

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Invoicing.OnlinePayments') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
				"VALUES ('Invoicing.OnlinePayments', 'Online invoice payments', 'Pay-online links and the Online payments settings tab (Workforce & Business Operations plan, Phase B2). Requires Invoicing.CustomerInvoicing and the Payments.StripeConnect cluster switch. Seeded off.', 'Business', 0);");

			foreach (var required in new[] { "Invoicing.CustomerInvoicing", "Payments.StripeConnect" })
			{
				Execute.Sql(
					"IF NOT EXISTS (SELECT 1 FROM [FeatureFlagPrerequisites] p " +
					"  JOIN [FeatureFlags] f ON f.[FeatureFlagId] = p.[FeatureFlagId] " +
					"  JOIN [FeatureFlags] r ON r.[FeatureFlagId] = p.[RequiredFeatureFlagId] " +
					"  WHERE f.[FlagKey] = 'Invoicing.OnlinePayments' AND r.[FlagKey] = '" + required + "') " +
					"INSERT INTO [FeatureFlagPrerequisites] ([FeatureFlagId], [RequiredFeatureFlagId], [RequiredValue]) " +
					"SELECT f.[FeatureFlagId], r.[FeatureFlagId], NULL FROM [FeatureFlags] f CROSS JOIN [FeatureFlags] r " +
					"WHERE f.[FlagKey] = 'Invoicing.OnlinePayments' AND r.[FlagKey] = '" + required + "';");
			}
		}

		public override void Down()
		{
			// Operator-owned flag rows and prerequisite edges are never removed on rollback; the tables are dropped only when empty.
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_InvoicePayments_Gateway' AND object_id = OBJECT_ID('[InvoicePayments]')) DROP INDEX [UX_InvoicePayments_Gateway] ON [InvoicePayments];");
			Execute.Sql("IF OBJECT_ID('[InvoicePayments]', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InvoicePayments_Gateway' AND object_id = OBJECT_ID('[InvoicePayments]')) " +
				"CREATE INDEX [IX_InvoicePayments_Gateway] ON [InvoicePayments] ([Provider], [GatewayTransactionId]);");
			Execute.Sql("IF OBJECT_ID('[PaymentProviderEvents]', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [PaymentProviderEvents]) DROP TABLE [PaymentProviderEvents];");
			Execute.Sql("IF OBJECT_ID('[InvoicePaymentRequests]', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [InvoicePaymentRequests]) DROP TABLE [InvoicePaymentRequests];");
			Execute.Sql("IF OBJECT_ID('[DepartmentPaymentConnections]', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [DepartmentPaymentConnections]) DROP TABLE [DepartmentPaymentConnections];");
		}
	}
}
