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
	/// Field Records for the four operational apps (RMS plan RMS-1D). Everything here is computed from the
	/// authenticated principal and the department: the app flag, the minimum app version, the renderer capability
	/// the client reports, the verified Call/Unit/group/command context, the Protected Data state, and each
	/// definition version's own client surface. A client that sends a different origin, context or capability
	/// gets a narrower catalog, never a wider one, and every sync row is re-authorized at read time.
	/// </summary>
	public class FieldRecordsService : IFieldRecordsService
	{
		private readonly IRecordsCutoverService _cutover;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IFeatureToggleService _flags;
		private readonly IRecordDefinitionsService _definitions;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IRecordsService _records;
		private readonly IRecordWorkAssignmentsService _assignments;
		private readonly IUnitsService _units;
		private readonly IDepartmentGroupsService _groups;
		private readonly ICallsService _calls;
		private readonly IIncidentCommandService _command;
		private readonly IRecordsFieldRolloutService _rollout;

		public FieldRecordsService(IRecordsCutoverService cutover, IRecordsAuthorizationService authorization, IFeatureToggleService flags, IRecordDefinitionsService definitions,
			IDepartmentDataProtectionService protection, IRecordsService records, IRecordWorkAssignmentsService assignments, IUnitsService units, IDepartmentGroupsService groups,
			ICallsService calls, IIncidentCommandService command, IRecordsFieldRolloutService rollout)
		{
			_rollout = rollout;
			_cutover = cutover;
			_authorization = authorization;
			_flags = flags;
			_definitions = definitions;
			_protection = protection;
			_records = records;
			_assignments = assignments;
			_units = units;
			_groups = groups;
			_calls = calls;
			_command = command;
		}

		#region Preflight

		public async Task<FieldRecordPreflight> PreflightAsync(int departmentId, string userId, RmsOriginClient origin, string appVersion, string clientCapability)
		{
			var preflight = new FieldRecordPreflight
			{
				Origin = origin,
				AppVersion = appVersion,
				ClientCapability = NormalizeCapability(clientCapability),
				MinimumAppVersion = MinimumVersionFor(origin),
				ServerTimestampMs = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds()
			};

			if (!FieldRecordCatalogV1.IsFieldOrigin(origin))
			{
				preflight.Reasons.Add(FieldRecordCatalogV1.ExclusionReasons.OriginNotField);
				return preflight;
			}

			var moduleState = await _cutover.GetModuleStateAsync(departmentId);
			preflight.ModuleEnabled = moduleState.FlagEnabled;
			preflight.RecordsUsable = moduleState.RecordsUsable;
			if (!moduleState.FlagEnabled) preflight.Reasons.Add(FieldRecordCatalogV1.ExclusionReasons.ModuleDisabled);
			else if (!moduleState.RecordsUsable) preflight.Reasons.Add(FieldRecordCatalogV1.ExclusionReasons.RecordsNotUsable);

			preflight.AppEnabled = moduleState.FlagEnabled && await _flags.IsEnabledAsync(RecordsApiFlagFor(origin), departmentId);
			if (!preflight.AppEnabled) preflight.Reasons.Add(FieldRecordCatalogV1.ExclusionReasons.AppDisabled);

			if (!await _authorization.IsActiveMemberAsync(userId, departmentId)) preflight.Reasons.Add(FieldRecordCatalogV1.ExclusionReasons.NotMember);
			if (!FieldRecordCatalogV1.MeetsMinimum(appVersion, preflight.MinimumAppVersion)) preflight.Reasons.Add(FieldRecordCatalogV1.ExclusionReasons.AppVersionTooOld);

			preflight.ProtectionState = await ProtectionStateAsync(departmentId);
			preflight.Ok = preflight.Reasons.Count == 0;
			return preflight;
		}

		#endregion

		#region Context

		public async Task<FieldRecordContextVerification> VerifyContextAsync(int departmentId, string userId, RmsOriginClient origin, FieldRecordContext context)
		{
			context ??= new FieldRecordContext();
			var verification = FieldRecordContextVerification.Allowed();
			if (context.IsEmpty)
				return verification;

			if (context.CallId.HasValue)
			{
				var call = await _calls.GetCallByIdAsync(context.CallId.Value);
				if (call == null || call.DepartmentId != departmentId || !await _authorization.CanReadSourceCallAsync(userId, departmentId, call))
					return FieldRecordContextVerification.Denied(FieldRecordCatalogV1.ExclusionReasons.ContextNotVerified);
				verification.CallNumber = call.Number;
			}

			if (context.UnitId.HasValue)
			{
				var unit = await _units.GetUnitByIdAsync(context.UnitId.Value);
				if (unit == null || unit.DepartmentId != departmentId)
					return FieldRecordContextVerification.Denied(FieldRecordCatalogV1.ExclusionReasons.ContextNotVerified);
				verification.UnitName = unit.Name;
				// The Unit app authors on the apparatus the caller is actually staffed on; every other app may
				// reference a unit it can see but never claims crew membership from the client's word.
				var state = await _units.GetLastUnitStateByUnitIdAsync(unit.UnitId);
				verification.StaffedOnUnit = state?.Roles != null && state.Roles.Any(r => string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase));
				if (origin == RmsOriginClient.Unit && !verification.StaffedOnUnit)
					return FieldRecordContextVerification.Denied(FieldRecordCatalogV1.ExclusionReasons.ContextNotVerified);
			}

			if (context.GroupId.HasValue)
			{
				var group = await _groups.GetGroupByIdAsync(context.GroupId.Value);
				if (group == null || group.DepartmentId != departmentId)
					return FieldRecordContextVerification.Denied(FieldRecordCatalogV1.ExclusionReasons.ContextNotVerified);
				var visible = await _authorization.GetVisibleGroupIdsAsync(userId, departmentId);
				if (visible != null && !visible.Contains(group.DepartmentGroupId))
					return FieldRecordContextVerification.Denied(FieldRecordCatalogV1.ExclusionReasons.ContextNotAllowed);
				verification.GroupName = group.Name;
			}

			if (!string.IsNullOrWhiteSpace(context.CommandRole))
			{
				// A command role is never taken on the client's word: it must exist on the Call's active command.
				if (!context.CallId.HasValue)
					return FieldRecordContextVerification.Denied(FieldRecordCatalogV1.ExclusionReasons.ContextNotVerified);
				var command = await _command.GetActiveCommandForCallAsync(departmentId, context.CallId.Value);
				if (command == null)
					return FieldRecordContextVerification.Denied(FieldRecordCatalogV1.ExclusionReasons.ContextNotVerified);
				verification.CommandName = command.Name;
				verification.HoldsCommandRole = string.Equals(command.CurrentCommanderUserId, userId, StringComparison.OrdinalIgnoreCase);
				if (!verification.HoldsCommandRole)
				{
					var board = await _command.GetCommandBoardAsync(departmentId, context.CallId.Value);
					verification.HoldsCommandRole = board?.Nodes != null && board.Nodes.Any(n => string.Equals(n.SupervisorUserId, userId, StringComparison.OrdinalIgnoreCase)
						&& string.Equals(n.Name, context.CommandRole, StringComparison.OrdinalIgnoreCase));
				}
				if (!verification.HoldsCommandRole)
					return FieldRecordContextVerification.Denied(FieldRecordCatalogV1.ExclusionReasons.ContextNotVerified);
			}

			return verification;
		}

		#endregion

		#region Catalog

		public async Task<FieldRecordCatalog> GetCatalogAsync(int departmentId, string userId, FieldRecordCatalogRequest request)
		{
			request ??= new FieldRecordCatalogRequest();
			var context = request.Context ?? new FieldRecordContext();
			var capability = NormalizeCapability(request.ClientCapability);
			var catalog = new FieldRecordCatalog
			{
				Origin = request.Origin,
				ContextKind = context.Kind,
				ServerTimestampMs = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds()
			};

			var preflight = await PreflightAsync(departmentId, userId, request.Origin, request.AppVersion, capability);
			catalog.ProtectionState = preflight.ProtectionState;
			if (!preflight.Ok)
			{
				catalog.Reasons.AddRange(preflight.Reasons);
				return await RecordCatalogOutcomeAsync(departmentId, userId, request, capability, catalog);
			}

			var verification = await VerifyContextAsync(departmentId, userId, request.Origin, context);
			catalog.ContextVerified = verification.Ok;
			if (!verification.Ok)
			{
				catalog.Reasons.AddRange(verification.Reasons);
				return await RecordCatalogOutcomeAsync(departmentId, userId, request, capability, catalog);
			}

			catalog.ScopeStamp = await _authorization.GetReadScopeStampAsync(userId, departmentId);
			if (catalog.ScopeStamp == null)
			{
				catalog.Reasons.Add(FieldRecordCatalogV1.ExclusionReasons.NotMember);
				return await RecordCatalogOutcomeAsync(departmentId, userId, request, capability, catalog);
			}

			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.CreateRecord))
			{
				// A member who cannot author still gets an empty catalog rather than an error: the app shows read-only.
				catalog.Ok = true;
				return await RecordCatalogOutcomeAsync(departmentId, userId, request, capability, catalog);
			}

			var enforced = string.Equals(preflight.ProtectionState, DepartmentDataProtectionState.Enabled.ToString(), StringComparison.Ordinal)
				|| string.Equals(preflight.ProtectionState, DepartmentDataProtectionState.Rotating.ToString(), StringComparison.Ordinal);

			AddLockedStarters(catalog, request.Origin, context, capability);
			// A transient listing failure and a department with nothing published look identical to the app, and the
			// app caches what it is told, so say the catalog is unusable rather than handing back the starters alone.
			// It says so under its own reason: a database that hiccuped is a retry, not grounds for every device in
			// the department to throw its cache away and re-download together against the store that just failed.
			if (!await AddDepartmentDefinitionsAsync(departmentId, catalog, request.Origin, request.AppVersion, capability, context, enforced))
			{
				catalog.Reasons.Add(FieldRecordCatalogV1.ExclusionReasons.CatalogUnavailable);
				return await RecordCatalogOutcomeAsync(departmentId, userId, request, capability, catalog);
			}

			catalog.Definitions = catalog.Definitions.OrderBy(d => d.Locked ? 1 : 0).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
			catalog.Ok = true;
			return await RecordCatalogOutcomeAsync(departmentId, userId, request, capability, catalog);
		}

		/// <summary>
		/// A catalog refusal is the one rollout outcome a client cannot report faithfully — it may not have been
		/// given a reason it understands, and a refused client is exactly the one whose telemetry is unreliable.
		/// So the server records it (RMS plan RMS-1D rollout dashboards) and never lets that recording fail a read.
		/// </summary>
		private async Task<FieldRecordCatalog> RecordCatalogOutcomeAsync(int departmentId, string userId, FieldRecordCatalogRequest request, string capability, FieldRecordCatalog catalog)
		{
			try
			{
				var outcome = catalog.Ok ? "ok" : (catalog.Reasons.FirstOrDefault() ?? "denied");
				await _rollout.RecordAsync(departmentId, userId, request.Origin, request.AppVersion, capability, RmsFieldRolloutEventTypes.Catalog, outcome);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, "Field Records catalog outcome could not be recorded.");
			}
			return catalog;
		}

		/// <summary>The locked starter allowlist for the app: system definitions a field client may always start.</summary>
		private static void AddLockedStarters(FieldRecordCatalog catalog, RmsOriginClient origin, FieldRecordContext context, string capability)
		{
			foreach (var key in FieldRecordCatalogV1.LockedStarterAllowlist(origin))
			{
				var contexts = FieldRecordCatalogV1.LockedLaunchContexts(key);
				if (!contexts.Contains(context.Kind, StringComparer.OrdinalIgnoreCase))
				{
					catalog.Exclusions.Add(new FieldRecordCatalogExclusion { DefinitionKey = key, Reason = FieldRecordCatalogV1.ExclusionReasons.ContextNotAllowed });
					continue;
				}
				if (!RecordsClientCapabilities.Satisfies(capability, RecordsClientCapabilities.Locked))
				{
					catalog.Exclusions.Add(new FieldRecordCatalogExclusion { DefinitionKey = key, Reason = FieldRecordCatalogV1.ExclusionReasons.CapabilityUnsupported });
					continue;
				}
				var type = RmsDefinitionKeys.LockedTypes.TryGetValue(key, out var value) ? (RmsOperationalRecordType?)value : null;
				catalog.Definitions.Add(new FieldRecordCatalogEntry
				{
					DefinitionKey = key,
					Version = RmsDefinitionKeys.LockedDefinitionVersion,
					Name = type?.ToString() ?? key,
					Category = "System",
					Locked = true,
					RecordType = type.HasValue ? (int)type.Value : (int?)null,
					LifecyclePreset = RmsLifecyclePreset.QuickEntry.ToString(),
					LaunchContexts = contexts.ToList(),
					AllowOffline = true,
					AllowAttachments = true,
					MinimumClientCapability = RecordsClientCapabilities.Locked,
					Restricted = RmsDefinitionKeys.RestrictedClass.Contains(key),
					SupportsPrefill = true,
					PrefillVersion = 1
				});
			}
		}

		/// <summary>False when the definitions could not be listed; the caller must not report that as an empty catalog.</summary>
		private async Task<bool> AddDepartmentDefinitionsAsync(int departmentId, FieldRecordCatalog catalog, RmsOriginClient origin, string appVersion, string capability, FieldRecordContext context, bool protectionEnforced)
		{
			List<RmsRecordDefinitionVersion> published;
			List<RecordDefinitionSummary> summaries;
			try
			{
				published = await _definitions.GetPublishedAsync(departmentId) ?? new List<RmsRecordDefinitionVersion>();
				summaries = await _definitions.ListAsync(departmentId, true) ?? new List<RecordDefinitionSummary>();
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, "Field Records catalog could not list department definitions.");
				return false;
			}

			var byKey = summaries.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);
			foreach (var version in published)
			{
				var summary = byKey.TryGetValue(version.DefinitionKey, out var found) ? found : null;
				if (summary != null && summary.Retired)
				{
					catalog.Exclusions.Add(new FieldRecordCatalogExclusion { DefinitionKey = version.DefinitionKey, Reason = FieldRecordCatalogV1.ExclusionReasons.Retired });
					continue;
				}

				var surface = version.ClientSurface ?? new RecordDefinitionClientSurface();
				if (!SurfaceAllows(surface, origin))
				{
					catalog.Exclusions.Add(new FieldRecordCatalogExclusion { DefinitionKey = version.DefinitionKey, Reason = FieldRecordCatalogV1.ExclusionReasons.SurfaceNotEnabled });
					continue;
				}

				var contexts = surface.LaunchContexts.Count > 0 ? surface.LaunchContexts : new List<string> { FieldRecordCatalogV1.LaunchContexts.None };
				if (!contexts.Contains(context.Kind, StringComparer.OrdinalIgnoreCase))
				{
					catalog.Exclusions.Add(new FieldRecordCatalogExclusion { DefinitionKey = version.DefinitionKey, Reason = FieldRecordCatalogV1.ExclusionReasons.ContextNotAllowed });
					continue;
				}

				if (!FieldRecordCatalogV1.MeetsMinimum(appVersion, surface.MinimumAppVersion))
				{
					catalog.Exclusions.Add(new FieldRecordCatalogExclusion { DefinitionKey = version.DefinitionKey, Reason = FieldRecordCatalogV1.ExclusionReasons.AppVersionTooOld });
					continue;
				}

				var required = version.MinimumClientCapability ?? RecordsClientCapabilities.Derive(version.Schema);
				if (!RecordsClientCapabilities.Satisfies(capability, required))
				{
					catalog.Exclusions.Add(new FieldRecordCatalogExclusion { DefinitionKey = version.DefinitionKey, Reason = FieldRecordCatalogV1.ExclusionReasons.CapabilityUnsupported });
					continue;
				}

				var fields = version.Schema?.AllFields()?.ToList() ?? new List<RecordFieldSchema>();
				var protectedFields = fields.Any(f => f.Classification == RmsFieldClassification.Protected);
				if (protectedFields && !protectionEnforced)
				{
					// A definition that seals values needs an enrolled department; without one the field app would
					// have nowhere to put ciphertext and would quietly store plaintext instead.
					catalog.Exclusions.Add(new FieldRecordCatalogExclusion { DefinitionKey = version.DefinitionKey, Reason = FieldRecordCatalogV1.ExclusionReasons.ProtectedDataUnavailable });
					continue;
				}

				catalog.Definitions.Add(new FieldRecordCatalogEntry
				{
					DefinitionKey = version.DefinitionKey,
					Version = version.Version,
					Name = summary?.Name ?? version.DefinitionKey,
					Category = summary?.Category,
					Locked = false,
					LifecyclePreset = ((RmsLifecyclePreset)version.LifecyclePreset).ToString(),
					LaunchContexts = contexts.ToList(),
					// Protected values never sit in an offline draft, whatever the surface asks for.
					AllowOffline = surface.AllowOffline && !protectedFields,
					AllowAttachments = surface.AllowAttachments,
					MinimumAppVersion = surface.MinimumAppVersion,
					MinimumClientCapability = required,
					Restricted = fields.Any(f => f.Classification != RmsFieldClassification.Standard),
					RequiresProtectedGrant = protectedFields,
					SchemaChecksum = version.SchemaChecksum,
					SupportsPrefill = true,
					PrefillVersion = version.Version
				});
			}

			return true;
		}

		private static bool SurfaceAllows(RecordDefinitionClientSurface surface, RmsOriginClient origin)
		{
			switch (origin)
			{
				case RmsOriginClient.Responder: return surface.Responder;
				case RmsOriginClient.Unit: return surface.Unit;
				case RmsOriginClient.IncidentCommand: return surface.IncidentCommand;
				case RmsOriginClient.Dispatch: return surface.Dispatch;
				default: return false;
			}
		}

		#endregion

		#region Prefill

		public async Task<FieldRecordPrefill> PrefillAsync(int departmentId, string userId, FieldRecordCatalogRequest request, string definitionKey, int version)
		{
			request ??= new FieldRecordCatalogRequest();
			var catalog = await GetCatalogAsync(departmentId, userId, request);
			if (!catalog.Ok || !catalog.Includes(definitionKey, version))
				throw new UnauthorizedAccessException("The definition is not in this client's catalog for this context.");

			var context = request.Context ?? new FieldRecordContext();
			var entry = catalog.Definitions.First(d => string.Equals(d.DefinitionKey, definitionKey, StringComparison.OrdinalIgnoreCase) && d.Version == version);
			var now = DateTime.UtcNow;
			var prefill = new FieldRecordPrefill
			{
				DefinitionKey = entry.DefinitionKey,
				Version = entry.Version,
				PrefillVersion = entry.PrefillVersion,
				CallId = context.CallId,
				UnitId = context.UnitId,
				CalculatedOn = now
			};

			Call call = null;
			if (context.CallId.HasValue)
			{
				call = await _calls.GetCallByIdAsync(context.CallId.Value);
				if (call != null && call.DepartmentId != departmentId) call = null;
			}

			Unit unit = null;
			if (context.UnitId.HasValue)
			{
				unit = await _units.GetUnitByIdAsync(context.UnitId.Value);
				if (unit != null && unit.DepartmentId != departmentId) unit = null;
			}

			var group = context.GroupId.HasValue ? await _groups.GetGroupByIdAsync(context.GroupId.Value) : await _groups.GetGroupForUserAsync(userId, departmentId);
			if (group != null && group.DepartmentId == departmentId) prefill.StationGroupId = group.DepartmentGroupId;

			if (unit != null) prefill.SuggestedUnitIds.Add(unit.UnitId);
			prefill.SuggestedParticipantUserIds.Add(userId);

			// Locked definitions carry no schema here; the app fills their fixed fields from the same context block.
			if (entry.Locked)
				return prefill;

			var schemaVersion = await _definitions.GetVersionAsync(departmentId, entry.DefinitionKey, entry.Version);
			var schema = schemaVersion?.Schema;
			if (schema == null)
				return prefill;

			foreach (var section in schema.Sections.Where(s => !s.Repeating))
			{
				foreach (var field in section.Fields)
				{
					// Prefill is minimum-necessary: identity and time only, never a restricted or protected value.
					if (field.Classification != RmsFieldClassification.Standard) continue;
					switch (field.Type)
					{
						case RmsFieldType.CallReference when call != null:
							Add(prefill, section.Key, field.Key, call.CallId.ToString(), "call", call.CallId.ToString(), now, referenceType: "call", referenceId: call.CallId.ToString());
							break;
						case RmsFieldType.Unit when unit != null:
							Add(prefill, section.Key, field.Key, unit.UnitId.ToString(), "unit", unit.UnitId.ToString(), now, referenceType: "unit", referenceId: unit.UnitId.ToString());
							break;
						case RmsFieldType.Group when group != null:
							Add(prefill, section.Key, field.Key, group.DepartmentGroupId.ToString(), "group", group.DepartmentGroupId.ToString(), now, referenceType: "group", referenceId: group.DepartmentGroupId.ToString());
							break;
						case RmsFieldType.Person when IsAuthorField(field.Key):
							Add(prefill, section.Key, field.Key, userId, "user", userId, now, referenceType: "user", referenceId: userId);
							break;
						case RmsFieldType.Address when call != null && IsLocationField(field.Key):
							Add(prefill, section.Key, field.Key, call.Address, "call.address", call.CallId.ToString(), now);
							break;
						case RmsFieldType.DateTime when call != null && IsStartField(field.Key):
							Add(prefill, section.Key, field.Key, call.LoggedOn.ToString("O"), "call.logged_on", call.CallId.ToString(), now);
							break;
						case RmsFieldType.DateTime when call == null && IsStartField(field.Key):
							Add(prefill, section.Key, field.Key, now.ToString("O"), "now", null, now);
							break;
					}
				}
			}

			return prefill;
		}

		private static void Add(FieldRecordPrefill prefill, string sectionKey, string fieldKey, string value, string source, string sourceId, DateTime now, string referenceType = null, string referenceId = null)
		{
			if (string.IsNullOrWhiteSpace(value)) return;
			prefill.Values.Add(new RecordValueInput { SectionKey = sectionKey, FieldKey = fieldKey, Value = value, ReferenceType = referenceType, ReferenceId = referenceId });
			prefill.Provenance.Add(new FieldRecordPrefillProvenance { FieldKey = fieldKey, Source = source, SourceId = sourceId, CapturedOn = now });
		}

		private static bool IsAuthorField(string key) => Contains(key, "author", "reported_by", "completed_by", "member", "officer", "recorded_by");
		private static bool IsLocationField(string key) => Contains(key, "location", "address", "scene", "site");
		private static bool IsStartField(string key) => Contains(key, "start", "began", "occurred", "logged", "dispatched", "time");
		private static bool Contains(string key, params string[] needles) => key != null && needles.Any(n => key.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);

		#endregion

		#region Sync

		public async Task<FieldRecordSyncBundle> SyncAsync(int departmentId, string userId, FieldRecordSyncRequest request, CancellationToken cancellationToken = default)
		{
			request ??= new FieldRecordSyncRequest();
			var take = Math.Max(1, Math.Min(RecordsFieldConfig.SyncTakeMax, request.Take <= 0 ? RecordsFieldConfig.SyncTakeMax : request.Take));
			var now = DateTime.UtcNow;
			var bundle = new FieldRecordSyncBundle { Since = request.Since, ServerTimestampMs = new DateTimeOffset(now).ToUnixTimeMilliseconds() };

			var catalogRequest = new FieldRecordCatalogRequest { Origin = request.Origin, AppVersion = request.AppVersion, ClientCapability = request.ClientCapability, Context = request.Context };
			var catalog = await GetCatalogAsync(departmentId, userId, catalogRequest);
			if (request.IncludeCatalog) bundle.Catalog = catalog;
			if (!catalog.Ok)
			{
				bundle.Reasons.AddRange(catalog.Reasons);
				// A scope or policy refusal invalidates what the device holds; a transient catalog failure does not,
				// so that one answers "retry" rather than telling every syncing device to reset at once.
				bundle.ResetRequired = request.Since > 0 && !catalog.Reasons.Contains(FieldRecordCatalogV1.ExclusionReasons.CatalogUnavailable);
				bundle.ServerTimestampMs = 0;
				return bundle;
			}

			bundle.ScopeStamp = catalog.ScopeStamp;
			// A scope change invalidates every cached row on the device, catalog included.
			if (request.Since > 0 && !string.Equals(request.ScopeStamp, bundle.ScopeStamp, StringComparison.Ordinal))
			{
				bundle.ResetRequired = true;
				bundle.ServerTimestampMs = 0;
				return bundle;
			}

			var since = request.Since <= 0 ? (DateTime?)null : DateTimeOffset.FromUnixTimeMilliseconds(request.Since).UtcDateTime;
			var rows = await _records.GetChangesSinceAsync(departmentId, since, take + 1, request.SinceId);
			bundle.HasMore = rows.Count > take;
			var page = rows.Take(take).ToList();
			foreach (var projection in page)
			{
				cancellationToken.ThrowIfCancellationRequested();
				// The assignment queue narrows; live authorization decides. A row the caller may not read leaves
				// as a tombstone id so a previously cached copy is evicted, never as content.
				if (projection.DeletedOn.HasValue || !await _authorization.CanUserViewRecordAsync(userId, projection.RmsRecordSearchProjectionId, departmentId))
					bundle.Tombstones.Add(projection.RmsRecordSearchProjectionId);
				else
					bundle.Changes.Add(projection);
			}

			if (bundle.HasMore && page.Count > 0)
			{
				var last = page[page.Count - 1];
				bundle.ServerTimestampMs = new DateTimeOffset(DateTime.SpecifyKind(last.ModifiedOn, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
				bundle.ServerCursorId = last.RmsRecordSearchProjectionId;
			}
			else
			{
				// Replay the final millisecond so a concurrent write in that clock bucket is not skipped.
				bundle.ServerTimestampMs = new DateTimeOffset(now).ToUnixTimeMilliseconds() - 1;
			}

			bundle.Drafts = await DraftsAsync(departmentId, userId);
			bundle.Assignments = await _assignments.GetQueueAsync(departmentId, userId, request.Context, RecordsFieldConfig.AssignmentsMax);

			var finalScope = await _authorization.GetReadScopeStampAsync(userId, departmentId);
			if (finalScope == null || !string.Equals(finalScope, bundle.ScopeStamp, StringComparison.Ordinal))
			{
				// Policy moved under the read; the whole page is discarded rather than partially trusted.
				return new FieldRecordSyncBundle { Since = request.Since, ScopeStamp = finalScope, ResetRequired = true, ServerTimestampMs = 0, Catalog = bundle.Catalog, Ok = finalScope != null };
			}

			bundle.Ok = true;
			return bundle;
		}

		private async Task<List<RmsRecordSearchProjection>> DraftsAsync(int departmentId, string userId)
		{
			var query = new RmsRecordQuery
			{
				OwnerUserId = userId,
				ViewerUserId = userId,
				States = new List<int> { (int)RmsRecordState.Draft, (int)RmsRecordState.Returned, (int)RmsRecordState.ReadyForReview },
				VisibleGroupIds = await _authorization.GetVisibleGroupIdsAsync(userId, departmentId),
				Take = RecordsFieldConfig.SyncDraftsMax
			};
			return await _records.QueryAsync(departmentId, query) ?? new List<RmsRecordSearchProjection>();
		}

		#endregion

		#region Helpers

		private static string RecordsApiFlagFor(RmsOriginClient origin)
		{
			switch (origin)
			{
				case RmsOriginClient.Responder: return FeatureFlagKeys.RecordsFieldResponder;
				case RmsOriginClient.Unit: return FeatureFlagKeys.RecordsFieldUnit;
				case RmsOriginClient.IncidentCommand: return FeatureFlagKeys.RecordsFieldIncidentCommand;
				case RmsOriginClient.Dispatch: return FeatureFlagKeys.RecordsFieldDispatch;
				default: return FeatureFlagKeys.RecordsSystem;
			}
		}

		private static string MinimumVersionFor(RmsOriginClient origin)
		{
			switch (origin)
			{
				case RmsOriginClient.Responder: return RecordsFieldConfig.MinimumResponderVersion;
				case RmsOriginClient.Unit: return RecordsFieldConfig.MinimumUnitVersion;
				case RmsOriginClient.IncidentCommand: return RecordsFieldConfig.MinimumIncidentCommandVersion;
				case RmsOriginClient.Dispatch: return RecordsFieldConfig.MinimumDispatchVersion;
				default: return null;
			}
		}

		/// <summary>An unknown or missing capability is the oldest one, never the newest: a client is never assumed able.</summary>
		private static string NormalizeCapability(string capability)
		{
			var trimmed = (capability ?? string.Empty).Trim().ToLowerInvariant();
			return RecordsClientCapabilities.Rank(trimmed) >= 0 ? trimmed : RecordsClientCapabilities.Locked;
		}

		private async Task<string> ProtectionStateAsync(int departmentId)
		{
			try
			{
				var policy = await _protection.GetPolicyByDepartmentIdAsync(departmentId);
				return policy == null ? DepartmentDataProtectionState.Disabled.ToString() : ((DepartmentDataProtectionState)policy.State).ToString();
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, "Field Records preflight could not read the Protected Data policy.");
				return DepartmentDataProtectionState.Disabled.ToString();
			}
		}

		#endregion
	}
}
