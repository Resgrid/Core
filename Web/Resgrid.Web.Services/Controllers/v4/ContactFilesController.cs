using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.ContactFiles;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Files attached to Contacts: site documents, pre-plans, floor plans, site photos and drawings
	/// (Contacts plan Phase A). Mirrors CallFiles: metadata lists carry a signed, expiring anonymous
	/// download URL; uploads are base64.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ContactFilesController : V4AuthenticatedApiControllerbase
	{
		/// <summary>Signed-link kind marker; keeps a contact-file link from validating as a call-file link and vice versa.</summary>
		public const string SignedLinkKind = "c";

		#region Members and Constructors
		private readonly IContactsService _contactsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IProtectedReadService _protectedReadService;
		private readonly IProtectedWriteService _protectedWriteService;

		public ContactFilesController(IContactsService contactsService, IDepartmentsService departmentsService,
			IProtectedReadService protectedReadService, IProtectedWriteService protectedWriteService)
		{
			_contactsService = contactsService;
			_departmentsService = departmentsService;
			_protectedReadService = protectedReadService;
			_protectedWriteService = protectedWriteService;
		}

		/// <summary>The caller's Protected Data Grant, when presented (plan section 3.1 step 6).</summary>
		private string ProtectedGrantToken => Request.Headers[DataProtectionController.GrantHeader].ToString();
		#endregion Members and Constructors

		/// <summary>
		/// Gets the files attached to a contact
		/// </summary>
		/// <param name="contactId">Contact to list files for</param>
		/// <param name="includeData">Include the base64 data in the result (metadata only otherwise)</param>
		/// <param name="type">Optional type filter (Document = 0, PrePlan = 1, FloorPlan = 2, SitePhoto = 3, SiteDrawing = 4, Other = 5); omit for all</param>
		[HttpGet("GetFilesForContact")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<ActionResult<ContactFilesResult>> GetFilesForContact(string contactId, bool includeData = false, int? type = null)
		{
			var result = new ContactFilesResult();

			var contact = await _contactsService.GetContactByIdAsync(contactId);
			if (contact == null || contact.IsDeleted)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (contact.DepartmentId != DepartmentId)
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			ContactAttachmentTypes? typeFilter = type.HasValue && Enum.IsDefined(typeof(ContactAttachmentTypes), type.Value)
				? (ContactAttachmentTypes?)type.Value
				: null;

			var attachments = await _contactsService.GetContactAttachmentsAsync(contactId, DepartmentId, typeFilter);

			var rows = new List<ContactAttachment>();
			foreach (var meta in attachments)
			{
				var attachment = meta;
				if (includeData)
					attachment = await _contactsService.GetContactAttachmentByIdAsync(meta.ContactAttachmentId) ?? meta;
				rows.Add(attachment);
			}

			// Attended protected read (catalog v12): names decrypt with a valid grant or read as REDACTED;
			// includeData additionally decrypts the rgdpb payload through the broker, and a concealed payload
			// serializes as null — never ciphertext bytes.
			var protectedRead = await _protectedReadService.ResolveContactAttachmentsForReadAsync(DepartmentId, rows,
				ProtectedGrantToken, UserId, includeData: includeData);

			foreach (var attachment in rows)
			{
				var fileData = ConvertContactFileData(attachment, department, includeData);
				fileData.IsProtected = protectedRead.IsProtected;
				fileData.ProtectedReason = protectedRead.ProtectedReason;
				result.Data.Add(fileData);
			}

			result.PageSize = result.Data.Count;
			result.Status = result.Data.Any() ? ResponseHelper.Success : ResponseHelper.NotFound;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Streams a contact file from a signed link (as issued by GetFilesForContact / GetCallSiteInfo)
		/// </summary>
		/// <param name="query">Signed link token</param>
		[HttpGet("GetFile")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> GetFile(string query)
		{
			var attachment = await ResolveSignedAttachmentAsync(query);
			if (attachment == null)
				return NotFound();

			var contentType = attachment.FileType;
			if (string.IsNullOrWhiteSpace(contentType))
				contentType = FileHelper.GetContentTypeByExtension(Path.GetExtension(attachment.FileName ?? string.Empty).ToLowerInvariant());
			if (string.IsNullOrWhiteSpace(contentType))
				contentType = MediaTypeNames.Application.Octet;

			return File(attachment.Data, contentType, attachment.FileName);
		}

		/// <summary>
		/// Existence check for a signed contact file link
		/// </summary>
		/// <param name="query">Signed link token</param>
		[HttpHead("GetFile")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> GetFileHead(string query)
		{
			var attachment = await ResolveSignedAttachmentAsync(query);
			return attachment == null ? NotFound() : Ok();
		}

		/// <summary>
		/// Attaches a file to a contact
		/// </summary>
		[HttpPost("UploadContactFile")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Contacts_Update)]
		public async Task<ActionResult<UploadContactFileResult>> UploadContactFile([FromBody] UploadContactFileInput input, CancellationToken cancellationToken)
		{
			var result = new UploadContactFileResult();

			if (input == null || !ModelState.IsValid)
				return BadRequest();

			var contact = await _contactsService.GetContactByIdAsync(input.ContactId);
			if (contact == null || contact.IsDeleted)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (contact.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!Enum.IsDefined(typeof(ContactAttachmentTypes), input.Type))
				return BadRequest("Unknown contact file type.");

			byte[] data;
			try
			{
				data = Convert.FromBase64String(input.Data);
			}
			catch (Exception)
			{
				return BadRequest("File data must be base64.");
			}

			if (data.Length == 0)
				return BadRequest("File data is empty.");

			if (data.Length > ContactAttachment.MaxSizeBytes)
				return BadRequest($"Attachment is too large, must be smaller than {ContactAttachment.MaxSizeBytes / (1024 * 1024)}MB.");

			// ADP write preflight (plan 3.3): refuse BEFORE inserting the transient plaintext row.
			var writePreflight = await _protectedWriteService.PreflightWriteAsync(DepartmentId, ProtectedGrantToken, UserId, false, cancellationToken);
			if (!writePreflight.Success)
				return Problem(type: writePreflight.Reason,
					title: writePreflight.Reason == "broker_unavailable"
						? "Protected storage is temporarily unavailable; the file was not saved."
						: "Recent multi-factor verification is required to modify protected data.",
					statusCode: writePreflight.Reason == "broker_unavailable" ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status403Forbidden);

			var attachment = new ContactAttachment
			{
				ContactId = contact.ContactId,
				DepartmentId = DepartmentId,
				ContactAttachmentType = input.Type,
				Name = string.IsNullOrWhiteSpace(input.Name) ? input.FileName : input.Name,
				FileName = input.FileName,
				FileType = FileHelper.GetContentTypeByExtension(Path.GetExtension(input.FileName ?? string.Empty).ToLowerInvariant()),
				Data = data
			};

			var saved = await _contactsService.SaveContactAttachmentAsync(attachment, UserId,
				IpAddressHelper.GetRequestIP(Request, true), $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);

			result.Id = saved.ContactAttachmentId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Created;
			ResponseHelper.PopulateV4ResponseData(result);

			return CreatedAtAction(nameof(GetFilesForContact), new { contactId = contact.ContactId }, result);
		}

		/// <summary>
		/// Removes a file from a contact
		/// </summary>
		/// <param name="contactFileId">Id of the contact file</param>
		[HttpDelete("DeleteContactFile")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Contacts_Delete)]
		public async Task<ActionResult<DeleteContactFileResult>> DeleteContactFile(int contactFileId, CancellationToken cancellationToken)
		{
			var result = new DeleteContactFileResult();

			var deleted = await _contactsService.DeleteContactAttachmentAsync(contactFileId, DepartmentId, UserId,
				IpAddressHelper.GetRequestIP(Request, true), $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);

			if (!deleted)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			result.PageSize = 0;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		#region Helpers

		private async Task<ContactAttachment> ResolveSignedAttachmentAsync(string query)
		{
			if (string.IsNullOrWhiteSpace(query))
				return null;

			string decryptedQuery;
			try
			{
				var decodedQuery = Encoding.UTF8.GetString(Convert.FromBase64String(query));
				decryptedQuery = SymmetricEncryption.Decrypt(decodedQuery, Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);
			}
			catch (Exception)
			{
				// Malformed/foreign query: value-free not-found, never a 500.
				return null;
			}

			if (!TryValidateSignedContactFileQuery(decryptedQuery, out var departmentId, out var attachmentId))
				return null;

			var attachment = await _contactsService.GetContactAttachmentByIdAsync(attachmentId);
			if (attachment == null || attachment.DepartmentId != departmentId)
				return null;

			if (attachment.Data == null || attachment.Data.Length == 0)
				return null;

			// ADP: this is an ANONYMOUS signed-link route — it can never carry a grant, so a protected
			// department's enveloped file is simply not available here (and ciphertext bytes are never
			// served). Attended clients fetch through GetFilesForContact with includeData and their grant.
			if (Resgrid.Services.ProtectedReadService.IsBinaryEnveloped(attachment.Data) ||
				ProtectedDataEnvelope.HasEnvelopePrefix(attachment.FileName) || attachment.IsProtected)
				return null;

			return attachment;
		}

		/// <summary>
		/// Validates the decrypted signed-link payload "c|dept|attachmentId|expiresUtcTicks". The leading kind
		/// marker means a call-file link never resolves here and a contact-file link never resolves on CallFiles.
		/// Expired or malformed reads as not-found.
		/// </summary>
		public static bool TryValidateSignedContactFileQuery(string decryptedQuery, out int departmentId, out int attachmentId)
		{
			departmentId = 0;
			attachmentId = 0;

			if (string.IsNullOrWhiteSpace(decryptedQuery))
				return false;

			var items = decryptedQuery.Split('|');
			if (items.Length != 4 || items[0] != SignedLinkKind)
				return false;

			if (!int.TryParse(items[1].Trim(), out departmentId) || departmentId <= 0)
				return false;

			if (!int.TryParse(items[2].Trim(), out attachmentId) || attachmentId <= 0)
				return false;

			if (!long.TryParse(items[3].Trim(), out var expiresTicks))
				return false;

			return DateTime.UtcNow.Ticks <= expiresTicks;
		}

		/// <summary>Builds the signed, expiring anonymous download URL for a contact attachment.</summary>
		public static string BuildSignedUrl(int departmentId, int contactAttachmentId)
		{
			var expiresTicks = DateTime.UtcNow.AddMinutes(Math.Max(5, Config.SecurityConfig.SignedFileLinkTtlMinutes)).Ticks;
			var query = SymmetricEncryption.Encrypt($"{SignedLinkKind}|{departmentId}|{contactAttachmentId}|{expiresTicks}", Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);

			return Config.SystemBehaviorConfig.ResgridApiBaseUrl + "/api/v4/ContactFiles/GetFile?query=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(query));
		}

		public static ContactFileResultData ConvertContactFileData(ContactAttachment attachment, Department department, bool includeData)
		{
			var file = new ContactFileResultData();
			file.Id = attachment.ContactAttachmentId.ToString();
			file.ContactId = attachment.ContactId;
			file.Type = attachment.ContactAttachmentType;
			file.TypeName = Enum.IsDefined(typeof(ContactAttachmentTypes), attachment.ContactAttachmentType)
				? ((ContactAttachmentTypes)attachment.ContactAttachmentType).ToString()
				: ContactAttachmentTypes.Other.ToString();
			file.Name = attachment.Name;
			file.FileName = attachment.FileName;
			file.Size = attachment.Size;
			file.Mime = string.IsNullOrWhiteSpace(attachment.FileType)
				? FileHelper.GetContentTypeByExtension(Path.GetExtension(attachment.FileName ?? string.Empty))
				: attachment.FileType;
			// An enveloped file has no anonymous link: the signed route cannot carry a grant.
			file.Url = attachment.IsProtected ? null : BuildSignedUrl(attachment.DepartmentId, attachment.ContactAttachmentId);
			file.UserId = attachment.AddedByUserId;
			file.RedactedFields = Resgrid.Services.ProtectedReadService.GetRedactedFieldIds(attachment, Resgrid.Services.ProtectedReadService.ContactAttachmentFieldAccessors);
			file.Timestamp = attachment.AddedOn.TimeConverterToString(department);

			if (includeData && attachment.Data != null)
				file.Data = Convert.ToBase64String(attachment.Data);

			return file;
		}

		#endregion
	}
}
