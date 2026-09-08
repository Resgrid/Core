using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;
using Resgrid.Framework;
using Resgrid.Model.Events;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using Resgrid.Model.Helpers;
using Resgrid.Localization;
using Resgrid.Web.Areas.User.Models.Contacts;
using Resgrid.WebCore.Areas.User.Models;
using Resgrid.WebCore.Areas.User.Models.Contacts;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class ContactsController : SecureBaseController
	{
		#region Private Members and Constructors

		private readonly IContactsService _contactsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IAddressService _addressService;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICallsService _callsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IUserDefinedFieldsService _userDefinedFieldsService;
		private readonly IUdfRenderingService _udfRenderingService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IRouteService _routeService;
		private readonly IPhoneNumberProcesserProvider _phoneNumberProcesser;
		private readonly IProtectedReadService _protectedReadService;
		private readonly IContactPreplanOwnershipGate _preplanOwnership;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Contacts.Contacts> _localizer;

		public ContactsController(IContactsService contactsService, IDepartmentsService departmentsService, IUserProfileService userProfileService,
			IAddressService addressService, IEventAggregator eventAggregator, ICallsService callsService, IAuthorizationService authorizationService,
			IUserDefinedFieldsService userDefinedFieldsService, IUdfRenderingService udfRenderingService,
			IDepartmentGroupsService departmentGroupsService, IRouteService routeService, IPhoneNumberProcesserProvider phoneNumberProcesser,
			IProtectedReadService protectedReadService, IContactPreplanOwnershipGate preplanOwnership,
			IStringLocalizer<Resgrid.Localization.Areas.User.Contacts.Contacts> localizer)
		{
			_preplanOwnership = preplanOwnership;
			_localizer = localizer;
			_contactsService = contactsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_addressService = addressService;
			_eventAggregator = eventAggregator;
			_callsService = callsService;
			_authorizationService = authorizationService;
			_userDefinedFieldsService = userDefinedFieldsService;
			_udfRenderingService = udfRenderingService;
			_departmentGroupsService = departmentGroupsService;
			_routeService = routeService;
			_phoneNumberProcesser = phoneNumberProcesser;
			_protectedReadService = protectedReadService;
		}

		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<IActionResult> Index()
		{
			var model = new ContactsIndexView();
			model.ContactCategories = await _contactsService.GetContactCategoriesForDepartmentAsync(DepartmentId);
			model.Contacts = await _contactsService.GetAllContactsForDepartmentAsync(DepartmentId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.PreplanReviewOverdueContactIds = new HashSet<string>(
				(await _contactsService.GetPreplansDueForReviewAsync(DepartmentId)).Select(x => x.ContactId), StringComparer.Ordinal);

			// ADP: server-rendered lists show REDACTED for protected values (no grant server-side).
			await _protectedReadService.ResolveContactsForReadAsync(DepartmentId, model.Contacts, null, UserId);

			// The categories tree carries its own Contact collections (separate instances).
			if (model.ContactCategories != null)
			{
				foreach (var category in model.ContactCategories)
				{
					if (category.Contacts != null && category.Contacts.Any())
						await _protectedReadService.ResolveContactsForReadAsync(DepartmentId, category.Contacts.ToList(), null, UserId);
				}
			}

			List<BSTreeModel> trees = new List<BSTreeModel>();
			var tree0 = new BSTreeModel();
			tree0.id = "TreeGroup_-1";
			tree0.text = "All Contacts";
			tree0.icon = "";
			trees.Add(tree0);

			var tree1 = new BSTreeModel();
			tree1.id = "TreeGroup_0";
			tree1.text = "No Category Contacts";
			tree1.icon = "";
			trees.Add(tree1);

			if (model.ContactCategories != null && model.ContactCategories.Any())
			{
				foreach (var category in model.ContactCategories)
				{
					var tree = new BSTreeModel();
					tree.id = $"TreeGroup_{category.ContactCategoryId.ToString()}";
					tree.text = category.Name;
					tree.icon = "";

					trees.Add(tree);
				}
			}
			model.TreeData = Newtonsoft.Json.JsonConvert.SerializeObject(trees);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<IActionResult> View(string contactId)
		{
			if (String.IsNullOrWhiteSpace(contactId))
				return Unauthorized();

			var model = new ViewContactView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Contact = await _contactsService.GetContactByIdAsync(contactId);

			var noteTypes = new List<ContactNoteType>();
			noteTypes.Add(new ContactNoteType { ContactNoteTypeId = "", Name = "No Type" });
			noteTypes.AddRange(await _contactsService.GetContactNoteTypesByDepartmentIdAsync(DepartmentId));
			model.NoteTypes = noteTypes;

			if (model.Contact == null)
				return Unauthorized();

			if (model.Contact.DepartmentId != DepartmentId)
				return Unauthorized();

			// Load physical address if it exists
			if (model.Contact.PhysicalAddressId.HasValue)
				model.PhysicalAddress = await _addressService.GetAddressByIdAsync(model.Contact.PhysicalAddressId.Value);

			// Load mailing address if it exists and is different from physical
			if (model.Contact.MailingAddressId.HasValue &&
				(!model.Contact.PhysicalAddressId.HasValue || model.Contact.MailingAddressId != model.Contact.PhysicalAddressId))
				model.MailingAddress = await _addressService.GetAddressByIdAsync(model.Contact.MailingAddressId.Value);

			model.Notes = await _contactsService.GetContactNotesByContactIdAsync(contactId, DepartmentId);

			// Contacts plan Phase A: Pre-Plan and Files tabs. ADP catalog v12: render REDACTED; RevealContact
			// returns the pre-plan and hazard values alongside the contact's own fields.
			model.Preplan = await _contactsService.GetPreplanByContactIdAsync(contactId, DepartmentId);
			model.Hazards = await _contactsService.GetHazardsByContactIdAsync(contactId, DepartmentId);
			model.Attachments = await _contactsService.GetContactAttachmentsAsync(contactId, DepartmentId);
			if (model.Preplan != null)
				await _protectedReadService.ResolveContactPreplansForReadAsync(DepartmentId, new List<ContactPreplan> { model.Preplan }, null, UserId);
			await _protectedReadService.ResolveContactPreplanHazardsForReadAsync(DepartmentId, model.Hazards, null, UserId);
			await _protectedReadService.ResolveContactAttachmentsForReadAsync(DepartmentId, model.Attachments, null, UserId);

			// ADP: render REDACTED; the reveal is client-side (step-up modal then RevealContact).
			var protectedRead = await _protectedReadService.ResolveContactsForReadAsync(DepartmentId,
				new List<Contact> { model.Contact }, null, UserId);
			await _protectedReadService.ResolveContactNotesForReadAsync(DepartmentId, model.Notes, null, UserId);
			model.IsProtectedContact = protectedRead.IsProtected;

			model.RouteStops = await _routeService.GetRouteStopsForContactAsync(contactId, DepartmentId) ?? new List<RouteStop>();
			if (model.RouteStops.Count > 0)
			{
				var planIds = model.RouteStops.Select(s => s.RoutePlanId).Distinct().ToList();
				var allPlans = await _routeService.GetRoutePlansForDepartmentAsync(DepartmentId);
				model.RoutePlans = allPlans.Where(p => planIds.Contains(p.RoutePlanId)).ToList();
			}
			else
			{
				model.RoutePlans = new List<RoutePlan>();
			}

			var udfDefinition = await _userDefinedFieldsService.GetActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Contact);
			if (udfDefinition != null)
			{
				bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
				bool isGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
				var udfFields = await _userDefinedFieldsService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Contact, isDeptAdmin, isGroupAdmin);
				var udfValues = await _userDefinedFieldsService.GetFieldValuesForEntityAsync(DepartmentId, (int)UdfEntityType.Contact, contactId);
				var visibleFieldIds = udfFields.Select(f => f.UdfFieldId).ToHashSet();
				var filteredValues = (udfValues ?? new List<UdfFieldValue>()).Where(v => visibleFieldIds.Contains(v.UdfFieldId)).ToList();
				model.UdfReadOnlyHtml = _udfRenderingService.GenerateReadOnlyHtml(udfDefinition, udfFields, filteredValues);
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_Create)]
		public async Task<IActionResult> Add()
		{
			var model = new AddContactView();
			model.Contact = new Contact();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			ViewBag.Countries = new SelectList(Countries.CountryNames);
			ViewBag.TimeZones = new SelectList(TimeZones.Zones, "Key", "Value");
			ViewBag.Languages = new SelectList(SupportedLocales.SupportedLanguagesMap, "Key", "Value");

			var categories = await _contactsService.GetContactCategoriesForDepartmentAsync(DepartmentId);
			ViewBag.Categories = new SelectList(categories, "ContactCategoryId", "Name");

			var udfDefinition = await _userDefinedFieldsService.GetActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Contact);
			if (udfDefinition != null)
			{
				bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
				bool isGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
				var udfFields = await _userDefinedFieldsService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Contact, isDeptAdmin, isGroupAdmin);
				model.UdfFormHtml = _udfRenderingService.GenerateHtmlFormFields(udfDefinition, udfFields, new List<UdfFieldValue>());
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Contacts_Create)]
		public async Task<IActionResult> Add(AddContactView model, CancellationToken cancellationToken)
		{
			if (model.Contact.ContactType == 0 && (string.IsNullOrWhiteSpace(model.Contact.FirstName) || string.IsNullOrWhiteSpace(model.Contact.LastName)))
				ModelState.AddModelError("Contact.ContactType", "For the Person Contact Type you must supply and First Name and Last Name.");
			else if (model.Contact.ContactType == 1 && string.IsNullOrWhiteSpace(model.Contact.CompanyName))
				ModelState.AddModelError("Contact.ContactType", "For the Company Contact Type you must supply a Company Name.");

			ViewBag.Countries = new SelectList(Countries.CountryNames);
			ViewBag.TimeZones = new SelectList(TimeZones.Zones, "Key", "Value");
			ViewBag.Languages = new SelectList(SupportedLocales.SupportedLanguagesMap, "Key", "Value");
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			var addPostCategories = await _contactsService.GetContactCategoriesForDepartmentAsync(DepartmentId);
			ViewBag.Categories = new SelectList(addPostCategories, "ContactCategoryId", "Name");

			// They specified a street address for physical
			if (!String.IsNullOrWhiteSpace(model.PhysicalAddress1))
			{
				if (String.IsNullOrEmpty(model.PhysicalCity))
					ModelState.AddModelError("City", string.Format("The Physical City field is required"));

				if (String.IsNullOrEmpty(model.PhysicalCountry))
					ModelState.AddModelError("Country", string.Format("The Physical Country field is required"));

				if (String.IsNullOrEmpty(model.PhysicalPostalCode))
					ModelState.AddModelError("PostalCode", string.Format("The Physical Postal Code field is required"));

				if (String.IsNullOrEmpty(model.PhysicalState))
					ModelState.AddModelError("State", string.Format("The Physical State/Provence field is required"));
			}

			if (!String.IsNullOrWhiteSpace(model.MailingAddress1) && !model.MailingAddressSameAsPhysical)
			{
				if (String.IsNullOrEmpty(model.MailingCity))
					ModelState.AddModelError("City", string.Format("The Mailing City field is required"));

				if (String.IsNullOrEmpty(model.MailingCountry))
					ModelState.AddModelError("Country", string.Format("The Mailing Country field is required"));

				if (String.IsNullOrEmpty(model.MailingPostalCode))
					ModelState.AddModelError("PostalCode", string.Format("The Mailing Postal Code field is required"));

				if (String.IsNullOrEmpty(model.MailingState))
					ModelState.AddModelError("State", string.Format("The Mailing State/Provence field is required"));
			}

			if (!String.IsNullOrWhiteSpace(model.LocationGpsLatitude) && !LocationHelpers.IsValidLatitude(model.LocationGpsLatitude))
			{
				ModelState.AddModelError("LocationGpsLatitude", "Location Latitude value seems invalid, MUST be decimal format.");
			}

			if (!String.IsNullOrWhiteSpace(model.LocationGpsLongitude) && !LocationHelpers.IsValidLongitude(model.LocationGpsLongitude))
			{
				ModelState.AddModelError("LocationGpsLongitude", "Location Longitude value seems invalid, MUST be decimal format.");
			}

			if (!String.IsNullOrWhiteSpace(model.EntranceGpsLatitude) && !LocationHelpers.IsValidLatitude(model.EntranceGpsLatitude))
			{
				ModelState.AddModelError("EntranceGpsLatitude", "Entrance Latitude value seems invalid, MUST be decimal format.");
			}

			if (!String.IsNullOrWhiteSpace(model.EntranceGpsLongitude) && !LocationHelpers.IsValidLongitude(model.EntranceGpsLongitude))
			{
				ModelState.AddModelError("EntranceGpsLongitude", "Entrance Longitude value seems invalid, MUST be decimal format.");
			}

			if (!String.IsNullOrWhiteSpace(model.ExitGpsLatitude) && !LocationHelpers.IsValidLongitude(model.ExitGpsLatitude))
			{
				ModelState.AddModelError("ExitGpsLatitude", "Exit Longitude value seems invalid, MUST be decimal format.");
			}

			if (!String.IsNullOrWhiteSpace(model.ExitGpsLongitude) && !LocationHelpers.IsValidLongitude(model.ExitGpsLongitude))
			{
				ModelState.AddModelError("ExitGpsLongitude", "Exit Longitude value seems invalid, MUST be decimal format.");
			}

			// Server-side phone backstop: validate + normalize each number to E.164 so saved values are
			// Twilio-sendable (mirrors EditUserProfile). Region comes from the contact's physical country.
			model.Contact.CellPhoneNumber = PhoneValidationHelper.ValidateAndNormalize(_phoneNumberProcesser, ModelState, "Contact.CellPhoneNumber", "cell phone number", model.Contact.CellPhoneNumber, model.PhysicalCountry);
			model.Contact.HomePhoneNumber = PhoneValidationHelper.ValidateAndNormalize(_phoneNumberProcesser, ModelState, "Contact.HomePhoneNumber", "home phone number", model.Contact.HomePhoneNumber, model.PhysicalCountry);
			model.Contact.FaxPhoneNumber = PhoneValidationHelper.ValidateAndNormalize(_phoneNumberProcesser, ModelState, "Contact.FaxPhoneNumber", "fax number", model.Contact.FaxPhoneNumber, model.PhysicalCountry);
			model.Contact.OfficePhoneNumber = PhoneValidationHelper.ValidateAndNormalize(_phoneNumberProcesser, ModelState, "Contact.OfficePhoneNumber", "office phone number", model.Contact.OfficePhoneNumber, model.PhysicalCountry);

			Address physicalAddress = new Address();
			Address mailingAddress = new Address();

			if (ModelState.IsValid)
			{
				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.ContactAdded;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

				// Each pair of inputs writes the column its label names. The location pair used to
				// write EntranceGpsCoordinates, which the entrance pair then overwrote, so
				// LocationGpsCoordinates was never populated from this form.
				if (!String.IsNullOrWhiteSpace(model.LocationGpsLatitude) && !String.IsNullOrWhiteSpace(model.LocationGpsLongitude))
				{
					model.Contact.LocationGpsCoordinates = $"{model.LocationGpsLatitude},{model.LocationGpsLongitude}";
				}

				if (!String.IsNullOrWhiteSpace(model.EntranceGpsLatitude) && !String.IsNullOrWhiteSpace(model.EntranceGpsLongitude))
				{
					model.Contact.EntranceGpsCoordinates = $"{model.EntranceGpsLatitude},{model.EntranceGpsLongitude}";
				}

				if (!String.IsNullOrWhiteSpace(model.ExitGpsLatitude) && !String.IsNullOrWhiteSpace(model.ExitGpsLongitude))
				{
					model.Contact.ExitGpsCoordinates = $"{model.ExitGpsLatitude},{model.ExitGpsLongitude}";
				}

				if (!String.IsNullOrWhiteSpace(model.PhysicalAddress1))
				{
					physicalAddress.Address1 = model.PhysicalAddress1;
					physicalAddress.City = model.PhysicalCity;
					physicalAddress.Country = model.PhysicalCountry;
					physicalAddress.PostalCode = model.PhysicalPostalCode;
					physicalAddress.State = model.PhysicalState;

					physicalAddress = await _addressService.SaveAddressAsync(physicalAddress, cancellationToken);
					model.Contact.PhysicalAddressId = physicalAddress.AddressId;

					if (model.MailingAddressSameAsPhysical)
						model.Contact.MailingAddressId = physicalAddress.AddressId;
				}

				if (!String.IsNullOrWhiteSpace(model.MailingAddress1) && !model.MailingAddressSameAsPhysical)
				{
					mailingAddress.Address1 = model.MailingAddress1;
					mailingAddress.City = model.MailingCity;
					mailingAddress.Country = model.MailingCountry;
					mailingAddress.PostalCode = model.MailingPostalCode;
					mailingAddress.State = model.MailingState;

					mailingAddress = await _addressService.SaveAddressAsync(mailingAddress, cancellationToken);
					model.Contact.MailingAddressId = mailingAddress.AddressId;
				}

				model.Contact.DepartmentId = DepartmentId;
				model.Contact.AddedByUserId = UserId;
				model.Contact.AddedOn = DateTime.UtcNow;

				await _contactsService.SaveContactAsync(model.Contact, cancellationToken);

				// Save UDF field values for the new contact
				var udfDefinitionForCreate = await _userDefinedFieldsService.GetActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Contact);
				var udfValues = Request.Form.Keys
					.Where(k => k.StartsWith("udf_") && !k.EndsWith("_exists"))
					.Select(k => new UdfFieldValue
					{
						UdfFieldId = k.Substring(4),
						UdfDefinitionId = udfDefinitionForCreate?.UdfDefinitionId,
						Value = Request.Form[k]
					}).ToList();

				if (udfValues.Any())
				{
					bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
					bool isGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
					var validationErrors = await _userDefinedFieldsService.SaveFieldValuesForEntityAsync(DepartmentId, (int)UdfEntityType.Contact, model.Contact.ContactId, udfValues, UserId, isDeptAdmin, isGroupAdmin, cancellationToken);

					if (validationErrors.Count > 0)
					{
						foreach (var kvp in validationErrors)
						{
							foreach (var errorMessage in kvp.Value)
								ModelState.AddModelError(kvp.Key, errorMessage);
						}

						return View(model);
					}
				}

				auditEvent.After = model.Contact.CloneJsonToString();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				return RedirectToAction("Index", "Contacts", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_Create)]
		public async Task<IActionResult> Edit(string contactId)
		{
			if (String.IsNullOrWhiteSpace(contactId))
				return BadRequest();

			var model = new EditContactView();
			model.Contact = await _contactsService.GetContactByIdAsync(contactId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (model.Contact == null)
				return BadRequest();

			if (model.Contact.DepartmentId != DepartmentId)
				return Unauthorized();

			// ADP: the edit form renders protected values as the REDACTED sentinel; unchanged
			// fields posted back are restored to their stored envelopes by the write safety net.
			// Editing blind is workable but poor, so the page also carries the reveal banner.
			var protectedEditRead = await _protectedReadService.ResolveContactsForReadAsync(DepartmentId,
				new List<Contact> { model.Contact }, null, UserId);
			model.IsProtectedContact = protectedEditRead.IsProtected;

			// Each pair of inputs reads the column its label names. The Entrance coordinates used
			// to be read into the LOCATION inputs (and the location block then overwrote them), so
			// the Entrance boxes on this form were never populated at all.
			if (!String.IsNullOrWhiteSpace(model.Contact.LocationGpsCoordinates) &&
				model.Contact.LocationGpsCoordinates.Contains(','))
			{
				var locationGpsCoordinates = model.Contact.LocationGpsCoordinates.Split(',');
				model.LocationGpsLatitude = locationGpsCoordinates[0];
				model.LocationGpsLongitude = locationGpsCoordinates[1];
			}

			if (!String.IsNullOrWhiteSpace(model.Contact.EntranceGpsCoordinates) &&
				model.Contact.EntranceGpsCoordinates.Contains(','))
			{
				var entranceGpsCoordinates = model.Contact.EntranceGpsCoordinates.Split(',');
				model.EntranceGpsLatitude = entranceGpsCoordinates[0];
				model.EntranceGpsLongitude = entranceGpsCoordinates[1];
			}

			if (!String.IsNullOrWhiteSpace(model.Contact.ExitGpsCoordinates) &&
				model.Contact.ExitGpsCoordinates.Contains(','))
			{
				var exitGpsCoordinates = model.Contact.ExitGpsCoordinates.Split(',');
				model.ExitGpsLatitude = exitGpsCoordinates[0];
				model.ExitGpsLongitude = exitGpsCoordinates[1];
			}

			if (model.Contact.PhysicalAddressId.HasValue)
			{
				var address = await _addressService.GetAddressByIdAsync(model.Contact.PhysicalAddressId.Value);
				model.PhysicalAddress1 = address.Address1;
				model.PhysicalCity = address.City;
				model.PhysicalCountry = address.Country;
				model.PhysicalPostalCode = address.PostalCode;
				model.PhysicalState = address.State;
			}

			if (model.Contact.MailingAddressId.HasValue)
			{
				var address = await _addressService.GetAddressByIdAsync(model.Contact.MailingAddressId.Value);

				model.MailingAddress1 = address.Address1;
				model.MailingCity = address.City;
				model.MailingCountry = address.Country;
				model.MailingPostalCode = address.PostalCode;
				model.MailingState = address.State;
			}

			ViewBag.Countries = new SelectList(Countries.CountryNames);
			ViewBag.TimeZones = new SelectList(TimeZones.Zones, "Key", "Value");
			ViewBag.Languages = new SelectList(SupportedLocales.SupportedLanguagesMap, "Key", "Value");

			var editCategories = await _contactsService.GetContactCategoriesForDepartmentAsync(DepartmentId);
			ViewBag.Categories = new SelectList(editCategories, "ContactCategoryId", "Name", model.Contact.ContactCategoryId);

			var udfDefinitionEdit = await _userDefinedFieldsService.GetActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Contact);
			if (udfDefinitionEdit != null)
			{
				bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
				bool isGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
				var udfFieldsEdit = await _userDefinedFieldsService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Contact, isDeptAdmin, isGroupAdmin);
				var udfValuesEdit = await _userDefinedFieldsService.GetFieldValuesForEntityAsync(DepartmentId, (int)UdfEntityType.Contact, contactId);
				var visibleFieldIds = udfFieldsEdit.Select(f => f.UdfFieldId).ToHashSet();
				var filteredValues = (udfValuesEdit ?? new List<UdfFieldValue>()).Where(v => visibleFieldIds.Contains(v.UdfFieldId)).ToList();
				model.UdfFormHtml = _udfRenderingService.GenerateHtmlFormFields(udfDefinitionEdit, udfFieldsEdit, filteredValues);
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Contacts_Create)]
		public async Task<IActionResult> Edit(EditContactView model, CancellationToken cancellationToken)
		{
			if (model.Contact.ContactType == 0 && (string.IsNullOrWhiteSpace(model.Contact.FirstName) || string.IsNullOrWhiteSpace(model.Contact.LastName)))
				ModelState.AddModelError("Contact.ContactType", "For the Person Contact Type you must supply and First Name and Last Name.");
			else if (model.Contact.ContactType == 1 && string.IsNullOrWhiteSpace(model.Contact.CompanyName))
				ModelState.AddModelError("Contact.ContactType", "For the Company Contact Type you must supply a Company Name.");

			ViewBag.Countries = new SelectList(Countries.CountryNames);
			ViewBag.TimeZones = new SelectList(TimeZones.Zones, "Key", "Value");
			ViewBag.Languages = new SelectList(SupportedLocales.SupportedLanguagesMap, "Key", "Value");
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			var editPostCategories = await _contactsService.GetContactCategoriesForDepartmentAsync(DepartmentId);
			ViewBag.Categories = new SelectList(editPostCategories, "ContactCategoryId", "Name");

			// They specified a street address for physical
			if (!String.IsNullOrWhiteSpace(model.PhysicalAddress1))
			{
				if (String.IsNullOrEmpty(model.PhysicalCity))
					ModelState.AddModelError("City", string.Format("The Physical City field is required"));

				if (String.IsNullOrEmpty(model.PhysicalCountry))
					ModelState.AddModelError("Country", string.Format("The Physical Country field is required"));

				if (String.IsNullOrEmpty(model.PhysicalPostalCode))
					ModelState.AddModelError("PostalCode", string.Format("The Physical Postal Code field is required"));

				if (String.IsNullOrEmpty(model.PhysicalState))
					ModelState.AddModelError("State", string.Format("The Physical State/Provence field is required"));
			}

			if (!String.IsNullOrWhiteSpace(model.MailingAddress1) && !model.MailingAddressSameAsPhysical)
			{
				if (String.IsNullOrEmpty(model.MailingCity))
					ModelState.AddModelError("City", string.Format("The Mailing City field is required"));

				if (String.IsNullOrEmpty(model.MailingCountry))
					ModelState.AddModelError("Country", string.Format("The Mailing Country field is required"));

				if (String.IsNullOrEmpty(model.MailingPostalCode))
					ModelState.AddModelError("PostalCode", string.Format("The Mailing Postal Code field is required"));

				if (String.IsNullOrEmpty(model.MailingState))
					ModelState.AddModelError("State", string.Format("The Mailing State/Provence field is required"));
			}

			if (!String.IsNullOrWhiteSpace(model.LocationGpsLatitude) && !LocationHelpers.IsValidLatitude(model.LocationGpsLatitude))
			{
				ModelState.AddModelError("LocationGpsLatitude", "Location Latitude value seems invalid, MUST be decimal format.");
			}

			if (!String.IsNullOrWhiteSpace(model.LocationGpsLongitude) && !LocationHelpers.IsValidLongitude(model.LocationGpsLongitude))
			{
				ModelState.AddModelError("LocationGpsLongitude", "Location Longitude value seems invalid, MUST be decimal format.");
			}

			if (!String.IsNullOrWhiteSpace(model.EntranceGpsLatitude) && !LocationHelpers.IsValidLatitude(model.EntranceGpsLatitude))
			{
				ModelState.AddModelError("EntranceGpsLatitude", "Entrance Latitude value seems invalid, MUST be decimal format.");
			}

			if (!String.IsNullOrWhiteSpace(model.EntranceGpsLongitude) && !LocationHelpers.IsValidLongitude(model.EntranceGpsLongitude))
			{
				ModelState.AddModelError("EntranceGpsLongitude", "Entrance Longitude value seems invalid, MUST be decimal format.");
			}

			if (!String.IsNullOrWhiteSpace(model.ExitGpsLatitude) && !LocationHelpers.IsValidLongitude(model.ExitGpsLatitude))
			{
				ModelState.AddModelError("ExitGpsLatitude", "Exit Longitude value seems invalid, MUST be decimal format.");
			}

			if (!String.IsNullOrWhiteSpace(model.ExitGpsLongitude) && !LocationHelpers.IsValidLongitude(model.ExitGpsLongitude))
			{
				ModelState.AddModelError("ExitGpsLongitude", "Exit Longitude value seems invalid, MUST be decimal format.");
			}

			// Server-side phone backstop: validate + normalize each number to E.164 so saved values are
			// Twilio-sendable (mirrors EditUserProfile). Region comes from the contact's physical country.
			model.Contact.CellPhoneNumber = PhoneValidationHelper.ValidateAndNormalize(_phoneNumberProcesser, ModelState, "Contact.CellPhoneNumber", "cell phone number", model.Contact.CellPhoneNumber, model.PhysicalCountry);
			model.Contact.HomePhoneNumber = PhoneValidationHelper.ValidateAndNormalize(_phoneNumberProcesser, ModelState, "Contact.HomePhoneNumber", "home phone number", model.Contact.HomePhoneNumber, model.PhysicalCountry);
			model.Contact.FaxPhoneNumber = PhoneValidationHelper.ValidateAndNormalize(_phoneNumberProcesser, ModelState, "Contact.FaxPhoneNumber", "fax number", model.Contact.FaxPhoneNumber, model.PhysicalCountry);
			model.Contact.OfficePhoneNumber = PhoneValidationHelper.ValidateAndNormalize(_phoneNumberProcesser, ModelState, "Contact.OfficePhoneNumber", "office phone number", model.Contact.OfficePhoneNumber, model.PhysicalCountry);

			if (ModelState.IsValid)
			{
				var contact = await _contactsService.GetContactByIdAsync(model.Contact.ContactId);

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.ContactEdited;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				auditEvent.Before = contact.CloneJsonToString();

				// The STORED row is the save target, not the posted one. This form binds 22 of the
				// contact's columns; the entity has more - the image, the geofence, the five
				// government-ID fields, both address links and the audit stamps - and persisting the
				// posted object blanked every one of them on every edit. For a protected department
				// that included the only copy of enveloped ID numbers. Copying the posted values
				// onto the stored row is also what the rest of this area does (UnitsController.EditUnit).
				contact.ContactType = model.Contact.ContactType;
				contact.ContactCategoryId = model.Contact.ContactCategoryId;
				contact.FirstName = model.Contact.FirstName;
				contact.MiddleName = model.Contact.MiddleName;
				contact.LastName = model.Contact.LastName;
				contact.OtherName = model.Contact.OtherName;
				contact.CompanyName = model.Contact.CompanyName;
				contact.Email = model.Contact.Email;
				contact.HomePhoneNumber = model.Contact.HomePhoneNumber;
				contact.CellPhoneNumber = model.Contact.CellPhoneNumber;
				contact.FaxPhoneNumber = model.Contact.FaxPhoneNumber;
				contact.OfficePhoneNumber = model.Contact.OfficePhoneNumber;
				contact.Description = model.Contact.Description;
				contact.OtherInfo = model.Contact.OtherInfo;
				contact.Website = model.Contact.Website;
				contact.Twitter = model.Contact.Twitter;
				contact.Facebook = model.Contact.Facebook;
				contact.LinkedIn = model.Contact.LinkedIn;
				contact.Instagram = model.Contact.Instagram;
				contact.Threads = model.Contact.Threads;
				contact.Bluesky = model.Contact.Bluesky;
				contact.Mastodon = model.Contact.Mastodon;

				// ADP: a protected contact renders its coordinates as the REDACTED placeholder, which
				// has no comma to split, so the latitude/longitude inputs come back EMPTY for a value
				// the editor was never shown. Clearing on empty would destroy the stored coordinates
				// on ANY save of this page. The form round-trips the placeholder in a hidden field,
				// so "empty inputs + placeholder" means unchanged, while empty inputs with no
				// placeholder stay a deliberate clear.
				string ResolveCoordinates(string latitude, string longitude, string postedValue, string storedValue)
				{
					if (!String.IsNullOrWhiteSpace(latitude) && !String.IsNullOrWhiteSpace(longitude))
						return $"{latitude},{longitude}";

					return postedValue == ProtectedDataEnvelope.RedactionValue ? storedValue : null;
				}

				// Each pair of inputs writes the column its label names; the location pair used to
				// write EntranceGpsCoordinates, which the entrance pair then overwrote.
				contact.LocationGpsCoordinates = ResolveCoordinates(model.LocationGpsLatitude, model.LocationGpsLongitude,
					model.Contact.LocationGpsCoordinates, contact.LocationGpsCoordinates);
				contact.EntranceGpsCoordinates = ResolveCoordinates(model.EntranceGpsLatitude, model.EntranceGpsLongitude,
					model.Contact.EntranceGpsCoordinates, contact.EntranceGpsCoordinates);
				contact.ExitGpsCoordinates = ResolveCoordinates(model.ExitGpsLatitude, model.ExitGpsLongitude,
					model.Contact.ExitGpsCoordinates, contact.ExitGpsCoordinates);

				if (!String.IsNullOrWhiteSpace(model.PhysicalAddress1))
				{
					var physicalAddress = new Address();

					if (contact.PhysicalAddressId.HasValue)
						physicalAddress = await _addressService.GetAddressByIdAsync(contact.PhysicalAddressId.Value);

					physicalAddress.Address1 = model.PhysicalAddress1;
					physicalAddress.City = model.PhysicalCity;
					physicalAddress.Country = model.PhysicalCountry;
					physicalAddress.PostalCode = model.PhysicalPostalCode;
					physicalAddress.State = model.PhysicalState;

					physicalAddress = await _addressService.SaveAddressAsync(physicalAddress, cancellationToken);
					contact.PhysicalAddressId = physicalAddress.AddressId;

					if (model.MailingAddressSameAsPhysical)
						contact.MailingAddressId = physicalAddress.AddressId;
				}

				if (!String.IsNullOrWhiteSpace(model.MailingAddress1) && !model.MailingAddressSameAsPhysical)
				{
					var mailingAddress = new Address();

					if (contact.MailingAddressId.HasValue)
						mailingAddress = await _addressService.GetAddressByIdAsync(contact.MailingAddressId.Value);

					mailingAddress.Address1 = model.MailingAddress1;
					mailingAddress.City = model.MailingCity;
					mailingAddress.Country = model.MailingCountry;
					mailingAddress.PostalCode = model.MailingPostalCode;
					mailingAddress.State = model.MailingState;

					mailingAddress = await _addressService.SaveAddressAsync(mailingAddress, cancellationToken);
					contact.MailingAddressId = mailingAddress.AddressId;
				}

				// AddedOn/AddedByUserId belong to whoever created the contact; an edit stamps the
				// edit fields instead. They used to be overwritten with the editing user and now,
				// which lost the creator on the first edit.
				contact.DepartmentId = DepartmentId;
				contact.EditedByUserId = UserId;
				contact.EditedOn = DateTime.UtcNow;

				await _contactsService.SaveContactAsync(contact, cancellationToken);

				// Save UDF field values for the updated contact
				var udfDefinitionForEdit = await _userDefinedFieldsService.GetActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Contact);
				var udfValues = Request.Form.Keys
					.Where(k => k.StartsWith("udf_") && !k.EndsWith("_exists"))
					.Select(k => new UdfFieldValue
					{
						UdfFieldId = k.Substring(4),
						UdfDefinitionId = udfDefinitionForEdit?.UdfDefinitionId,
						Value = Request.Form[k]
					}).ToList();

				if (udfValues.Any())
				{
					bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
					bool isGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
					var validationErrors = await _userDefinedFieldsService.SaveFieldValuesForEntityAsync(DepartmentId, (int)UdfEntityType.Contact, model.Contact.ContactId, udfValues, UserId, isDeptAdmin, isGroupAdmin, cancellationToken);

					if (validationErrors.Count > 0)
					{
						foreach (var kvp in validationErrors)
						{
							foreach (var errorMessage in kvp.Value)
								ModelState.AddModelError(kvp.Key, errorMessage);
						}

						return View(model);
					}
				}

				// The saved row, not the posted one - "after" has to describe what was persisted.
				auditEvent.After = contact.CloneJsonToString();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				return RedirectToAction("Index", "Contacts", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_Create)]
		public async Task<IActionResult> Delete(string contactId, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(contactId))
				return BadRequest();

			var contact = await _contactsService.GetContactByIdAsync(contactId);

			if (contact == null)
				return NotFound();

			if (contact.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!(await _authorizationService.CanUserDeleteContactAsync(UserId, DepartmentId)))
				return Unauthorized();

			var result = await _contactsService.DeleteContactAsync(contactId, UserId, DepartmentId, IpAddressHelper.GetRequestIP(Request, true), $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);

			return RedirectToAction("Index", "Contacts", new { Area = "User" });
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Connect_Create)]
		public async Task<IActionResult> AddNote(AddContactNoteView model, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				var contact = await _contactsService.GetContactByIdAsync(model.ContactId);

				if (contact == null)
					return BadRequest();

				if (contact.DepartmentId != DepartmentId)
					return BadRequest();

				var note = new ContactNote();
				note.ContactId = model.ContactId;
				note.Note = model.Note;
				note.DepartmentId = DepartmentId;
				note.AddedByUserId = UserId;
				note.ShouldAlert = model.ShouldAlert;
				note.ContactNoteTypeId = model.ContactNoteTypeId;
				note.ExpiresOn = model.ExpiresOn;
				note.AddedOn = DateTime.UtcNow;

				await _contactsService.SaveContactNoteAsync(note, cancellationToken);

				return Ok();
			}

			return BadRequest();
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_Create)]
		public async Task<IActionResult> Categories()
		{
			var model = new ContactCategoriesView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Categories = await _contactsService.GetContactCategoriesForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_Create)]
		public async Task<IActionResult> AddCategory()
		{
			var model = new AddCategoryView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Contacts_Create)]
		public async Task<IActionResult> AddCategory(AddCategoryView model, CancellationToken cancellationToken)
		{
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (ModelState.IsValid)
			{
				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.ContactCategoryAdded;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

				model.Category.DepartmentId = DepartmentId;
				model.Category.AddedByUserId = UserId;
				model.Category.AddedOn = DateTime.UtcNow;

				await _contactsService.SaveContactCategoryAsync(model.Category, cancellationToken);

				auditEvent.After = model.Category.CloneJsonToString();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				return RedirectToAction("Categories", "Contacts", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<IActionResult> ViewCategory(string categoryId)
		{
			if (String.IsNullOrWhiteSpace(categoryId))
				return Unauthorized();

			var model = new AddCategoryView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Category = await _contactsService.GetContactCategoryByIdAsync(categoryId);

			if (model.Category == null)
				return Unauthorized();

			if (model.Category.DepartmentId != DepartmentId)
				return Unauthorized();

			if (model.Category.Contacts != null && model.Category.Contacts.Any())
				await _protectedReadService.ResolveContactsForReadAsync(DepartmentId, model.Category.Contacts.ToList(), null, UserId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_Create)]
		public async Task<IActionResult> EditCategory(string categoryId)
		{
			if (String.IsNullOrWhiteSpace(categoryId))
				return Unauthorized();

			var model = new AddCategoryView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Category = await _contactsService.GetContactCategoryByIdAsync(categoryId);

			if (model.Category == null)
				return Unauthorized();

			if (model.Category.DepartmentId != DepartmentId)
				return Unauthorized();

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Contacts_Create)]
		public async Task<IActionResult> EditCategory(AddCategoryView model, CancellationToken cancellationToken)
		{
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (ModelState.IsValid)
			{
				var category = await _contactsService.GetContactCategoryByIdAsync(model.Category.ContactCategoryId);

				if (category == null || category.DepartmentId != DepartmentId)
					return Unauthorized();

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.ContactCategoryEdited;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

				auditEvent.Before = category.CloneJsonToString();

				model.Category.DepartmentId = DepartmentId;
				model.Category.EditedByUserId = UserId;
				model.Category.EditedOn = DateTime.UtcNow;

				await _contactsService.SaveContactCategoryAsync(model.Category, cancellationToken);

				auditEvent.After = model.Category.CloneJsonToString();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				return RedirectToAction("Categories", "Contacts", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_Delete)]
		public async Task<IActionResult> DeleteCategory(string categoryId)
		{
			if (String.IsNullOrWhiteSpace(categoryId))
				return Unauthorized();

			var category = await _contactsService.GetContactCategoryByIdAsync(categoryId);

			if (category == null)
				return Unauthorized();

			if (category.DepartmentId != DepartmentId)
				return Unauthorized();

			if (category.Contacts != null && category.Contacts.Any())
				return RedirectToAction("Categories", "Contacts", new { Area = "User" });

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.ContactCategoryRemoved;
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

			auditEvent.Before = category.CloneJsonToString();

			await _contactsService.DeleteContactCategoryAsync(category);

			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			return RedirectToAction("Categories", "Contacts", new { Area = "User" });
		}

		/// <summary>
		/// ADP client-side reveal (plan 7.2): decrypted cataloged fields of one contact for a
		/// caller holding a currently-valid Protected Data Grant, presented via the
		/// X-Resgrid-Protected-Grant header and held in JS memory only.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<IActionResult> RevealContact([FromForm] string contactId)
		{
			if (String.IsNullOrWhiteSpace(contactId))
				return BadRequest();

			var contact = await _contactsService.GetContactByIdAsync(contactId);
			if (contact == null || contact.DepartmentId != DepartmentId)
				return NotFound();

			string grantToken = Request.Headers["X-Resgrid-Protected-Grant"];
			var resolved = await _protectedReadService.ResolveContactsForReadAsync(DepartmentId,
				new List<Contact> { contact }, grantToken, UserId);

			if (resolved.IsProtected && resolved.ProtectedReason != null)
				return Json(new { success = false, error = resolved.ProtectedReason });

			var fields = Resgrid.Services.ProtectedReadService.ContactFieldAccessors
				.ToDictionary(a => a.Key, a => a.Value.Get(contact));

			// Catalog v12: the View page's Pre-Plan tab marks its values with the pre-plan/hazard id as a
			// suffix, so one step-up reveals the contact, its plan and its hazards together.
			var preplanReveal = await AddPreplanRevealFieldsAsync(fields, contactId, grantToken, suffixed: true);
			if (preplanReveal != null && preplanReveal.IsProtected && preplanReveal.ProtectedReason != null)
				return Json(new { success = false, error = preplanReveal.ProtectedReason });

			// The contact's UDF values are cataloged too, and the form renders them as inputs marked
			// for this module. Revealing the contact but leaving its custom fields showing the
			// placeholder would be an odd half-reveal of the same record.
			// The reveal must hide exactly what the hosting page hides: a grant is step-up proof,
			// never a field-visibility decision.
			bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
			bool isGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);

			var resolvedUdf = await ProtectedUdfRevealHelper.AddUdfValuesAsync(fields, _userDefinedFieldsService,
				_protectedReadService, DepartmentId, UdfEntityType.Contact, contactId, grantToken, UserId,
				isDeptAdmin, isGroupAdmin);

			// The UDF resolve is a SEPARATE grant validation, and it is the one that can fail on its
			// own: a record whose own cataloged columns are all empty produces no slots, so its
			// resolve returns without ever checking the grant. If the custom fields are enveloped,
			// this call is where an expired grant actually surfaces - and dropping the reason would
			// answer success with placeholders the client silently declines to write, so the member
			// clicks Reveal and nothing happens. Null when the record has no custom values at all.
			if (resolvedUdf != null && resolvedUdf.IsProtected && resolvedUdf.ProtectedReason != null)
				return Json(new { success = false, error = resolvedUdf.ProtectedReason });

			return Json(new { success = true, fields });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<IActionResult> GetNotesJson(string contactId)
		{
			List<ContactNoteJson> notes = new List<ContactNoteJson>();

			var contact = await _contactsService.GetContactByIdAsync(contactId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (contact == null)
				return Json(notes);

			if (contact.DepartmentId != DepartmentId)
				return Json(notes);

			var contactNotes = await _contactsService.GetContactNotesByContactIdAsync(contactId, DepartmentId);
			var noteTypes = await _contactsService.GetContactNoteTypesByDepartmentIdAsync(DepartmentId);

			if (contactNotes != null && contactNotes.Any())
			{
				foreach (var note in contactNotes)
				{
					var noteJson = new ContactNoteJson();
					noteJson.ContactNoteId = note.ContactNoteId;
					noteJson.Note = ProtectedDataEnvelope.SafeDisplay(note.Note);

					if (note.ExpiresOn.HasValue)
						noteJson.ExpiresOn = note.ExpiresOn.Value.FormatForDepartment(department);

					noteJson.CreatedOn = note.AddedOn.FormatForDepartment(department);
					noteJson.CreatedBy = await UserHelper.GetFullNameForUser(note.AddedByUserId);
					noteJson.ShouldAlert = note.ShouldAlert;

					if (note.ShouldAlert)
						noteJson.BackgroundColor = "#FFCC00";
					else
						noteJson.BackgroundColor = "#FFFFFF";

					noteJson.ContactNoteType = "";

					if (!String.IsNullOrWhiteSpace(note.ContactNoteTypeId))
					{
						var noteType = noteTypes.FirstOrDefault(x => x.ContactNoteTypeId == note.ContactNoteTypeId);

						if (noteType != null)
							noteJson.ContactNoteType = noteType.Name;
					}

					notes.Add(noteJson);
				}
			}

			return Json(notes);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<IActionResult> GetCallsJson(string contactId)
		{
			List<CallJson> callsJson = new List<CallJson>();

			var contact = await _contactsService.GetContactByIdAsync(contactId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (contact == null)
				return Json(callsJson);

			if (contact.DepartmentId != DepartmentId)
				return Json(callsJson);

			var calls = await _callsService.GetCallsByContactIdAsync(contactId, DepartmentId);

			if (calls != null && calls.Any())
			{
				foreach (var call in calls)
				{
					var callJson = new CallJson();
					callJson.CallId = call.CallId;
					callJson.CallNumber = call.Number;
					callJson.CallName = ProtectedDataEnvelope.SafeDisplay(call.Name);
					callJson.CallNature = ProtectedDataEnvelope.SafeDisplay(call.NatureOfCall);
					callJson.LoggedOn = call.LoggedOn.FormatForDepartment(department);
					callJson.Priority = call.Priority;

					var priority = await _callsService.GetCallPrioritiesByIdAsync(call.Priority, DepartmentId);

					if (priority != null)
					{
						callJson.PriorityName = priority.Name;
						callJson.PriorityColor = priority.Color;
					}
					else
					{
						callJson.PriorityName = "Unknown Priority";
						callJson.PriorityColor = "#000000";
					}

					callsJson.Add(callJson);
				}
			}

			return Json(callsJson);
		}
		#region Pre-plans and site attachments (Contacts plan Phase A, A6)

		private static readonly HashSet<string> AllowedAttachmentExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"jpg", "jpeg", "png", "gif", "pdf", "doc", "docx", "ppt", "pptx", "pps", "ppsx", "odt", "xls", "xlsx", "txt", "csv", "dwg", "dxf"
		};

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_Update)]
		public async Task<IActionResult> Preplan(string contactId)
		{
			var model = await BuildPreplanViewAsync(contactId, null);
			if (model == null)
				return Unauthorized();

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Contacts_Update)]
		public async Task<IActionResult> Preplan(ContactPreplanView model, CancellationToken cancellationToken)
		{
			if (model?.Contact == null || string.IsNullOrWhiteSpace(model.Contact.ContactId))
				return BadRequest();

			var contact = await _contactsService.GetContactByIdAsync(model.Contact.ContactId);
			if (contact == null || contact.IsDeleted || contact.DepartmentId != DepartmentId)
				return Unauthorized();

			var posted = model.Preplan ?? new ContactPreplan();

			if (!Enum.IsDefined(typeof(ContactPreplanConstructionTypes), posted.ConstructionType) ||
				!Enum.IsDefined(typeof(ContactPreplanRoofTypes), posted.RoofType) ||
				!Enum.IsDefined(typeof(ContactPreplanOccupancyTypes), posted.OccupancyType))
				ModelState.AddModelError("Preplan", "Unknown construction, roof or occupancy type.");

			if (!ModelState.IsValid)
			{
				var rebuilt = await BuildPreplanViewAsync(contact.ContactId, posted);
				rebuilt.NextReviewDue = model.NextReviewDue;
				rebuilt.MarkReviewed = model.MarkReviewed;
				return View(rebuilt);
			}

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var existing = await _contactsService.GetPreplanByContactIdAsync(contact.ContactId, DepartmentId);

			posted.ContactId = contact.ContactId;
			posted.DepartmentId = DepartmentId;
			posted.NextReviewDue = model.NextReviewDue.HasValue
				? DateTimeHelpers.ConvertToUtc(model.NextReviewDue.Value, department.TimeZone)
				: (DateTime?)null;
			posted.LastReviewedOn = existing?.LastReviewedOn;
			posted.ReviewedByUserId = existing?.ReviewedByUserId;

			if (model.MarkReviewed)
			{
				posted.LastReviewedOn = DateTime.UtcNow;
				posted.ReviewedByUserId = UserId;
			}

			try
			{
				await _contactsService.SavePreplanAsync(posted, UserId, IpAddressHelper.GetRequestIP(Request, true),
					$"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);
			}
			catch (InvalidOperationException ex) when (ex.Message == IContactPreplanOwnershipGate.RecordsOwnedReason)
			{
				var rebuilt = await BuildPreplanViewAsync(contact.ContactId, null);
				rebuilt.Message = _localizer["PreplanRecordsOwnedNotice"].Value;
				return View(rebuilt);
			}

			return RedirectToAction("View", "Contacts", new { Area = "User", contactId = contact.ContactId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Contacts_Delete)]
		public async Task<IActionResult> DeletePreplan(string contactId, CancellationToken cancellationToken)
		{
			var contact = await _contactsService.GetContactByIdAsync(contactId);
			if (contact == null || contact.DepartmentId != DepartmentId)
				return Unauthorized();

			try
			{
				await _contactsService.DeletePreplanAsync(contactId, DepartmentId, UserId, IpAddressHelper.GetRequestIP(Request, true),
					$"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);
			}
			catch (InvalidOperationException ex) when (ex.Message == IContactPreplanOwnershipGate.RecordsOwnedReason)
			{
				return RedirectToAction("Preplan", "Contacts", new { Area = "User", contactId });
			}

			return RedirectToAction("View", "Contacts", new { Area = "User", contactId = contactId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<IActionResult> GetHazardsJson(string contactId)
		{
			var result = new List<ContactHazardJson>();

			var contact = await _contactsService.GetContactByIdAsync(contactId);
			if (contact == null || contact.DepartmentId != DepartmentId)
				return Json(result);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var hazards = await _contactsService.GetHazardsByContactIdAsync(contactId, DepartmentId);

			// ADP: a server-rendered list holds no grant; the editor page reveals through RevealContactPreplan.
			await _protectedReadService.ResolveContactPreplanHazardsForReadAsync(DepartmentId, hazards, null, UserId);

			foreach (var hazard in hazards)
				result.Add(await ToHazardJsonAsync(hazard, department));

			return Json(result);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Contacts_Update)]
		public async Task<IActionResult> SaveHazard([FromBody] SaveContactHazardInput input, CancellationToken cancellationToken)
		{
			if (input == null || !ModelState.IsValid)
				return Json(new { success = false, error = "A title is required." });

			var contact = await _contactsService.GetContactByIdAsync(input.ContactId);
			if (contact == null || contact.IsDeleted || contact.DepartmentId != DepartmentId)
				return Json(new { success = false, error = "Contact not found." });

			if (!Enum.IsDefined(typeof(ContactPreplanHazardTypes), input.HazardType) ||
				!Enum.IsDefined(typeof(ContactPreplanHazardSeverities), input.Severity))
				return Json(new { success = false, error = "Unknown hazard type or severity." });

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

			try
			{
				var saved = await _contactsService.SaveHazardAsync(hazard, UserId, IpAddressHelper.GetRequestIP(Request, true),
					$"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);

				return Json(new { success = true, id = saved.ContactPreplanHazardId });
			}
			catch (InvalidOperationException ex) when (ex.Message == IContactPreplanOwnershipGate.RecordsOwnedReason)
			{
				return Json(new { success = false, error = _localizer["PreplanRecordsOwnedNotice"].Value });
			}
			catch (InvalidOperationException)
			{
				return Json(new { success = false, error = "Hazard not found." });
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Contacts_Update)]
		public async Task<IActionResult> DeleteHazard([FromForm] string contactPreplanHazardId, CancellationToken cancellationToken)
		{
			try
			{
				var deleted = await _contactsService.DeleteHazardAsync(contactPreplanHazardId, DepartmentId, UserId, IpAddressHelper.GetRequestIP(Request, true),
					$"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);

				return Json(new { success = deleted });
			}
			catch (InvalidOperationException ex) when (ex.Message == IContactPreplanOwnershipGate.RecordsOwnedReason)
			{
				return Json(new { success = false, error = _localizer["PreplanRecordsOwnedNotice"].Value });
			}
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<IActionResult> Attachments(string contactId)
		{
			var model = await BuildAttachmentsViewAsync(contactId);
			if (model == null)
				return Unauthorized();

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Contacts_Update)]
		public async Task<IActionResult> UploadAttachment(string contactId, int attachmentType, string name, IFormFile file, CancellationToken cancellationToken)
		{
			var contact = await _contactsService.GetContactByIdAsync(contactId);
			if (contact == null || contact.IsDeleted || contact.DepartmentId != DepartmentId)
				return Unauthorized();

			string error = null;
			if (file == null || file.Length == 0)
				error = "Choose a file to upload.";
			else if (!Enum.IsDefined(typeof(ContactAttachmentTypes), attachmentType))
				error = "Unknown file type.";
			else if (!AllowedAttachmentExtensions.Contains(FileHelper.GetFileExtensionWithoutDot(file.FileName) ?? string.Empty))
				error = $"File type ({FileHelper.GetFileExtensionWithoutDot(file.FileName)}) is not importable.";
			else if (file.Length > ContactAttachment.MaxSizeBytes)
				error = "Attachment is too large, must be smaller than 30MB.";

			if (error != null)
			{
				var model = await BuildAttachmentsViewAsync(contactId);
				model.Message = error;
				return View("Attachments", model);
			}

			byte[] data;
			using (var stream = new MemoryStream())
			{
				await file.CopyToAsync(stream, cancellationToken);
				data = stream.ToArray();
			}

			var attachment = new ContactAttachment
			{
				ContactId = contact.ContactId,
				DepartmentId = DepartmentId,
				ContactAttachmentType = attachmentType,
				Name = string.IsNullOrWhiteSpace(name) ? file.FileName : name.Trim(),
				FileName = Path.GetFileName(file.FileName),
				FileType = file.ContentType,
				Data = data
			};

			await _contactsService.SaveContactAttachmentAsync(attachment, UserId, IpAddressHelper.GetRequestIP(Request, true),
				$"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);

			return RedirectToAction("Attachments", "Contacts", new { Area = "User", contactId = contact.ContactId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Contacts_Delete)]
		public async Task<IActionResult> DeleteAttachment(string contactId, int contactAttachmentId, CancellationToken cancellationToken)
		{
			await _contactsService.DeleteContactAttachmentAsync(contactAttachmentId, DepartmentId, UserId, IpAddressHelper.GetRequestIP(Request, true),
				$"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);

			return RedirectToAction("Attachments", "Contacts", new { Area = "User", contactId = contactId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<IActionResult> GetContactAttachment(int contactAttachmentId)
		{
			var attachment = await _contactsService.GetContactAttachmentByIdAsync(contactAttachmentId);

			if (attachment == null || attachment.DepartmentId != DepartmentId || attachment.Data == null)
				return NotFound();

			// ADP: an enveloped payload/name is ciphertext — the MVC surface has no per-download grant
			// flow, so a protected file is simply not served here (same rule as call files).
			if (attachment.IsProtected || Resgrid.Services.ProtectedReadService.IsBinaryEnveloped(attachment.Data) ||
				ProtectedDataEnvelope.HasEnvelopePrefix(attachment.FileName))
				return NotFound();

			var contentType = string.IsNullOrWhiteSpace(attachment.FileType)
				? FileHelper.GetContentTypeByExtension(Path.GetExtension(attachment.FileName ?? string.Empty))
				: attachment.FileType;
			if (string.IsNullOrWhiteSpace(contentType))
				contentType = "application/octet-stream";

			return new FileContentResult(attachment.Data, contentType)
			{
				FileDownloadName = attachment.FileName
			};
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<IActionResult> GetAttachmentsJson(string contactId)
		{
			var result = new List<ContactAttachmentJson>();

			var contact = await _contactsService.GetContactByIdAsync(contactId);
			if (contact == null || contact.DepartmentId != DepartmentId)
				return Json(result);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var attachments = await _contactsService.GetContactAttachmentsAsync(contactId, DepartmentId);
			await _protectedReadService.ResolveContactAttachmentsForReadAsync(DepartmentId, attachments, null, UserId);

			foreach (var attachment in attachments)
				result.Add(await ToAttachmentJsonAsync(attachment, department));

			return Json(result);
		}

		private async Task<ContactPreplanView> BuildPreplanViewAsync(string contactId, ContactPreplan posted)
		{
			if (string.IsNullOrWhiteSpace(contactId))
				return null;

			var contact = await _contactsService.GetContactByIdAsync(contactId);
			if (contact == null || contact.IsDeleted || contact.DepartmentId != DepartmentId)
				return null;

			var model = new ContactPreplanView();
			model.Contact = contact;
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Preplan = posted ?? await _contactsService.GetPreplanByContactIdAsync(contactId, DepartmentId) ?? new ContactPreplan();
			model.Hazards = await _contactsService.GetHazardsByContactIdAsync(contactId, DepartmentId);

			// RMS-5 write cutover: once the department's pre-plans are owned by Records occupancies this page is
			// read-only and points at the occupancy; the service refuses writes regardless (Contacts plan Phase A note).
			model.IsRecordsOwned = await _preplanOwnership.IsRecordsOwnedAsync(DepartmentId);
			if (model.IsRecordsOwned)
				model.OccupancyId = await _preplanOwnership.GetOccupancyIdForContactAsync(DepartmentId, contactId);

			// ADP catalog v12: the editor renders REDACTED for enveloped values; RevealContactPreplan fills them
			// in after the step-up, and a value that stays REDACTED round-trips to the write net, which
			// restores it from the stored row instead of persisting the placeholder.
			var preplanRead = posted == null && !string.IsNullOrWhiteSpace(model.Preplan.ContactPreplanId)
				? await _protectedReadService.ResolveContactPreplansForReadAsync(DepartmentId, new List<ContactPreplan> { model.Preplan }, null, UserId)
				: new ProtectedReadResult();
			await _protectedReadService.ResolveContactPreplanHazardsForReadAsync(DepartmentId, model.Hazards, null, UserId);

			if (posted == null && model.Preplan.NextReviewDue.HasValue)
				model.NextReviewDue = model.Preplan.NextReviewDue.Value.TimeConverter(model.Department);

			model.ConstructionTypes = EnumSelectList<ContactPreplanConstructionTypes>(model.Preplan.ConstructionType);
			model.RoofTypes = EnumSelectList<ContactPreplanRoofTypes>(model.Preplan.RoofType);
			model.OccupancyTypes = EnumSelectList<ContactPreplanOccupancyTypes>(model.Preplan.OccupancyType);
			model.HazardTypes = EnumSelectList<ContactPreplanHazardTypes>(0);
			model.HazardSeverities = EnumSelectList<ContactPreplanHazardSeverities>(0);

			var protectedRead = await _protectedReadService.ResolveContactsForReadAsync(DepartmentId, new List<Contact> { contact }, null, UserId);
			model.IsProtectedContact = protectedRead.IsProtected || preplanRead.IsProtected;

			return model;
		}

		/// <summary>
		/// Reveal endpoint for the pre-plan editor (ADP plan 7.2, catalog v12): decrypted pre-plan values keyed by
		/// catalog field id, plus every hazard's text keyed "&lt;fieldId&gt;:&lt;hazardId&gt;" for the grid. Authorizes
		/// the contact on top of validating the grant.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Contacts_Update)]
		public async Task<IActionResult> RevealContactPreplan([FromForm] string contactId)
		{
			if (String.IsNullOrWhiteSpace(contactId))
				return BadRequest();

			var contact = await _contactsService.GetContactByIdAsync(contactId);
			if (contact == null || contact.DepartmentId != DepartmentId)
				return NotFound();

			string grantToken = Request.Headers["X-Resgrid-Protected-Grant"];
			var fields = new Dictionary<string, string>();
			var resolved = await AddPreplanRevealFieldsAsync(fields, contactId, grantToken, suffixed: false);

			if (resolved != null && resolved.IsProtected && resolved.ProtectedReason != null)
				return Json(new { success = false, error = resolved.ProtectedReason });

			return Json(new { success = true, fields });
		}

		/// <summary>
		/// Adds the contact's pre-plan and hazard values to a reveal payload. Plain field ids for the editor
		/// (its inputs are the only ones on the page); ":&lt;rowId&gt;"-suffixed ids for read-only views, where several
		/// pre-plans can share a page (the call Site Info tab). Hazards are always suffixed: there are many of them.
		/// Returns the first resolve that reported a reason, or null when the contact has no pre-plan.
		/// </summary>
		private async Task<ProtectedReadResult> AddPreplanRevealFieldsAsync(Dictionary<string, string> fields, string contactId, string grantToken, bool suffixed)
		{
			var preplan = await _contactsService.GetPreplanByContactIdAsync(contactId, DepartmentId);
			if (preplan == null)
				return null;

			var preplanRead = await _protectedReadService.ResolveContactPreplansForReadAsync(DepartmentId, new List<ContactPreplan> { preplan }, grantToken, UserId);
			var hazardRead = await _protectedReadService.ResolveContactPreplanHazardsForReadAsync(DepartmentId, preplan.Hazards, grantToken, UserId);

			AddPreplanFields(fields, preplan, suffixed);

			if (preplanRead.IsProtected && preplanRead.ProtectedReason != null)
				return preplanRead;
			if (hazardRead.IsProtected && hazardRead.ProtectedReason != null)
				return hazardRead;
			return preplanRead;
		}

		/// <summary>Writes one resolved pre-plan (and its hazards) into a reveal payload; shared with the call page.</summary>
		public static void AddPreplanFields(Dictionary<string, string> fields, ContactPreplan preplan, bool suffixed)
		{
			if (preplan == null)
				return;

			foreach (var accessor in Resgrid.Services.ProtectedReadService.ContactPreplanFieldAccessors)
				fields[suffixed ? $"{accessor.Key}:{preplan.ContactPreplanId}" : accessor.Key] = accessor.Value.Get(preplan);

			foreach (var hazard in preplan.Hazards ?? new List<ContactPreplanHazard>())
			{
				foreach (var accessor in Resgrid.Services.ProtectedReadService.ContactPreplanHazardFieldAccessors)
					fields[$"{accessor.Key}:{hazard.ContactPreplanHazardId}"] = accessor.Value.Get(hazard);
			}
		}

		private async Task<ContactAttachmentsView> BuildAttachmentsViewAsync(string contactId)
		{
			if (string.IsNullOrWhiteSpace(contactId))
				return null;

			var contact = await _contactsService.GetContactByIdAsync(contactId);
			if (contact == null || contact.IsDeleted || contact.DepartmentId != DepartmentId)
				return null;

			var model = new ContactAttachmentsView();
			model.Contact = contact;
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Attachments = await _contactsService.GetContactAttachmentsAsync(contactId, DepartmentId);
			await _protectedReadService.ResolveContactAttachmentsForReadAsync(DepartmentId, model.Attachments, null, UserId);
			model.AttachmentTypes = EnumSelectList<ContactAttachmentTypes>(0);

			var protectedRead = await _protectedReadService.ResolveContactsForReadAsync(DepartmentId, new List<Contact> { contact }, null, UserId);
			model.IsProtectedContact = protectedRead.IsProtected;

			return model;
		}

		private static SelectList EnumSelectList<TEnum>(int selected) where TEnum : struct, Enum
		{
			var items = Enum.GetValues(typeof(TEnum)).Cast<TEnum>()
				.Select(v => new SelectListItem { Value = Convert.ToInt32(v).ToString(), Text = SplitPascalCase(v.ToString()) })
				.ToList();

			return new SelectList(items, "Value", "Text", selected.ToString());
		}

		/// <summary>"TypeIFireResistive" → "Type I Fire Resistive"; keeps roman numerals and acronyms readable without a resource per enum member.</summary>
		public static string SplitPascalCase(string value)
		{
			if (string.IsNullOrEmpty(value))
				return value;

			var builder = new System.Text.StringBuilder(value.Length + 8);
			for (int i = 0; i < value.Length; i++)
			{
				var c = value[i];
				if (i > 0 && char.IsUpper(c))
				{
					var previous = value[i - 1];
					var next = i + 1 < value.Length ? value[i + 1] : '\0';
					if (char.IsLower(previous) || (char.IsUpper(previous) && char.IsLower(next)))
						builder.Append(' ');
				}
				builder.Append(c);
			}

			return builder.ToString();
		}

		private async Task<ContactHazardJson> ToHazardJsonAsync(ContactPreplanHazard hazard, Department department)
		{
			var json = new ContactHazardJson();
			json.ContactPreplanHazardId = hazard.ContactPreplanHazardId;
			json.ContactId = hazard.ContactId;
			json.HazardType = hazard.HazardType;
			json.HazardTypeName = Enum.IsDefined(typeof(ContactPreplanHazardTypes), hazard.HazardType) ? SplitPascalCase(((ContactPreplanHazardTypes)hazard.HazardType).ToString()) : hazard.HazardType.ToString();
			json.Severity = hazard.Severity;
			json.SeverityName = Enum.IsDefined(typeof(ContactPreplanHazardSeverities), hazard.Severity) ? ((ContactPreplanHazardSeverities)hazard.Severity).ToString() : hazard.Severity.ToString();
			json.SeverityColor = HazardSeverityColor(hazard.Severity);
			json.Title = ProtectedDataEnvelope.SafeDisplay(hazard.Title);
			json.Description = ProtectedDataEnvelope.SafeDisplay(hazard.Description);
			json.LocationDescription = ProtectedDataEnvelope.SafeDisplay(hazard.LocationDescription);
			json.GpsCoordinates = ProtectedDataEnvelope.SafeDisplay(hazard.GpsCoordinates);
			json.ShouldAlert = hazard.ShouldAlert;
			json.AddedOn = hazard.AddedOn.FormatForDepartment(department);
			json.AddedBy = string.IsNullOrWhiteSpace(hazard.AddedByUserId) ? "" : await UserHelper.GetFullNameForUser(hazard.AddedByUserId);

			return json;
		}

		/// <summary>Danger = red, Caution = amber, Info = blue; shared with the dispatch alert banner.</summary>
		public static string HazardSeverityColor(int severity)
		{
			switch (severity)
			{
				case (int)ContactPreplanHazardSeverities.Danger:
					return "#F8D7DA";
				case (int)ContactPreplanHazardSeverities.Caution:
					return "#FFF3CD";
				default:
					return "#D1ECF1";
			}
		}

		private async Task<ContactAttachmentJson> ToAttachmentJsonAsync(ContactAttachment attachment, Department department)
		{
			var json = new ContactAttachmentJson();
			json.ContactAttachmentId = attachment.ContactAttachmentId;
			json.ContactId = attachment.ContactId;
			json.Type = attachment.ContactAttachmentType;
			json.TypeName = Enum.IsDefined(typeof(ContactAttachmentTypes), attachment.ContactAttachmentType) ? SplitPascalCase(((ContactAttachmentTypes)attachment.ContactAttachmentType).ToString()) : attachment.ContactAttachmentType.ToString();
			json.Name = ProtectedDataEnvelope.SafeDisplay(attachment.Name);
			json.FileName = ProtectedDataEnvelope.SafeDisplay(attachment.FileName);
			json.Mime = attachment.FileType;
			json.Size = attachment.Size;
			json.AddedOn = attachment.AddedOn.FormatForDepartment(department);
			json.AddedBy = string.IsNullOrWhiteSpace(attachment.AddedByUserId) ? "" : await UserHelper.GetFullNameForUser(attachment.AddedByUserId);
			json.Url = Url.Action("GetContactAttachment", "Contacts", new { Area = "User", contactAttachmentId = attachment.ContactAttachmentId });

			return json;
		}

		#endregion
	}
}
