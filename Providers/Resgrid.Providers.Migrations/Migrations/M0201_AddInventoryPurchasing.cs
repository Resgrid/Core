using System.Linq;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(201)]
	public class M0201_AddInventoryPurchasing : Migration
	{
		private static string N(string value) => value;
		private static readonly string[] Tables = { "InventoryVendors", "InventoryPurchaseOrders", "InventoryPurchaseOrderItems" };
		private const string ContactTenantIndex = "UX_Contacts_InventoryPurchasingTenant";
		private const string TransactionLink = "FK_InventoryTransactions_PurchaseOrderItemId";

		public override void Up()
		{
			// Contacts owns organization identity; this key makes every vendor reference tenant-scoped.
			Index("Contacts", "InventoryPurchasingTenant", true, "DepartmentId", "ContactId");
			foreach (var name in Tables)
			{
				var table = Create.Table(N(name)).WithColumn(N("Id")).AsString(36).NotNullable().PrimaryKey()
					.WithColumn(N("DepartmentId")).AsInt32().NotNullable()
					.WithColumn(N("Revision")).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn(N("CreatedOn")).AsDateTime2().NotNullable()
					.WithColumn(N("ModifiedOn")).AsDateTime2().Nullable()
					.WithColumn(N("CreatedBy")).AsString(128).Nullable()
					.WithColumn(N("Content")).AsString(int.MaxValue).Nullable()
					.WithColumn(N("IsProtected")).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn(N("IsDeleted")).AsBoolean().NotNullable().WithDefaultValue(false);
				switch (name)
				{
					case "InventoryVendors":
						table.WithColumn(N("ContactId")).AsString(128).NotNullable();
						break;
					case "InventoryPurchaseOrders":
						table.WithColumn(N("VendorId")).AsString(36).NotNullable()
							.WithColumn(N("Status")).AsInt32().NotNullable().WithDefaultValue(0)
							.WithColumn(N("CurrencyCode")).AsString(3).NotNullable()
							.WithColumn(N("OrderedOn")).AsDateTime2().Nullable()
							.WithColumn(N("ReceivedOn")).AsDateTime2().Nullable();
						break;
					case "InventoryPurchaseOrderItems":
						table.WithColumn(N("PurchaseOrderId")).AsString(36).NotNullable()
							.WithColumn(N("ItemId")).AsString(36).NotNullable()
							.WithColumn(N("LineNumber")).AsInt32().NotNullable()
							.WithColumn(N("QuantityOrdered")).AsDecimal(24, 6).NotNullable()
							.WithColumn(N("QuantityReceived")).AsDecimal(24, 6).NotNullable().WithDefaultValue(0);
						break;
				}
				Index(name, "TenantId", true, "DepartmentId", "Id");
				Check(name, "Revision", "Revision >= 1");
				Create.ForeignKey(N("FK_" + name + "_Department")).FromTable(N(name)).ForeignColumn(N("DepartmentId"))
					.ToTable(N("Departments")).PrimaryColumn(N("DepartmentId"));
			}
			Create.ForeignKey(N("FK_InventoryVendors_Contact")).FromTable(N("InventoryVendors")).ForeignColumns(N("DepartmentId"), N("ContactId"))
				.ToTable(N("Contacts")).PrimaryColumns(N("DepartmentId"), N("ContactId"));
			Link("InventoryPurchaseOrders", "VendorId", "InventoryVendors");
			Link("InventoryPurchaseOrderItems", "PurchaseOrderId", "InventoryPurchaseOrders");
			Link("InventoryPurchaseOrderItems", "ItemId", "InventoryItems");
			Check("InventoryPurchaseOrders", "Status", "Status BETWEEN 0 AND 4");
			Check("InventoryPurchaseOrderItems", "LineNumber", "LineNumber >= 1");
			Check("InventoryPurchaseOrderItems", "Quantity", "QuantityOrdered > 0 AND QuantityReceived >= 0 AND QuantityReceived <= QuantityOrdered");
			Execute.Sql("CREATE UNIQUE INDEX UX_InventoryVendors_ActiveContact ON InventoryVendors(DepartmentId,ContactId) WHERE IsDeleted = 0;");
			Execute.Sql("CREATE UNIQUE INDEX UX_InventoryPurchaseOrderItems_ActiveLine ON InventoryPurchaseOrderItems(DepartmentId,PurchaseOrderId,LineNumber) WHERE IsDeleted = 0;");
			Index("InventoryPurchaseOrders", "VendorStatus", false, "DepartmentId", "VendorId", "Status");
			Index("InventoryPurchaseOrderItems", "Item", false, "DepartmentId", "ItemId");

			// A receipt retains the exact order line; prices remain in protected Content.
			Alter.Table(N("InventoryTransactions")).AddColumn(N("PurchaseOrderItemId")).AsString(36).Nullable();
			Link("InventoryTransactions", "PurchaseOrderItemId", "InventoryPurchaseOrderItems");
			Check("InventoryTransactions", "PurchaseOrderReference", "PurchaseOrderItemId IS NULL OR ReferenceType = 6");
			Index("InventoryTransactions", "PurchaseOrderItem", false, "DepartmentId", "PurchaseOrderItemId", "EntryId");
		}

		private void Link(string table, string column, string parent) =>
			Create.ForeignKey(N("FK_" + table + "_" + column)).FromTable(N(table)).ForeignColumns(N("DepartmentId"), N(column))
				.ToTable(N(parent)).PrimaryColumns(N("DepartmentId"), N("Id"));

		private void Index(string table, string suffix, bool unique, params string[] columns)
		{
			var index = Create.Index(N((unique ? "UX_" : "IX_") + table + "_" + suffix)).OnTable(N(table)).OnColumn(N(columns[0])).Ascending();
			for (var i = 1; i < columns.Length; i++) index = index.OnColumn(N(columns[i])).Ascending();
			if (unique) index.WithOptions().Unique();
		}

		private void Check(string table, string suffix, string expression) =>
			Execute.Sql("ALTER TABLE " + N(table) + " ADD CONSTRAINT " + N("CK_" + table + "_" + suffix) + " CHECK (" + N(expression) + ");");

		public override void Down()
		{
			// Removing purchasing must never orphan a receipt or discard procurement evidence.
			Execute.Sql("IF " + string.Join(" OR ", Tables.Select(t => "EXISTS (SELECT 1 FROM " + N(t) + ")"))
				+ " OR EXISTS (SELECT 1 FROM InventoryTransactions WHERE PurchaseOrderItemId IS NOT NULL) THROW 51000, 'Inventory purchasing data must be exported and removed through the authorized retention process before rollback.', 1;");
			Delete.ForeignKey(N(TransactionLink)).OnTable(N("InventoryTransactions"));
			Delete.Index(N("IX_InventoryTransactions_PurchaseOrderItem")).OnTable(N("InventoryTransactions"));
			Execute.Sql("ALTER TABLE " + N("InventoryTransactions") + " DROP CONSTRAINT " + N("CK_InventoryTransactions_PurchaseOrderReference") + ";");
			Delete.Column(N("PurchaseOrderItemId")).FromTable(N("InventoryTransactions"));
			foreach (var table in Tables.Reverse()) Delete.Table(N(table));
			Delete.Index(N(ContactTenantIndex)).OnTable(N("Contacts"));
		}
	}
}
