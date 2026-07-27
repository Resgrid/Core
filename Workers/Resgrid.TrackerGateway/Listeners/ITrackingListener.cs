using System.Threading;
using System.Threading.Tasks;
using Resgrid.TrackerGateway.Hosting;

namespace Resgrid.TrackerGateway.Listeners
{
	public interface ITrackingListener
	{
		TrackingListenerDefinition Definition { get; }
		bool IsBound { get; }
		Task Completion { get; }

		Task StartAsync(CancellationToken cancellationToken);
		Task StopAsync(CancellationToken cancellationToken);
	}
}
