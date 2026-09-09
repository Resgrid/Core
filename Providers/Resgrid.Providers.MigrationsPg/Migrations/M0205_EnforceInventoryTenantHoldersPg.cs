using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
    [Migration(205)]
    public class M0205_EnforceInventoryTenantHoldersPg : Migration
    {
        private static string N(string value) => value.ToLowerInvariant();
        public override void Up()
        {
            TenantKey("DepartmentGroups", "DepartmentGroupId");
            TenantKey("Units", "UnitId");
            Holder("InventoryLocations", "GroupId", "DepartmentGroups", "DepartmentGroupId");
            Holder("InventoryLocations", "UnitId", "Units", "UnitId");
            Holder("InventoryIssuances", "IssuedToUnitId", "Units", "UnitId");
        }
        private void TenantKey(string parent, string key)
        {
            if (!Schema.Table(N(parent)).Exists()
                || Schema.Table(N(parent)).Constraint(N("UQ_" + parent + "_DepartmentId_" + key)).Exists()
                || Schema.Table(N(parent)).Constraint(N("UQ_InventoryHolder_" + parent)).Exists()
                || Schema.Table(N(parent)).Constraint(N("UQ_InventoryHolder205_" + parent)).Exists()) return;
            Create.UniqueConstraint(N("UQ_InventoryHolder205_" + parent)).OnTable(N(parent)).Columns(N("DepartmentId"), N(key));
        }
        private void Holder(string table, string column, string parent, string key)
        {
            if (!Schema.Table(N(parent)).Exists() || !Schema.Table(N(table)).Exists()
                || Schema.Table(N(table)).Constraint(N("FK_" + table + "_TenantHolder_" + column)).Exists()) return;
            // Add the tenant constraint alongside the original holder FK, including on databases that ran the amended M0198.
            Create.ForeignKey(N("FK_" + table + "_TenantHolder_" + column)).FromTable(N(table)).ForeignColumns(N("DepartmentId"), N(column))
                .ToTable(N(parent)).PrimaryColumns(N("DepartmentId"), N(key));
        }
        public override void Down()
        {
            foreach (var link in new[] { ("InventoryLocations", "GroupId"), ("InventoryLocations", "UnitId"), ("InventoryIssuances", "IssuedToUnitId") })
                if (Schema.Table(N(link.Item1)).Exists() && Schema.Table(N(link.Item1)).Constraint(N("FK_" + link.Item1 + "_TenantHolder_" + link.Item2)).Exists())
                    Delete.ForeignKey(N("FK_" + link.Item1 + "_TenantHolder_" + link.Item2)).OnTable(N(link.Item1));
            // Leave pre-existing canonical and M0198-owned keys and every inventory row intact.
            foreach (var parent in new[] { "DepartmentGroups", "Units" })
                if (Schema.Table(N(parent)).Exists() && Schema.Table(N(parent)).Constraint(N("UQ_InventoryHolder205_" + parent)).Exists())
                    Delete.UniqueConstraint(N("UQ_InventoryHolder205_" + parent)).FromTable(N(parent));
        }
    }
}
