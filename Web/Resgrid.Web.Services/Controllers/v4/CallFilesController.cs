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
using Resgrid.Model.Helpers;
using Resgrid.Web.Services.Models.v4.CallFiles;
using System;
using System.IO;
using System.Net.Mime;
using System.Threading;
using System.Web;
using System.Text;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Call Priorities, for example Low, Medium, High. Call Priorities can be system provided ones or custom for a department
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CallFilesController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly ICallsService _callsService;
		private readonly IDepartmentsService _departmentsService;

		public CallFilesController(ICallsService callsService, IDepartmentsService departmentsService)
		{
			_callsService = callsService;
			_departmentsService = departmentsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Get the files for a call in the Resgrid System
		/// </summary>
		/// <param name="callId">CallId to get the files for</param>
		/// <param name="includeData">Include the data in the result</param>
		/// <param name="type">Type of file to get (Any = 0, Audio = 1, Images = 2, Files = 3, Videos = 4)</param>
		/// <returns></returns>
		[HttpGet("GetFilesForCall")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<CallFilesResult>> GetFilesForCall(int callId, bool includeData, int type)
		{
			var result = new CallFilesResult();
			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			call = await _callsService.PopulateCallData(call, false, true, false, false, false, false, false, false, false);

			if (call.Attachments != null && call.Attachments.Any())
			{
				foreach (var attachment in call.Attachments)
				{
					if (type == 0)
						result.Data.Add(ConvertCallFileData(attachment, department, includeData));
					else if (type == attachment.CallAttachmentType)
						result.Data.Add(ConvertCallFileData(attachment, department, includeData));
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
		/// Get a users avatar from the Resgrid system based on their ID
		/// </summary>
		/// <param name="query">ID of the file</param>
		/// <returns></returns>
		[HttpGet("GetFile")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> GetFile(string query)
		{
			if (String.IsNullOrWhiteSpace(query))
				return NotFound();

			var decodedQuery = Encoding.UTF8.GetString(Convert.FromBase64String(query));

			var decryptedQuery = SymmetricEncryption.Decrypt(decodedQuery, Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);

			var items = decryptedQuery.Split(char.Parse("|"));

			if (String.IsNullOrWhiteSpace(items[0]) || items[0] == "0" || String.IsNullOrWhiteSpace(items[1]))
				return NotFound();

			int departmentId = int.Parse(items[0].Trim());
			string id = items[1].Trim();

			var attachment = await _callsService.GetCallAttachmentAsync(int.Parse(id));

			if (attachment == null)
				return NotFound();

			var call = await _callsService.GetCallByIdAsync(attachment.CallId);
			if (call.DepartmentId != departmentId)
				return Unauthorized();

			var extension = Path.GetExtension(attachment.FileName).ToLowerInvariant();
			var contentType = FileHelper.GetContentTypeByExtension(extension);
			return File(attachment.Data, contentType);
		}

		/// <summary>
		/// Get a users avatar from the Resgrid system based on their ID
		/// </summary>
		/// <param name="query">ID of the file</param>
		/// <returns></returns>
		[HttpHead("GetFile")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> GetFileHead(string query)
		{
			if (String.IsNullOrWhiteSpace(query))
				return NotFound();

			var decodedQuery = Encoding.UTF8.GetString(Convert.FromBase64String(query));

			var decryptedQuery = SymmetricEncryption.Decrypt(decodedQuery, Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);

			var items = decryptedQuery.Split(char.Parse("|"));

			if (String.IsNullOrWhiteSpace(items[0]) || items[0] == "0" || String.IsNullOrWhiteSpace(items[1]))
				return NotFound();

			int departmentId = int.Parse(items[0].Trim());
			string id = items[1].Trim();

			var attachment = await _callsService.GetCallAttachmentAsync(int.Parse(id));

			if (attachment == null)
				return NotFound();

			var call = await _callsService.GetCallByIdAsync(attachment.CallId);
			if (call.DepartmentId != departmentId)
				return Unauthorized();

			return Ok();
		}

		/// <summary>
		/// Attaches a file to a call
		/// </summary>
		/// <param name="input">ID of the user</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns></returns>
		[HttpPost("SaveCallFile")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<SaveCallFileResult>> SaveCallFile(SaveCallFileInput input, CancellationToken cancellationToken)
		{
			var result = new SaveCallFileResult();

			if (!ModelState.IsValid)
				return BadRequest();

			var call = await _callsService.GetCallByIdAsync(int.Parse(input.CallId));

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (call.State != (int)CallStates.Active)
				return BadRequest();

			var callAttachment = new CallAttachment();
			callAttachment.CallId = int.Parse(input.CallId);
			callAttachment.CallAttachmentType = input.Type;

			if (String.IsNullOrWhiteSpace(input.Name))
				callAttachment.FileName = "cameraPhoneUpload.png";
			else
				callAttachment.FileName = input.Name;

			callAttachment.UserId = input.UserId;
			callAttachment.Timestamp = DateTime.UtcNow;
			callAttachment.Data = Convert.FromBase64String(input.Data);

			if (!String.IsNullOrWhiteSpace(input.Latitude))
			{
				callAttachment.Latitude = decimal.Parse(input.Latitude);
			}

			if (!String.IsNullOrWhiteSpace(input.Longitude))
			{
				callAttachment.Longitude = decimal.Parse(input.Longitude);
			}

			var saved = await _callsService.SaveCallAttachmentAsync(callAttachment, cancellationToken);


			result.Id = saved.CallAttachmentId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Created;
			ResponseHelper.PopulateV4ResponseData(result);

			return CreatedAtAction(nameof(GetFile), new { departmentId = call.DepartmentId, id = saved.CallAttachmentId }, result);
		}

		public static CallFileResultData ConvertCallFileData(CallAttachment attachment, Department department, bool includeData)
		{
			var file = new CallFileResultData();
			file.Id = attachment.CallAttachmentId.ToString();
			file.CallId = attachment.CallId.ToString();
			file.FileName = attachment.FileName;
			file.Type = attachment.CallAttachmentType;

			var query = SymmetricEncryption.Encrypt($"{department.DepartmentId}|{attachment.CallAttachmentId}", Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);

			file.Url = Config.SystemBehaviorConfig.ResgridApiBaseUrl + "/api/v4/CallFiles/GetFile?query=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(query));
			file.Name = attachment.Name;
			file.Size = attachment.Size.GetValueOrDefault();
			file.Mime = FileHelper.GetContentTypeByExtension(Path.GetExtension(attachment.FileName));

			if (attachment.Timestamp.HasValue)
				file.Timestamp = attachment.Timestamp.Value.TimeConverterToString(department);
			else
				file.Timestamp = DateTime.UtcNow.TimeConverterToString(department);

			if (!String.IsNullOrWhiteSpace(attachment.UserId))
				file.UserId = attachment.UserId;

			if (includeData)
				file.Data = Convert.ToBase64String(attachment.Data);

			return file;
		}
	}
}
