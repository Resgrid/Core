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
	public class CallFilesController : V4AuthenticatedApiControllerbaseSystemAuth
	{
		#region Members and Constructors
		private readonly ICallsService _callsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IProtectedReadService _protectedCallReadService;
		private readonly IProtectedWriteService _protectedWriteService;

		public CallFilesController(ICallsService callsService, IDepartmentsService departmentsService,
			IProtectedReadService protectedCallReadService, IProtectedWriteService protectedWriteService)
		{
			_callsService = callsService;
			_departmentsService = departmentsService;
			_protectedCallReadService = protectedCallReadService;
			_protectedWriteService = protectedWriteService;
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

			// Attended protected read (plan 7.1): attachment names/coordinates decrypt with a valid
			// grant or read as REDACTED/null. includeData additionally decrypts the binary payload
			// through the broker; a concealed payload serializes as null, never ciphertext bytes.
			var protectedRead = await _protectedCallReadService.ResolveAttachmentsForReadAsync(DepartmentId,
				call.Attachments?.ToList(), Request.Headers[DataProtectionController.GrantHeader].ToString(), UserId,
				includeData: includeData);

			if (call.Attachments != null && call.Attachments.Any())
			{
				foreach (var attachment in call.Attachments)
				{
					CallFileResultData fileData = null;
					if (type == 0)
						fileData = ConvertCallFileData(attachment, department, includeData);
					else if (type == attachment.CallAttachmentType)
						fileData = ConvertCallFileData(attachment, department, includeData);

					if (fileData != null)
					{
						fileData.IsProtected = protectedRead.IsProtected;
						fileData.ProtectedReason = protectedRead.ProtectedReason;
						result.Data.Add(fileData);
					}
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

			string decryptedQuery;
			try
			{
				var decodedQuery = Encoding.UTF8.GetString(Convert.FromBase64String(query));
				decryptedQuery = SymmetricEncryption.Decrypt(decodedQuery, Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);
			}
			catch (Exception)
			{
				// Malformed/foreign query: value-free not-found, never a 500.
				return NotFound();
			}

			// Expiry-aware signed link (legacy no-expiry links honored only while the transition
			// flag allows them).
			if (!TryValidateSignedFileQuery(decryptedQuery, out int departmentId, out int attachmentId))
				return NotFound();

			var attachment = await _callsService.GetCallAttachmentAsync(attachmentId);

			if (attachment == null)
				return NotFound();

			var call = await _callsService.GetCallByIdAsync(attachment.CallId);
			if (call.DepartmentId != departmentId)
				return Unauthorized();

			if (String.IsNullOrWhiteSpace(attachment.FileName) || attachment.Data == null || attachment.Data.Length == 0)
				return NotFound();

			// ADP: this is an ANONYMOUS signed-link route — it can never carry a grant, so a
			// protected department's enveloped attachment is simply not available here (and
			// ciphertext bytes are never served). Attended clients fetch through GetFilesForCall
			// with their grant instead.
			if (Resgrid.Services.ProtectedReadService.IsBinaryEnveloped(attachment.Data) ||
				ProtectedDataEnvelope.HasEnvelopePrefix(attachment.FileName))
				return NotFound();

			var extension = Path.GetExtension(attachment.FileName).ToLowerInvariant();
			var contentType = FileHelper.GetContentTypeByExtension(extension);

			if (String.IsNullOrWhiteSpace(contentType))
				contentType = MediaTypeNames.Image.Png;

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

			string decryptedQuery;
			try
			{
				var decodedQuery = Encoding.UTF8.GetString(Convert.FromBase64String(query));
				decryptedQuery = SymmetricEncryption.Decrypt(decodedQuery, Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);
			}
			catch (Exception)
			{
				// Malformed/foreign query: value-free not-found, never a 500.
				return NotFound();
			}

			if (!TryValidateSignedFileQuery(decryptedQuery, out int departmentId, out int attachmentId))
				return NotFound();

			var attachment = await _callsService.GetCallAttachmentAsync(attachmentId);

			if (attachment == null)
				return NotFound();

			if (String.IsNullOrWhiteSpace(attachment.FileName) || attachment.Data == null || attachment.Data.Length == 0)
				return NotFound();

			// Same ADP rule as the GET route: anonymous links never see protected attachments.
			if (Resgrid.Services.ProtectedReadService.IsBinaryEnveloped(attachment.Data) ||
				ProtectedDataEnvelope.HasEnvelopePrefix(attachment.FileName))
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

			var effectiveDepartmentId = GetEffectiveDepartmentId(input.DepartmentId);

			if (call.DepartmentId != effectiveDepartmentId)
				return Unauthorized();

			if (call.State != (int)CallStates.Active)
				return BadRequest();

			if (String.IsNullOrWhiteSpace(input.Data))
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

			try
			{
				callAttachment.Data = Convert.FromBase64String(input.Data);
			}
			catch (Exception ex)
			{
				return BadRequest();
			}

			if (!String.IsNullOrWhiteSpace(input.Latitude))
			{
				callAttachment.Latitude = decimal.Parse(input.Latitude);
			}

			if (!String.IsNullOrWhiteSpace(input.Longitude))
			{
				callAttachment.Longitude = decimal.Parse(input.Longitude);
			}

			// ADP write preflight (plan 3.3): refuse BEFORE inserting the transient plaintext row.
			var writePreflight = await _protectedWriteService.PreflightWriteAsync(call.DepartmentId,
				Request.Headers[DataProtectionController.GrantHeader].ToString(), UserId, IsSystemApiKeyRequest, cancellationToken);
			if (!writePreflight.Success)
				return Problem(type: writePreflight.Reason,
					title: "Recent multi-factor verification is required to modify protected data.",
					statusCode: StatusCodes.Status403Forbidden);

			var saved = await _callsService.SaveCallAttachmentAsync(callAttachment, cancellationToken);

			// ADP two-phase write (plan 19.2): encrypt names/coordinates AND the rgdpb binary
			// payload now that the identity pk (an AAD component) exists, then persist the
			// enveloped row.
			var protectedWrite = await _protectedWriteService.PrepareCallAttachmentWriteAsync(call.DepartmentId, saved,
				Request.Headers[DataProtectionController.GrantHeader].ToString(), UserId, IsSystemApiKeyRequest, cancellationToken);
			if (!protectedWrite.Success)
			{
				Resgrid.Framework.Logging.LogError($"ADP protected write failed AFTER insert for call attachment {saved.CallAttachmentId} in department {call.DepartmentId} ({protectedWrite.Reason}); transient plaintext row pending re-encryption.");
				return Problem(type: protectedWrite.Reason,
					title: "The attachment was saved but could not be protected; protected storage is temporarily unavailable. Do not resubmit — the attachment will be encrypted automatically.",
					statusCode: StatusCodes.Status503ServiceUnavailable);
			}
			if (protectedWrite.IsProtected)
				saved = await _callsService.SaveCallAttachmentAsync(saved, cancellationToken);


			result.Id = saved.CallAttachmentId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Created;
			ResponseHelper.PopulateV4ResponseData(result);

			return CreatedAtAction(nameof(GetFile), new { departmentId = call.DepartmentId, id = saved.CallAttachmentId }, result);
		}

		/// <summary>
		/// Validates the decrypted signed-link payload: "dept|attachmentId" (legacy, accepted only
		/// while SecurityConfig.AllowLegacySignedFileLinks) or "dept|attachmentId|expiresUtcTicks"
		/// (current). An expired or malformed link reads as not-found — value-free.
		/// </summary>
		public static bool TryValidateSignedFileQuery(string decryptedQuery, out int departmentId, out int attachmentId)
		{
			departmentId = 0;
			attachmentId = 0;

			if (String.IsNullOrWhiteSpace(decryptedQuery))
				return false;

			var items = decryptedQuery.Split(char.Parse("|"));
			if (items.Length < 2 || String.IsNullOrWhiteSpace(items[0]) || items[0].Trim() == "0" || String.IsNullOrWhiteSpace(items[1]))
				return false;

			if (!int.TryParse(items[0].Trim(), out departmentId) || !int.TryParse(items[1].Trim(), out attachmentId))
				return false;

			if (items.Length >= 3)
			{
				if (!long.TryParse(items[2].Trim(), out var expiresTicks))
					return false;

				if (DateTime.UtcNow.Ticks > expiresTicks)
					return false;
			}
			else if (!Config.SecurityConfig.AllowLegacySignedFileLinks)
			{
				return false;
			}

			return true;
		}

		public static CallFileResultData ConvertCallFileData(CallAttachment attachment, Department department, bool includeData)
		{
			var file = new CallFileResultData();
			file.Id = attachment.CallAttachmentId.ToString();
			file.CallId = attachment.CallId.ToString();
			file.FileName = attachment.FileName;
			file.Type = attachment.CallAttachmentType;

			// Signed anonymous link WITH EXPIRY (third segment, UTC ticks): links regenerate on
			// every authenticated list call, so a leaked URL stops working after the TTL.
			var expiresTicks = DateTime.UtcNow.AddMinutes(Math.Max(5, Config.SecurityConfig.SignedFileLinkTtlMinutes)).Ticks;
			var query = SymmetricEncryption.Encrypt($"{department.DepartmentId}|{attachment.CallAttachmentId}|{expiresTicks}", Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);

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

			// Null after a protected-read redaction (or when metadata-only resolution stripped the
			// ciphertext) — a concealed payload is simply absent.
			if (includeData && attachment.Data != null)
				file.Data = Convert.ToBase64String(attachment.Data);

			return file;
		}
	}
}
