using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// CAD email call updates (cad-email-call-updates-plan.md). An inbound dispatch email carrying the CAD call ID of a call the
	/// department already has updates that call (per-field policies, dispatcher edits kept, late emails noted), closes or cancels
	/// it, or is noted on a recently closed one, instead of creating a second call. Emails with no ID, or with CAD updates off,
	/// create a call exactly as before through the caller's delegate. Every email that carried an ID leaves a ledger row
	/// (CallEmailUpdates): hashes of what the CAD set, never content. The row is written before the email changes or notes anything and
	/// moves on as each step is done (Applying, Saved once the call is, FollowedUp once the page is recorded or the resources are
	/// released, then the outcome; Noting before a note-only outcome's note), so a redelivery of the same email — recognised by its
	/// provider message id — does nothing once it finished, finishes the steps an interruption left, and never takes a value the call
	/// merely shows as proof the CAD wrote it.
	/// </summary>
	public class EmailCallUpdateService : IEmailCallUpdateService
	{
		public const string GroupLocation = "Location";
		public const string GroupPriority = "Priority";
		public const string GroupType = "Type";
		public const string GroupName = "Name";
		public const string GroupNature = "Nature";
		public const string GroupNotes = "Notes";
		public const string GroupResources = "Resources";

		/// <summary>The ledger's hash of the map pin (stored with the field groups; changes to it are reported under Location).</summary>
		private const string HashCoordinates = "Coordinates";

		/// <summary>What a close's row records in ChangedFields: the kind, and Live when the call was active (its release and notice).</summary>
		private const string StepClose = "Close";
		private const string StepCancel = "Cancel";
		private const string StepLive = "Live";

		/// <summary>CallEmailUpdates.MessageId's length (M0330).</summary>
		private const int MessageIdLength = 500;

		public const string ReasonPolicy = "Policy";
		public const string ReasonEdited = "EditedInResgrid";
		public const string ReasonProtected = "Protected";

		/// <summary>Close emails also find calls closed this long ago (any later email for them only leaves a ledger row).</summary>
		private static readonly TimeSpan CloseLookback = TimeSpan.FromDays(30);
		private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);
		private const int NoteTextLimit = 500;

		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ICallEmailFactory _callEmailFactory;
		private readonly ICallsService _callsService;
		private readonly ICallEmailUpdatesRepository _callEmailUpdatesRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly ICallBroadcastOutboxService _callBroadcastOutboxService;
		private readonly ICallClosureService _callClosureService;
		private readonly ICallDispatchStatusService _callDispatchStatusService;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly ICallDispatchesRepository _callDispatchesRepository;
		private readonly ICallDispatchGroupRepository _callDispatchGroupRepository;
		private readonly ICallDispatchUnitRepository _callDispatchUnitRepository;
		private readonly ICallDispatchRoleRepository _callDispatchRoleRepository;
		private readonly IEventAggregator _eventAggregator;

		public EmailCallUpdateService(IDepartmentSettingsService departmentSettingsService, ICallEmailFactory callEmailFactory, ICallsService callsService,
			ICallEmailUpdatesRepository callEmailUpdatesRepository, IDepartmentsService departmentsService, IGeoLocationProvider geoLocationProvider,
			ICallBroadcastOutboxService callBroadcastOutboxService, ICallClosureService callClosureService, ICallDispatchStatusService callDispatchStatusService,
			IUnitsService unitsService, IDepartmentGroupsService departmentGroupsService, ICallDispatchesRepository callDispatchesRepository,
			ICallDispatchGroupRepository callDispatchGroupRepository, ICallDispatchUnitRepository callDispatchUnitRepository,
			ICallDispatchRoleRepository callDispatchRoleRepository, IEventAggregator eventAggregator)
		{
			_departmentSettingsService = departmentSettingsService;
			_callEmailFactory = callEmailFactory;
			_callsService = callsService;
			_callEmailUpdatesRepository = callEmailUpdatesRepository;
			_departmentsService = departmentsService;
			_geoLocationProvider = geoLocationProvider;
			_callBroadcastOutboxService = callBroadcastOutboxService;
			_callClosureService = callClosureService;
			_callDispatchStatusService = callDispatchStatusService;
			_unitsService = unitsService;
			_departmentGroupsService = departmentGroupsService;
			_callDispatchesRepository = callDispatchesRepository;
			_callDispatchGroupRepository = callDispatchGroupRepository;
			_callDispatchUnitRepository = callDispatchUnitRepository;
			_callDispatchRoleRepository = callDispatchRoleRepository;
			_eventAggregator = eventAggregator;
		}

		public async Task<CallEmailParseResult> ParseAsync(int formatType, CallEmail email, string managingUserId, List<IdentityUser> users,
			Department department, List<Unit> units, int priority, List<DepartmentCallPriority> priorities, List<CallType> callTypes,
			CancellationToken cancellationToken = default)
		{
			EmailLabeledFieldsConfig labels = null;
			if (formatType == (int)CallEmailTypes.LabeledFields && department != null)
				labels = await _departmentSettingsService.GetEmailLabeledFieldsConfigAsync(department.DepartmentId);

			return await _callEmailFactory.ParseCallEmailAsync((CallEmailTypes)formatType, email, managingUserId, users ?? new List<IdentityUser>(),
				department, units, priority, priorities, callTypes, _geoLocationProvider, labels);
		}

		public async Task<EmailCallUpdatePreview> PreviewAsync(int departmentId, int formatType, string subject, string body,
			EmailCallUpdateConfig config = null, EmailLabeledFieldsConfig labeledFields = null, CancellationToken cancellationToken = default)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(departmentId) ?? new List<DepartmentCallPriority>();
			var callTypes = await _callsService.GetCallTypesForDepartmentAsync(departmentId) ?? new List<CallType>();
			var units = await _unitsService.GetUnitsForDepartmentAsync(departmentId) ?? new List<Unit>();
			var defaultPriority = CallPriorityMatch.DefaultForImport(priorities);

			var email = new CallEmail { MessageId = "preview", Subject = subject, Body = body, TextBody = body };
			labeledFields ??= formatType == (int)CallEmailTypes.LabeledFields ? await _departmentSettingsService.GetEmailLabeledFieldsConfigAsync(departmentId) : null;
			var parsed = await _callEmailFactory.ParseCallEmailAsync((CallEmailTypes)formatType, email, department?.ManagingUserId, new List<IdentityUser>(),
				department, units, defaultPriority, priorities, callTypes, _geoLocationProvider, labeledFields);

			config ??= await _departmentSettingsService.GetEmailCallUpdateConfigAsync(departmentId, formatType);
			var preview = new EmailCallUpdatePreview
			{
				UpdatesEnabled = config.Enabled,
				IsBuiltInPreset = config.IsBuiltInPreset,
				CloseKind = CadEmailText.DetectClose(config, email),
				CadCallId = CadEmailText.ExtractCallId(config.Enabled ? config : WithSource(config), email, parsed?.MatchKey)
			};

			var call = parsed?.Call;
			if (call == null)
				return preview;

			preview.FormatFoundCall = true;
			preview.Name = call.Name;
			preview.Nature = Truncate(call.NatureOfCall, NoteTextLimit);
			preview.Address = call.Address;
			preview.Coordinates = call.GeoLocationData;
			preview.Type = call.Type;
			preview.Priority = PriorityName(priorities, call.Priority);
			preview.Notes = Truncate(call.Notes, NoteTextLimit);
			if (call.UnitDispatches != null)
				preview.Units = call.UnitDispatches.Select(d => units.FirstOrDefault(u => u.UnitId == d.UnitId)?.Name).Where(n => n != null).ToList();

			return preview;
		}

		public async Task<EmailCallUpdateResult> ProcessAsync(EmailCallUpdateRequest request, Func<Call, Task<Call>> createCall, CancellationToken cancellationToken = default)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			if (createCall == null)
				throw new ArgumentNullException(nameof(createCall));

			var parsed = request.Parsed ?? new CallEmailParseResult();
			var email = request.Email ?? new CallEmail();
			var config = request.Config ?? await _departmentSettingsService.GetEmailCallUpdateConfigAsync(request.DepartmentId, request.FormatType);

			if (config == null || !config.Enabled)
				return await CreateAsync(request, parsed, null, config, createCall, false);

			var cadCallId = CadEmailText.ExtractCallId(config, email, parsed.MatchKey);
			if (cadCallId == null)
				return await CreateAsync(request, parsed, null, config, createCall, false);

			var closeKind = CadEmailText.DetectClose(config, email);

			await using var held = await _callEmailUpdatesRepository.AcquireCadCallLockAsync(request.DepartmentId, cadCallId,
				request.LockTimeout ?? DefaultLockTimeout, cancellationToken);
			if (held == null)
			{
				Logging.LogInfo($"CAD email for department {request.DepartmentId}: another email with the same CAD call ID is still being handled.");
				return new EmailCallUpdateResult { Outcome = CallEmailUpdateOutcomes.Busy, CadCallId = cadCallId };
			}

			// A redelivery (the caller failed after this email was handled, so the provider sent it again) finds the rows it left. A finished
			// row: nothing is done again. A row whose call save went through (Saved, FollowedUp): the rest of that work is finished from
			// where it stopped, never the change again. Rows still Applying prove nothing (the save may or may not have happened), so the
			// email runs again: values already on the call are no change, text already appended is not appended again, and what those rows
			// meant to change still decides the re-page, so a page the first attempt owed is not lost.
			var department = request.Department;
			var intended = new List<string>();
			var messageId = StoredMessageId(email);
			if (!string.IsNullOrWhiteSpace(messageId))
			{
				var rows = (await _callEmailUpdatesRepository.GetByMessageAsync(request.DepartmentId, cadCallId, messageId, cancellationToken))?.ToList() ?? new List<CallEmailUpdate>();
				var handled = rows.LastOrDefault(r => IsFinished(r.Outcome));
				if (handled != null)
				{
					var earlier = handled.CallId.HasValue ? await _callsService.GetCallByIdAsync(handled.CallId.Value) : null;
					Logging.LogInfo($"CAD email for department {request.DepartmentId}: this email (CAD call ID {cadCallId}) was already handled; the redelivery changes nothing.");
					return new EmailCallUpdateResult { Outcome = CallEmailUpdateOutcomes.AlreadyHandled, Call = earlier, CadCallId = cadCallId };
				}

				var resumable = rows.LastOrDefault(r => (r.Outcome == (int)CallEmailUpdateOutcomes.Saved || r.Outcome == (int)CallEmailUpdateOutcomes.FollowedUp) && r.CallId.HasValue);
				var resumeCall = resumable != null ? await _callsService.GetCallByIdAsync(resumable.CallId.Value) : null;
				if (resumable == null)
				{
					// A close whose save went through just before its row could say so: the call carries the close this engine writes.
					var pendingClose = rows.LastOrDefault(r => r.Outcome == (int)CallEmailUpdateOutcomes.Applying && r.CallId.HasValue && IsClose(r));
					var closedCall = pendingClose != null ? await _callsService.GetCallByIdAsync(pendingClose.CallId.Value) : null;
					if (closedCall != null && ClosedByCadEmail(closedCall, pendingClose))
					{
						await MoveAsync(pendingClose, CallEmailUpdateOutcomes.Saved);
						(resumable, resumeCall) = (pendingClose, closedCall);
					}
				}

				if (resumable != null && resumeCall != null && !resumeCall.IsDeleted)
				{
					department ??= await _departmentsService.GetDepartmentByIdAsync(request.DepartmentId, false);
					var resumed = IsClose(resumable)
						? await ResumeCloseAsync(department, resumeCall, config, cadCallId, resumable, cancellationToken)
						: await ResumeUpdateAsync(department, resumeCall, parsed, config, cadCallId, resumable, cancellationToken);
					resumed.CadCallId = cadCallId;
					return resumed;
				}

				intended = rows.Where(r => r.Outcome == (int)CallEmailUpdateOutcomes.Applying && !IsClose(r)).SelectMany(Steps).Distinct().ToList();
			}

			department ??= await _departmentsService.GetDepartmentByIdAsync(request.DepartmentId, false);

			var open = (await _callEmailUpdatesRepository.GetOpenCallsForCadCallIdAsync(request.DepartmentId, cadCallId, cancellationToken))?.ToList() ?? new List<Call>();
			if (open.Count == 0 && parsed.LegacyMatch != null)
			{
				// Parkland County 2 calls opened before the ledger existed carry no ID: find them the way the format always did.
				var active = await _callsService.GetActiveCallsByDepartmentAsync(request.DepartmentId) ?? new List<Call>();
				open = active.Where(c => SafeMatch(parsed.LegacyMatch, c)).ToList();
			}

			if (open.Count > 0)
			{
				var ordered = open.OrderByDescending(c => c.LoggedOn).ThenByDescending(c => c.CallId).ToList();
				var call = await _callsService.GetCallByIdAsync(ordered[0].CallId);
				if (call != null && !call.IsDeleted)
				{
					var history = (await _callEmailUpdatesRepository.GetByCallAsync(request.DepartmentId, call.CallId, cancellationToken))?.ToList() ?? new List<CallEmailUpdate>();
					var others = ordered.Skip(1).Select(c => c.CallId).ToList();

					var result = closeKind != CadEmailCloseKinds.None
						? await CloseAsync(request, department, call, parsed, config, closeKind, cadCallId, history, others, cancellationToken)
						: await UpdateAsync(request, department, call, parsed, config, cadCallId, history, others, intended, cancellationToken);
					result.CadCallId = cadCallId;
					return result;
				}
			}

			if (closeKind != CadEmailCloseKinds.None)
			{
				// A close for a call that is already closed changes nothing; one for a call Resgrid never had creates nothing.
				var closed = (await _callEmailUpdatesRepository.GetClosedCallsForCadCallIdAsync(request.DepartmentId, cadCallId, DateTime.UtcNow - CloseLookback, cancellationToken))
					?.OrderByDescending(c => c.ClosedOn).FirstOrDefault();
				var outcome = closed != null ? CallEmailUpdateOutcomes.ClosedNote : CallEmailUpdateOutcomes.CloseUnmatched;
				await WriteAheadAsync(request, closed?.CallId, cadCallId, outcome, null, null);
				return new EmailCallUpdateResult { Outcome = outcome, Call = closed, CadCallId = cadCallId };
			}

			var window = config.GetClosedMatchMinutes();
			if (window > 0)
			{
				var closed = (await _callEmailUpdatesRepository.GetClosedCallsForCadCallIdAsync(request.DepartmentId, cadCallId, DateTime.UtcNow.AddMinutes(-window), cancellationToken))
					?.OrderByDescending(c => c.ClosedOn).FirstOrDefault();
				if (closed != null)
				{
					var changes = Diff(closed, parsed, await LoadPrioritiesAsync(request.DepartmentId));
					await NoteOnlyAsync(request, department, closed.CallId, cadCallId, CallEmailUpdateOutcomes.ClosedNote, changes.Select(c => c.Group), null,
						Header(cadCallId, "arrived for a call that is already closed; nothing was changed") + Lines(changes.Select(c => Describe(c, false))));
					return new EmailCallUpdateResult { Outcome = CallEmailUpdateOutcomes.ClosedNote, Call = closed, CadCallId = cadCallId, Changes = changes };
				}
			}

			var created = await CreateAsync(request, parsed, cadCallId, config, createCall, true);
			created.CadCallId = cadCallId;
			return created;
		}

		public async Task RecordCreatedAsync(EmailCallUpdateRequest request, Call saved, string cadCallId)
		{
			if (request == null || saved == null || saved.CallId <= 0 || string.IsNullOrWhiteSpace(cadCallId))
				return;

			await RecordAsync(request, saved.CallId, cadCallId, CallEmailUpdateOutcomes.Created, null, Snapshot(saved, null));
		}

		/// <summary>A new call through the caller's own path; with an ID, the call carries it and the ledger gets its baseline.</summary>
		private async Task<EmailCallUpdateResult> CreateAsync(EmailCallUpdateRequest request, CallEmailParseResult parsed, string cadCallId,
			EmailCallUpdateConfig config, Func<Call, Task<Call>> createCall, bool withId)
		{
			var call = parsed.Call;
			if (withId && call != null)
			{
				if (!parsed.MatchKeyIsComposite || config.GetIdSource() != CadCallIdSources.FromFormat)
				{
					if (string.IsNullOrWhiteSpace(call.ExternalIdentifier))
						call.ExternalIdentifier = cadCallId;
					if (string.IsNullOrWhiteSpace(call.IncidentNumber))
						call.IncidentNumber = cadCallId;
				}
			}

			var saved = await createCall(call);

			if (!withId)
				return new EmailCallUpdateResult { Outcome = CallEmailUpdateOutcomes.NotApplicable, Call = saved };

			if (saved != null && saved.CallId > 0)
				await RecordAsync(request, saved.CallId, cadCallId, CallEmailUpdateOutcomes.Created, null, Snapshot(saved, null));

			return new EmailCallUpdateResult { Outcome = CallEmailUpdateOutcomes.Created, Call = saved };
		}

		private async Task<EmailCallUpdateResult> UpdateAsync(EmailCallUpdateRequest request, Department department, Call call, CallEmailParseResult parsed,
			EmailCallUpdateConfig config, string cadCallId, List<CallEmailUpdate> history, List<int> otherCallIds, List<string> intended,
			CancellationToken cancellationToken)
		{
			var priorities = await LoadPrioritiesAsync(request.DepartmentId);
			var sentOn = request.Email?.SentOn;
			var lastApplied = LastApplied(history);

			if (sentOn.HasValue && lastApplied.HasValue && sentOn.Value < lastApplied.Value)
			{
				var stale = Diff(call, parsed, priorities);
				await NoteOnlyAsync(request, department, call.CallId, cadCallId, CallEmailUpdateOutcomes.Stale, stale.Select(c => c.Group), null,
					Header(cadCallId, "arrived after a newer one; its values were not applied") + Lines(stale.Select(c => Describe(c, false))));
				return new EmailCallUpdateResult { Outcome = CallEmailUpdateOutcomes.Stale, Call = call, Changes = stale };
			}

			var previous = LatestHashes(history);
			var hashes = new Dictionary<string, string>(previous ?? new Dictionary<string, string>());
			var changes = new List<EmailCallFieldChange>();
			var notes = new List<string>();

			bool EditedInResgrid(string group, string currentValue) =>
				config.DispatcherEditsWin && previous != null && previous.TryGetValue(group, out var hash) && hash != CadEmailText.Hash(currentValue);

			// Where the email and the call already agree, that value is the CAD's now (it just sent it), so a later change to it is a
			// person's. This also settles a value an earlier run of this same email saved without its row saying so.
			Agree(config, call, parsed, hashes);

			// Location: the address, its coordinates (the CAD's, else geocoded) and What3Words.
			if (parsed.Has(CallEmailParsedFields.Address) && !CadEmailText.SameValue(call.Address, parsed.Call.Address))
			{
				var change = new EmailCallFieldChange { Group = GroupLocation, OldValue = call.Address, NewValue = parsed.Call.Address };
				if (config.GetLocationPolicy() != CadFieldPolicies.Update)
					change.Reason = ReasonPolicy;
				else if (EditedInResgrid(GroupLocation, call.Address))
					change.Reason = ReasonEdited;
				else
				{
					// A new address is a new place: the pin follows it even where a dispatcher had moved it, and the note says so.
					if (!string.IsNullOrWhiteSpace(call.GeoLocationData) && EditedInResgrid(HashCoordinates, call.GeoLocationData))
						notes.Add("The map pin a dispatcher had moved was replaced, because the CAD changed the address.");

					call.Address = parsed.Call.Address;
					call.GeoLocationData = parsed.Has(CallEmailParsedFields.Coordinates) ? parsed.Call.GeoLocationData : await GeocodeAsync(parsed.Call.Address);
					call.W3W = parsed.Has(CallEmailParsedFields.What3Words) ? parsed.Call.W3W : null;
					if (call.LocationPoiId.HasValue)
					{
						call.LocationPoiId = null;
						notes.Add("The call's location point of interest was removed because the CAD changed the address.");
					}

					if (string.IsNullOrWhiteSpace(call.GeoLocationData))
						notes.Add("The new address could not be placed on the map.");

					hashes[GroupLocation] = CadEmailText.Hash(call.Address);
					hashes[HashCoordinates] = CadEmailText.Hash(call.GeoLocationData);
					change.Applied = true;
				}

				changes.Add(change);
			}
			else if (parsed.Has(CallEmailParsedFields.Coordinates) && !CadEmailText.SameValue(call.GeoLocationData, parsed.Call.GeoLocationData) &&
				config.GetLocationPolicy() == CadFieldPolicies.Update && !EditedInResgrid(GroupLocation, call.Address) &&
				!EditedInResgrid(HashCoordinates, call.GeoLocationData))
			{
				// Same address, new coordinates from the CAD (a corrected map pin). A pin a dispatcher moved (it no longer matches the
				// CAD's last one) stays; calls whose baseline predates the pin hash take the CAD's, as they always did.
				changes.Add(new EmailCallFieldChange { Group = GroupLocation, OldValue = call.GeoLocationData, NewValue = parsed.Call.GeoLocationData, Applied = true });
				call.GeoLocationData = parsed.Call.GeoLocationData;
				hashes[HashCoordinates] = CadEmailText.Hash(call.GeoLocationData);
			}

			if (parsed.Has(CallEmailParsedFields.Priority) && call.Priority != parsed.Call.Priority)
			{
				var change = new EmailCallFieldChange { Group = GroupPriority, OldValue = PriorityName(priorities, call.Priority), NewValue = PriorityName(priorities, parsed.Call.Priority) };
				if (config.GetPriorityPolicy() != CadFieldPolicies.Update)
					change.Reason = ReasonPolicy;
				else if (EditedInResgrid(GroupPriority, call.Priority.ToString()))
					change.Reason = ReasonEdited;
				else
				{
					call.Priority = parsed.Call.Priority;
					hashes[GroupPriority] = CadEmailText.Hash(call.Priority.ToString());
					change.Applied = true;
				}

				changes.Add(change);
			}

			if (parsed.Has(CallEmailParsedFields.Type) && !string.IsNullOrWhiteSpace(parsed.Call.Type) && !CadEmailText.SameValue(call.Type, parsed.Call.Type))
			{
				var change = new EmailCallFieldChange { Group = GroupType, OldValue = call.Type, NewValue = parsed.Call.Type };
				if (config.GetTypePolicy() != CadFieldPolicies.Update)
					change.Reason = ReasonPolicy;
				else if (EditedInResgrid(GroupType, call.Type))
					change.Reason = ReasonEdited;
				else
				{
					call.Type = parsed.Call.Type;
					call.CallTypeId = parsed.Call.CallTypeId;
					hashes[GroupType] = CadEmailText.Hash(call.Type);
					change.Applied = true;
				}

				changes.Add(change);
			}

			if (parsed.Has(CallEmailParsedFields.Name) && !CadEmailText.SameValue(call.Name, parsed.Call.Name))
			{
				var change = new EmailCallFieldChange { Group = GroupName, OldValue = call.Name, NewValue = parsed.Call.Name };
				if (config.GetNamePolicy() != CadFieldPolicies.Update)
					change.Reason = ReasonPolicy;
				else if (EditedInResgrid(GroupName, call.Name))
					change.Reason = ReasonEdited;
				else
				{
					call.Name = parsed.Call.Name;
					hashes[GroupName] = CadEmailText.Hash(call.Name);
					change.Applied = true;
				}

				changes.Add(change);
			}

			var stamp = $"— CAD update {(department != null ? DateTime.UtcNow.TimeConverter(department) : DateTime.UtcNow):g} —";
			var natureChange = ApplyText(GroupNature, config.GetNaturePolicy(), call.NatureOfCall, parsed.Has(CallEmailParsedFields.Nature) ? parsed.Call.NatureOfCall : null,
				stamp, EditedInResgrid, value => call.NatureOfCall = value, hashes);
			if (natureChange != null)
				changes.Add(natureChange);

			var notesChange = ApplyText(GroupNotes, config.GetNotesPolicy(), call.Notes, parsed.Has(CallEmailParsedFields.Notes) ? parsed.Call.Notes : null,
				stamp, EditedInResgrid, value => call.Notes = value, hashes);
			if (notesChange != null)
				changes.Add(notesChange);

			// Resources: add what the CAD names and the call lacks; never remove (D6), only list.
			var additions = await FindAdditionsAsync(call, parsed);
			if (additions.Count > 0)
				changes.Add(new EmailCallFieldChange { Group = GroupResources, NewValue = string.Join(", ", additions.Names), Applied = true });
			if (additions.NoLongerListed.Count > 0)
				notes.Add("No longer listed by CAD: " + string.Join(", ", additions.NoLongerListed));

			var applied = changes.Any(c => c.Applied);
			var repageRule = config.GetRepageWhen();
			var live = call.State == (int)CallStates.Active;

			// An earlier run of this email whose save may have gone through (its row never said) changed these too: they still count
			// for the re-page, so a page it owed is sent; the values themselves are already the call's (no change above).
			var appliedGroups = changes.Where(c => c.Applied).Select(c => c.Group).ToList();
			var repage = live && Repages(repageRule, appliedGroups.Concat(intended ?? new List<string>()));

			if (!applied && !repage)
			{
				// Nothing to save: the call keeps every value. What the CAD sent that was not applied is still written down.
				if (otherCallIds.Count > 0)
					notes.Add("This email also matched calls " + string.Join(", ", otherCallIds) + "; only this call is updated.");

				var note = changes.Count > 0 || notes.Count > 0
					? Header(cadCallId, "changed nothing on this call") + Lines(changes.Select(c => Describe(c, true)).Concat(notes))
					: null;
				await NoteOnlyAsync(request, department, call.CallId, cadCallId, CallEmailUpdateOutcomes.NoChange, null, hashes.Count > 0 ? hashes : null, note);
				return new EmailCallUpdateResult { Outcome = CallEmailUpdateOutcomes.NoChange, Call = call, Changes = changes };
			}

			// Who the page goes to, read before anything is written: everyone on the call for a re-page, else only what this email adds.
			var paging = live ? PageRecipients(additions, repage, config.PageAddedResources, repage ? additions.Current ?? await LoadDispatchesAsync(call) : null) : null;

			if (repage)
			{
				call.LastDispatchedOn = DateTime.UtcNow;
				if (repageRule == CadRepageRules.EveryUpdate)
					call.IncreaseDispatchCount();
			}

			// The ledger row goes in first, with the hashes this update sets (a ledger that cannot be written stops the update before the
			// call changes), and moves on at each step: Saved once the call is, FollowedUp once its page is in the outbox, Updated at the
			// end. A redelivery finishes from the step it finds; a row left Applying is never taken as the CAD's values.
			var row = await WriteAheadAsync(request, call.CallId, cadCallId, CallEmailUpdateOutcomes.Applying, appliedGroups, hashes);

			// The call row alone (no child collections: the repository's cascade would delete dispatch rows a person added meanwhile);
			// added resources are inserted as their own rows below.
			NullChildCollections(call);
			var saved = await _callsService.SaveCallAsync(call, cancellationToken);
			await MoveAsync(row, CallEmailUpdateOutcomes.Saved);

			// The page is in the outbox before the added rows are written, so a failure from here on still leaves worker 12 a page to send.
			var page = paging != null ? await _callBroadcastOutboxService.RecordAsync(saved, paging.Users, paging.Groups, paging.Units, paging.Roles, department?.ManagingUserId) : null;
			if (page != null)
				await MoveAsync(row, CallEmailUpdateOutcomes.FollowedUp);
			var addedCount = await InsertAdditionsAsync(saved, additions, cancellationToken);

			if (otherCallIds.Count > 0)
				notes.Add("This email also matched calls " + string.Join(", ", otherCallIds) + "; only this call was updated.");

			await AddNoteAsync(department, saved.CallId, Header(cadCallId, applied ? "updated this call" : "paged this call again") +
				Lines(changes.Select(c => Describe(c, true)).Concat(notes)));
			await MoveAsync(row, CallEmailUpdateOutcomes.Updated);

			_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent { DepartmentId = saved.DepartmentId, Call = saved });

			var repaged = false;
			if (page != null && saved.State == (int)CallStates.Active)
			{
				var accepted = await SendPageAsync(saved, page);
				repaged = repage && accepted;
			}

			return new EmailCallUpdateResult
			{
				Outcome = CallEmailUpdateOutcomes.Updated,
				Call = saved,
				Changes = changes,
				Repaged = repaged,
				AddedResources = addedCount
			};
		}

		private async Task<EmailCallUpdateResult> CloseAsync(EmailCallUpdateRequest request, Department department, Call call, CallEmailParseResult parsed,
			EmailCallUpdateConfig config, CadEmailCloseKinds closeKind, string cadCallId, List<CallEmailUpdate> history, List<int> otherCallIds,
			CancellationToken cancellationToken)
		{
			var cancel = closeKind == CadEmailCloseKinds.Cancel;
			var verb = cancel ? "cancelled" : "closed";
			var sentOn = request.Email?.SentOn;
			var lastApplied = LastApplied(history);
			if (sentOn.HasValue && lastApplied.HasValue && sentOn.Value < lastApplied.Value)
			{
				await NoteOnlyAsync(request, department, call.CallId, cadCallId, CallEmailUpdateOutcomes.Stale, null, null,
					Header(cadCallId, $"{verb} the call, but arrived after a newer email; the call was left open"));
				return new EmailCallUpdateResult { Outcome = CallEmailUpdateOutcomes.Stale, Call = call };
			}

			if (await _callClosureService.GetBlockingIncidentCommandAsync(call.DepartmentId, call.CallId) != null)
			{
				await NoteOnlyAsync(request, department, call.CallId, cadCallId, CallEmailUpdateOutcomes.CloseBlocked, null, null,
					Header(cadCallId, $"{verb} this call; it is still open here because incident command is active"));
				return new EmailCallUpdateResult { Outcome = CallEmailUpdateOutcomes.CloseBlocked, Call = call };
			}

			var wentOut = call.State == (int)CallStates.Active;

			// As for an update: the row first (Applying), naming the close and whether the call was out, moved on at each step: Saved once
			// the call is saved closed, FollowedUp once its resources are released, then Closed or Cancelled once the notice and note are.
			var steps = new List<string> { cancel ? StepCancel : StepClose };
			if (wentOut)
				steps.Add(StepLive);
			var row = await WriteAheadAsync(request, call.CallId, cadCallId, CallEmailUpdateOutcomes.Applying, steps, null);

			call.State = (int)(cancel ? CallStates.Cancelled : CallStates.Closed);
			call.ClosedOn = DateTime.UtcNow;
			call.ClosedByUserId = department?.ManagingUserId;
			call.CompletedNotes = CloseText(cancel);
			NullChildCollections(call);

			var saved = await _callsService.SaveCallAsync(call, cancellationToken);
			await MoveAsync(row, CallEmailUpdateOutcomes.Saved);

			_eventAggregator.SendMessage<CallClosedEvent>(new CallClosedEvent { DepartmentId = saved.DepartmentId, Call = saved });

			await FinishCloseAsync(department, saved, config, cadCallId, row, cancel, wentOut,
				otherCallIds.Count > 0 ? "This email also matched calls " + string.Join(", ", otherCallIds) + "; only this call was closed." : null, cancellationToken);

			return new EmailCallUpdateResult { Outcome = cancel ? CallEmailUpdateOutcomes.Cancelled : CallEmailUpdateOutcomes.Closed, Call = saved };
		}

		/// <summary>
		/// What follows a CAD close once the call is saved closed: its resources released, the close notice, the note, then the row's
		/// outcome. The call stays closed whatever fails here (logged, never a reason to reopen it); each step runs on its own, so a failed
		/// release never costs the notice. The row records the release, so a redelivery after an interruption finishes only what is left.
		/// </summary>
		private async Task FinishCloseAsync(Department department, Call saved, EmailCallUpdateConfig config, string cadCallId, CallEmailUpdate row, bool cancel,
			bool wentOut, string extraLine, CancellationToken cancellationToken)
		{
			var verb = cancel ? "cancelled" : "closed";
			var released = row.Outcome == (int)CallEmailUpdateOutcomes.FollowedUp;

			Call populated = null;
			async Task<Call> LoadAsync()
			{
				try
				{
					return await _callsService.PopulateCallData(await _callsService.GetCallByIdAsync(saved.CallId), true, false, false, true, true, true, false, false, false);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Loading call {saved.CallId} to release its resources and send the close notice after a CAD close.");
					return null;
				}
			}

			if (!released)
			{
				populated = await LoadAsync();
				if (populated != null)
				{
					if (wentOut && ((populated.GroupDispatches != null && populated.GroupDispatches.Any()) || (populated.UnitDispatches != null && populated.UnitDispatches.Any())))
					{
						try
						{
							await _callDispatchStatusService.ApplyReleaseStatusesAsync(populated, cancellationToken: cancellationToken);
						}
						catch (Exception ex)
						{
							Logging.LogException(ex, $"Releasing the resources of call {saved.CallId} after a CAD close.");
						}
					}

					released = true;
					await MoveAsync(row, CallEmailUpdateOutcomes.FollowedUp);
				}
			}

			if (released && config.NotifyOnClose && wentOut)
			{
				populated ??= await LoadAsync();
				if (populated != null)
				{
					try
					{
						await _callClosureService.NotifyCallClosedAsync(populated, department?.ManagingUserId, cancellationToken);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"Sending the close notice for call {saved.CallId} after a CAD close.");
					}
				}
			}

			await AddNoteAsync(department, saved.CallId, Header(cadCallId, $"{verb} this call") + (extraLine != null ? Lines(new[] { extraLine }) : string.Empty));

			// A close whose call could not even be loaded stays Saved: a redelivery still releases and notifies.
			if (released)
				await MoveAsync(row, cancel ? CallEmailUpdateOutcomes.Cancelled : CallEmailUpdateOutcomes.Closed);
		}

		/// <summary>A redelivered close whose call save went through: the release, notice and note it had left.</summary>
		private async Task<EmailCallUpdateResult> ResumeCloseAsync(Department department, Call call, EmailCallUpdateConfig config, string cadCallId,
			CallEmailUpdate row, CancellationToken cancellationToken)
		{
			var steps = Steps(row);
			var cancel = steps.Contains(StepCancel);
			Logging.LogInfo($"CAD email for department {row.DepartmentId}: finishing the close of call {call.CallId} (CAD call ID {cadCallId}) an earlier delivery saved.");

			await FinishCloseAsync(department, call, config, cadCallId, row, cancel, steps.Contains(StepLive), null, cancellationToken);
			return new EmailCallUpdateResult { Outcome = cancel ? CallEmailUpdateOutcomes.Cancelled : CallEmailUpdateOutcomes.Closed, Call = call };
		}

		/// <summary>
		/// A redelivered update whose call save went through: the page (when its row says it was not recorded yet), the resource rows the
		/// call still lacks, and the note. The field values are the call's already and are not applied again.
		/// </summary>
		private async Task<EmailCallUpdateResult> ResumeUpdateAsync(Department department, Call call, CallEmailParseResult parsed, EmailCallUpdateConfig config,
			string cadCallId, CallEmailUpdate row, CancellationToken cancellationToken)
		{
			Logging.LogInfo($"CAD email for department {row.DepartmentId}: finishing the update of call {call.CallId} (CAD call ID {cadCallId}) an earlier delivery saved.");

			var groups = Steps(row);
			var additions = await FindAdditionsAsync(call, parsed);
			var live = call.State == (int)CallStates.Active;

			CallBroadcastOutboxEntry page = null;
			var repage = false;
			if (row.Outcome == (int)CallEmailUpdateOutcomes.Saved && live)
			{
				repage = Repages(config.GetRepageWhen(), groups);
				var paging = PageRecipients(additions, repage, config.PageAddedResources, repage ? additions.Current ?? await LoadDispatchesAsync(call) : null);
				page = paging != null ? await _callBroadcastOutboxService.RecordAsync(call, paging.Users, paging.Groups, paging.Units, paging.Roles, department?.ManagingUserId) : null;
				if (page != null)
					await MoveAsync(row, CallEmailUpdateOutcomes.FollowedUp);
			}

			var addedCount = await InsertAdditionsAsync(call, additions, cancellationToken);
			var lines = groups.Where(g => g != GroupResources).Select(g => $"{(g == GroupLocation ? "Address" : g)} updated").ToList();
			if (additions.Names.Count > 0)
				lines.Add("Added: " + string.Join(", ", additions.Names));
			await AddNoteAsync(department, call.CallId, Header(cadCallId, groups.Count > 0 ? "updated this call" : "paged this call again") + Lines(lines));
			await MoveAsync(row, CallEmailUpdateOutcomes.Updated);

			_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent { DepartmentId = call.DepartmentId, Call = call });

			var repaged = false;
			if (page != null)
				repaged = repage && await SendPageAsync(call, page);

			return new EmailCallUpdateResult { Outcome = CallEmailUpdateOutcomes.Updated, Call = call, Repaged = repaged, AddedResources = addedCount };
		}

		/// <summary>Nature or notes under a text policy; protected (enveloped) text cannot be read here, so it is replaced or noted, never appended to.</summary>
		private static EmailCallFieldChange ApplyText(string group, CadTextPolicies policy, string current, string incoming, string stamp,
			Func<string, string, bool> editedInResgrid, Action<string> set, Dictionary<string, string> hashes)
		{
			if (string.IsNullOrWhiteSpace(incoming))
				return null;

			var isProtected = ProtectedDataEnvelope.IsEnveloped(current) || current == ProtectedDataEnvelope.RedactionValue;
			if (!isProtected && CadEmailText.SameValue(current, incoming))
				return null;

			// Text the call already ends with was appended already (by this email's earlier run, or the CAD sent it twice).
			if (!isProtected && policy == CadTextPolicies.Append && !string.IsNullOrWhiteSpace(current) &&
				current.TrimEnd().EndsWith(incoming.Trim(), StringComparison.Ordinal))
				return null;

			var change = new EmailCallFieldChange { Group = group, NewValue = incoming };
			if (policy == CadTextPolicies.NoteOnly)
			{
				change.Reason = ReasonPolicy;
				return change;
			}

			if (isProtected && policy == CadTextPolicies.Append)
			{
				change.Reason = ReasonProtected;
				return change;
			}

			if (!isProtected && editedInResgrid(group, current))
			{
				change.Reason = ReasonEdited;
				return change;
			}

			if (policy == CadTextPolicies.Append && !string.IsNullOrWhiteSpace(current))
				set(current.TrimEnd() + "\n\n" + stamp + "\n" + incoming.Trim());
			else
				set(incoming);

			hashes[group] = CadEmailText.Hash(policy == CadTextPolicies.Append ? null : incoming);
			if (policy == CadTextPolicies.Append)
				hashes.Remove(group);

			change.Applied = true;
			return change;
		}

		/// <summary>The differences between a call and a parsed email, nothing applied (stale and closed-call notes).</summary>
		private static List<EmailCallFieldChange> Diff(Call call, CallEmailParseResult parsed, List<DepartmentCallPriority> priorities)
		{
			var changes = new List<EmailCallFieldChange>();
			if (call == null || parsed?.Call == null)
				return changes;

			if (parsed.Has(CallEmailParsedFields.Address) && !CadEmailText.SameValue(call.Address, parsed.Call.Address))
				changes.Add(new EmailCallFieldChange { Group = GroupLocation, OldValue = call.Address, NewValue = parsed.Call.Address });
			if (parsed.Has(CallEmailParsedFields.Priority) && call.Priority != parsed.Call.Priority)
				changes.Add(new EmailCallFieldChange { Group = GroupPriority, OldValue = PriorityName(priorities, call.Priority), NewValue = PriorityName(priorities, parsed.Call.Priority) });
			if (parsed.Has(CallEmailParsedFields.Type) && !string.IsNullOrWhiteSpace(parsed.Call.Type) && !CadEmailText.SameValue(call.Type, parsed.Call.Type))
				changes.Add(new EmailCallFieldChange { Group = GroupType, OldValue = call.Type, NewValue = parsed.Call.Type });
			if (parsed.Has(CallEmailParsedFields.Name) && !CadEmailText.SameValue(call.Name, parsed.Call.Name))
				changes.Add(new EmailCallFieldChange { Group = GroupName, OldValue = call.Name, NewValue = parsed.Call.Name });
			return changes;
		}

		/// <summary>One note line for a field change.</summary>
		private static string Describe(EmailCallFieldChange change, bool forUpdate)
		{
			var label = change.Group == GroupLocation ? "Address" : change.Group;
			if (change.Group == GroupResources)
				return "Added: " + change.NewValue;

			if (change.Group == GroupNature || change.Group == GroupNotes)
			{
				if (change.Applied)
					return $"{label} updated.";

				var suffix = change.Reason == ReasonEdited ? " (kept: changed in Resgrid)" : change.Reason == ReasonProtected ? " (not appended: protected text)" : " (not applied)";
				return $"{label}{suffix}: CAD sent \"{Truncate(change.NewValue, NoteTextLimit)}\"";
			}

			if (change.Applied)
				return $"{label}: \"{change.OldValue}\" → \"{change.NewValue}\"";

			if (!forUpdate)
				return $"{label}: CAD sent \"{change.NewValue}\" (here: \"{change.OldValue}\")";

			return change.Reason == ReasonEdited
				? $"{label} kept (changed in Resgrid); CAD sent \"{change.NewValue}\""
				: $"{label} not changed (department setting); CAD sent \"{change.NewValue}\"";
		}

		private static string Header(string cadCallId, string what) => $"CAD email (call ID {cadCallId}) {what}.";

		/// <summary>
		/// The note's detail lines, each ending in a full stop: the web shows a note as one paragraph, so the line breaks alone would
		/// run "… → "14 Oak Ave" Priority: …" together.
		/// </summary>
		private static string Lines(IEnumerable<string> lines)
		{
			var list = lines.Where(l => !string.IsNullOrWhiteSpace(l))
				.Select(l => l.TrimEnd())
				.Select(l => l.EndsWith(".", StringComparison.Ordinal) ? l : l + ".")
				.ToList();
			return list.Count == 0 ? string.Empty : "\n" + string.Join("\n", list);
		}

		/// <summary>Writes the CAD note on the call; false (logged) when it could not be written.</summary>
		private async Task<bool> AddNoteAsync(Department department, int callId, string text)
		{
			try
			{
				await _callsService.SaveCallNoteAsync(new CallNote
				{
					CallId = callId,
					UserId = department?.ManagingUserId,
					Note = text.Length > 4000 ? text.Substring(0, 4000) : text,
					Source = (int)CallNoteSources.CadEmail,
					Timestamp = DateTime.UtcNow
				});
				return true;
			}
			catch (Exception ex)
			{
				// The update is saved; a note that could not be written is logged, never a reason to fail the email.
				Logging.LogException(ex, $"Writing the CAD update note on call {callId}.");
				return false;
			}
		}

		private static CallEmailUpdate LedgerRow(EmailCallUpdateRequest request, int? callId, string cadCallId, CallEmailUpdateOutcomes outcome,
			IEnumerable<string> changedGroups, Dictionary<string, string> hashes)
		{
			var groups = changedGroups?.Distinct().ToList();
			return new CallEmailUpdate
			{
				CallEmailUpdateId = Guid.NewGuid().ToString(),
				DepartmentId = request.DepartmentId,
				CallId = callId,
				CadCallId = cadCallId,
				Source = (int)request.Source,
				IntakeChannelId = request.IntakeChannelId,
				MessageId = StoredMessageId(request.Email),
				SentOn = request.Email?.SentOn,
				ReceivedOn = DateTime.UtcNow,
				Outcome = (int)outcome,
				ChangedFields = groups == null || groups.Count == 0 ? null : Clip(string.Join(",", groups), 500),
				FieldHashes = hashes == null || hashes.Count == 0 ? null : JsonConvert.SerializeObject(hashes)
			};
		}

		/// <summary>
		/// The ledger row, written before the email changes or notes anything. A failure goes to the caller (whose retry starts clean,
		/// nothing having been done), so an email is never applied without the row a redelivery is recognised by.
		/// </summary>
		private async Task<CallEmailUpdate> WriteAheadAsync(EmailCallUpdateRequest request, int? callId, string cadCallId, CallEmailUpdateOutcomes outcome,
			IEnumerable<string> changedGroups, Dictionary<string, string> hashes)
		{
			var row = LedgerRow(request, callId, cadCallId, outcome, changedGroups, hashes);
			await _callEmailUpdatesRepository.InsertAsync(row, CancellationToken.None);
			return row;
		}

		/// <summary>The row for a call the caller's path already created; never throws (later emails still find the call by the ID it carries).</summary>
		private async Task RecordAsync(EmailCallUpdateRequest request, int? callId, string cadCallId, CallEmailUpdateOutcomes outcome,
			IEnumerable<string> changedGroups, Dictionary<string, string> hashes)
		{
			try
			{
				await _callEmailUpdatesRepository.InsertAsync(LedgerRow(request, callId, cadCallId, outcome, changedGroups, hashes), CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Recording a CAD email update for department {request.DepartmentId}.");
			}
		}

		/// <summary>
		/// Moves the row written first on to the next step (or to what the email did), from the step it is at. Never throws: a row that
		/// could not be moved stays at its earlier step, which only means a redelivery redoes that step's work.
		/// </summary>
		private async Task MoveAsync(CallEmailUpdate row, CallEmailUpdateOutcomes to)
		{
			try
			{
				if (await _callEmailUpdatesRepository.SetOutcomeAsync(row.CallEmailUpdateId, row.Outcome, (int)to, CancellationToken.None))
					row.Outcome = (int)to;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Recording a step of the CAD email update of call {row.CallId}.");
			}
		}

		/// <summary>
		/// A note-only outcome (stale, no change, close blocked, a closed call noted): its row goes in as Noting, the note is written,
		/// then the row takes the outcome, so an email interrupted between the two (or whose note could not be written) writes its note
		/// on a redelivery instead of never. Without a note the row takes the outcome at once.
		/// </summary>
		private async Task NoteOnlyAsync(EmailCallUpdateRequest request, Department department, int callId, string cadCallId, CallEmailUpdateOutcomes outcome,
			IEnumerable<string> groups, Dictionary<string, string> hashes, string note)
		{
			if (note == null)
			{
				await WriteAheadAsync(request, callId, cadCallId, outcome, groups, hashes);
				return;
			}

			var row = await WriteAheadAsync(request, callId, cadCallId, CallEmailUpdateOutcomes.Noting, groups, hashes);
			if (await AddNoteAsync(department, callId, note))
				await MoveAsync(row, outcome);
		}

		/// <summary>The CAD's values on a new call: what later emails are compared with to tell a person's edit from the CAD's.</summary>
		private static Dictionary<string, string> Snapshot(Call call, Dictionary<string, string> into)
		{
			var hashes = into ?? new Dictionary<string, string>();
			hashes[GroupLocation] = CadEmailText.Hash(call.Address);
			hashes[HashCoordinates] = CadEmailText.Hash(call.GeoLocationData);
			hashes[GroupPriority] = CadEmailText.Hash(call.Priority.ToString());
			hashes[GroupType] = CadEmailText.Hash(call.Type);
			hashes[GroupName] = CadEmailText.Hash(call.Name);
			if (!ProtectedDataEnvelope.IsEnveloped(call.NatureOfCall))
				hashes[GroupNature] = CadEmailText.Hash(call.NatureOfCall);
			if (!ProtectedDataEnvelope.IsEnveloped(call.Notes))
				hashes[GroupNotes] = CadEmailText.Hash(call.Notes);
			return hashes;
		}

		/// <summary>
		/// What the CAD last set, per field group: the newest row whose values are known to be on the call (finished, or Saved/FollowedUp:
		/// its save went through). A row still Applying or Noting never counts — a value the call happens to show is no proof the CAD
		/// wrote it (a person may have typed the same thing).
		/// </summary>
		private static Dictionary<string, string> LatestHashes(List<CallEmailUpdate> history)
		{
			var latest = history.Where(h => !string.IsNullOrWhiteSpace(h.FieldHashes) && h.Outcome != (int)CallEmailUpdateOutcomes.Applying &&
				h.Outcome != (int)CallEmailUpdateOutcomes.Noting).OrderBy(h => h.ReceivedOn).LastOrDefault();
			return latest == null ? null : ReadHashes(latest);
		}

		/// <summary>
		/// The newest sent time among the emails that set the call's state: created, updated, no change (the CAD sent what the call has),
		/// closed or cancelled, or saved and still finishing. A stale email, a close incident command blocked, a note on a closed call and
		/// a row still Applying or Noting changed nothing, so a correction sent before them still applies.
		/// </summary>
		private static DateTime? LastApplied(List<CallEmailUpdate> history)
		{
			return history.Where(h => h.SentOn.HasValue && SetsState(h.Outcome)).Select(h => h.SentOn).DefaultIfEmpty(null).Max();
		}

		private static bool SetsState(int outcome)
		{
			switch ((CallEmailUpdateOutcomes)outcome)
			{
				case CallEmailUpdateOutcomes.Created:
				case CallEmailUpdateOutcomes.Updated:
				case CallEmailUpdateOutcomes.NoChange:
				case CallEmailUpdateOutcomes.Closed:
				case CallEmailUpdateOutcomes.Cancelled:
				case CallEmailUpdateOutcomes.Saved:
				case CallEmailUpdateOutcomes.FollowedUp:
					return true;
				default:
					return false;
			}
		}

		/// <summary>A row whose email is done: anything but a step still in progress.</summary>
		private static bool IsFinished(int outcome) =>
			outcome != (int)CallEmailUpdateOutcomes.Applying && outcome != (int)CallEmailUpdateOutcomes.Saved &&
			outcome != (int)CallEmailUpdateOutcomes.FollowedUp && outcome != (int)CallEmailUpdateOutcomes.Noting;

		/// <summary>The groups (or close steps) a row recorded.</summary>
		private static List<string> Steps(CallEmailUpdate row) =>
			string.IsNullOrWhiteSpace(row?.ChangedFields)
				? new List<string>()
				: row.ChangedFields.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

		private static bool IsClose(CallEmailUpdate row)
		{
			var steps = Steps(row);
			return steps.Contains(StepClose) || steps.Contains(StepCancel);
		}

		private static string CloseText(bool cancel) => cancel ? "Cancelled from CAD email." : "Closed from CAD email.";

		/// <summary>
		/// The close a row still Applying wrote is on the call: closed or cancelled as it said, with the completion text this engine writes,
		/// at or after the row went in. A person closing the call by hand writes their own.
		/// </summary>
		private static bool ClosedByCadEmail(Call call, CallEmailUpdate row)
		{
			var cancel = Steps(row).Contains(StepCancel);
			return call.State == (int)(cancel ? CallStates.Cancelled : CallStates.Closed) && call.CompletedNotes == CloseText(cancel) &&
				call.ClosedOn.HasValue && call.ClosedOn.Value >= row.ReceivedOn.AddSeconds(-1);
		}

		/// <summary>Whether the department's re-page rule pages for these changed groups (EveryUpdate pages every update).</summary>
		private static bool Repages(CadRepageRules rule, IEnumerable<string> changedGroups)
		{
			var groups = changedGroups?.ToList() ?? new List<string>();
			switch (rule)
			{
				case CadRepageRules.EveryUpdate:
					return true;
				case CadRepageRules.AnyChange:
					return groups.Any(g => g != GroupResources);
				case CadRepageRules.LocationOrPriorityChanged:
					return groups.Contains(GroupLocation) || groups.Contains(GroupPriority);
				default:
					return false;
			}
		}

		/// <summary>
		/// The fields where the email and the call already hold the same value take that value as the CAD's in the new baseline (for
		/// nature and notes only under Replace: appended text has no single value).
		/// </summary>
		private static void Agree(EmailCallUpdateConfig config, Call call, CallEmailParseResult parsed, Dictionary<string, string> hashes)
		{
			if (parsed?.Call == null)
				return;

			if (parsed.Has(CallEmailParsedFields.Address) && CadEmailText.SameValue(call.Address, parsed.Call.Address))
				hashes[GroupLocation] = CadEmailText.Hash(call.Address);
			if (parsed.Has(CallEmailParsedFields.Coordinates) && !string.IsNullOrWhiteSpace(parsed.Call.GeoLocationData) &&
				CadEmailText.SameValue(call.GeoLocationData, parsed.Call.GeoLocationData))
				hashes[HashCoordinates] = CadEmailText.Hash(call.GeoLocationData);
			if (parsed.Has(CallEmailParsedFields.Priority) && call.Priority == parsed.Call.Priority)
				hashes[GroupPriority] = CadEmailText.Hash(call.Priority.ToString());
			if (parsed.Has(CallEmailParsedFields.Type) && !string.IsNullOrWhiteSpace(parsed.Call.Type) && CadEmailText.SameValue(call.Type, parsed.Call.Type))
				hashes[GroupType] = CadEmailText.Hash(call.Type);
			if (parsed.Has(CallEmailParsedFields.Name) && CadEmailText.SameValue(call.Name, parsed.Call.Name))
				hashes[GroupName] = CadEmailText.Hash(call.Name);
			if (parsed.Has(CallEmailParsedFields.Nature) && config.GetNaturePolicy() == CadTextPolicies.Replace && !ProtectedDataEnvelope.IsEnveloped(call.NatureOfCall) &&
				!string.IsNullOrWhiteSpace(parsed.Call.NatureOfCall) && CadEmailText.SameValue(call.NatureOfCall, parsed.Call.NatureOfCall))
				hashes[GroupNature] = CadEmailText.Hash(call.NatureOfCall);
			if (parsed.Has(CallEmailParsedFields.Notes) && config.GetNotesPolicy() == CadTextPolicies.Replace && !ProtectedDataEnvelope.IsEnveloped(call.Notes) &&
				!string.IsNullOrWhiteSpace(parsed.Call.Notes) && CadEmailText.SameValue(call.Notes, parsed.Call.Notes))
				hashes[GroupNotes] = CadEmailText.Hash(call.Notes);
		}

		private static Dictionary<string, string> ReadHashes(CallEmailUpdate row)
		{
			if (string.IsNullOrWhiteSpace(row?.FieldHashes))
				return null;

			try
			{
				return JsonConvert.DeserializeObject<Dictionary<string, string>>(row.FieldHashes);
			}
			catch (JsonException)
			{
				return null;
			}
		}

		private async Task<string> GeocodeAsync(string address)
		{
			try
			{
				var coordinates = await _geoLocationProvider.GetLatLonFromAddress(address);
				return string.IsNullOrWhiteSpace(coordinates) || coordinates.Trim().Length <= 1 ? null : coordinates;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Geocoding a CAD-updated address.");
				return null;
			}
		}

		private async Task<List<DepartmentCallPriority>> LoadPrioritiesAsync(int departmentId)
		{
			try
			{
				return await _callsService.GetActiveCallPrioritiesForDepartmentAsync(departmentId) ?? new List<DepartmentCallPriority>();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new List<DepartmentCallPriority>();
			}
		}

		private static string PriorityName(List<DepartmentCallPriority> priorities, int priority)
		{
			var match = priorities?.FirstOrDefault(p => p.DepartmentCallPriorityId == priority);
			if (match != null && !string.IsNullOrWhiteSpace(match.Name))
				return match.Name;

			return Enum.IsDefined(typeof(CallPriority), priority) ? ((CallPriority)priority).ToString() : priority.ToString();
		}

		private sealed class Additions
		{
			public List<CallDispatch> Users { get; } = new List<CallDispatch>();
			public List<CallDispatchGroup> Groups { get; } = new List<CallDispatchGroup>();
			public List<CallDispatchUnit> Units { get; } = new List<CallDispatchUnit>();
			public List<CallDispatchRole> Roles { get; } = new List<CallDispatchRole>();
			public List<string> Names { get; } = new List<string>();
			public List<string> NoLongerListed { get; } = new List<string>();
			public int Count => Users.Count + Groups.Count + Units.Count + Roles.Count;

			/// <summary>The call with its dispatch lists as they were before this email, when the email named resources.</summary>
			public Call Current { get; set; }
		}

		private sealed class Recipients
		{
			public List<string> Users { get; } = new List<string>();
			public List<int> Groups { get; } = new List<int>();
			public List<int> Units { get; } = new List<int>();
			public List<int> Roles { get; } = new List<int>();
		}

		/// <summary>The units, groups and roles the email names that the call does not have, and the units CAD stopped naming.</summary>
		private async Task<Additions> FindAdditionsAsync(Call call, CallEmailParseResult parsed)
		{
			var additions = new Additions();
			if (!parsed.Has(CallEmailParsedFields.Resources) || parsed.Call == null)
				return additions;

			var current = await LoadDispatchesAsync(call);
			if (current == null)
				return additions;

			additions.Current = current;

			var units = parsed.Call.UnitDispatches != null && parsed.Call.UnitDispatches.Any() ? await _unitsService.GetUnitsForDepartmentAsync(call.DepartmentId) ?? new List<Unit>() : new List<Unit>();
			var groups = parsed.Call.GroupDispatches != null && parsed.Call.GroupDispatches.Any() ? await _departmentGroupsService.GetAllGroupsForDepartmentAsync(call.DepartmentId) ?? new List<DepartmentGroup>() : new List<DepartmentGroup>();

			var haveUnits = new HashSet<int>((current.UnitDispatches ?? new List<CallDispatchUnit>()).Select(d => d.UnitId));
			foreach (var unitId in (parsed.Call.UnitDispatches ?? new List<CallDispatchUnit>()).Select(d => d.UnitId).Distinct())
			{
				if (haveUnits.Contains(unitId))
					continue;
				additions.Units.Add(new CallDispatchUnit { CallId = call.CallId, UnitId = unitId, LastDispatchedOn = DateTime.UtcNow });
				additions.Names.Add(units.FirstOrDefault(u => u.UnitId == unitId)?.Name ?? $"unit {unitId}");
			}

			if (parsed.Call.UnitDispatches != null && parsed.Call.UnitDispatches.Any())
			{
				var listed = new HashSet<int>(parsed.Call.UnitDispatches.Select(d => d.UnitId));
				foreach (var unitId in haveUnits.Where(id => !listed.Contains(id)))
					additions.NoLongerListed.Add(units.FirstOrDefault(u => u.UnitId == unitId)?.Name ?? $"unit {unitId}");
			}

			var haveGroups = new HashSet<int>((current.GroupDispatches ?? new List<CallDispatchGroup>()).Select(d => d.DepartmentGroupId));
			foreach (var groupId in (parsed.Call.GroupDispatches ?? new List<CallDispatchGroup>()).Select(d => d.DepartmentGroupId).Distinct())
			{
				if (haveGroups.Contains(groupId))
					continue;
				additions.Groups.Add(new CallDispatchGroup { CallId = call.CallId, DepartmentGroupId = groupId });
				additions.Names.Add(groups.FirstOrDefault(g => g.DepartmentGroupId == groupId)?.Name ?? $"group {groupId}");
			}

			var haveRoles = new HashSet<int>((current.RoleDispatches ?? new List<CallDispatchRole>()).Select(d => d.RoleId));
			var roleCount = 0;
			foreach (var roleId in (parsed.Call.RoleDispatches ?? new List<CallDispatchRole>()).Select(d => d.RoleId).Distinct())
			{
				if (haveRoles.Contains(roleId))
					continue;
				additions.Roles.Add(new CallDispatchRole { CallId = call.CallId, RoleId = roleId });
				roleCount++;
			}

			if (roleCount > 0)
				additions.Names.Add(roleCount == 1 ? "1 role" : $"{roleCount} roles");

			// Members are not taken from an update: formats put the whole roster (or the group's) on every email they read, which
			// says nothing about what the CAD changed, and a call a dispatcher built by hand must not gain everyone.
			return additions;
		}

		private async Task<int> InsertAdditionsAsync(Call call, Additions additions, CancellationToken cancellationToken)
		{
			if (additions.Count == 0)
				return 0;

			var now = DateTime.UtcNow;
			var count = 0;
			foreach (var row in additions.Users) { row.CallId = call.CallId; row.DispatchedOn = now; await _callDispatchesRepository.InsertAsync(row, cancellationToken, true); count++; }
			foreach (var row in additions.Groups) { row.CallId = call.CallId; row.DispatchedOn = now; await _callDispatchGroupRepository.InsertAsync(row, cancellationToken, true); count++; }
			foreach (var row in additions.Units) { row.CallId = call.CallId; row.DispatchedOn = now; await _callDispatchUnitRepository.InsertAsync(row, cancellationToken, true); count++; }
			foreach (var row in additions.Roles) { row.CallId = call.CallId; row.DispatchedOn = now; await _callDispatchRoleRepository.InsertAsync(row, cancellationToken, true); count++; }
			return count;
		}

		private async Task<Call> LoadDispatchesAsync(Call call)
		{
			return await _callsService.PopulateCallData(await _callsService.GetCallByIdAsync(call.CallId), true, false, false, true, true, true, false, false, false);
		}

		/// <summary>
		/// Who an update pages: everyone on the call (<paramref name="current"/>'s dispatch lists) plus what the email adds for a re-page,
		/// else only the added resources when the department pages them; null when nobody is paged.
		/// </summary>
		private static Recipients PageRecipients(Additions additions, bool repage, bool pageAdded, Call current)
		{
			if (!repage && (additions.Count == 0 || !pageAdded))
				return null;

			var recipients = new Recipients();
			if (repage && current != null)
			{
				recipients.Users.AddRange((current.Dispatches ?? new List<CallDispatch>()).Select(d => d.UserId));
				recipients.Groups.AddRange((current.GroupDispatches ?? new List<CallDispatchGroup>()).Select(d => d.DepartmentGroupId));
				recipients.Units.AddRange((current.UnitDispatches ?? new List<CallDispatchUnit>()).Select(d => d.UnitId));
				recipients.Roles.AddRange((current.RoleDispatches ?? new List<CallDispatchRole>()).Select(d => d.RoleId));
			}

			recipients.Users.AddRange(additions.Users.Select(u => u.UserId).Where(u => !recipients.Users.Contains(u)));
			recipients.Groups.AddRange(additions.Groups.Select(g => g.DepartmentGroupId).Where(g => !recipients.Groups.Contains(g)));
			recipients.Units.AddRange(additions.Units.Select(u => u.UnitId).Where(u => !recipients.Units.Contains(u)));
			recipients.Roles.AddRange(additions.Roles.Select(r => r.RoleId).Where(r => !recipients.Roles.Contains(r)));
			return recipients;
		}

		/// <summary>
		/// Sends the page the update recorded in the broadcast outbox (before its added rows were written), with the saved call. A send
		/// or reload that fails leaves the recorded page to worker 12. Returns true when the queue took it.
		/// </summary>
		private async Task<bool> SendPageAsync(Call saved, CallBroadcastOutboxEntry page)
		{
			try
			{
				var populated = await _callsService.PopulateCallData(await _callsService.GetCallByIdAsync(saved.CallId), true, true, false, true, true, true, true, false, false);
				if (populated == null)
					return false;

				return await _callBroadcastOutboxService.SendPendingForCallAsync(populated, page) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Paging call {saved.CallId} after a CAD update; worker 12 sends the recorded page.");
				return false;
			}
		}

		private static void NullChildCollections(Call call)
		{
			call.Dispatches = null;
			call.GroupDispatches = null;
			call.UnitDispatches = null;
			call.RoleDispatches = null;
			call.Attachments = null;
			call.CallNotes = null;
			call.Protocols = null;
			call.References = null;
			call.Contacts = null;
			call.VideoFeeds = null;
		}

		private static bool SafeMatch(Func<Call, bool> match, Call call)
		{
			try
			{
				return call != null && !call.IsDeleted && match(call);
			}
			catch
			{
				return false;
			}
		}

		/// <summary>For the preview of a disabled config: still show the ID its source would read.</summary>
		private static EmailCallUpdateConfig WithSource(EmailCallUpdateConfig config)
		{
			var copy = config.Normalized();
			copy.Enabled = true;
			return copy;
		}

		private static string Truncate(string value, int length)
		{
			if (string.IsNullOrEmpty(value))
				return value;
			return value.Length > length ? value.Substring(0, length) + "…" : value;
		}

		/// <summary>At most <paramref name="length"/> characters, no ellipsis: for a value stored in a column of that length.</summary>
		private static string Clip(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);

		/// <summary>The provider message id as the ledger stores (and finds) it.</summary>
		private static string StoredMessageId(CallEmail email) =>
			string.IsNullOrWhiteSpace(email?.MessageId) ? null : Clip(email.MessageId, MessageIdLength);
	}
}
