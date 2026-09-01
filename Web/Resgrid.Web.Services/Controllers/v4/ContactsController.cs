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

		public ContactsController(
			IContactsService contactsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			Model.Services.IAuthorizationService authorizationService,
			IEventAggregator eventAggregator,
			IUserDefinedFieldsService userDefinedFieldsService,
			IProtectedReadService protectedReadService
			)
		{
			_protectedReadService = protectedReadService;
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

		// ── Private helpers ──────────────────────────────────────────────────────

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
	}
}
