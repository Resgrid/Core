using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IFormAutomationsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.FormAutomation}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.FormAutomation}" />
	public interface IFormAutomationsRepository : IRepository<FormAutomation>
	{
		Task<IEnumerable<FormAutomation>> GetFormAutomationsByFormIdAsync(string formId);
	}
}
