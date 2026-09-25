using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.AdminAssist;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Attended, read-only troubleshooting. No send, mutation, credential probe or model dependency.</summary>
	public sealed partial class AdminAssistDiagnosticSource(IAdminAssistDiagnosticStore store, IAdminAssistAccessService access,
		IAdminAssistCatalog catalog, IAuthorizationService visibility, IRecordsAuthorizationService membership,
		IDepartmentMembersRepository members, ICallsService calls, IUnitsService units, IUsersService users,
		IUserProfileService profiles, IProtectedReadService protectedRead, IProtectedGrantContext grant,
		IEnumerable<IAdminAssistEvidenceSource> sources, IDepartmentsService departments,
		IDepartmentGroupsService groups, IPersonnelRolesService roles,
		ICertificationService qualifications, IShiftsService shifts, IChecklistsService checklists,
		IWorkOrdersService orders, IWorkOrderReportingService orderReports, IActionLogsRepository actions,
		IUnitsRepository unitRows, IAdminAssistPermissionEvaluator permissionEvaluator) : IAdminAssistDiagnosticSource
	{
		public async Task RequireScopeAsync(AdminAssistActor actor, DiagnosticRequest r, CancellationToken ct)
		{
			if (!await access.CanAccessAsync(actor, false, ct).WaitAsync(ct)) throw new UnauthorizedAccessException();
			if (r.MemberId != null)
			{
				var member = await members.GetDepartmentMemberByDepartmentIdAndUserIdAsync(actor.DepartmentId, r.MemberId).WaitAsync(ct);
				if (member == null || member.DepartmentId != actor.DepartmentId || member.UserId != r.MemberId || member.IsDeleted ||
					!await visibility.CanUserViewPersonAsync(actor.UserId, r.MemberId, actor.DepartmentId).WaitAsync(ct)) throw new UnauthorizedAccessException();
			}
			if (r.CallId.HasValue)
			{
				if (!await visibility.CanUserViewCallAsync(actor.UserId, r.CallId.Value).WaitAsync(ct)) throw new UnauthorizedAccessException();
				var call = await calls.GetCallByIdAsync(r.CallId.Value, true).WaitAsync(ct);
				if (call == null || call.DepartmentId != actor.DepartmentId) throw new UnauthorizedAccessException();
			}
			if (r.UnitId.HasValue)
			{
				if (!await visibility.CanUserViewUnitAsync(actor.UserId, r.UnitId.Value).WaitAsync(ct)) throw new UnauthorizedAccessException();
				var unit = await unitRows.GetByIdAsync(r.UnitId.Value).WaitAsync(ct);
				if (unit == null || unit.DepartmentId != actor.DepartmentId) throw new UnauthorizedAccessException();
			}
			if (r.RoleId.HasValue && !await visibility.CanUserViewRoleAsync(actor.UserId, r.RoleId.Value).WaitAsync(ct)) throw new UnauthorizedAccessException();
			if (r.GroupId.HasValue)
			{
				var group = await groups.GetGroupByIdAsync(r.GroupId.Value, true).WaitAsync(ct);
				if (group?.DepartmentId != actor.DepartmentId || !await visibility.CanUserEditDepartmentGroupAsync(actor.UserId, r.GroupId.Value).WaitAsync(ct)) throw new UnauthorizedAccessException();
			}
			if (r.Flow == "map" && await permissionEvaluator.EvaluateCurrentAsync(actor, actor.UserId,
				r.UnitId.HasValue ? "CanSeeUnitLocations" : "CanSeePersonnelLocations",
				r.UnitId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? r.MemberId, ct) != true) throw new UnauthorizedAccessException();
			if (r.CapabilityId != null && !catalog.Capabilities.Any(c => c.Id == r.CapabilityId)) throw new ArgumentException("Unknown capability.");
		}
		private static DiagnosticCheck Check(string id, bool? issue, DateTime now, string basis = "Current", decimal? value = null,
			string source = "Configuration", string destination = null) => new(id,
			issue == null ? "InsufficientEvidence" : issue.Value ? basis == "Historical" ? "ConfirmedCause" : "PossibleCause" : "NoIssueFound",
			basis, "Diagnostic.Check." + id, source, DiagnosticPolicy.Version, now, value, destination);
		public async Task<DiagnosticSourceResult> ReadAsync(AdminAssistActor actor, DiagnosticRequest r, DateTime now, CancellationToken ct)
		{
			await RequireScopeAsync(actor, r, ct);
			var checks = new List<DiagnosticCheck>();
			IReadOnlyList<DiagnosticTraceAttempt> attempts = Array.Empty<DiagnosticTraceAttempt>(); bool truncated = false;
			IReadOnlyList<DiagnosticStatusHeader> statusHistory = Array.Empty<DiagnosticStatusHeader>();
			async Task Read(string name, Func<Task> operation)
			{
				int before = checks.Count;
				try { await operation().WaitAsync(ct); }
				catch (OperationCanceledException) { throw; }
				catch (UnauthorizedAccessException) { checks.RemoveRange(before, checks.Count - before); checks.Add(Check(name, null, now, "Restricted")); }
				catch (Exception) { checks.RemoveRange(before, checks.Count - before); checks.Add(Check(name, null, now)); }
			}
			if (r.Flow == "paging")
			{
				await Read("PagingPreferences", async () =>
				{
					var profile = await profiles.GetProfileByUserIdAsync(r.MemberId, true).WaitAsync(ct) ?? throw new InvalidOperationException();
					var channels = NotificationChannelSelection.From(profile.SendSms, profile.MobileNumberVerified, profile.SendEmail, profile.EmailVerified, profile.SendPush);
					checks.Add(Check("PagingPreferences", !channels.Sms && !channels.Email && !channels.Push, now, source: "UserProfile"));
					checks.Add(Check("MemberActive", !await membership.IsAssignableMemberAsync(r.MemberId, actor.DepartmentId).WaitAsync(ct), now, source: "Membership"));
				});
				await Read("HistoricalTrace", async () =>
				{
					var rows = await store.ReadDiagnosticTracesAsync(actor.DepartmentId, r.CallId.Value, r.FromUtc, r.UntilUtc, 2000, ct);
					truncated = rows.Count > 2000;
					var projection = rows.Take(2000).ToArray();
					var fields = new Dictionary<string, (Func<AdminAssistDispatchTraceRow, string>, Action<AdminAssistDispatchTraceRow, string>)> { ["adminassistdispatchtraces.content"] = (x => x.Content, (x, value) => x.Content = value) };
					await protectedRead.ResolveRecordsEntitiesForReadAsync(actor.DepartmentId, projection.Select(x => (x, x.AdminAssistDispatchTraceId)).ToArray(), fields, grant.GrantToken, actor.UserId, ct);
					var observed = new List<DispatchTraceObservation>();
					foreach (var row in projection)
					{
						if (row.Content == null || !row.Content.TrimStart().StartsWith("{", StringComparison.Ordinal)) throw new UnauthorizedAccessException();
						var o = JsonSerializer.Deserialize<DispatchTraceObservation>(row.Content);
						if (o == null || row.DepartmentId != actor.DepartmentId || row.CallId != r.CallId || o.DepartmentId != actor.DepartmentId || o.CallId != r.CallId || o.Id != row.AdminAssistDispatchTraceId || !Guid.TryParseExact(o.Id, "D", out _) ||
							o.AttemptId != row.AttemptId || o.Stage.ToString() != row.Stage || o.ResolverVersion != row.ResolverVersion || !Guid.TryParseExact(o.AttemptId, "D", out _) || o.OccurredOnUtc < r.FromUtc || o.OccurredOnUtc >= r.UntilUtc ||
							!Enum.IsDefined(o.Stage) || !Enum.IsDefined(o.Channel) || !Enum.IsDefined(o.Reason) || o.Sequence is < 1 or > 10001 || o.PriorDropped < 0 ||
							o.LogicalMessageId != null && !Guid.TryParseExact(o.LogicalMessageId, "D", out _)) throw new InvalidOperationException();
						observed.Add(o);
					}
					// Sequence counts include the whole broadcast. Do not disclose even those counts when
					// another recorded recipient is outside this administrator's current source scope.
					foreach (var person in observed.Where(o => o.Channel != DispatchTraceChannel.UnitPush && !string.IsNullOrWhiteSpace(o.RecipientId)).Select(o => o.RecipientId).Distinct())
						if (!await visibility.CanUserViewPersonAsync(actor.UserId, person, actor.DepartmentId).WaitAsync(ct)) throw new UnauthorizedAccessException();
					foreach (var unit in observed.Where(o => o.Channel == DispatchTraceChannel.UnitPush && !string.IsNullOrWhiteSpace(o.RecipientId)).Select(o => o.RecipientId).Distinct())
					{
						if (!int.TryParse(unit, out var id) || !await visibility.CanUserViewUnitAsync(actor.UserId, id).WaitAsync(ct)) throw new UnauthorizedAccessException();
						if ((await unitRows.GetByIdAsync(id).WaitAsync(ct))?.DepartmentId != actor.DepartmentId) throw new UnauthorizedAccessException();
					}
					truncated |= observed.Select(o => o.AttemptId).Distinct().Count() > 20;
					attempts = DiagnosticPolicy.Trace(observed, r.MemberId, truncated);
					checks.Add(Check("HistoricalTrace", attempts.Count == 0 || attempts.Any(a => !a.Complete) || truncated ? null : false, r.UntilUtc, "Historical", attempts.Count, "DispatchTrace"));
					foreach (var attempt in attempts)
					{
						var events = attempt.Events;
						// Positive recorded failures are scoped to their channel attempt, never a claim that every route failed.
						if (events.Any(e => e.Stage is "Failed" or "ProviderDeclined" or "ServiceDeclined")) checks.Add(Check("ChannelFailure", true, events.First(e => e.Stage is "Failed" or "ProviderDeclined" or "ServiceDeclined").OccurredOnUtc, "Historical", source: "DispatchTrace"));
						if (events.Any(e => e.Stage == "Skipped" && e.Reason != "DuplicateRoute")) checks.Add(Check("ChannelSkipped", true, events.First(e => e.Stage == "Skipped" && e.Reason != "DuplicateRoute").OccurredOnUtc, "Historical", source: "DispatchTrace"));
						if (attempt.Complete && events.All(e => e.Stage != "Selected")) checks.Add(Check("NotSelected", true, r.UntilUtc, "Historical", source: "DispatchTrace"));
					}
				});
				checks.Add(Check("DeliveryUnknown", null, now)); checks.Add(Check("PushDeviceHealth", null, now));
			}
			if (r.Flow == "map") await Read("MapMarker", () => MapAsync(actor, r, now, checks, ct));
			if (r.Flow == "access") await Read("EffectivePermission", () => PermissionAsync(actor, r, now, checks, ct));
			if (r.Flow is "access" or "integration") await Read("CapabilityAccess", async () =>
			{
				var capability = await access.GetCapabilityAsync(actor, r.CapabilityId, ct);
				checks.Add(Check("CapabilityAccess", capability.State == EvidenceState.Unknown ? null : capability.State != EvidenceState.Known, now, source: "CapabilityAccess", destination: capability.Destination));
				checks.Add(Check("Entitlement", capability.CommercialState is null or EvidenceState.Unknown ? null : capability.CommercialState == EvidenceState.Unavailable, now, source: "Entitlement"));
			});
			if (r.Flow == "imports")
			{
				await EvidenceAsync(actor, now, checks, "EmailImportPolling", new[] { "importHeartbeatMissing", "emailImportFailureCount", "emailImportSourceCount" }, ct);
				await EvidenceAsync(actor, now, checks, "DepartmentSettings", new[] { "EnableTextToCall", "textSourcePresent" }, ct);
				checks.Add(Check("ImportAcceptance", null, now));
			}
			if (r.Flow == "statuses")
			{
				await EvidenceAsync(actor, now, checks, "DepartmentSettings", new[] { "DisabledAutoAvailable", "AutoSetStatusForShiftDispatchPersonnel", "PersonnelOnUnitSetUnitStatus", "ShiftCallDispatchPersonnelStatusToSet", "ShiftCallReleasePersonnelStatusToSet", "UnitCallDispatchStatusToSet", "UnitCallReleaseStatusToSet" }, ct);
				await Read("StatusHistory", async () =>
				{
					var history = await store.ReadDiagnosticStatusesAsync(actor.DepartmentId, r.MemberId, r.UnitId, r.FromUtc, r.UntilUtc, ct);
					statusHistory = history.Take(100).ToArray();
					checks.Add(Check("StatusHistory", history.Count > 100 ? null : false, r.UntilUtc, "Historical", Math.Min(history.Count, 100), "StatusHistory"));
				});
				checks.Add(Check("StatusWriterUnknown", null, now));
			}
			if (r.Flow == "coverage") await Read("QualifiedRoster", () => CoverageAsync(actor, r, now, checks, ct));
			if (r.Flow == "equipment")
			{
				await Read("EquipmentReadiness", () => EquipmentChecksAsync(actor, r, now, checks, ct));
				await Read("MaintenanceReadiness", () => MaintenanceAsync(actor, r, now, checks, ct));
			}
			if (r.Flow == "integration")
			{
				await EvidenceAsync(actor, now, checks, "Readiness", new[] { "failedWorkflowCount", "overdueRecordReviewCount" }, ct);
				checks.Add(Check("PlatformTelemetry", null, now));
			}
			await RequireScopeAsync(actor, r, ct);
			return new(checks.Distinct().ToArray(), attempts, truncated) { StatusHistory = statusHistory };
		}
		private async Task EvidenceAsync(AdminAssistActor actor, DateTime now, List<DiagnosticCheck> checks, string sourceId, string[] ids, CancellationToken ct)
		{
			IReadOnlyList<ConfigurationEvidence> values;
			try { values = await sources.Single(s => s.SourceId == sourceId).ReadAsync(actor, now, ct).WaitAsync(ct); }
			catch (OperationCanceledException) { throw; }
			catch (UnauthorizedAccessException) { foreach (var id in ids) checks.Add(Check(id, null, now, "Restricted")); return; }
			catch (Exception) { foreach (var id in ids) checks.Add(Check(id, null, now)); return; }
			foreach (var id in ids)
			{
				var e = values.SingleOrDefault(e => e.Id == id);
				bool? issue = e?.IsFresh(now, TimeSpan.FromMinutes(1)) == true ?
					id is "emailImportSourceCount" ? e.Number == 0 : id is "textSourcePresent" or "EnableTextToCall" ? !e.Boolean :
					sourceId == "DepartmentSettings" ? null : e.Boolean ?? (e.Number.HasValue ? e.Number > 0 : null) : null;
				checks.Add(Check(id, issue, now, e?.State == EvidenceState.Redacted ? "Restricted" : "Current", e?.State == EvidenceState.Known ? e.Number ?? (e.Boolean.HasValue ? e.Boolean.Value ? 1 : 0 : null) : null, sourceId));
			}
		}
	}
}
