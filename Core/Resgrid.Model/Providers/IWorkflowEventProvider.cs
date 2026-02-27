namespace Resgrid.Model.Providers
{
	/// <summary>
	/// Subscribes to domain events and enqueues workflow runs for active workflows
	/// whose trigger matches the event type.
	/// </summary>
	public interface IWorkflowEventProvider
	{
		// Marker interface – initialization occurs in the constructor via event aggregator subscriptions.
	}
}

