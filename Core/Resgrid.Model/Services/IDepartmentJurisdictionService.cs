using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Industrial ERT G8 CA1: the department's jurisdiction code (TemplateJurisdictions, e.g. "CA-ON") read from its address, so the
	/// certification, training-category and checklist template catalogs can offer what fits first. Null when it cannot be read.
	/// </summary>
	public interface IDepartmentJurisdictionService
	{
		Task<string> GetJurisdictionAsync(int departmentId);
	}
}
