using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services.AiDestination
{
	/// <summary>
	/// The CallCreated / CallUpdated triggers for Enhanced AI destination advice, in one place for every host and every save path (web,
	/// v4, imports, intake, workers): every path that saves a call raises <see cref="CallAddedEvent"/> or <see cref="CallUpdatedEvent"/>.
	/// An auto-activated singleton like <see cref="CoreEventService"/>: each event runs fire-and-forget in its own lifetime scope (its own
	/// unit of work), so the publisher is never held up and a failure never reaches it. A status-only update (same nature, note, type,
	/// priority and destination as the last one this process saw) queues nothing. A review that could not be queued is forgotten again,
	/// so the next save with the same content retries it.
	/// </summary>
	public sealed class AiDestinationReviewEventListener
	{
		private const int MaxRemembered = 5000;
		private static readonly TimeSpan RememberFor = TimeSpan.FromMinutes(30);

		private readonly ILifetimeScope _lifetimeScope;
		private readonly ConcurrentDictionary<int, (string Fingerprint, DateTime SeenOnUtc)> _lastSeen = new ConcurrentDictionary<int, (string, DateTime)>();

		public AiDestinationReviewEventListener(IEventAggregator eventAggregator, ILifetimeScope lifetimeScope)
		{
			_lifetimeScope = lifetimeScope;

			eventAggregator.AddListener<CallAddedEvent>(message => _ = OnCallAsync(message?.Call, AiDestinationReviewTriggers.CallCreated));
			eventAggregator.AddListener<CallUpdatedEvent>(message => _ = OnCallAsync(message?.Call, AiDestinationReviewTriggers.CallUpdated));
		}

		/// <summary>True when the call changed in a way the advice reads (or this process has not seen it lately); remembers it.</summary>
		public bool HasRelevantChange(Call call, DateTime nowUtc) => TryRemember(call, nowUtc, out _);

		private bool TryRemember(Call call, DateTime nowUtc, out (string Fingerprint, DateTime SeenOnUtc) remembered)
		{
			var fingerprint = Fingerprint(call);
			if (_lastSeen.TryGetValue(call.CallId, out var seen) && seen.Fingerprint == fingerprint && nowUtc - seen.SeenOnUtc < RememberFor)
			{
				remembered = default;
				return false;
			}

			if (_lastSeen.Count >= MaxRemembered)
				_lastSeen.Clear();

			remembered = (fingerprint, nowUtc);
			_lastSeen[call.CallId] = remembered;
			return true;
		}

		/// <summary>
		/// What the advice reads from the call row. The call's state is left out: a state change of an open call (pending to active) changes
		/// nothing the model sees, and a closed call is never reviewed.
		/// </summary>
		public static string Fingerprint(Call call)
		{
			var text = string.Join("\u001f", call.Priority, call.CallTypeId, call.Type, call.NatureOfCall, call.Notes, call.DestinationPoiId,
				call.DestinationAddress, call.DestinationGeoLocationData, call.GeoLocationData);
			return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
		}

		/// <summary>Handles one call event (public for tests; the aggregator calls it fire-and-forget). Never throws.</summary>
		public async Task OnCallAsync(Call call, AiDestinationReviewTriggers trigger)
		{
			(string Fingerprint, DateTime SeenOnUtc) remembered = default;
			var settled = false;
			try
			{
				if (!AiDestinationConfig.Enabled || call == null || call.CallId <= 0 || call.DepartmentId <= 0 || call.IsDeleted)
					return;
				if (call.State != (int)CallStates.Active && call.State != (int)CallStates.Pending)
					return;
				if (!TryRemember(call, DateTime.UtcNow, out remembered))
					return;

				using var scope = _lifetimeScope.BeginLifetimeScope();
				settled = await scope.Resolve<IAiDestinationAdvisorService>().RequestReviewAsync(call.DepartmentId, call.CallId, trigger);
			}
			catch (Exception ex)
			{
				// Nothing awaits this, so an escaping exception would go unobserved.
				Framework.Logging.LogException(ex, $"AI destination review trigger failed for call {call?.CallId}.");
			}

			// Not settled (the queue write failed): forget this content (unless a newer save already replaced it) so the next save asks again.
			if (!settled && remembered.Fingerprint != null)
				_lastSeen.TryRemove(new KeyValuePair<int, (string, DateTime)>(call.CallId, remembered));
		}
	}
}
