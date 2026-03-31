using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using System.Linq;
using Resgrid.Model;
using Resgrid.Web.Helpers;
using Resgrid.Web.Services.Models.v4.CallVideoFeeds;
using System;
using Resgrid.Model.Helpers;
using System.Net.Mime;
using System.Threading;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Video feeds attached to calls for live video monitoring during incidents
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CallVideoFeedsController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly ICallsService _callsService;
		private readonly IDepartmentsService _departmentsService;

		public CallVideoFeedsController(ICallsService callsService, IDepartmentsService departmentsService)
		{
			_callsService = callsService;
			_departmentsService = departmentsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Get video feeds for a call
		/// </summary>
		/// <param name="callId">CallId of the call you want to get video feeds for</param>
		/// <returns></returns>
		[HttpGet("GetCallVideoFeeds")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<CallVideoFeedsResult>> GetCallVideoFeeds(string callId)
		{
			if (String.IsNullOrWhiteSpace(callId))
				return BadRequest();

			var result = new CallVideoFeedsResult();

			var call = await _callsService.GetCallByIdAsync(int.Parse(callId));
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			var feeds = await _callsService.GetCallVideoFeedsByCallIdAsync(int.Parse(callId));

			if (feeds != null && feeds.Any())
			{
				foreach (var feed in feeds.Where(f => !f.IsDeleted))
				{
					var fullName = await UserHelper.GetFullNameForUser(feed.AddedByUserId);
					result.Data.Add(ConvertCallVideoFeed(feed, fullName, department));
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Saves a video feed to a call
		/// </summary>
		/// <param name="input">Video feed data</param>
		/// <param name="cancellationToken">The cancellation token</param>
		/// <returns>ActionResult.</returns>
		[HttpPost("SaveCallVideoFeed")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<SaveCallVideoFeedResult>> SaveCallVideoFeed(SaveCallVideoFeedInput input, CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid)
				return BadRequest();

			var call = await _callsService.GetCallByIdAsync(int.Parse(input.CallId));

			if (call == null)
				return BadRequest();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			var result = new SaveCallVideoFeedResult();

			var feed = new CallVideoFeed();
			feed.CallVideoFeedId = Guid.NewGuid().ToString();
			feed.CallId = int.Parse(input.CallId);
			feed.DepartmentId = DepartmentId;
			feed.Name = input.Name;
			feed.Url = input.Url;
			feed.FeedType = input.FeedType;
			feed.FeedFormat = input.FeedFormat;
			feed.Description = input.Description;
			feed.Status = (int)CallVideoFeedStatuses.Active;
			feed.AddedByUserId = UserId;
			feed.AddedOn = DateTime.UtcNow;
			feed.SortOrder = input.SortOrder;

			if (!String.IsNullOrWhiteSpace(input.Latitude) && !String.IsNullOrWhiteSpace(input.Longitude))
			{
				feed.Latitude = decimal.Parse(input.Latitude);
				feed.Longitude = decimal.Parse(input.Longitude);
			}

			var saved = await _callsService.SaveCallVideoFeedAsync(feed, cancellationToken);

			result.Id = saved.CallVideoFeedId;
			result.PageSize = 0;
			result.Status = ResponseHelper.Created;
			ResponseHelper.PopulateV4ResponseData(result);

			return CreatedAtAction(nameof(GetCallVideoFeeds), new { callId = saved.CallId }, result);
		}

		/// <summary>
		/// Updates an existing video feed
		/// </summary>
		/// <param name="input">Video feed data with Id</param>
		/// <param name="cancellationToken">The cancellation token</param>
		/// <returns>ActionResult.</returns>
		[HttpPut("EditCallVideoFeed")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<SaveCallVideoFeedResult>> EditCallVideoFeed(EditCallVideoFeedInput input, CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid)
				return BadRequest();

			var feed = await _callsService.GetCallVideoFeedByIdAsync(input.CallVideoFeedId);

			if (feed == null)
				return BadRequest();

			if (feed.DepartmentId != DepartmentId)
				return Unauthorized();

			var result = new SaveCallVideoFeedResult();

			feed.Name = input.Name;
			feed.Url = input.Url;
			feed.FeedType = input.FeedType;
			feed.FeedFormat = input.FeedFormat;
			feed.Description = input.Description;
			feed.Status = input.Status;
			feed.SortOrder = input.SortOrder;
			feed.UpdatedOn = DateTime.UtcNow;

			if (!String.IsNullOrWhiteSpace(input.Latitude) && !String.IsNullOrWhiteSpace(input.Longitude))
			{
				feed.Latitude = decimal.Parse(input.Latitude);
				feed.Longitude = decimal.Parse(input.Longitude);
			}

			var saved = await _callsService.SaveCallVideoFeedAsync(feed, cancellationToken);

			result.Id = saved.CallVideoFeedId;
			result.PageSize = 0;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Soft deletes a video feed
		/// </summary>
		/// <param name="callVideoFeedId">The video feed Id to delete</param>
		/// <param name="cancellationToken">The cancellation token</param>
		/// <returns>ActionResult.</returns>
		[HttpDelete("DeleteCallVideoFeed")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DeleteCallVideoFeedResult>> DeleteCallVideoFeed(string callVideoFeedId, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(callVideoFeedId))
				return BadRequest();

			var feed = await _callsService.GetCallVideoFeedByIdAsync(callVideoFeedId);

			if (feed == null)
				return BadRequest();

			if (feed.DepartmentId != DepartmentId)
				return Unauthorized();

			var result = new DeleteCallVideoFeedResult();

			await _callsService.DeleteCallVideoFeedAsync(feed, UserId, cancellationToken);

			result.PageSize = 0;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		public static CallVideoFeedResultData ConvertCallVideoFeed(CallVideoFeed feed, string fullName, Department department)
		{
			var feedResult = new CallVideoFeedResultData();
			feedResult.CallVideoFeedId = feed.CallVideoFeedId;
			feedResult.CallId = feed.CallId.ToString();
			feedResult.Name = feed.Name;
			feedResult.Url = feed.Url;
			feedResult.FeedType = feed.FeedType;
			feedResult.FeedFormat = feed.FeedFormat;
			feedResult.Description = feed.Description;
			feedResult.Status = feed.Status;
			feedResult.Latitude = feed.Latitude;
			feedResult.Longitude = feed.Longitude;
			feedResult.AddedByUserId = feed.AddedByUserId;
			feedResult.AddedOnFormatted = feed.AddedOn.TimeConverter(department).FormatForDepartment(department);
			feedResult.AddedOnUtc = feed.AddedOn;
			feedResult.SortOrder = feed.SortOrder;
			feedResult.FullName = fullName;

			return feedResult;
		}
	}
}
