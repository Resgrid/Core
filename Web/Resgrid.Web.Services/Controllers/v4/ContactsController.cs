using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.CallTypes;
using Resgrid.Web.Services.Models.v4.UserDefinedFields;
using System.Linq;
using Resgrid.Model;
using Resgrid.Web.Services.Models.v4.Contacts;
using System;
using Resgrid.Model.Helpers;
using Resgrid.Web.ServicesCore.Helpers;
using System.Collections.Generic;
using System.Threading;
using System.Net.Mime;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Contacts, which are people, entities, and things that can be contacted (i.e. people, departments, groups, etc.) to dispatch a call to.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ContactsController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IContactsService _contactsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IUserDefinedFieldsService _userDefinedFieldsService;
		private readonly IProtectedReadService _protectedReadService;
		private readonly IProtectedWriteService _protectedWriteService;

		public ContactsController(
			IContactsService contactsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			Model.Services.IAuthorizationService authorizationService,
			IEventAggregator eventAggregator,
			IUserDefinedFieldsService userDefinedFieldsService,
			IProtectedReadService protectedReadService,
			IProtectedWriteService protectedWriteService
			)
		{
			_protectedReadService = protectedReadService;
			_protectedWriteService = protectedWriteService;
			_contactsService = contactsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_authorizationService = authorizationService;
			_eventAggregator = eventAggregator;
			_userDefinedFieldsService = userDefinedFieldsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all the contact categories for the department.
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllContactCategories")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<ActionResult<ContactsCategoriesResult>> GetAllContactCategories()
		{
			var result = new ContactsCategoriesResult();

			var contractCategories = await _contactsService.GetContactCategoriesForDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (contractCategories != null && contractCategories.Any())
			{
				foreach (var category in contractCategories)
				{
					var addedOnPerson = await _userProfileService.GetProfileByUserIdAsync(category.AddedByUserId);
					UserProfile editedPerson = null;

					if (!String.IsNullOrWhiteSpace(category.EditedByUserId))
						editedPerson = await _userProfileService.GetProfileByUserIdAsync(category.AddedByUserId);

					result.Data.Add(ConvertCategoryData(category, department, addedOnPerson, editedPerson));
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

			return result;
		}

		/// <summary>
		/// Gets all the contacts for the department.
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllContacts")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<ActionResult<ContactsResult>> GetAllContacts()
		{
			var result = new ContactsResult();

			var contacts = await _contactsService.GetAllContactsForDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (contacts != null && contacts.Any())
			{
				// Attended protected read (plan 7.1): one broker round trip for the whole list;
				// without a valid grant, cataloged fields read as REDACTED — never envelopes.
				var protectedRead = await _protectedReadService.ResolveContactsForReadAsync(DepartmentId,
					contacts.ToList(), Request.Headers[DataProtectionController.GrantHeader].ToString(), UserId);

				foreach (var contact in contacts)
				{
					var addedOnPerson = await _userProfileService.GetProfileByUserIdAsync(contact.AddedByUserId);
					UserProfile editedPerson = null;

					if (!String.IsNullOrWhiteSpace(contact.EditedByUserId))
						editedPerson = await _userProfileService.GetProfileByUserIdAsync(contact.AddedByUserId);

					var contactData = ConvertContactData(contact, department, addedOnPerson, editedPerson);
					contactData.IsProtected = protectedRead.IsProtected;
					contactData.ProtectedReason = protectedRead.ProtectedReason;

					// Per ROW, not the batch union: a field redacted on one contact must not be
					// reported as redacted on every other contact in the list. Without this the
					// clients fall back to sniffing for the literal "REDACTED" string, which
					// mistakes a member who typed that word for a protected value.
					contactData.RedactedFields = Resgrid.Services.ProtectedReadService.GetRedactedFieldIds(
						contact, Resgrid.Services.ProtectedReadService.ContactFieldAccessors);

					result.Data.Add(contactData);
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

			return result;
		}

		/// <summary>
		/// Gets the contact by id
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetContactById")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<ActionResult<ContactResult>> GetContactById(string contactId)
		{
			var result = new ContactResult();

			var contact = await _contactsService.GetContactByIdAsync(contactId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (contact != null && contact.DepartmentId == DepartmentId)
			{
				// Attended protected read (plan 7.1): decrypt-or-redact before conversion.
				var protectedRead = await _protectedReadService.ResolveContactsForReadAsync(DepartmentId,
					new[] { contact }, Request.Headers[DataProtectionController.GrantHeader].ToString(), UserId);

				var addedOnPerson = await _userProfileService.GetProfileByUserIdAsync(contact.AddedByUserId);
				UserProfile editedPerson = null;

				if (!String.IsNullOrWhiteSpace(contact.EditedByUserId))
					editedPerson = await _userProfileService.GetProfileByUserIdAsync(contact.AddedByUserId);

				result.Data = ConvertContactData(contact, department, addedOnPerson, editedPerson);
				result.Data.IsProtected = protectedRead.IsProtected;
				result.Data.ProtectedReason = protectedRead.ProtectedReason;
				result.Data.RedactedFields = protectedRead.RedactedFields;

				var udfValues = await _userDefinedFieldsService.GetFieldValuesForEntityAsync(DepartmentId, (int)UdfEntityType.Contact, contactId);
				if (udfValues != null && udfValues.Any())
				{
					bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
					bool isGroupAdmin = IsCallerGroupAdmin();
					var visibleFields = await _userDefinedFieldsService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Contact, isDeptAdmin, isGroupAdmin);
					var visibleFieldIds = visibleFields.Select(f => f.UdfFieldId).ToHashSet();

					result.Data.UdfValues = udfValues
						.Where(v => visibleFieldIds.Contains(v.UdfFieldId))
						.Select(v => new UdfFieldValueResultData
						{
							UdfFieldValueId = v.UdfFieldValueId,
							UdfFieldId = v.UdfFieldId,
							UdfDefinitionId = v.UdfDefinitionId,
							EntityId = v.EntityId,
							EntityType = v.EntityType,
							Value = v.Value
						}).ToList();
				}

				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets all the Notes for a Contact by the contact id
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetContactNotesByContactId")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<ActionResult<ContactNotesResult>> GetContactNotesByContactId(string contactId)
		{
			var result = new ContactNotesResult();

			var contact = await _contactsService.GetContactByIdAsync(contactId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (contact != null && contact.DepartmentId == DepartmentId)
			{
				var contactNotes = await _contactsService.GetContactNotesByContactIdAsync(contactId, Int32.MaxValue, false);

				// Attended protected read (plan 7.1): note text decrypts with a valid grant or reads
				// as REDACTED — never an envelope.
				var protectedRead = await _protectedReadService.ResolveContactNotesForReadAsync(DepartmentId,
					contactNotes?.ToList(), Request.Headers[DataProtectionController.GrantHeader].ToString(), UserId);

				foreach (var contactNote in contactNotes)
				{
					var addedOnPerson = await _userProfileService.GetProfileByUserIdAsync(contactNote.AddedByUserId);
					UserProfile editedPerson = null;

					if (!String.IsNullOrWhiteSpace(contactNote.EditedByUserId))
						editedPerson = await _userProfileService.GetProfileByUserIdAsync(contactNote.EditedByUserId);

					ContactNoteType noteType = null;
					if (!String.IsNullOrWhiteSpace(contactNote.ContactNoteTypeId))
						noteType = await _contactsService.GetContactNoteTypeByIdAsync(contactNote.ContactNoteTypeId);

					var noteData = ConvertContactNoteData(contactNote, noteType, department, addedOnPerson, editedPerson);
					noteData.IsProtected = protectedRead.IsProtected;
					noteData.ProtectedReason = protectedRead.ProtectedReason;
					result.Data.Add(noteData);
				}

				result.PageSize = contactNotes.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		// ── Pre-plans and hazards (Contacts plan Phase A, A5) ─────────────────────

		/// <summary>
		/// Gets the pre-incident plan for a contact (with its hazards). Data is null when the contact has none.
		/// </summary>
		/// <param name="contactId">Id of the contact</param>
		[HttpGet("GetContactPreplan")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<ActionResult<ContactPreplanResult>> GetContactPreplan(string contactId)
		{
			var result = new ContactPreplanResult();

			var contact = await _contactsService.GetContactByIdAsync(contactId);
			if (contact == null || contact.IsDeleted)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (contact.DepartmentId != DepartmentId)
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var preplan = await _contactsService.GetPreplanByContactIdAsync(contactId, DepartmentId);

			if (preplan != null)
			{
				// Attended protected read (catalog v12): pre-plan and hazard text decrypt with a valid grant
				// or read as REDACTED — never an envelope.
				var preplanRead = await _protectedReadService.ResolveContactPreplansForReadAsync(DepartmentId, new[] { preplan }, ProtectedGrantToken, UserId);
				var hazardRead = await _protectedReadService.ResolveContactPreplanHazardsForReadAsync(DepartmentId, preplan.Hazards, ProtectedGrantToken, UserId);

				result.Data = ConvertPreplanData(preplan, department);
				ApplyProtection(result.Data, preplanRead, hazardRead);
				result.PageSize = 1;
			}
			else
			{
				result.PageSize = 0;
			}

			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Creates or replaces the pre-incident plan for a contact
		/// </summary>
		[HttpPost("SaveContactPreplan")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Contacts_Update)]
		public async Task<ActionResult<SaveContactPreplanResult>> SaveContactPreplan([FromBody] SaveContactPreplanInput input, CancellationToken cancellationToken)
		{
			var result = new SaveContactPreplanResult();

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

			if (!Enum.IsDefined(typeof(ContactPreplanConstructionTypes), input.ConstructionType) ||
				!Enum.IsDefined(typeof(ContactPreplanRoofTypes), input.RoofType) ||
				!Enum.IsDefined(typeof(ContactPreplanOccupancyTypes), input.OccupancyType))
				return BadRequest("Unknown construction, roof or occupancy type.");

			// ADP write preflight (plan 3.3): an attended caller in a protected department needs a current grant
			// BEFORE the transient plaintext row is inserted; the service's write net then envelopes it.
			var writePreflight = await _protectedWriteService.PreflightWriteAsync(DepartmentId, ProtectedGrantToken, UserId, false, cancellationToken);
			if (!writePreflight.Success)
				return ProtectedWriteProblem(writePreflight);

			var existing = await _contactsService.GetPreplanByContactIdAsync(contact.ContactId, DepartmentId);

			var preplan = new ContactPreplan
			{
				ContactId = contact.ContactId,
				DepartmentId = DepartmentId,
				ConstructionType = input.ConstructionType,
				RoofType = input.RoofType,
				OccupancyType = input.OccupancyType,
				OccupancyNotes = input.OccupancyNotes,
				OccupancyHours = input.OccupancyHours,
				OccupantLoad = input.OccupantLoad,
				HasOccupantsNeedingAssistance = input.HasOccupantsNeedingAssistance,
				OccupantsNeedingAssistanceNotes = input.OccupantsNeedingAssistanceNotes,
				GasShutoffLocation = input.GasShutoffLocation,
				ElectricShutoffLocation = input.ElectricShutoffLocation,
				WaterShutoffLocation = input.WaterShutoffLocation,
				UtilityNotes = input.UtilityNotes,
				KnoxBoxLocation = input.KnoxBoxLocation,
				GateCode = input.GateCode,
				AlarmPanelLocation = input.AlarmPanelLocation,
				AlarmCompany = input.AlarmCompany,
				AlarmCompanyPhone = input.AlarmCompanyPhone,
				AccessNotes = input.AccessNotes,
				NearestHydrantLocation = input.NearestHydrantLocation,
				RequiredFireFlowGpm = input.RequiredFireFlowGpm,
				WaterSupplyNotes = input.WaterSupplyNotes,
				EmergencyContactName = input.EmergencyContactName,
				EmergencyContactPhone = input.EmergencyContactPhone,
				SecondaryContactName = input.SecondaryContactName,
				SecondaryContactPhone = input.SecondaryContactPhone,
				HazmatOnSite = input.HazmatOnSite,
				GeneralHazardNotes = input.GeneralHazardNotes,
				TacticalSummary = input.TacticalSummary,
				NextReviewDue = input.NextReviewDueUtc,
				LastReviewedOn = existing?.LastReviewedOn,
				ReviewedByUserId = existing?.ReviewedByUserId
			};

			if (input.MarkReviewed)
			{
				preplan.LastReviewedOn = DateTime.UtcNow;
				preplan.ReviewedByUserId = UserId;
			}

			ContactPreplan saved;
			try
			{
				saved = await _contactsService.SavePreplanAsync(preplan, UserId, IpAddressHelper.GetRequestIP(Request, true),
					$"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);
			}
			catch (InvalidOperationException ex) when (ex.Message == IContactPreplanOwnershipGate.RecordsOwnedReason)
			{
				// RMS-5 write cutover: the occupancy master owns this pre-plan now; edit it under Records.
				return Problem(statusCode: StatusCodes.Status409Conflict, title: "Pre-plans for this department are owned by Records occupancies.", type: "contacts_preplan_records_owned");
			}

			result.Id = saved.ContactPreplanId;
			result.PageSize = 0;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Removes the pre-incident plan (and its hazards) from a contact
		/// </summary>
		/// <param name="contactId">Id of the contact</param>
		[HttpDelete("DeleteContactPreplan")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Contacts_Delete)]
		public async Task<ActionResult<DeleteContactPreplanResult>> DeleteContactPreplan(string contactId, CancellationToken cancellationToken)
		{
			var result = new DeleteContactPreplanResult();

			var contact = await _contactsService.GetContactByIdAsync(contactId);
			if (contact == null || contact.DepartmentId != DepartmentId)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			bool deleted;
			try
			{
				deleted = await _contactsService.DeletePreplanAsync(contactId, DepartmentId, UserId, IpAddressHelper.GetRequestIP(Request, true),
					$"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);
			}
			catch (InvalidOperationException ex) when (ex.Message == IContactPreplanOwnershipGate.RecordsOwnedReason)
			{
				// RMS-5 write cutover: the occupancy master owns this pre-plan now; edit it under Records.
				return Problem(statusCode: StatusCodes.Status409Conflict, title: "Pre-plans for this department are owned by Records occupancies.", type: "contacts_preplan_records_owned");
			}

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

		/// <summary>
		/// Gets the premise hazards for a contact, most severe first
		/// </summary>
		/// <param name="contactId">Id of the contact</param>
		[HttpGet("GetContactHazards")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<ActionResult<ContactHazardsResult>> GetContactHazards(string contactId)
		{
			var result = new ContactHazardsResult();

			var contact = await _contactsService.GetContactByIdAsync(contactId);
			if (contact == null || contact.IsDeleted)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (contact.DepartmentId != DepartmentId)
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var hazards = await _contactsService.GetHazardsByContactIdAsync(contactId, DepartmentId);

			// Attended protected read (catalog v12).
			var hazardRead = await _protectedReadService.ResolveContactPreplanHazardsForReadAsync(DepartmentId, hazards, ProtectedGrantToken, UserId);

			foreach (var hazard in hazards)
			{
				var data = ConvertHazardData(hazard, department);
				data.IsProtected = hazardRead.IsProtected;
				data.ProtectedReason = hazardRead.ProtectedReason;
				result.Data.Add(data);
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Creates (no id) or updates (with id) a premise hazard on a contact
		/// </summary>
		[HttpPost("SaveContactHazard")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Contacts_Update)]
		public async Task<ActionResult<SaveContactHazardResult>> SaveContactHazard([FromBody] SaveContactHazardInput input, CancellationToken cancellationToken)
		{
			var result = new SaveContactHazardResult();

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

			if (!Enum.IsDefined(typeof(ContactPreplanHazardTypes), input.HazardType) ||
				!Enum.IsDefined(typeof(ContactPreplanHazardSeverities), input.Severity))
				return BadRequest("Unknown hazard type or severity.");

			// ADP write preflight (plan 3.3); see SaveContactPreplan.
			var writePreflight = await _protectedWriteService.PreflightWriteAsync(DepartmentId, ProtectedGrantToken, UserId, false, cancellationToken);
			if (!writePreflight.Success)
				return ProtectedWriteProblem(writePreflight);

			var hazard = new ContactPreplanHazard
			{
				ContactPreplanHazardId = string.IsNullOrWhiteSpace(input.ContactPreplanHazardId) ? null : input.ContactPreplanHazardId,
				ContactId = contact.ContactId,
				DepartmentId = DepartmentId,
				HazardType = input.HazardType,
				Severity = input.Severity,
				Title = input.Title,
				Description = input.Description,
				LocationDescription = input.LocationDescription,
				GpsCoordinates = input.GpsCoordinates,
				ShouldAlert = input.ShouldAlert
			};

			ContactPreplanHazard saved;
			try
			{
				saved = await _contactsService.SaveHazardAsync(hazard, UserId, IpAddressHelper.GetRequestIP(Request, true),
					$"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);
			}
			catch (InvalidOperationException ex) when (ex.Message == IContactPreplanOwnershipGate.RecordsOwnedReason)
			{
				// RMS-5 write cutover: the occupancy master owns this pre-plan now; edit it under Records.
				return Problem(statusCode: StatusCodes.Status409Conflict, title: "Pre-plans for this department are owned by Records occupancies.", type: "contacts_preplan_records_owned");
			}
			catch (InvalidOperationException)
			{
				// The hazard id belongs to another contact/department: value-free not found.
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			result.Id = saved.ContactPreplanHazardId;
			result.PageSize = 0;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Removes a premise hazard from a contact
		/// </summary>
		/// <param name="contactPreplanHazardId">Id of the hazard</param>
		[HttpDelete("DeleteContactHazard")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Contacts_Update)]
		public async Task<ActionResult<DeleteContactHazardResult>> DeleteContactHazard(string contactPreplanHazardId, CancellationToken cancellationToken)
		{
			var result = new DeleteContactHazardResult();

			bool deleted;
			try
			{
				deleted = await _contactsService.DeleteHazardAsync(contactPreplanHazardId, DepartmentId, UserId, IpAddressHelper.GetRequestIP(Request, true),
					$"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);
			}
			catch (InvalidOperationException ex) when (ex.Message == IContactPreplanOwnershipGate.RecordsOwnedReason)
			{
				// RMS-5 write cutover: the occupancy master owns this pre-plan now; edit it under Records.
				return Problem(statusCode: StatusCodes.Status409Conflict, title: "Pre-plans for this department are owned by Records occupancies.", type: "contacts_preplan_records_owned");
			}

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

		// ── Private helpers ──────────────────────────────────────────────────────

		/// <summary>The caller's Protected Data Grant, when presented (plan section 3.1 step 6).</summary>
		private string ProtectedGrantToken => Request.Headers[DataProtectionController.GrantHeader].ToString();

		/// <summary>Maps a blocked protected write to its value-free problem response (plan 3.3/19.2).</summary>
		private ObjectResult ProtectedWriteProblem(ProtectedWriteResult write) =>
			Problem(type: write.Reason,
				title: write.Reason == "broker_unavailable"
					? "Protected storage is temporarily unavailable; the change was not saved."
					: "Recent multi-factor verification is required to modify protected data.",
				statusCode: write.Reason == "broker_unavailable" ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status403Forbidden);

		/// <summary>Stamps the batch-level protection metadata onto a pre-plan DTO and its hazards (per-row redaction ids come from the converters).</summary>
		public static void ApplyProtection(ContactPreplanData data, ProtectedReadResult preplanRead, ProtectedReadResult hazardRead)
		{
			if (data == null)
				return;

			data.IsProtected = (preplanRead?.IsProtected ?? false) || (hazardRead?.IsProtected ?? false);
			data.ProtectedReason = preplanRead?.ProtectedReason ?? hazardRead?.ProtectedReason;

			foreach (var hazard in data.Hazards)
			{
				hazard.IsProtected = hazardRead?.IsProtected ?? false;
				hazard.ProtectedReason = hazardRead?.ProtectedReason;
			}
		}

		/// <summary>
		/// Returns true if the current caller holds a group-admin claim for any group.
		/// </summary>
		private bool IsCallerGroupAdmin()
		{
			return HttpContext.User.Claims
				.Any(c => c.Type.StartsWith(ResgridClaimTypes.Resources.Group + "/", StringComparison.Ordinal)
					&& c.Value == ResgridClaimTypes.Actions.Update);
		}

		public static ContactCategoryResultData ConvertCategoryData(ContactCategory category, Department department, UserProfile addedProfile, UserProfile editedProfile)
		{
			var cat = new ContactCategoryResultData();

			cat.ContactCategoryId = category.ContactCategoryId;
			cat.Name = category.Name;
			cat.Description = category.Description;
			cat.Color = category.Color;
			cat.AddedOnUtc = category.AddedOn;
			cat.AddedOn = category.AddedOn.FormatForDepartment(department);
			cat.AddedByUserId = category.AddedByUserId;
			cat.AddedByUserName = addedProfile.FullName.AsFirstNameLastName;
			cat.EditedOnUtc = category.EditedOn;

			if (category.EditedOn.HasValue)
				cat.EditedOn = category.EditedOn.Value.FormatForDepartment(department);

			cat.EditedByUserId = category.EditedByUserId;

			if (editedProfile != null)
				cat.EditedByUserName = editedProfile.FullName.AsFirstNameLastName;

			return cat;
		}

		public static ContactResultData ConvertContactData(Contact contact, Department department, UserProfile addedProfile, UserProfile editedProfile)
		{
			var con = new ContactResultData();

			con.ContactId = contact.ContactId;
			con.ContactType = contact.ContactType;
			con.OtherName = contact.OtherName;
			con.ContactCategoryId = contact.ContactCategoryId;
			//public virtual ContactCategory Category { get; set; }
			con.FirstName = contact.FirstName;
			con.MiddleName = contact.MiddleName;
			con.LastName = contact.LastName;
			con.CompanyName = contact.CompanyName;
			con.Email = contact.Email;
			con.PhysicalAddressId = contact.PhysicalAddressId;
			con.MailingAddressId = contact.MailingAddressId;
			con.Website = contact.Website;
			con.Twitter = contact.Twitter;
			con.Facebook = contact.Facebook;
			con.LinkedIn = contact.LinkedIn;
			con.Instagram = contact.Instagram;
			con.Threads = contact.Threads;
			con.Bluesky = contact.Bluesky;
			con.Mastodon = contact.Mastodon;
			con.LocationGpsCoordinates = contact.LocationGpsCoordinates;
			con.EntranceGpsCoordinates = contact.EntranceGpsCoordinates;
			con.ExitGpsCoordinates = contact.ExitGpsCoordinates;
			con.LocationGeofence = contact.LocationGeofence;
			con.CountryIssuedIdNumber = contact.CountryIssuedIdNumber;
			con.CountryIdName = contact.CountryIdName;
			con.StateIdNumber = contact.StateIdNumber;
			con.StateIdName = contact.StateIdName;
			con.StateIdCountryName = contact.StateIdCountryName;
			con.Description = contact.Description;
			con.OtherInfo = contact.OtherInfo;
			con.HomePhoneNumber = contact.HomePhoneNumber;
			con.CellPhoneNumber = contact.CellPhoneNumber;
			con.FaxPhoneNumber = contact.FaxPhoneNumber;
			con.OfficePhoneNumber = contact.OfficePhoneNumber;

			con.AddedOnUtc = contact.AddedOn;
			con.AddedOn = contact.AddedOn.FormatForDepartment(department);
			con.AddedByUserId = contact.AddedByUserId;
			con.AddedByUserName = addedProfile.FullName.AsFirstNameLastName;

			con.EditedOnUtc = contact.EditedOn;
			if (contact.EditedOn.HasValue)
				con.EditedOn = contact.EditedOn.Value.FormatForDepartment(department);

			con.EditedByUserId = contact.EditedByUserId;
			if (editedProfile != null)
				con.EditedByUserName = editedProfile.FullName.AsFirstNameLastName;

			return con;
		}

		public static ContactNoteResultData ConvertContactNoteData(ContactNote contactNote, ContactNoteType contactNoteType, Department department, UserProfile addedProfile, UserProfile editedProfile)
		{
			var conNote = new ContactNoteResultData();
			conNote.ContactId = contactNote.ContactId;
			conNote.ContactNoteId = contactNote.ContactNoteId;
			conNote.ContactNoteTypeId = contactNote.ContactNoteTypeId;
			conNote.Note = contactNote.Note;
			conNote.ShouldAlert = contactNote.ShouldAlert;
			conNote.Visibility = contactNote.Visibility;

			if (contactNoteType != null)
			{
				conNote.ContactNoteTypeId = contactNoteType.ContactNoteTypeId;
				conNote.NoteType = contactNoteType.Name;
			}

			if (contactNote.ExpiresOn.HasValue)
			{
				conNote.ExpiresOnUtc = contactNote.ExpiresOn;
				conNote.ExpiresOn = contactNote.ExpiresOn.Value.FormatForDepartment(department);
			}

			conNote.IsDeleted = contactNote.IsDeleted;
			conNote.AddedOnUtc = contactNote.AddedOn;
			conNote.AddedOn = contactNote.AddedOn.FormatForDepartment(department);
			conNote.AddedByUserId = contactNote.AddedByUserId;
			conNote.AddedByName = addedProfile.FullName.AsFirstNameLastName;

			if (contactNote.EditedOn.HasValue)
			{
				conNote.EditedOnUtc = contactNote.EditedOn;
				conNote.EditedOn = contactNote.EditedOn.Value.FormatForDepartment(department);
				conNote.EditedByUserId = contactNote.EditedByUserId;
				conNote.EditedByName = editedProfile.FullName.AsFirstNameLastName;
			}

			return conNote;
		}

		public static ContactPreplanData ConvertPreplanData(ContactPreplan preplan, Department department)
		{
			var data = new ContactPreplanData();
			data.ContactPreplanId = preplan.ContactPreplanId;
			data.ContactId = preplan.ContactId;
			data.ConstructionType = preplan.ConstructionType;
			data.ConstructionTypeName = EnumName<ContactPreplanConstructionTypes>(preplan.ConstructionType);
			data.RoofType = preplan.RoofType;
			data.RoofTypeName = EnumName<ContactPreplanRoofTypes>(preplan.RoofType);
			data.OccupancyType = preplan.OccupancyType;
			data.OccupancyTypeName = EnumName<ContactPreplanOccupancyTypes>(preplan.OccupancyType);
			data.OccupancyNotes = preplan.OccupancyNotes;
			data.OccupancyHours = preplan.OccupancyHours;
			data.OccupantLoad = preplan.OccupantLoad;
			data.HasOccupantsNeedingAssistance = preplan.HasOccupantsNeedingAssistance;
			data.OccupantsNeedingAssistanceNotes = preplan.OccupantsNeedingAssistanceNotes;
			data.GasShutoffLocation = preplan.GasShutoffLocation;
			data.ElectricShutoffLocation = preplan.ElectricShutoffLocation;
			data.WaterShutoffLocation = preplan.WaterShutoffLocation;
			data.UtilityNotes = preplan.UtilityNotes;
			data.KnoxBoxLocation = preplan.KnoxBoxLocation;
			data.GateCode = preplan.GateCode;
			data.AlarmPanelLocation = preplan.AlarmPanelLocation;
			data.AlarmCompany = preplan.AlarmCompany;
			data.AlarmCompanyPhone = preplan.AlarmCompanyPhone;
			data.AccessNotes = preplan.AccessNotes;
			data.NearestHydrantLocation = preplan.NearestHydrantLocation;
			data.RequiredFireFlowGpm = preplan.RequiredFireFlowGpm;
			data.WaterSupplyNotes = preplan.WaterSupplyNotes;
			data.EmergencyContactName = preplan.EmergencyContactName;
			data.EmergencyContactPhone = preplan.EmergencyContactPhone;
			data.SecondaryContactName = preplan.SecondaryContactName;
			data.SecondaryContactPhone = preplan.SecondaryContactPhone;
			data.HazmatOnSite = preplan.HazmatOnSite;
			data.GeneralHazardNotes = preplan.GeneralHazardNotes;
			data.TacticalSummary = preplan.TacticalSummary;

			data.LastReviewedOnUtc = preplan.LastReviewedOn;
			if (preplan.LastReviewedOn.HasValue)
				data.LastReviewedOn = preplan.LastReviewedOn.Value.FormatForDepartment(department);
			data.ReviewedByUserId = preplan.ReviewedByUserId;

			data.NextReviewDueUtc = preplan.NextReviewDue;
			if (preplan.NextReviewDue.HasValue)
				data.NextReviewDue = preplan.NextReviewDue.Value.FormatForDepartment(department);
			data.IsReviewOverdue = preplan.IsReviewOverdue(DateTime.UtcNow);

			data.AddedOnUtc = preplan.AddedOn;
			data.AddedOn = preplan.AddedOn.FormatForDepartment(department);
			data.AddedByUserId = preplan.AddedByUserId;

			data.EditedOnUtc = preplan.EditedOn;
			if (preplan.EditedOn.HasValue)
				data.EditedOn = preplan.EditedOn.Value.FormatForDepartment(department);
			data.EditedByUserId = preplan.EditedByUserId;

			if (preplan.Hazards != null)
			{
				foreach (var hazard in preplan.Hazards)
					data.Hazards.Add(ConvertHazardData(hazard, department));
			}

			// Per ROW, not the batch union (ProtectedRedactedFieldIdsTests): only the fields this plan actually had withheld.
			data.RedactedFields = Resgrid.Services.ProtectedReadService.GetRedactedFieldIds(preplan, Resgrid.Services.ProtectedReadService.ContactPreplanFieldAccessors);

			return data;
		}

		public static ContactHazardData ConvertHazardData(ContactPreplanHazard hazard, Department department)
		{
			var data = new ContactHazardData();
			data.ContactPreplanHazardId = hazard.ContactPreplanHazardId;
			data.ContactPreplanId = hazard.ContactPreplanId;
			data.ContactId = hazard.ContactId;
			data.HazardType = hazard.HazardType;
			data.HazardTypeName = EnumName<ContactPreplanHazardTypes>(hazard.HazardType);
			data.Severity = hazard.Severity;
			data.SeverityName = EnumName<ContactPreplanHazardSeverities>(hazard.Severity);
			data.Title = hazard.Title;
			data.Description = hazard.Description;
			data.LocationDescription = hazard.LocationDescription;
			data.GpsCoordinates = hazard.GpsCoordinates;
			data.ShouldAlert = hazard.ShouldAlert;

			data.AddedOnUtc = hazard.AddedOn;
			data.AddedOn = hazard.AddedOn.FormatForDepartment(department);
			data.AddedByUserId = hazard.AddedByUserId;

			data.EditedOnUtc = hazard.EditedOn;
			if (hazard.EditedOn.HasValue)
				data.EditedOn = hazard.EditedOn.Value.FormatForDepartment(department);
			data.EditedByUserId = hazard.EditedByUserId;

			data.RedactedFields = Resgrid.Services.ProtectedReadService.GetRedactedFieldIds(hazard, Resgrid.Services.ProtectedReadService.ContactPreplanHazardFieldAccessors);

			return data;
		}

		private static string EnumName<TEnum>(int value) where TEnum : struct, Enum
		{
			return Enum.IsDefined(typeof(TEnum), value) ? Enum.GetName(typeof(TEnum), value) : value.ToString();
		}
	}
}
