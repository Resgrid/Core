using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// RecordOperationalSummaryV1 over the v4 contract (RMS plan sections 5.1 and 4.7): the authorization-scoped,
	/// paged, department-scoped read API that Billing, deployment and customer BI tools use instead of a database
	/// credential. Same gates as Records: flag first, then per-record visibility for a member or the configured
	/// grant for a system principal; a row the caller cannot see is omitted, never tombstoned, because the feed
	/// carries facts rather than sync state.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class RecordSummariesController : V4AuthenticatedApiControllerbase, IActionFilter
	{
		private readonly IRecordOperationalSummaryService _summaries;
		private readonly IRecordsCutoverService _cutoverService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;

		private SystemPrincipalRecordGrant _systemGrant;
		private bool _systemGrantResolved;

		public RecordSummariesController(IRecordOperationalSummaryService summaries, IRecordsCutoverService cutoverService, IRecordsAuthorizationService recordsAuthorizationService)
		{
			_summaries = summaries;
			_cutoverService = cutoverService;
			_recordsAuthorizationService = recordsAuthorizationService;
		}

		/// <summary>A system principal with no configured Record grant for the resolved department is refused before any action runs.</summary>
		public void OnActionExecuting(ActionExecutingContext context)
		{
			if (IsSystemPrincipal && SystemGrant == null)
				context.Result = Problem(statusCode: StatusCodes.Status403Forbidden,
					title: "This system principal has no configured Record grant for this department.", type: "record_grant_missing");
		}

		public void OnActionExecuted(ActionExecutedContext context)
		{
		}

		/// <summary>The summary for one record's official revision, or a specific revision a consumer pinned earlier.</summary>
		[HttpGet("Get")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<ActionResult<RecordOperationalSummaryResult>> Get(string recordId, RmsRecordKind kind = RmsRecordKind.Operational, string revisionId = null)
		{
			if (string.IsNullOrWhiteSpace(recordId))
				return BadRequest();
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();
			if (!await CanViewRecordAsync(recordId))
				return NotFound();

			RecordOperationalSummaryV1 summary;
			try
			{
				summary = await _summaries.BuildAsync(DepartmentId, recordId, kind, revisionId);
			}
			catch (InvalidOperationException ex)
			{
				return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
			}
			if (summary == null)
				return NotFound();

			var result = new RecordOperationalSummaryResult { Data = summary, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Summaries for records whose projection changed after <paramref name="since"/> (Unix ms), oldest first. Follow
		/// <see cref="RecordOperationalSummariesResult.NextCursor"/> until HasMore is false. Voided and superseded rows are
		/// included so a consumer that pinned a revision learns it must re-read.
		/// </summary>
		[HttpGet("List")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<ActionResult<RecordOperationalSummariesResult>> List(long since = 0, int take = 50, string cursor = null, RmsRecordKind? kind = null)
		{
			if (since < 0 || since > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
				return BadRequest();
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			var result = new RecordOperationalSummariesResult();
			if (moduleState.RecordsUsable)
			{
				RecordOperationalSummaryPage page;
				try
				{
					page = await _summaries.QueryAsync(DepartmentId, new RecordOperationalSummaryQuery
					{
						ChangedSince = RecordsApiHelper.FromUnixMs(since),
						Cursor = cursor,
						Take = Math.Max(1, Math.Min(RecordOperationalSummaryQuery.MaxTake, take)),
						RecordKind = kind
					});
				}
				catch (ArgumentException)
				{
					return BadRequest();
				}
				catch (InvalidOperationException ex)
				{
					return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
				}

				foreach (var summary in page.Items)
				{
					if (await CanViewRecordAsync(summary.RecordId))
						result.Data.Add(summary);
				}
				result.HasMore = page.HasMore;
				result.NextCursor = page.NextCursor;
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		#region Principal helpers

		private SystemPrincipalRecordGrant SystemGrant
		{
			get
			{
				if (!_systemGrantResolved)
				{
					_systemGrant = RecordsSystemPrincipal.ResolveGrant(User, DepartmentId);
					_systemGrantResolved = true;
				}

				return _systemGrant;
			}
		}

		private bool IsSystemPrincipal => RecordsSystemPrincipal.IsSystemPrincipal(User);

		private async Task<bool> CanViewRecordAsync(string recordId)
		{
			var grant = SystemGrant;
			if (grant != null)
				return await _recordsAuthorizationService.CanSystemPrincipalViewRecordAsync(grant, recordId);

			return await _recordsAuthorizationService.IsActiveMemberAsync(UserId, DepartmentId)
				&& await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, recordId, DepartmentId);
		}

		#endregion
	}
}
