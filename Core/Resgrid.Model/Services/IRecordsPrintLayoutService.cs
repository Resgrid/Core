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
	}
}
