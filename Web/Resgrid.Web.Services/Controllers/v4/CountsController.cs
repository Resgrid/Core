using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Counts;
using PolicyAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Counts for menu and tab badges in one request, so the apps never load a list just to number it
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CountsController : V4AuthenticatedApiControllerbase
	{
		private readonly IBadgeCountsService _badgeCountsService;
		private readonly PolicyAuthorizationService _policyAuthorization;

		public CountsController(IBadgeCountsService badgeCountsService, PolicyAuthorizationService policyAuthorization)
		{
			_badgeCountsService = badgeCountsService;
			_policyAuthorization = policyAuthorization;
		}

		/// <summary>
		/// Gets the signed-in member's badge counts. Each count is computed only when asked for and only when the member holds
		/// the view permission of the list it counts; the others are left out. Known keys: calls (open calls), messages
		/// (unread inbox), chats (unread chat messages), weather (active Extreme and Severe alerts) and the member counts
		/// listed in the API documentation; an unknown key is ignored, so the apps can ask for keys newer servers add.
		/// The department-wide counts are cached briefly and cleared on every change, so asking after each realtime ping is
		/// cheap.
		/// </summary>
		/// <param name="include">Comma-separated keys of the counts to compute. Empty asks for every count.</param>
		/// <param name="activeUnitId">Unit the member is operating as (Unit app): counts that cover a unit include it when the member may act as it</param>
		/// <returns>BadgeCountsResult with one entry per count the member can see</returns>
		[HttpGet("GetBadgeCounts")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<BadgeCountsResult>> GetBadgeCounts(string include = null, int? activeUnitId = null)
		{
			var context = new BadgeCountContext
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				ActiveUnitId = activeUnitId,
				TimeZone = TimeZone,
				NowUtc = DateTime.UtcNow
			};

			var counts = await _badgeCountsService.GetBadgeCountsAsync(context, ParseInclude(include), IsAllowedAsync) ?? new List<BadgeCount>();

			var result = new BadgeCountsResult
			{
				Data = counts.Select(count => new BadgeCountResultData
				{
					Key = count.Key,
					Count = count.Count,
					Breakdown = count.Breakdown == null ? null : new Dictionary<string, int>(count.Breakdown)
				}).ToList(),
				Status = ResponseHelper.Success
			};

			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>The <c>include</c> list as keys (trimmed, distinct, any case); empty means every count.</summary>
		public static IReadOnlyCollection<string> ParseInclude(string include)
		{
			if (string.IsNullOrWhiteSpace(include))
				return Array.Empty<string>();

			return include.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		private async Task<bool> IsAllowedAsync(string policy)
		{
			// The interface overload (no resource): the (user, policy) form is an extension method over it.
			var authorized = await _policyAuthorization.AuthorizeAsync(User, null, policy);
			return authorized.Succeeded;
		}
	}
}
