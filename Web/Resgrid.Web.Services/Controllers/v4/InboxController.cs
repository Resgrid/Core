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
using Resgrid.Services;
using System.Threading;
using Resgrid.Model.Providers;
using System;

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
		#region Members and Constructors
		private readonly INovuProvider _novuProvider;

		public InboxController(
			INovuProvider novuProvider
			)
		{
			_novuProvider = novuProvider;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Deletes a message from the inbox by its ID
		/// </summary>
		/// <returns></returns>
		[HttpDelete("DeleteMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		[Authorize(Policy = ResgridResources.Messages_Delete)]
		public async Task<ActionResult> DeleteMessage(string messageId)
		{
			if (string.IsNullOrWhiteSpace(messageId))
			{
				return BadRequest("Message ID is required");
			}

			try
			{
				var result = await _novuProvider.DeleteMessage(messageId);

				if (result)
				{
					return Ok();
				}
				else
				{
					return StatusCode(StatusCodes.Status500InternalServerError, "Failed to delete message");
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Error deleting message with ID: {messageId}");
				return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while deleting the message");
			}
		}
	}
}
