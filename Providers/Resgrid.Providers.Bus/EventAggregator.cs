using System;
using Easy.MessageHub;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Bus
{
	public class EventAggregator: IEventAggregator
	{
		private readonly IMessageHub _hub;

		public EventAggregator()
		{
			_hub = new MessageHub();
		}

		public void SendMessage<TMessage>(TMessage message)
		{
			_hub.Publish(message);
		}

		public Guid AddListener<T>(Action<T> listener)
		{
			return _hub.Subscribe<T>(listener);
		}

		public void RemoveListener(Guid token)
		{
			if (_hub.IsSubscribed(token))
				_hub.Unsubscribe(token);
		}
	}
}
