using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IAutofillsService
	{
		Task<Autofill> SaveAutofillAsync(Autofill autofill, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<Autofill>> GetAllAutofillsForDepartmentAsync(int departmentId);
		Task<List<Autofill>> GetAllAutofillsForDepartmentByTypeAsync(int departmentId, AutofillTypes type);
		Task<Autofill> GetAutofillByIdAsync(string id);

		Task<bool> DeleteAutofillAsync(string id, CancellationToken cancellationToken = default(CancellationToken));
	}
}
