using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// What a report author needs to look up about a call: its times, every unit and personnel status with who set it and
	/// from which app (the crew's own, a dispatcher's, or Incident Command's), crews, dispatches, check-ins, the command's
	/// timeline, objectives, tactic timestamps and mutual aid, and the run reports other stations and members wrote for it.
	/// Read-only; the apps use it to fill and check incident and run reports.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ReportSourcesController : V4AuthenticatedApiControllerbase
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

		/// <summary>
		/// The call's report sources. <paramref name="recordId"/> is the report being written (incident or run report): it is
		/// left out of the run report list and named in the access audit of every run report returned.
		/// </summary>
		[HttpGet("GetCallSources")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<ActionResult<CallSourcesResult>> GetCallSources(int callId, string recordId = null)
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled)
				return NotFound();
			// A member-facing lookup: the source-call rule is a member rule, so a system principal has nothing to read here.
			if (RecordsSystemPrincipal.IsSystemPrincipal(User) || !ClaimsAuthorizationHelper.CanViewCalls() || !await _authorization.IsActiveMemberAsync(UserId, DepartmentId))
				return Forbid();
			if (callId <= 0 || recordId?.Length > 64)
				return BadRequest();

			var call = await _calls.GetCallByIdAsync(callId);
			if (call == null || call.IsDeleted || call.DepartmentId != DepartmentId)
				return NotFound();
			if (!await _authorization.CanReadSourceCallAsync(UserId, DepartmentId, call))
				return Forbid();

			var sources = await _sources.GetForCallAsync(DepartmentId, call);
			if (sources == null)
				return NotFound();

			var runReports = await _runReports.GetForCallAsync(DepartmentId, UserId, callId, recordId,
				string.IsNullOrWhiteSpace(recordId) ? "Looked up in an app while writing a report for the call" : "Looked up in an app while writing report " + recordId, RmsOriginClient.Api);

			var result = new CallSourcesResult { Data = new CallSourcesResultData { Sources = sources, RunReports = runReports }, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}
	}
}
