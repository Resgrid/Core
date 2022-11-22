using Resgrid.Model.Events;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IPersonnelLocationEventProvider
	{
		Task<bool> EnqueuePersonnelLocationEventAsync(PersonnelLocationEvent personnelLocationEvent);
	}
}
