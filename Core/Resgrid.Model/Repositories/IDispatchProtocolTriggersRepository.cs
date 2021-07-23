using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDispatchProtocolTriggersRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DispatchProtocolTrigger}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DispatchProtocolTrigger}" />
	public interface IDispatchProtocolTriggersRepository: IRepository<DispatchProtocolTrigger>
	{
		Task<IEnumerable<DispatchProtocolTrigger>> GetDispatchProtocolTriggersByProtocolIdAsync(int protocolId);
	}
}
