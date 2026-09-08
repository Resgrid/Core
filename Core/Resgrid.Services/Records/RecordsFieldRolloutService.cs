using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Per-app Field Records rollout telemetry and its dashboard (RMS plan RMS-1D). A client reports coded
	/// outcomes; the server stamps them with the authenticated department and member and stores nothing else, so
	/// a rollout number can never carry record content. The dashboard is department-admin only and combines those
	/// events with what the Records themselves already say about which app created and finalized them.
	/// </summary>
	public class RecordsFieldRolloutService : IRecordsFieldRolloutService
	{
		/// <summary>How far back a dashboard may look. Rollout telemetry is operational, not an archive.</summary>
		public const int MaxWindowDays = 90;

		/// <summary>Most events one dashboard pass reads; a busier department reports on what fits rather than stalling.</summary>
		public const int MaxWindowEvents = 100000;

		private static readonly RmsOriginClient[] FieldApps = { RmsOriginClient.Responder, RmsOriginClient.Unit, RmsOriginClient.IncidentCommand, RmsOriginClient.Dispatch };

		private readonly IRmsFieldRolloutEventsRepository _events;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IFeatureToggleService _flags;

		public RecordsFieldRolloutService(IRmsFieldRolloutEventsRepository events, IRecordsAuthorizationService authorization, IRmsOperationalRecordsRepository records, IFeatureToggleService flags)
		{
			_events = events;
			_authorization = authorization;
			_records = records;
			_flags = flags;
		}

		#region Recording

		public async Task<int> RecordBatchAsync(int departmentId, string userId, RecordFieldRolloutBatch batch, CancellationToken cancellationToken = default)
		{
			if (batch == null || batch.Events == null || batch.Events.Count == 0)
				return 0;
			// A client that is not one of the four apps has no rollout to report; its events are dropped rather
			// than stored under an origin it does not have.
			if (!FieldRecordCatalogV1.IsFieldOrigin(batch.OriginClient))
				return 0;
			if (!await _authorization.IsActiveMemberAsync(userId, departmentId))
				return 0;

			var now = DateTime.UtcNow;
			var rows = new List<RmsFieldRolloutEvent>();
			foreach (var input in batch.Events.Take(RecordFieldRolloutBatch.MaxEvents))
			{
				if (input == null || !RmsFieldRolloutEventTypes.IsKnown(input.EventType))
					continue;
				var occurredOn = input.OccurredOn ?? now;
				// A clock that is wrong or hostile cannot move a row outside the window it is reported in.
				if (occurredOn > now.AddMinutes(5)) occurredOn = now;
				if (occurredOn < now.AddDays(-MaxWindowDays)) occurredOn = now.AddDays(-MaxWindowDays);

				rows.Add(new RmsFieldRolloutEvent
				{
					RmsFieldRolloutEventId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					OriginClient = (int)batch.OriginClient,
					AppVersion = Trim(batch.AppVersion, 32),
					ClientCapability = Trim(batch.ClientCapability, 32),
					EventType = input.EventType.Trim().ToLowerInvariant(),
					Outcome = Trim(input.Outcome, 48) ?? "ok",
					DefinitionKey = Trim(input.DefinitionKey, 64),
					DefinitionVersion = input.DefinitionVersion,
					RecordId = Trim(input.RecordId, 36),
					UserId = userId,
					DurationMs = input.DurationMs is > 0 and < 86_400_000 ? input.DurationMs : null,
					ItemCount = input.ItemCount is >= 0 and <= 1_000_000 ? input.ItemCount : null,
					OccurredOn = occurredOn,
					RecordedOn = now
				});
			}

			return rows.Count == 0 ? 0 : await _events.InsertBatchAsync(rows, cancellationToken);
		}

		public async Task RecordAsync(int departmentId, string userId, RmsOriginClient origin, string appVersion, string clientCapability, string eventType, string outcome, CancellationToken cancellationToken = default)
		{
			if (!FieldRecordCatalogV1.IsFieldOrigin(origin) || !RmsFieldRolloutEventTypes.IsKnown(eventType))
				return;
			var now = DateTime.UtcNow;
			await _events.InsertBatchAsync(new[]
			{
				new RmsFieldRolloutEvent
				{
					RmsFieldRolloutEventId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					OriginClient = (int)origin,
					AppVersion = Trim(appVersion, 32),
					ClientCapability = Trim(clientCapability, 32),
					EventType = eventType.Trim().ToLowerInvariant(),
					Outcome = Trim(outcome, 48) ?? "ok",
					UserId = userId,
					OccurredOn = now,
					RecordedOn = now
				}
			}, cancellationToken);
		}

		#endregion

		#region Dashboard

		public async Task<RecordsFieldRollout> GetAsync(int departmentId, string userId, int windowDays = 30, CancellationToken cancellationToken = default)
		{
			if (!await _authorization.IsDepartmentAdminAsync(userId, departmentId))
				throw new UnauthorizedAccessException("The Field Records rollout dashboard is department administration only.");

			windowDays = Math.Max(1, Math.Min(MaxWindowDays, windowDays));
			var since = DateTime.UtcNow.AddDays(-windowDays);
			var rollout = new RecordsFieldRollout { WindowDays = windowDays, WindowStart = since };

			foreach (var app in FieldApps)
			{
				var flag = FieldFlagFor(app);
				var enabled = false;
				try { enabled = await _flags.IsEnabledAsync(flag, departmentId); }
				catch (Exception ex) { Framework.Logging.LogException(ex, "Field Records rollout could not read " + flag + "."); }
				rollout.AppFlags[app.ToString()] = enabled;
				rollout.AnyAppEnabled |= enabled;
				rollout.MinimumAppVersions[app.ToString()] = MinimumVersionFor(app) ?? string.Empty;
			}

			// The window read is the large one: an abandoned dashboard should stop it rather than run it out.
			var events = (await _events.GetForWindowAsync(departmentId, since, MaxWindowEvents, cancellationToken))?.ToList() ?? new List<RmsFieldRolloutEvent>();
			// Records are the ground truth for adoption: an app that reports nothing still shows the work it did.
			var created = (await _records.GetCreatedSinceAsync(departmentId, since, MaxWindowEvents))?.ToList() ?? new List<RmsOperationalRecord>();
			var finalized = (await _records.GetFinalizedSinceAsync(departmentId, since))?.ToList() ?? new List<RmsOperationalRecord>();

			foreach (var app in FieldApps)
			{
				var appEvents = events.Where(e => e.OriginClient == (int)app).ToList();
				var summary = Summarize(app, appEvents, MinimumVersionFor(app));
				summary.RecordsCreated = created.Count(r => r.OriginClient == (int)app);
				summary.RecordsFinalized = finalized.Count(r => r.OriginClient == (int)app);
				rollout.Apps.Add(summary);
			}

			return rollout;
		}

		/// <summary>Aggregates one app's window. Every number here is a count, a distinct-user count or a median.</summary>
		public static RecordsFieldRolloutApp Summarize(RmsOriginClient app, List<RmsFieldRolloutEvent> events, string minimumVersion)
		{
			var summary = new RecordsFieldRolloutApp { OriginClient = app.ToString() };
			if (events == null || events.Count == 0)
				return summary;

			summary.ActiveUsers = events.Where(e => !string.IsNullOrWhiteSpace(e.UserId)).Select(e => e.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count();

			summary.Versions = events
				.Where(e => !string.IsNullOrWhiteSpace(e.AppVersion))
				.GroupBy(e => e.AppVersion, StringComparer.OrdinalIgnoreCase)
				.Select(group => new RecordsFieldRolloutVersion
				{
					AppVersion = group.Key,
					Users = group.Where(e => !string.IsNullOrWhiteSpace(e.UserId)).Select(e => e.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
					Events = group.Count()
				})
				.OrderByDescending(version => version.AppVersion, Comparer<string>.Create(FieldRecordCatalogV1.CompareVersions))
				.ThenByDescending(version => version.Users)
				.ToList();

			// Compatible adoption is per person, not per event: a member who reported an old version once and a
			// current one since is counted on the version they are actually running now.
			var latestByUser = events
				.Where(e => !string.IsNullOrWhiteSpace(e.UserId) && !string.IsNullOrWhiteSpace(e.AppVersion))
				.GroupBy(e => e.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(group => group.Key, group => group.OrderByDescending(e => e.OccurredOn).First().AppVersion, StringComparer.OrdinalIgnoreCase);
			summary.CompatibleUsers = latestByUser.Values.Count(version => FieldRecordCatalogV1.MeetsMinimum(version, minimumVersion));

			foreach (var entry in events)
			{
				var ok = IsOk(entry.Outcome);
				switch (entry.EventType)
				{
					case RmsFieldRolloutEventTypes.Catalog:
						summary.CatalogRequests++;
						if (!ok)
						{
							summary.CatalogFailures++;
							Bump(summary.CatalogFailureReasons, entry.Outcome);
						}
						break;
					case RmsFieldRolloutEventTypes.DraftStarted:
						summary.DraftsStarted++;
						break;
					case RmsFieldRolloutEventTypes.DraftSaved:
						if (ok) summary.DraftsSaved++;
						else summary.DraftSaveFailures++;
						break;
					case RmsFieldRolloutEventTypes.Sync:
						summary.Syncs++;
						if (!ok) summary.SyncFailures++;
						break;
					case RmsFieldRolloutEventTypes.Conflict:
						summary.Conflicts++;
						Bump(summary.ConflictKinds, entry.Outcome);
						break;
					case RmsFieldRolloutEventTypes.Attachment:
						if (ok) summary.AttachmentsUploaded++;
						else summary.AttachmentFailures++;
						break;
					case RmsFieldRolloutEventTypes.Completed:
						summary.Completed++;
						break;
					case RmsFieldRolloutEventTypes.Abandoned:
						summary.Abandoned++;
						break;
					case RmsFieldRolloutEventTypes.WebHandoff:
						summary.WebHandoffs++;
						break;
				}
			}

			var durations = events
				.Where(e => e.EventType == RmsFieldRolloutEventTypes.Completed && e.DurationMs.HasValue && e.DurationMs > 0)
				.Select(e => e.DurationMs.Value)
				.OrderBy(value => value)
				.ToList();
			if (durations.Count > 0)
				summary.MedianTimeToCompleteMs = durations.Count % 2 == 1 ? durations[durations.Count / 2] : (durations[durations.Count / 2 - 1] + durations[durations.Count / 2]) / 2;

			return summary;
		}

		#endregion

		#region Helpers

		private static bool IsOk(string outcome) => string.IsNullOrWhiteSpace(outcome) || outcome.Equals("ok", StringComparison.OrdinalIgnoreCase);

		private static void Bump(IDictionary<string, int> counts, string key)
		{
			var name = string.IsNullOrWhiteSpace(key) ? "unknown" : key.Trim();
			counts[name] = counts.TryGetValue(name, out var current) ? current + 1 : 1;
		}

		private static string Trim(string value, int max) => string.IsNullOrWhiteSpace(value) ? null : (value.Trim().Length > max ? value.Trim().Substring(0, max) : value.Trim());

		private static string FieldFlagFor(RmsOriginClient app)
		{
			switch (app)
			{
				case RmsOriginClient.Responder: return FeatureFlagKeys.RecordsFieldResponder;
				case RmsOriginClient.Unit: return FeatureFlagKeys.RecordsFieldUnit;
				case RmsOriginClient.IncidentCommand: return FeatureFlagKeys.RecordsFieldIncidentCommand;
				default: return FeatureFlagKeys.RecordsFieldDispatch;
			}
		}

		private static string MinimumVersionFor(RmsOriginClient app)
		{
			switch (app)
			{
				case RmsOriginClient.Responder: return RecordsFieldConfig.MinimumResponderVersion;
				case RmsOriginClient.Unit: return RecordsFieldConfig.MinimumUnitVersion;
				case RmsOriginClient.IncidentCommand: return RecordsFieldConfig.MinimumIncidentCommandVersion;
				default: return RecordsFieldConfig.MinimumDispatchVersion;
			}
		}

		#endregion
	}
}
