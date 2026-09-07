using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>The DepartmentDefault print layout (RMS plan section 4.10.1), versioned on every save.</summary>
	public interface IRecordsPrintLayoutService
	{
		/// <summary>The saved layout with its parsed config, or an unsaved default (Version 0, generated layout version).</summary>
		Task<RmsRecordPrintLayout> GetDepartmentDefaultAsync(int departmentId);

		Task<RmsRecordPrintLayout> SaveDepartmentDefaultAsync(int departmentId, string userId, RecordsPrintLayoutConfig config, CancellationToken cancellationToken = default);

		/// <summary>The Definition-scope layout for a department definition; a generated default (Version 0) when none was saved.</summary>
		Task<RmsRecordPrintLayout> GetDefinitionLayoutAsync(int departmentId, string definitionKey);

		/// <summary>Saves (versions) the Definition-scope layout from the designer.</summary>
		Task<RmsRecordPrintLayout> SaveDefinitionLayoutAsync(int departmentId, string userId, string definitionKey, RecordsDefinitionLayoutConfig config, CancellationToken cancellationToken = default);

		/// <summary>Print-time resolution: definition layout (when it applies to the version) → department default → generated default.</summary>
		Task<RecordsResolvedPrintLayout> ResolveForDefinitionAsync(int departmentId, string definitionKey, int definitionVersion);
	}
}
