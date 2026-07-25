using System;
using Easy.MessageHub;
using Resgrid.Model.Providers;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace Resgrid.Providers.Bus
{
	public class EventAggregator: IEventAggregator
	{
		private readonly IMessageHub _hub;
		private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Guid, Func<object, Task>>> _asyncListeners;

		public EventAggregator()
		{
			_hub = new MessageHub();
			_asyncListeners = new ConcurrentDictionary<Type, ConcurrentDictionary<Guid, Func<object, Task>>>();
		}

		public void SendMessage<TMessage>(TMessage message)
		{
			_hub.Publish(message);
		}

		public async Task SendMessageAsync<TMessage>(TMessage message)
		{
			if (!_asyncListeners.TryGetValue(typeof(TMessage), out var listeners))
				return;

			await Task.WhenAll(listeners.Values.Select(listener => listener(message)));
		}

		public Guid AddListener<T>(Action<T> listener)
		{
			return _hub.Subscribe<T>(listener);
		}

		public Guid AddAsyncListener<T>(Func<T, Task> listener, Action<Exception> onError = null)
		{
			if (listener == null)
				throw new ArgumentNullException(nameof(listener));

			var token = Guid.NewGuid();
			var listeners = _asyncListeners.GetOrAdd(
				typeof(T),
				_ => new ConcurrentDictionary<Guid, Func<object, Task>>());
			listeners[token] = async message =>
			{
				try
				{
					await listener((T)message);
				}
				catch (Exception ex)
				{
					if (onError == null)
						throw;

					onError(ex);
				}
			};

			return token;
		}

		public void RemoveListener(Guid token)
		{
			foreach (var listeners in _asyncListeners.Values)
			{
				if (listeners.TryRemove(token, out _))
					return;
			}

			if (_hub.IsSubscribed(token))
				_hub.Unsubscribe(token);
		}
	}
}
