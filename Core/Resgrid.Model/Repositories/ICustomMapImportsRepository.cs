using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface ICustomMapImportsRepository : IRepository<CustomMapImport>
	{
		Task<IEnumerable<CustomMapImport>> GetImportsForMapAsync(string mapId);
		Task<IEnumerable<CustomMapImport>> GetPendingImportsAsync();
	}
}
