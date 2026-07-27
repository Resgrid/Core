using System.Collections.Generic;
using Resgrid.TrackerGateway.Hosting;

namespace Resgrid.TrackerGateway.Health
{
	public sealed class TrackingGatewayReadinessSnapshot
	{
		public TrackingGatewayReadinessSnapshot(
			int expectedListeners,
			int boundListeners,
			bool isReady,
			bool hasFailure)
		{
			ExpectedListeners = expectedListeners;
			BoundListeners = boundListeners;
			IsReady = isReady;
			HasFailure = hasFailure;
		}

		public int ExpectedListeners { get; }
		public int BoundListeners { get; }
		public bool IsReady { get; }
		public bool HasFailure { get; }
	}

	public sealed class TrackingGatewayReadinessState
	{
		private readonly object _syncRoot = new object();
		private readonly HashSet<string> _expectedListenerKeys =
			new HashSet<string>();
		private readonly HashSet<string> _boundListenerKeys =
			new HashSet<string>();
		private bool _hasFailure;
		private bool _stopping;

		public void Initialize(TrackingListenerPlan plan)
		{
			lock (_syncRoot)
			{
				_expectedListenerKeys.Clear();
				_boundListenerKeys.Clear();
				_hasFailure = false;
				_stopping = false;

				foreach (var listener in plan.Listeners)
					_expectedListenerKeys.Add(listener.Key);
			}
		}

		public void MarkBound(TrackingListenerDefinition definition)
		{
			lock (_syncRoot)
			{
				if (_expectedListenerKeys.Contains(definition.Key))
					_boundListenerKeys.Add(definition.Key);
			}
		}

		public void MarkStopped(TrackingListenerDefinition definition)
		{
			lock (_syncRoot)
			{
				_boundListenerKeys.Remove(definition.Key);
			}
		}

		public void MarkFailed()
		{
			lock (_syncRoot)
			{
				_hasFailure = true;
			}
		}

		public void MarkStopping()
		{
			lock (_syncRoot)
			{
				_stopping = true;
			}
		}

		public TrackingGatewayReadinessSnapshot GetSnapshot()
		{
			lock (_syncRoot)
			{
				var isReady = !_hasFailure &&
				              !_stopping &&
				              _boundListenerKeys.Count == _expectedListenerKeys.Count;
				return new TrackingGatewayReadinessSnapshot(
					_expectedListenerKeys.Count,
					_boundListenerKeys.Count,
					isReady,
					_hasFailure);
			}
		}
	}
}
