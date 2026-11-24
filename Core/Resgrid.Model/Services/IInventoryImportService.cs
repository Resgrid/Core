using System.Threading.Tasks;
using Resgrid.Model.Inventories;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Bulk seeding of the inventory catalog and opening quantities from a file: items (with categories, units, levels and
	/// costs), locations, lots, serialized assets, par levels, and the quantity on hand at each location. Preview never
	/// writes; apply re-plans under the posting lock and refuses when the plan no longer matches the preview.
	/// </summary>
	public interface IInventoryImportService
	{
		Task<InventoryImportPreview> PreviewImportAsync(InventoryActor actor, InventoryImportInput input);
		Task<InventoryImportResult> ApplyImportAsync(InventoryActor actor, InventoryImportInput input);
		/// <summary>The caller's visible catalog and stock in the import file's layout, for editing and importing again.</summary>
		Task<string> ExportImportFileAsync(InventoryActor actor);
	}
}
