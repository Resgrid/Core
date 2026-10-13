using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.CallPriorities;
using System.Linq;
using Resgrid.Model;
using Resgrid.Web.Services.Models.v4.Messages;
using Resgrid.Web.Services.Models.v4.Inbox;
using Resgrid.Services;
using System.Threading;
using Resgrid.Model.Providers;
using System;
using System.Text.RegularExpressions;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// For interacting with the Inbox
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class InboxController : V4AuthenticatedApiControllerbase
	{
		/// <summary>
		/// Novu message ids are MongoDB ObjectIds. Anything else is rejected before it reaches a Novu URL.
		/// </summary>
		private static readonly Regex MessageIdPattern = new Regex("^[0-9a-fA-F]{24}$", RegexOptions.Compiled);

		#region Members and Constructors
		private readonly INovuProvider _novuProvider;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUnitsService _unitsService;

		public InboxController(
			INovuProvider novuProvider,
			IDepartmentsService departmentsService,
			IUnitsService unitsService
			)
		{
			_novuProvider = novuProvider;
			_departmentsService = departmentsService;
			_unitsService = unitsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets the Novu subscriberHash for a unit's inbox, for the Unit app to open that inbox with. The unit must
		/// be in the caller's department. The user's own inbox hashes come with the rights
		/// (Security/GetCurrentUsersRights).
		/// </summary>
		/// <param name="unitId">The unit whose inbox to open</param>
		/// <returns></returns>
		[HttpGet("GetUnitSubscriberHash")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<UnitSubscriberHashResult>> GetUnitSubscriberHash(int unitId)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			if (department == null || string.IsNullOrWhiteSpace(department.Code))
				return NotFound();

			var unit = await GetUnitForCallerAsync(unitId);
			if (unit == null)
				return NotFound();

			var result = new UnitSubscriberHashResult();
			result.Data.UnitId = unit.UnitId.ToString();
			result.Data.SubscriberHash = _novuProvider.GetUnitSubscriberHash(unit.UnitId, department.Code);

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Deletes a message from the caller's own inbox by its ID. Without a unit the message must be in the
		/// caller's own inbox (Responder/Dispatch, or the IC app's); with one, in that unit's inbox (Unit app), and
		/// the unit must be in the caller's department. A message anywhere else is reported as not found.
		/// </summary>
		/// <param name="messageId">The Novu message (inbox notification) id</param>
		/// <param name="unitId">The unit whose inbox holds the message, when deleting from the Unit app</param>
		/// <returns></returns>
		[HttpDelete("DeleteMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		[Authorize(Policy = ResgridResources.Messages_Delete)]
		public async Task<ActionResult> DeleteMessage(string messageId, int? unitId = null)
		{
			if (string.IsNullOrWhiteSpace(messageId))
			{
				return BadRequest("Message ID is required");
			}

			if (!MessageIdPattern.IsMatch(messageId))
			{
				return BadRequest("Message ID is invalid");
			}

			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
				if (department == null || string.IsNullOrWhiteSpace(department.Code))
					return NotFound();

				NovuInboxDeleteResult result;
				if (unitId.HasValue)
				{
					var unit = await GetUnitForCallerAsync(unitId.Value);
					if (unit == null)
						return NotFound();

					result = await _novuProvider.DeleteUnitInboxMessage(unit.UnitId, department.Code, messageId);
				}
				else
				{
					result = await _novuProvider.DeleteUserInboxMessage(UserId, department.Code, messageId);
				}

				switch (result)
				{
					case NovuInboxDeleteResult.Deleted:
						return Ok();
					case NovuInboxDeleteResult.NotFound:
						return NotFound();
					default:
						return StatusCode(StatusCodes.Status500InternalServerError, "Failed to delete message");
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Error deleting message with ID: {messageId}");
				return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while deleting the message");
			}
		}

		/// <summary>
		/// The unit, when it is in the caller's department. That is the Unit app's own rule for which units a user
		/// may sign in as (Units/GetAllUnits is not filtered by the unit visibility matrix), so a matrix check here
		/// would leave someone signed in as a group-hidden unit with no inbox once Novu enforces HMAC. Null
		/// otherwise, which callers answer exactly like a missing unit so a unit id can't be used to probe other
		/// departments' units.
		/// </summary>
		private async Task<Unit> GetUnitForCallerAsync(int unitId)
		{
			var unit = await _unitsService.GetUnitByIdAsync(unitId);
			if (unit == null || unit.DepartmentId != DepartmentId)
				return null;

			return unit;
		}
	}
}
