using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IListener<in TMessage>
	{
		/// <summary>
		/// This will be called every time a TMessage is published through the event aggregator
		/// </summary>
		Task<bool> Handle(TMessage message);
	}

	/// <summary>
	/// Provides a way to add and remove a listener object from the EventAggregator
	/// </summary>
	public interface IEventSubscriptionManager
	{
		/// <summary>
		/// Adds the given listener object to the EventAggregator.
		/// </summary>
		/// <typeparam name="T">Listener Message type</typeparam>
		/// <param name="listener"></param>
		/// <returns>Returns the current IEventSubscriptionManager to allow for easy fluent additions.</returns>
		Guid AddListener<T>(Action<T> listener);

		/// <summary>
		/// Removes the listener object from the EventAggregator
		/// </summary>
		/// <param name="token">The object to be removed</param>
		/// <returns>Returnes the current IEventSubscriptionManager for fluent removals.</returns>
		void RemoveListener(Guid token);
	}

	public interface IEventPublisher
	{
		void SendMessage<TMessage>(TMessage message);
	}

	public interface IEventAggregator : IEventPublisher, IEventSubscriptionManager
	{
	}
}
