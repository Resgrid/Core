using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;

namespace Resgrid.Model.Services
{
	public interface IChecklistTemplateService
	{
		/// <summary>Returns null when Checklists is unavailable for this department.</summary>
		Task<IReadOnlyList<ChecklistTemplate>> SearchAsync(int departmentId, string query = null);
		/// <summary>Returns null when the feature or template is unavailable.</summary>
		Task<ChecklistTemplate> GetByIdAsync(int departmentId, string templateId);
	}
}
