using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Product-managed template packs and jurisdiction profiles (RMS plan section 4.1, RMS-1B launch templates and
	/// RMS-1C operational packs). Content lives in code; the catalog tables mirror it so departments see provenance,
	/// review dates and deprecation. Rendering a template for a profile/locale applies the locked overlay.
	/// </summary>
	public interface IRecordTemplatePacksService
	{
		Task<List<RecordTemplatePackSummary>> GetCatalogAsync();
		Task<List<RmsJurisdictionProfileVersion>> GetProfilesAsync();
		Task<RmsJurisdictionProfileVersion> GetProfileAsync(string profileKey);
		RecordTemplateDefinition GetTemplate(string templateKey);
		/// <summary>The template rendered for a profile and locale: labels, units and currency applied, provenance statement attached.</summary>
		Task<RecordTemplateRendering> RenderAsync(string templateKey, string profileKey, string locale);
		/// <summary>Upserts the code catalog into the product-scope tables (idempotent; called on first browse and by tests).</summary>
		Task<int> EnsureCatalogAsync(CancellationToken cancellationToken = default);
		/// <summary>Diff between a department clone's schema and the current product template, for the deliberate-incorporate flow.</summary>
		RecordDefinitionDiff DiffAgainstTemplate(string templateKey, string profileKey, string locale, RecordDefinitionSchema departmentSchema, string definitionKey, int version);
	}
}
