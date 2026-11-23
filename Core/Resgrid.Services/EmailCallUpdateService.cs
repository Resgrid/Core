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
	/// (CallEmailUpdates): hashes of what the CAD set, never content.
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

			var department = request.Department ?? await _departmentsService.GetDepartmentByIdAsync(request.DepartmentId, false);

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
						: await UpdateAsync(request, department, call, parsed, config, cadCallId, history, others, cancellationToken);
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
				await RecordAsync(request, closed?.CallId, cadCallId, outcome, null, null);
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
					await AddNoteAsync(department, closed.CallId,
						Header(cadCallId, "arrived for a call that is already closed; nothing was changed") + Lines(changes.Select(c => Describe(c, false))));
					await RecordAsync(request, closed.CallId, cadCallId, CallEmailUpdateOutcomes.ClosedNote, changes.Select(c => c.Group), null);
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
			EmailCallUpdateConfig config, string cadCallId, List<CallEmailUpdate> history, List<int> otherCallIds, CancellationToken cancellationToken)
		{
			var priorities = await LoadPrioritiesAsync(request.DepartmentId);
			var sentOn = request.Email?.SentOn;
			var lastApplied = history.Where(h => h.Outcome != (int)CallEmailUpdateOutcomes.Stale && h.SentOn.HasValue).Select(h => h.SentOn).DefaultIfEmpty(null).Max();

			if (sentOn.HasValue && lastApplied.HasValue && sentOn.Value < lastApplied.Value)
			{
				var stale = Diff(call, parsed, priorities);
				await AddNoteAsync(department, call.CallId, Header(cadCallId, "arrived after a newer one; its values were not applied") +
					Lines(stale.Select(c => Describe(c, false))));
				await RecordAsync(request, call.CallId, cadCallId, CallEmailUpdateOutcomes.Stale, stale.Select(c => c.Group), null);
				return new EmailCallUpdateResult { Outcome = CallEmailUpdateOutcomes.Stale, Call = call, Changes = stale };
			}

			var previous = LatestHashes(history);
			var hashes = new Dictionary<string, string>(previous ?? new Dictionary<string, string>());
			var changes = new List<EmailCallFieldChange>();
			var notes = new List<string>();
			var locationChanged = false;
			var priorityChanged = false;

			bool EditedInResgrid(string group, string currentValue) =>
				config.DispatcherEditsWin && previous != null && previous.TryGetValue(group, out var hash) && hash != CadEmailText.Hash(currentValue);

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
					change.Applied = true;
					locationChanged = true;
				}

				changes.Add(change);
			}
			else if (parsed.Has(CallEmailParsedFields.Coordinates) && !CadEmailText.SameValue(call.GeoLocationData, parsed.Call.GeoLocationData) &&
				config.GetLocationPolicy() == CadFieldPolicies.Update && !EditedInResgrid(GroupLocation, call.Address))
			{
				// Same address, new coordinates from the CAD (a corrected map pin).
				changes.Add(new EmailCallFieldChange { Group = GroupLocation, OldValue = call.GeoLocationData, NewValue = parsed.Call.GeoLocationData, Applied = true });
				call.GeoLocationData = parsed.Call.GeoLocationData;
				locationChanged = true;
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
					priorityChanged = true;
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
			var repage = live && (repageRule == CadRepageRules.EveryUpdate ||
				(repageRule == CadRepageRules.AnyChange && changes.Any(c => c.Applied && c.Group != GroupResources)) ||
				(repageRule == CadRepageRules.LocationOrPriorityChanged && (locationChanged || priorityChanged)));

			if (!applied && !repage)
			{
				// Nothing to save: the call keeps every value. What the CAD sent that was not applied is still written down.
				if (otherCallIds.Count > 0)
					notes.Add("This email also matched calls " + string.Join(", ", otherCallIds) + "; only this call is updated.");
				if (changes.Count > 0 || notes.Count > 0)
					await AddNoteAsync(department, call.CallId, Header(cadCallId, "changed nothing on this call") + Lines(changes.Select(c => Describe(c, true)).Concat(notes)));
				await RecordAsync(request, call.CallId, cadCallId, CallEmailUpdateOutcomes.NoChange, null, hashes.Count > 0 ? hashes : null);
				return new EmailCallUpdateResult { Outcome = CallEmailUpdateOutcomes.NoChange, Call = call, Changes = changes };
			}

			if (repage)
			{
				call.LastDispatchedOn = DateTime.UtcNow;
				if (repageRule == CadRepageRules.EveryUpdate)
					call.IncreaseDispatchCount();
			}

			// The call row alone (no child collections: the repository's cascade would delete dispatch rows a person added meanwhile);
			// added resources are inserted as their own rows below.
			NullChildCollections(call);
			var saved = await _callsService.SaveCallAsync(call, cancellationToken);
			var addedCount = await InsertAdditionsAsync(saved, additions, cancellationToken);

			if (otherCallIds.Count > 0)
				notes.Add("This email also matched calls " + string.Join(", ", otherCallIds) + "; only this call was updated.");

			await AddNoteAsync(department, saved.CallId, Header(cadCallId, applied ? "updated this call" : "paged this call again") +
				Lines(changes.Select(c => Describe(c, true)).Concat(notes)));
			await RecordAsync(request, saved.CallId, cadCallId, CallEmailUpdateOutcomes.Updated, changes.Where(c => c.Applied).Select(c => c.Group), hashes);

			_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent { DepartmentId = saved.DepartmentId, Call = saved });

			var repaged = false;
			if (saved.State == (int)CallStates.Active)
			{
				if (repage)
					repaged = await PageAsync(saved, department, null);
				else if (addedCount > 0 && config.PageAddedResources)
					await PageAsync(saved, department, additions);
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
			var verb = closeKind == CadEmailCloseKinds.Cancel ? "cancelled" : "closed";
			var sentOn = request.Email?.SentOn;
			var lastApplied = history.Where(h => h.Outcome != (int)CallEmailUpdateOutcomes.Stale && h.SentOn.HasValue).Select(h => h.SentOn).DefaultIfEmpty(null).Max();
			if (sentOn.HasValue && lastApplied.HasValue && sentOn.Value < lastApplied.Value)
			{
				await AddNoteAsync(department, call.CallId, Header(cadCallId, $"{verb} the call, but arrived after a newer email; the call was left open"));
				await RecordAsync(request, call.CallId, cadCallId, CallEmailUpdateOutcomes.Stale, null, null);
				return new EmailCallUpdateResult { Outcome = CallEmailUpdateOutcomes.Stale, Call = call };
			}

			if (await _callClosureService.GetBlockingIncidentCommandAsync(call.DepartmentId, call.CallId) != null)
			{
				await AddNoteAsync(department, call.CallId, Header(cadCallId, $"{verb} this call; it is still open here because incident command is active"));
				await RecordAsync(request, call.CallId, cadCallId, CallEmailUpdateOutcomes.CloseBlocked, null, null);
				return new EmailCallUpdateResult { Outcome = CallEmailUpdateOutcomes.CloseBlocked, Call = call };
			}

			var wentOut = call.State == (int)CallStates.Active;
			var closedBy = department?.ManagingUserId;

			call.State = (int)(closeKind == CadEmailCloseKinds.Cancel ? CallStates.Cancelled : CallStates.Closed);
			call.ClosedOn = DateTime.UtcNow;
			call.ClosedByUserId = closedBy;
			call.CompletedNotes = closeKind == CadEmailCloseKinds.Cancel ? "Cancelled from CAD email." : "Closed from CAD email.";
			NullChildCollections(call);

			var saved = await _callsService.SaveCallAsync(call, cancellationToken);
			await AddNoteAsync(department, saved.CallId, Header(cadCallId, $"{verb} this call") +
				(otherCallIds.Count > 0 ? Lines(new[] { "This email also matched calls " + string.Join(", ", otherCallIds) + "; only this call was closed." }) : string.Empty));
			await RecordAsync(request, saved.CallId, cadCallId, closeKind == CadEmailCloseKinds.Cancel ? CallEmailUpdateOutcomes.Cancelled : CallEmailUpdateOutcomes.Closed, null, null);

			_eventAggregator.SendMessage<CallClosedEvent>(new CallClosedEvent { DepartmentId = saved.DepartmentId, Call = saved });

			try
			{
				var populated = await _callsService.PopulateCallData(await _callsService.GetCallByIdAsync(saved.CallId), true, false, false, true, true, true, false, false, false);
				if (wentOut && populated != null && ((populated.GroupDispatches != null && populated.GroupDispatches.Any()) || (populated.UnitDispatches != null && populated.UnitDispatches.Any())))
					await _callDispatchStatusService.ApplyReleaseStatusesAsync(populated, cancellationToken: cancellationToken);

				if (config.NotifyOnClose && wentOut && populated != null)
					await _callClosureService.NotifyCallClosedAsync(populated, closedBy, cancellationToken);
			}
			catch (Exception ex)
			{
				// The call is closed; a release status or close notice that fails is logged, never a reason to reopen it.
				Logging.LogException(ex, $"Releasing or notifying call {saved.CallId} after a CAD close.");
			}

			return new EmailCallUpdateResult { Outcome = closeKind == CadEmailCloseKinds.Cancel ? CallEmailUpdateOutcomes.Cancelled : CallEmailUpdateOutcomes.Closed, Call = saved };
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

		private async Task AddNoteAsync(Department department, int callId, string text)
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
			}
			catch (Exception ex)
			{
				// The update is saved; a note that could not be written is logged, never a reason to fail the email.
				Logging.LogException(ex, $"Writing the CAD update note on call {callId}.");
			}
		}

		private async Task RecordAsync(EmailCallUpdateRequest request, int? callId, string cadCallId, CallEmailUpdateOutcomes outcome,
			IEnumerable<string> changedGroups, Dictionary<string, string> hashes)
		{
			try
			{
				var groups = changedGroups?.Distinct().ToList();
				await _callEmailUpdatesRepository.InsertAsync(new CallEmailUpdate
				{
					CallEmailUpdateId = Guid.NewGuid().ToString(),
					DepartmentId = request.DepartmentId,
					CallId = callId,
					CadCallId = cadCallId,
					Source = (int)request.Source,
					IntakeChannelId = request.IntakeChannelId,
					MessageId = Truncate(request.Email?.MessageId, 500),
					SentOn = request.Email?.SentOn,
					ReceivedOn = DateTime.UtcNow,
					Outcome = (int)outcome,
					ChangedFields = groups == null || groups.Count == 0 ? null : Truncate(string.Join(",", groups), 500),
					FieldHashes = hashes == null || hashes.Count == 0 ? null : JsonConvert.SerializeObject(hashes)
				}, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Recording a CAD email update for department {request.DepartmentId}.");
			}
		}

		/// <summary>The CAD's values on a new call: what later emails are compared with to tell a person's edit from the CAD's.</summary>
		private static Dictionary<string, string> Snapshot(Call call, Dictionary<string, string> into)
		{
			var hashes = into ?? new Dictionary<string, string>();
			hashes[GroupLocation] = CadEmailText.Hash(call.Address);
			hashes[GroupPriority] = CadEmailText.Hash(call.Priority.ToString());
			hashes[GroupType] = CadEmailText.Hash(call.Type);
			hashes[GroupName] = CadEmailText.Hash(call.Name);
			if (!ProtectedDataEnvelope.IsEnveloped(call.NatureOfCall))
				hashes[GroupNature] = CadEmailText.Hash(call.NatureOfCall);
			return hashes;
		}

		private static Dictionary<string, string> LatestHashes(List<CallEmailUpdate> history)
		{
			var latest = history.Where(h => !string.IsNullOrWhiteSpace(h.FieldHashes)).OrderBy(h => h.ReceivedOn).LastOrDefault();
			if (latest == null)
				return null;

			try
			{
				return JsonConvert.DeserializeObject<Dictionary<string, string>>(latest.FieldHashes);
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
		}

		/// <summary>The units, groups and roles the email names that the call does not have, and the units CAD stopped naming.</summary>
		private async Task<Additions> FindAdditionsAsync(Call call, CallEmailParseResult parsed)
		{
			var additions = new Additions();
			if (!parsed.Has(CallEmailParsedFields.Resources) || parsed.Call == null)
				return additions;

			var current = await _callsService.PopulateCallData(await _callsService.GetCallByIdAsync(call.CallId), true, false, false, true, true, true, false, false, false);
			if (current == null)
				return additions;

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

		/// <summary>
		/// Pages through the broadcast outbox, so a refused page is retried by worker 12: the resources <paramref name="only"/> names, or
		/// everyone on the call (a re-page). Returns true when the queue took it.
		/// </summary>
		private async Task<bool> PageAsync(Call saved, Department department, Additions only)
		{
			try
			{
				var populated = await _callsService.PopulateCallData(await _callsService.GetCallByIdAsync(saved.CallId), true, true, false, true, true, true, true, false, false);
				if (populated == null)
					return false;

				IEnumerable<string> users;
				IEnumerable<int> groups, units, roles;
				if (only != null)
				{
					users = only.Users.Select(u => u.UserId);
					groups = only.Groups.Select(g => g.DepartmentGroupId);
					units = only.Units.Select(u => u.UnitId);
					roles = only.Roles.Select(r => r.RoleId);
				}
				else
				{
					users = (populated.Dispatches ?? new List<CallDispatch>()).Select(d => d.UserId);
					groups = (populated.GroupDispatches ?? new List<CallDispatchGroup>()).Select(d => d.DepartmentGroupId);
					units = (populated.UnitDispatches ?? new List<CallDispatchUnit>()).Select(d => d.UnitId);
					roles = (populated.RoleDispatches ?? new List<CallDispatchRole>()).Select(d => d.RoleId);
				}

				var entry = await _callBroadcastOutboxService.RecordAsync(populated, users.ToList(), groups.ToList(), units.ToList(), roles.ToList(), department?.ManagingUserId);
				if (entry == null)
					return false;

				return await _callBroadcastOutboxService.SendPendingForCallAsync(populated, entry) > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Paging call {saved.CallId} after a CAD update.");
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
	}
}
