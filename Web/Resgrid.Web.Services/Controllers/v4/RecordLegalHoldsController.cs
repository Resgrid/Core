using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Services.Controllers.v4
{
	[Route("api/v{VersionId:apiVersion}/[controller]"), ApiVersion("4.0"), ApiExplorerSettings(GroupName = "v4")]
	[Authorize(Policy = ResgridResources.RecordLegalHold_Update)]
	public class RecordLegalHoldsController : V4AuthenticatedApiControllerbase
	{
		private readonly IRecordsLegalHoldService _holds;
		private readonly IRecordsCutoverService _cutover;
		public RecordLegalHoldsController(IRecordsLegalHoldService holds, IRecordsCutoverService cutover) { _holds = holds; _cutover = cutover; }
		[HttpGet("GetHolds")]
		public async Task<IActionResult> GetHolds()
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled) return NotFound();
			try { Response.Headers["Cache-Control"] = "no-store"; return Ok(await _holds.GetAsync(DepartmentId, UserId)); } catch (UnauthorizedAccessException) { return Forbid(); }
		}
		[HttpPost("Place")]
		public async Task<IActionResult> Place([FromBody] RmsRecordLegalHold input, CancellationToken cancellationToken)
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled) return NotFound();
			try { return StatusCode(201, await _holds.PlaceAsync(DepartmentId, UserId, input, cancellationToken)); }
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { return Problem(ex.Message, statusCode: 409); }
		}
		[HttpPost("Release")]
		public async Task<IActionResult> Release([FromBody] ReleaseHoldInput input, CancellationToken cancellationToken)
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled) return NotFound();
			if (input == null) return BadRequest();
			try { await _holds.ReleaseAsync(DepartmentId, UserId, input.HoldId, input.ExpectedVersion, input.Reason, cancellationToken); return NoContent(); }
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { return Problem(ex.Message, statusCode: 409); }
		}
		public class ReleaseHoldInput { public string HoldId { get; set; } public long ExpectedVersion { get; set; } public string Reason { get; set; } }
	}
}
