using Resgrid.Model.Events;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IUnitLocationEventProvider
	{
		Task<bool> EnqueueUnitLocationEventAsync(UnitLocationEvent unitLocationEvent);
	}
}
