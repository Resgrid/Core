using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records.Evidence
{
	/// <summary>
	/// The six evidence adapters RMS-3 ships, none optional (RMS plan section 4.5).
	/// <para>
	/// They live together because the interesting thing about them is that they are consistent: each reads one
	/// subsystem, bounds what it takes, records where it came from, and hands back a manifest.
	/// <see cref="RecordsEvidenceService"/> owns everything after that — serialization, checksum, the restricted
	/// grant check, retention and audit — so no adapter can be quietly weaker than its siblings.
	/// </para>
	/// <para>
	/// None of them hydrates a live source. That is the constraint the plan puts on this whole area: RMS captures
	/// an authorized snapshot with provenance, and never becomes a second copy of Chat, Tracking or Inventory.
	/// </para>
	/// </summary>
	internal static class EvidenceLimits
	{
		/// <summary>Widest coverage window any time-series adapter will honour.</summary>
		public static readonly TimeSpan MaxCoverage = TimeSpan.FromHours(24);

		/// <summary>Most source rows a single artifact may carry; a manifest is evidence, not an export.</summary>
		public const int MaxItems = 500;

		// Source IDs remain in the manifest. This bounded identity distinguishes independently selected
		// evidence; a later page/window must not supersede a different selection from the same subsystem.
		public static string SelectionIdentity(object selection) => "selection-sha256:" +
			RecordSnapshotSerializer.Checksum(RecordsEvidenceService.Serialize(selection));

		public static (DateTime start, DateTime end) RequireWindow(DateTime? start, DateTime? end)
		{
			var to = end ?? DateTime.UtcNow;
			var from = start ?? to - MaxCoverage;
			if (to < from || to - from > MaxCoverage)
				throw new ArgumentException("Choose a tracking window in chronological order, at most 24 hours. Capture additional windows separately.");
			return (from, to);
		}
	}

	/// <summary>
	/// Apparatus and equipment readiness at the time of the call — a checklist/work-order manifest with source
	/// completion IDs, coverage period and checksum (plan section 4.5).
	/// <para>
	/// The checklists and maintenance module this reads from is planned but not built. The adapter ships anyway,
	/// because the plan requires all six and because "unavailable" and "there was no readiness evidence" are
	/// different answers: an author who sees the source reported as absent knows not to draw a conclusion from
	/// the empty list. When the module lands, only <see cref="CaptureAsync"/> changes.
	/// </para>
	/// </summary>
	public class ReadinessPacketEvidenceAdapter : IRecordEvidenceAdapter
	{
		public const string SourceSubsystem = "Checklists";
		public const string UnavailableReason = "The checklists and maintenance module is not present in this build.";

		public RmsEvidenceKind Kind => RmsEvidenceKind.ReadinessPacket;

		public Task<bool> IsAvailableAsync(int departmentId) => Task.FromResult(false);

		public Task<RecordEvidenceCapture> CaptureAsync(RecordEvidenceCaptureRequest request, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(RecordEvidenceCapture.Unavailable(UnavailableReason));
		}
	}

	/// <summary>
	/// The recorded dispatch decision for a Call: card and version, alarm level, mode, the resource summary the
	/// card produced, and any shortfall (plan section 4.5).
	/// <para>
	/// Only what Run Cards actually audited is captured. The plan is explicit that RMS does not infer acceptance
	/// where Run Cards did not record it, so nothing here reconstructs whether a recommendation was followed.
	/// </para>
	/// </summary>
	public class RunCardActivationEvidenceAdapter : IRecordEvidenceAdapter
	{
		public const string SourceSubsystem = "RunCards";
		public const string IdentifierScheme = "resgrid:runcardactivation";

		private readonly IRunCardActivationsRepository _activations;

		public RunCardActivationEvidenceAdapter(IRunCardActivationsRepository activations)
		{
			_activations = activations;
		}

		public RmsEvidenceKind Kind => RmsEvidenceKind.RunCardActivation;

		public Task<bool> IsAvailableAsync(int departmentId) => Task.FromResult(true);

		public async Task<RecordEvidenceCapture> CaptureAsync(RecordEvidenceCaptureRequest request, CancellationToken cancellationToken = default)
		{
			if (!request.CallId.HasValue || request.CallId.Value <= 0)
				return RecordEvidenceCapture.Unavailable("Run card evidence needs the Call the record hangs off.");

			var activations = (await _activations.GetActivationsByCallIdAsync(request.CallId.Value))?
				.Where(a => a != null && a.DepartmentId == request.DepartmentId && a.CallId == request.CallId)
				.OrderBy(a => a.CreatedOn)
				.Take(EvidenceLimits.MaxItems + 1)
				.ToList() ?? new List<RunCardActivation>();
			if (activations.Count > EvidenceLimits.MaxItems) throw new ArgumentException("The Call has too many activations for one evidence capture.");

			if (activations.Count == 0)
				return RecordEvidenceCapture.Unavailable("No run card activation was recorded for this call.");

			var items = activations.Select(a => new
			{
				activation_id = a.RunCardActivationId,
				run_card_id = a.RunCardId,
				alarm_level = a.AlarmLevel,
				mode = a.ModeUsed,
				auto_dispatched = a.WasAutoDispatched,
				activated_on = a.CreatedOn,
				activated_by_user_id = a.CreatedByUserId,
				// The card's own recorded outcome, verbatim: what it selected and any shortfall it reported.
				result = ParseRecordedResult(a.ResultJson)
			}).ToList();

			return new RecordEvidenceCapture
			{
				Title = $"Run card activation for call {request.CallId.Value}",
				SourceSubsystem = SourceSubsystem,
				SourceEntityType = nameof(RunCardActivation),
				SourceEntityId = EvidenceLimits.SelectionIdentity(activations.Select(a => a.RunCardActivationId).OrderBy(x => x).ToArray()),
				IdentifierScheme = IdentifierScheme,
				CoverageStart = activations.First().CreatedOn,
				CoverageEnd = activations.Last().CreatedOn,
				SourceItemCount = items.Count,
				Classification = RmsEvidenceClassification.Unrestricted,
				Manifest = new { call_id = request.CallId.Value, activations = items }
			};
		}

		private static JToken ParseRecordedResult(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;

			try { return JToken.Parse(json); }
			catch (JsonReaderException ex) { throw new InvalidOperationException("A recorded Run Card decision is unreadable; repair the source before capturing evidence.", ex); }
		}
	}

	/// <summary>
	/// Bounded unit tracking evidence (plan section 4.5): stable fix ID, fix time, accuracy and the derived
	/// staleness at capture.
	/// <para>
	/// Full tracks are never copied. The window is clamped and the fixes are <em>sampled</em> across it — the
	/// point of this evidence is to show where a unit was at the moments that matter, captured before source
	/// retention purges them, not to mirror the tracking store into the Records tables.
	/// </para>
	/// </summary>
	public class TrackingFixEvidenceAdapter : IRecordEvidenceAdapter
	{
		public const string SourceSubsystem = "UnitTracking";
		public const string IdentifierScheme = "resgrid:unitlocation";

		/// <summary>Sample points per unit across the coverage window; the bound that keeps this evidence, not an export.</summary>
		public const int SamplesPerUnit = 24;

		private readonly IUnitLocationRepository _locations;
		private readonly IUnitsService _units;
		private readonly Lazy<IAuthorizationService> _authorization;

		public TrackingFixEvidenceAdapter(IUnitLocationRepository locations, IUnitsService units, Lazy<IAuthorizationService> authorization)
		{
			_locations = locations;
			_units = units; _authorization = authorization;
		}

		public RmsEvidenceKind Kind => RmsEvidenceKind.TrackingFix;

		public Task<bool> IsAvailableAsync(int departmentId) => Task.FromResult(true);

		public async Task<RecordEvidenceCapture> CaptureAsync(RecordEvidenceCaptureRequest request, CancellationToken cancellationToken = default)
		{
			var units = (request.UnitIds ?? new List<int>()).Where(u => u > 0).Distinct().ToList();
			if (units.Count > EvidenceLimits.MaxItems / SamplesPerUnit) throw new ArgumentException("Select at most " + EvidenceLimits.MaxItems / SamplesPerUnit + " units per tracking capture.");
			if (units.Count == 0)
				return RecordEvidenceCapture.Unavailable("Tracking evidence needs at least one unit.");

			var (from, to) = EvidenceLimits.RequireWindow(request.CoverageStart, request.CoverageEnd);
			var capturedOn = DateTime.UtcNow;
			var step = (to - from).TotalSeconds / Math.Max(1, SamplesPerUnit - 1);
			var manifestUnits = new List<object>();
			var total = 0;

			foreach (var unitId in units)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var unit = await _units.GetUnitByIdAsync(unitId);
				if (unit?.DepartmentId != request.DepartmentId || !await _authorization.Value.CanUserViewUnitLocationAsync(request.CapturedByUserId, unitId, request.DepartmentId)) throw new UnauthorizedAccessException("Tracking source access is not authorized.");
				var seen = new HashSet<int>();
				var fixes = new List<object>();

				for (var i = 0; i < SamplesPerUnit && total < EvidenceLimits.MaxItems; i++)
				{
					var at = from.AddSeconds(step * i);
					var location = await _locations.GetLastUnitLocationByUnitIdTimestampAsync(unitId, at);

					// Nothing before the sample point, or the same fix the previous sample already returned:
					// the unit had not moved, and repeating the row would inflate the manifest without adding fact.
					if (location == null || location.UnitId != unitId || location.Timestamp < from || location.Timestamp > at || !seen.Add(location.UnitLocationId))
						continue;

					fixes.Add(new
					{
						fix_id = location.UnitLocationId,
						fix_on = location.Timestamp,
						latitude = location.Latitude,
						longitude = location.Longitude,
						accuracy = location.Accuracy,
						altitude = location.Altitude,
						speed = location.Speed,
						heading = location.Heading,
						// Staleness is computed once, at capture: how old the fix already was when it was taken as
						// evidence. Recomputing it later against "now" would make old evidence look worse each year.
						staleness_seconds = (int)Math.Max(0, (capturedOn - location.Timestamp).TotalSeconds),
						timestamp_source = "Device"
					});
					total++;
				}

				if (!await _authorization.Value.CanUserViewUnitLocationAsync(request.CapturedByUserId, unitId, request.DepartmentId)) throw new UnauthorizedAccessException();
				if (fixes.Count > 0)
					manifestUnits.Add(new { unit_id = unitId, fixes });
			}

			if (total == 0)
				return RecordEvidenceCapture.Unavailable("No tracking fixes were recorded for those units in that window.");

			return new RecordEvidenceCapture
			{
				Title = $"Tracking fixes for {units.Count} unit(s)",
				SourceSubsystem = SourceSubsystem,
				SourceEntityType = nameof(UnitLocation),
				SourceEntityId = EvidenceLimits.SelectionIdentity(new { units = units.OrderBy(x => x).ToArray(), from, to }),
				IdentifierScheme = IdentifierScheme,
				CoverageStart = from,
				CoverageEnd = to,
				SourceItemCount = total,
				Classification = RmsEvidenceClassification.Unrestricted,
				Manifest = new { captured_on = capturedOn, sampled = true, samples_per_unit = SamplesPerUnit, units = manifestUnits }
			};
		}
	}

	/// <summary>
	/// Authorized promotion of selected incident-chat messages into the record (plan section 4.5): message,
	/// channel and call IDs, sequence, author and time, edit/moderation state, checksum and capture reason.
	/// <para>
	/// Only the messages the member explicitly selected are promoted, and only from a channel bound to the
	/// record's Call. RMS does not hydrate or retain an entire chat channel, so there is no "promote the channel"
	/// path here — that would turn a record attachment into a second chat store.
	/// </para>
	/// <para>
	/// Chat is operational conversation about a live incident, so the result is classified restricted: promoting
	/// it into a record that a wider audience can read must not widen who can read the conversation.
	/// </para>
	/// </summary>
	public class ChatPromotionEvidenceAdapter : IRecordEvidenceAdapter
	{
		public const string SourceSubsystem = "Chat";
		public const string IdentifierScheme = "resgrid:chatmessage";

		private readonly IChatMessageRepository _messages;
		private readonly IChatChannelRepository _channels;
		private readonly Lazy<IChatPermissionService> _permissions;

		public ChatPromotionEvidenceAdapter(IChatMessageRepository messages, IChatChannelRepository channels, Lazy<IChatPermissionService> permissions)
		{
			_messages = messages;
			_channels = channels;
			_permissions = permissions;
		}

		public RmsEvidenceKind Kind => RmsEvidenceKind.ChatPromotion;

		public Task<bool> IsAvailableAsync(int departmentId) => Task.FromResult(true);

		public async Task<RecordEvidenceCapture> CaptureAsync(RecordEvidenceCaptureRequest request, CancellationToken cancellationToken = default)
		{
			var ids = (request.SourceIds ?? new List<string>()).Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal).ToList();
			if (ids.Count > EvidenceLimits.MaxItems) throw new ArgumentException("Select at most " + EvidenceLimits.MaxItems + " messages per capture.");
			if (ids.Count == 0)
				return RecordEvidenceCapture.Unavailable("Chat evidence needs the messages the member selected.");

			// Channels bound to this record's Call. A message from anywhere else is not incident chat, and
			// promoting it would pull an unrelated conversation into an official record.
			var allowedChannels = new HashSet<string>(StringComparer.Ordinal);
			if (request.CallId.HasValue && request.CallId.Value > 0)
			{
				foreach (var channel in (await _channels.GetByCallIdAsync(request.CallId.Value))?.Where(c => c != null && c.DepartmentId == request.DepartmentId && c.CallId == request.CallId) ?? Enumerable.Empty<ChatChannel>())
					if (await _permissions.Value.CanAccessChannelAsync(channel, request.CapturedByUserId, null)) allowedChannels.Add(channel.ChatChannelId);
			}

			if (allowedChannels.Count == 0)
				return RecordEvidenceCapture.Unavailable("No incident chat channel is bound to this call.");

			var promoted = new List<object>();
			var channelIds = new HashSet<string>(StringComparer.Ordinal);
			DateTime? first = null, last = null;

			foreach (var id in ids)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var message = await _messages.GetByIdAsync(id) as ChatMessage;
				if (message == null || message.DepartmentId != request.DepartmentId || !allowedChannels.Contains(message.ChatChannelId))
					throw new UnauthorizedAccessException("A selected message is outside the authorized incident channel.");
				if (message.DeletedOn.HasValue || message.IsModerated) throw new UnauthorizedAccessException("Deleted or moderated messages cannot be promoted through ordinary channel access.");

				channelIds.Add(message.ChatChannelId);
				if (first == null || message.SentOn < first) first = message.SentOn;
				if (last == null || message.SentOn > last) last = message.SentOn;

				promoted.Add(new
				{
					message_id = message.ChatMessageId,
					channel_id = message.ChatChannelId,
					sequence = message.MessageSeq,
					sender_user_id = message.SenderUserId,
					sender_unit_id = message.SenderUnitId,
					sender_display_name = message.SenderDisplayName,
					sent_on = message.SentOn,
					// Edit and moderation state travel with the message: evidence that was edited after the fact
					// must say so, or the artifact overstates what was said at the time.
					edited_on = message.EditedOn,
					body = message.Body,
					message_type = message.MessageType,
					priority = message.Priority,
					thread_root_message_id = message.ThreadRootMessageId
				});
			}

			if (promoted.Count == 0)
				return RecordEvidenceCapture.Unavailable("None of the selected messages belong to this call's chat.");
			var currentChannels = (await _channels.GetByCallIdAsync(request.CallId.Value) ?? Enumerable.Empty<ChatChannel>()).Where(c => c != null && channelIds.Contains(c.ChatChannelId)).ToList();
			if (currentChannels.Count != channelIds.Count) throw new UnauthorizedAccessException();
			foreach (var channel in currentChannels)
				if (channel.DepartmentId != request.DepartmentId || channel.CallId != request.CallId || !await _permissions.Value.CanAccessChannelAsync(channel, request.CapturedByUserId, null)) throw new UnauthorizedAccessException();

			return new RecordEvidenceCapture
			{
				Title = $"{promoted.Count} promoted chat message(s)",
				SourceSubsystem = SourceSubsystem,
				SourceEntityType = nameof(ChatMessage),
				SourceEntityId = EvidenceLimits.SelectionIdentity(ids.OrderBy(x => x, StringComparer.Ordinal).ToArray()),
				IdentifierScheme = IdentifierScheme,
				CoverageStart = first,
				CoverageEnd = last,
				SourceItemCount = promoted.Count,
				Classification = RmsEvidenceClassification.Restricted,
				Manifest = new { call_id = request.CallId, channel_ids = channelIds.OrderBy(x => x, StringComparer.Ordinal).ToList(), messages = promoted }
			};
		}
	}

	/// <summary>
	/// Supplies and controlled-substance usage from the inventory ledger (plan section 4.5). RMS references the
	/// ledger rather than creating another stock system, so this reads the RMS-1 usage adapter and freezes what
	/// it returned into a manifest.
	/// <para>
	/// Controlled substances are the reason this classifies restricted: quantities drawn on a call are
	/// accountable data, and an artifact makes them readable long after the ledger row has moved on.
	/// </para>
	/// </summary>
	public class InventoryUsageEvidenceAdapter : IRecordEvidenceAdapter
	{
		public const string SourceSubsystem = "Inventory";
		public const string IdentifierScheme = "resgrid:inventory";

		private readonly IRmsInventoryUsageAdapter _usage;
		private readonly IRecordsAuthorizationService _authorization;

		public InventoryUsageEvidenceAdapter(IRmsInventoryUsageAdapter usage, IRecordsAuthorizationService authorization)
		{
			_usage = usage;
			_authorization = authorization;
		}

		public RmsEvidenceKind Kind => RmsEvidenceKind.InventoryUsage;

		public Task<bool> IsAvailableAsync(int departmentId) => Task.FromResult(true);

		public async Task<RecordEvidenceCapture> CaptureAsync(RecordEvidenceCaptureRequest request, CancellationToken cancellationToken = default)
		{
			if (!await _authorization.CanUseSourceInventoryAsync(request.CapturedByUserId, request.DepartmentId, null)) throw new UnauthorizedAccessException();
			var usage = (await _usage.GetUsageForRecordAsync(request.DepartmentId, request.RecordId))?
				.Take(EvidenceLimits.MaxItems + 1).ToList() ?? new List<RmsInventoryUsage>();
			if (usage.Count > EvidenceLimits.MaxItems) throw new ArgumentException("The record has too many inventory entries for one evidence capture.");

			if (usage.Count == 0)
				return RecordEvidenceCapture.Unavailable("No inventory usage is recorded against this record.");

			var items = usage.Select(u => new
			{
				reference_id = u.ReferenceId,
				reference_checksum = u.ReferenceChecksum,
				source = u.Source,
				inventory_id = u.InventoryId,
				item_name = u.ItemName,
				unit_of_measure = u.UnitOfMeasure,
				source_checksum = u.SourceChecksum,
				quantity = u.Quantity,
				note = u.Note,
				captured_by_user_id = u.CapturedByUserId,
				captured_on = u.CapturedOn
			}).ToList();

			return new RecordEvidenceCapture
			{
				Title = $"{items.Count} inventory usage entr{(items.Count == 1 ? "y" : "ies")}",
				SourceSubsystem = SourceSubsystem,
				SourceEntityType = nameof(RmsInventoryUsage),
				SourceEntityId = request.RecordId,
				IdentifierScheme = IdentifierScheme,
				CoverageStart = usage.Min(u => u.CapturedOn),
				CoverageEnd = usage.Max(u => u.CapturedOn),
				SourceItemCount = items.Count,
				Classification = RmsEvidenceClassification.Restricted,
				Manifest = new { record_id = request.RecordId, usage = items }
			};
		}
	}

	/// <summary>
	/// Participant certification and qualification validity at the incident time (plan section 4.5).
	/// <para>
	/// The plan draws a hard line here: a participant snapshot may carry the certification's type, status and
	/// validity, while <em>license numbers and source documents stay protected and Certification-owned</em>. So
	/// this manifest records what was valid and when it expired, and never the number on the card or the scanned
	/// document behind it.
	/// </para>
	/// </summary>
	public class CertificationSnapshotEvidenceAdapter : IRecordEvidenceAdapter
	{
		public const string SourceSubsystem = "Certifications";
		public const string IdentifierScheme = "resgrid:personnelcertification";

		private readonly ICertificationService _certifications;
		private readonly IRmsRecordParticipantsRepository _participants;
		private readonly Lazy<IAuthorizationService> _authorization;

		public CertificationSnapshotEvidenceAdapter(ICertificationService certifications, IRmsRecordParticipantsRepository participants, Lazy<IAuthorizationService> authorization)
		{
			_certifications = certifications;
			_participants = participants;
			_authorization = authorization;
		}

		public RmsEvidenceKind Kind => RmsEvidenceKind.CertificationSnapshot;

		public Task<bool> IsAvailableAsync(int departmentId) => Task.FromResult(true);

		public async Task<RecordEvidenceCapture> CaptureAsync(RecordEvidenceCaptureRequest request, CancellationToken cancellationToken = default)
		{
			var userIds = (request.UserIds ?? new List<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			// Default to the record's own participants: the people the filing already says were there.
			if (userIds.Count == 0 && request.RecordKind == RmsRecordKind.Operational)
			{
				var participants = await _participants.GetForRecordAsync(request.DepartmentId, request.RecordId, null);
				userIds = (participants ?? Enumerable.Empty<RmsRecordParticipant>())
					.Where(p => !string.IsNullOrWhiteSpace(p.UserId))
					.Select(p => p.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			}

			if (userIds.Count == 0)
				return RecordEvidenceCapture.Unavailable("Certification evidence needs at least one participant.");
			if (userIds.Count > EvidenceLimits.MaxItems) throw new ArgumentException("Select fewer participants for this capture.");

			// Validity is asserted as at the incident time, not as at capture: a certificate that expired last
			// month was still valid on the night, and the record has to be able to say so.
			var asOf = request.CoverageEnd ?? request.CoverageStart ?? DateTime.UtcNow;
			var people = new List<object>();
			var total = 0;

			foreach (var userId in userIds)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!await _authorization.Value.CanUserViewPersonAsync(request.CapturedByUserId, userId, request.DepartmentId)) throw new UnauthorizedAccessException("Personnel source access is not authorized.");
				var certifications = (await _certifications.GetCertificationsByUserIdAsync(userId))?
					.Where(c => c != null && c.DepartmentId == request.DepartmentId && c.UserId == userId)
					.Take(EvidenceLimits.MaxItems - total + 1).ToList() ?? new List<PersonnelCertification>();
				if (total + certifications.Count > EvidenceLimits.MaxItems) throw new ArgumentException("The selection has too many certifications for one capture.");

				if (!await _authorization.Value.CanUserViewPersonAsync(request.CapturedByUserId, userId, request.DepartmentId)) throw new UnauthorizedAccessException();
				if (certifications.Count == 0)
					continue;

				people.Add(new
				{
					user_id = userId,
					certifications = certifications.Select(c => new
					{
						// Type, status and validity only. Number, file name and document bytes stay with
						// Certifications, which owns them and their protection.
						source_id = c.PersonnelCertificationId,
						type = c.Type,
						name = c.Name,
						area = c.Area,
						issued_by = c.IssuedBy,
						received_on = c.RecievedOn,
						expires_on = c.ExpiresOn,
						valid_at_incident = (!c.RecievedOn.HasValue || c.RecievedOn.Value <= asOf) && (!c.ExpiresOn.HasValue || c.ExpiresOn.Value >= asOf)
					}).ToList()
				});
				total += certifications.Count;
			}

			if (total == 0)
				return RecordEvidenceCapture.Unavailable("None of those participants hold recorded certifications.");

			return new RecordEvidenceCapture
			{
				Title = $"Certification snapshot for {people.Count} member(s)",
				SourceSubsystem = SourceSubsystem,
				SourceEntityType = nameof(PersonnelCertification),
				SourceEntityId = EvidenceLimits.SelectionIdentity(new { users = userIds.Select(x => x.ToUpperInvariant()).OrderBy(x => x, StringComparer.Ordinal).ToArray(), asOf }),
				IdentifierScheme = IdentifierScheme,
				CoverageStart = asOf,
				CoverageEnd = asOf,
				SourceItemCount = total,
				Classification = RmsEvidenceClassification.Restricted,
				Manifest = new { as_of = asOf, people }
			};
		}
	}
}
