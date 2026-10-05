using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// The report editors' "Call data" lookup: everything Resgrid holds about a call that a report needs (its times, each
	/// unit's and person's statuses with who set them and from where, crews, command entries and objectives) plus the run
	/// reports other stations, units and members wrote for it. Read-only JSON in department-local time; the page applies
	/// what the author picks.
	/// </summary>
	[Area("User"), Authorize(Policy = ResgridResources.Record_View), DepartmentLocalTime]
	public class ReportSourcesController : SecureBaseController
	{
		private readonly ICallsService _calls;
		private readonly ICallSourceDataService _sources;
		private readonly IRecordCallReportsService _runReports;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRecordsCutoverService _cutover;

		public ReportSourcesController(ICallsService calls, ICallSourceDataService sources, IRecordCallReportsService runReports,
			IRecordsAuthorizationService authorization, IRecordsCutoverService cutover)
		{
			_calls = calls;
			_sources = sources;
			_runReports = runReports;
			_authorization = authorization;
			_cutover = cutover;
		}

		/// <summary>The call's report sources. <paramref name="recordId"/> is the report being written: it is left out of the run report list and named in the shown reports' access audit.</summary>
		[HttpGet]
		public async Task<IActionResult> Call(int callId, string recordId = null)
		{
			Response.Headers["Cache-Control"] = "no-store";
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled) return NotFound();
			if (!ClaimsAuthorizationHelper.CanViewCalls() || !await _authorization.IsActiveMemberAsync(UserId, DepartmentId)) return Forbid();
			if (callId <= 0 || recordId?.Length > 64) return BadRequest();

			var call = await _calls.GetCallByIdAsync(callId);
			if (call == null || call.IsDeleted || call.DepartmentId != DepartmentId) return NotFound();
			if (!await _authorization.CanReadSourceCallAsync(UserId, DepartmentId, call)) return Forbid();

			var data = await _sources.GetForCallAsync(DepartmentId, call);
			if (data == null) return NotFound();
			var runReports = await _runReports.GetForCallAsync(DepartmentId, UserId, callId, recordId,
				string.IsNullOrWhiteSpace(recordId) ? "Looked up while writing a report for the call" : "Looked up while writing report " + recordId);

			var time = DepartmentTime.From(ViewData);
			return Json(ReportSourcesJson.Build(data, runReports, time, Url));
		}
	}

	/// <summary>The lookup panel's JSON shape (camelCase, department-local times as display text and as datetime-local input values).</summary>
	public static class ReportSourcesJson
	{
		public static object Build(CallSourceData data, List<CallRunReport> runReports, DepartmentTime time, IUrlHelper url)
		{
			object T(DateTime? utc) => utc.HasValue ? new { display = time.Format(utc), input = time.Local(utc.Value).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), utc = utc.Value.ToString("o", CultureInfo.InvariantCulture) } : null;
			string Linkage(int? source) => CallStatusAttribution.IsInferred(source) ? "inferred" : CallStatusAttribution.IsAutoLinked(source) ? "auto" : null;

			return new
			{
				callId = data.CallId,
				number = data.Number,
				incidentNumber = data.IncidentNumber,
				address = data.Address,
				nature = data.Nature,
				loggedOn = T(data.LoggedOn),
				closedOn = T(data.ClosedOn),
				warnings = data.Warnings,
				entries = data.Entries.Select(e => new
				{
					id = e.Id,
					kind = e.Kind.ToString(),
					time = T(e.TimestampUtc),
					unitId = e.UnitId,
					userId = e.UserId,
					subject = e.SubjectName,
					label = e.Label,
					color = e.Color,
					detail = e.Detail,
					milestone = e.Milestone.ToString(),
					tactic = e.TacticTimestamp,
					linkage = Linkage(e.Linkage),
					origin = ((StatusSetOrigins)e.Origin).ToString(),
					setBy = e.SetByName,
					// A member setting their own status is the subject; the panel names the setter only when it was someone else.
					setBySelf = !string.IsNullOrWhiteSpace(e.UserId) && string.Equals(e.UserId, e.SetByUserId, StringComparison.OrdinalIgnoreCase)
				}),
				units = data.Units.Select(u => new
				{
					unitId = u.UnitId,
					name = u.Name,
					type = u.Type,
					wasDispatched = u.WasDispatched,
					assignedByCommand = u.AssignedByCommand,
					timesSource = ((Resgrid.Model.Reporting.CallUnitTimesSources)u.TimesSource).ToString(),
					staffing = u.Staffing,
					crew = u.CrewNames,
					times = new
					{
						dispatched = T(u.DispatchedOn),
						enroute = T(u.EnrouteOn),
						onScene = T(u.OnSceneOn),
						staging = T(u.StagingOn),
						cancelled = T(u.CancelledOn),
						cleared = T(u.ClearedOn),
						inService = T(u.InServiceOn),
						commandAssigned = T(u.CommandAssignedOn),
						commandReleased = T(u.CommandReleasedOn)
					}
				}),
				personnel = data.Personnel.Select(p => new
				{
					userId = p.UserId,
					name = p.Name,
					unitId = p.UnitId,
					unit = p.UnitName,
					role = p.Role,
					engaged = p.Engaged,
					wasDispatched = p.WasDispatched,
					assignedByCommand = p.AssignedByCommand,
					times = new { responding = T(p.RespondingOn), onScene = T(p.OnSceneOn), cleared = T(p.ClearedOn), first = T(p.FirstOn), last = T(p.LastOn) }
				}),
				command = data.Command == null ? null : new
				{
					name = data.Command.Name,
					established = T(data.Command.EstablishedOn),
					closed = T(data.Command.ClosedOn),
					commanders = data.Command.CommanderNames,
					tactics = NerisTacticTimestamps.Fields.Where(data.Command.TacticTimestamps.ContainsKey).Select(f => new { field = f, time = T(data.Command.TacticTimestamps[f]) }),
					mutualAid = data.Command.MutualAid.Select(a => new { agency = a.AgencyName, resources = a.ResourceNames, first = T(a.FirstOn) })
				},
				runReports = (runReports ?? new List<CallRunReport>()).Select(r => new
				{
					recordId = r.RecordId,
					url = url.Action("Details", "Records", new { area = "User", id = r.RecordId }),
					type = r.TypeName,
					reference = r.Reference,
					state = ((RmsRecordState)r.State).ToString(),
					isFinal = r.IsFinal,
					author = r.AuthorName,
					station = r.StationName,
					started = T(r.StartedOn),
					ended = T(r.EndedOn),
					location = r.Location,
					initialReport = r.InitialReport,
					cause = r.Cause,
					otherAgencies = r.OtherAgencies,
					otherUnits = r.OtherUnits,
					narrative = r.Narrative,
					withheld = r.ContentWithheld,
					units = r.Units.Select(u => new
					{
						unitId = u.UnitId,
						name = u.Name,
						crew = u.Crew,
						times = new { dispatched = T(u.DispatchedOn), enroute = T(u.EnrouteOn), onScene = T(u.OnSceneOn), cleared = T(u.ReleasedOn), inQuarters = T(u.InQuartersOn) }
					}),
					personnel = r.Personnel.Select(p => new { userId = p.UserId, name = p.Name, unitId = p.UnitId, role = p.Role, start = T(p.StartOn), end = T(p.EndOn) })
				})
			};
		}
	}
}
