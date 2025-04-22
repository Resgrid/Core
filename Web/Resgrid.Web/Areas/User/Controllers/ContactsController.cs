using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.Home;
using Resgrid.Web.Helpers;
using RestSharp;
using Resgrid.Framework;
using Resgrid.Model.Events;
using Resgrid.Providers.Bus;
using Resgrid.Web.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model.Helpers;
using Resgrid.Web.Areas.User.Models.BigBoardX;
using Resgrid.Model.Identity;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using Microsoft.Extensions.Localization;
using System.Reflection;
using Resgrid.Localization;
using Microsoft.AspNetCore.Localization;
using System.Web;
using Google.Api.Gax.Rest;
using Resgrid.Web.Areas.User.Models.Contacts;
using Resgrid.WebCore.Areas.User.Models;
using Resgrid.WebCore.Areas.User.Models.Contacts;
using SharpKml.Dom.Atom;
using SharpKml.Dom;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using Resgrid.WebCore.Areas.User.Models.Voice;

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

		public ContactsController(IContactsService contactsService, IDepartmentsService departmentsService, IUserProfileService userProfileService,
			IAddressService addressService, IEventAggregator eventAggregator, ICallsService callsService, IAuthorizationService authorizationService)
		{
			_contactsService = contactsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_addressService = addressService;
			_eventAggregator = eventAggregator;
			_callsService = callsService;
			_authorizationService = authorizationService;
		}

		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.Contacts_View)]
		public async Task<IActionResult> Index()
		{
			var model = new ContactsIndexView();
			model.ContactCategories = await _contactsService.GetContactCategoriesForDepartmentAsync(DepartmentId);
			model.Contacts = await _contactsService.GetAllContactsForDepartmentAsync(DepartmentId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

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
				Unauthorized();

			var model = new ViewContactView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Contact = await _contactsService.GetContactByIdAsync(contactId);

			var noteTypes = new List<ContactNoteType>();
			noteTypes.Add(new ContactNoteType { ContactNoteTypeId = "", Name = "No Type" });
			noteTypes.AddRange(await _contactsService.GetContactNoteTypesByDepartmentIdAsync(DepartmentId));
			model.NoteTypes = noteTypes;

			if (model.Contact == null)
				Unauthorized();

			if (model.Contact.DepartmentId != DepartmentId)
				Unauthorized();

			// Load physical address if it exists
			if (model.Contact.PhysicalAddressId.HasValue)
				model.PhysicalAddress = await _addressService.GetAddressByIdAsync(model.Contact.PhysicalAddressId.Value);

			// Load mailing address if it exists and is different from physical
			if (model.Contact.MailingAddressId.HasValue &&
				(!model.Contact.PhysicalAddressId.HasValue || model.Contact.MailingAddressId != model.Contact.PhysicalAddressId))
				model.MailingAddress = await _addressService.GetAddressByIdAsync(model.Contact.MailingAddressId.Value);

			model.Notes = await _contactsService.GetContactNotesByContactIdAsync(contactId, DepartmentId);

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

				if (!String.IsNullOrWhiteSpace(model.LocationGpsLatitude) && !String.IsNullOrWhiteSpace(model.LocationGpsLongitude))
				{
					model.Contact.EntranceGpsCoordinates = $"{model.LocationGpsLatitude},{model.LocationGpsLongitude}";
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
				Unauthorized();

			if (!String.IsNullOrWhiteSpace(model.Contact.EntranceGpsCoordinates))
			{
				var entranceGpsCoordinates = model.Contact.EntranceGpsCoordinates.Split(',');
				model.LocationGpsLatitude = entranceGpsCoordinates[0];
				model.LocationGpsLongitude = entranceGpsCoordinates[1];
			}

			if (!String.IsNullOrWhiteSpace(model.Contact.LocationGpsCoordinates))
			{
				var locationGpsCoordinates = model.Contact.LocationGpsCoordinates.Split(',');
				model.LocationGpsLatitude = locationGpsCoordinates[0];
				model.LocationGpsLongitude = locationGpsCoordinates[1];
			}

			if (!String.IsNullOrWhiteSpace(model.Contact.ExitGpsCoordinates))
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

				if (!String.IsNullOrWhiteSpace(model.LocationGpsLatitude) && !String.IsNullOrWhiteSpace(model.LocationGpsLongitude))
				{
					contact.EntranceGpsCoordinates = $"{model.LocationGpsLatitude},{model.LocationGpsLongitude}";
				}
				else
				{
					contact.EntranceGpsCoordinates = null;
				}

				if (!String.IsNullOrWhiteSpace(model.EntranceGpsLatitude) && !String.IsNullOrWhiteSpace(model.EntranceGpsLongitude))
				{
					contact.EntranceGpsCoordinates = $"{model.EntranceGpsLatitude},{model.EntranceGpsLongitude}";
				}
				else
				{
					contact.EntranceGpsCoordinates = null;
				}

				if (!String.IsNullOrWhiteSpace(model.ExitGpsLatitude) && !String.IsNullOrWhiteSpace(model.ExitGpsLongitude))
				{
					contact.ExitGpsCoordinates = $"{model.ExitGpsLatitude},{model.ExitGpsLongitude}";
				}
				else
				{
					contact.ExitGpsCoordinates = null;
				}

				if (!String.IsNullOrWhiteSpace(model.PhysicalAddress1))
				{
					var physicalAddress = new Address();

					if (contact.PhysicalAddressId.HasValue)
						physicalAddress = await _addressService.GetAddressByIdAsync(model.Contact.PhysicalAddressId.Value);

					physicalAddress.Address1 = model.PhysicalAddress1;
					physicalAddress.City = model.PhysicalCity;
					physicalAddress.Country = model.PhysicalCountry;
					physicalAddress.PostalCode = model.PhysicalPostalCode;
					physicalAddress.State = model.PhysicalState;

					physicalAddress = await _addressService.SaveAddressAsync(physicalAddress, cancellationToken);
					contact.PhysicalAddressId = physicalAddress.AddressId;

					if (model.MailingAddressSameAsPhysical)
						model.Contact.MailingAddressId = physicalAddress.AddressId;
				}

				if (!String.IsNullOrWhiteSpace(model.MailingAddress1) && !model.MailingAddressSameAsPhysical)
				{
					var mailingAddress = new Address();

					if (contact.MailingAddressId.HasValue)
						mailingAddress = await _addressService.GetAddressByIdAsync(model.Contact.MailingAddressId.Value);

					mailingAddress.Address1 = model.PhysicalAddress1;
					mailingAddress.City = model.PhysicalCity;
					mailingAddress.Country = model.PhysicalCountry;
					mailingAddress.PostalCode = model.PhysicalPostalCode;
					mailingAddress.State = model.PhysicalState;

					mailingAddress = await _addressService.SaveAddressAsync(mailingAddress, cancellationToken);
					contact.MailingAddressId = mailingAddress.AddressId;
				}

				model.Contact.DepartmentId = DepartmentId;
				model.Contact.AddedByUserId = UserId;
				model.Contact.AddedOn = DateTime.UtcNow;

				await _contactsService.SaveContactAsync(model.Contact, cancellationToken);

				auditEvent.After = model.Contact.CloneJsonToString();
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
				Unauthorized();

			if (!(await _authorizationService.CanUserDeleteContactAsync(UserId, DepartmentId)))
				Unauthorized();

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
				Unauthorized();

			var model = new AddCategoryView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Category = await _contactsService.GetContactCategoryByIdAsync(categoryId);

			if (model.Category == null)
				Unauthorized();

			if (model.Category.DepartmentId != DepartmentId)
				Unauthorized();

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Contacts_Create)]
		public async Task<IActionResult> EditCategory(string categoryId)
		{
			if (String.IsNullOrWhiteSpace(categoryId))
				Unauthorized();

			var model = new AddCategoryView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Category = await _contactsService.GetContactCategoryByIdAsync(categoryId);

			if (model.Category == null)
				Unauthorized();

			if (model.Category.DepartmentId != DepartmentId)
				Unauthorized();

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
					Unauthorized();

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
				Unauthorized();

			var category = await _contactsService.GetContactCategoryByIdAsync(categoryId);

			if (category == null)
				Unauthorized();

			if (category.DepartmentId != DepartmentId)
				Unauthorized();

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
					noteJson.Note = note.Note;

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
					callJson.CallName = call.Name;
					callJson.CallNature = call.NatureOfCall;
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
	}
}
