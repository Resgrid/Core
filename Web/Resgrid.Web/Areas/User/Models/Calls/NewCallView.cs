using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Calls
{
	public class NewCallView : BaseUserModel
	{
		public string UdfFormHtml { get; set; }
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public Call Call { get; set; }
		public string Message { get; set; }
		public List<DepartmentGroup> Groups { get; set; }
		public List<IdentityUser> UnGroupedUsers { get; set; }
		public int CallPriority { get; set; }
		public SelectList CallPriorities { get; set; }
		public SelectList CallTypes { get; set; }
		public string Latitude { get; set; }
		public string Longitude { get; set; }
		public Coordinates CenterCoordinates { get; set; }
		public W3W What3Words { get; set; }
		public string What3Word { get; set; }
		public string MapCenterLatitude { get; set; }
		public string MapCenterLongitude { get; set; }
		public bool ReCalcuateCallNumbers { get; set; }
		public List<Unit> Units { get; set; }
		public List<UnitState> UnitStates { get; set; }
		public List<CustomState> UnitStatuses { get; set; }
		public SelectList CallTemplates { get; set; }
		public int CallTemplateId { get; set; }
		public string NewCallFormData { get; set; }
		public string ClosedCallNotes { get; set; }
		public ClosedOnlyCallStates CallState { get; set; }
		public SelectList CallStates { get; set; }
		public List<Contact> Contacts { get; set; }
		public SelectList ContactsList { get; set; }
		public string PrimaryContact { get; set; }
		public List<string> AdditionalContacts { get; set; }
		public List<SelectListItem> DestinationPois { get; set; }
		[System.ComponentModel.DataAnnotations.DisplayFormat(DataFormatString = "{0:MM/dd/yyyy HH:mm}", ApplyFormatInEditMode = true)]
		public DateTime? ScheduleDispatchDate { get; set; }

		public NewCallView()
		{
			What3Words = new W3W();
			AdditionalContacts = new List<string>();
			DestinationPois = new List<SelectListItem>();
		}

		/// <summary>
		/// The pin the form posted, in the call's "lat,lon" form; null when none was placed. The form posts the pin as
		/// <see cref="Latitude"/>/<see cref="Longitude"/>, never as Call.GeoLocationData, so this is what the new-call field
		/// policy checks and what the call stores.
		/// </summary>
		public string PostedGeoLocation() =>
			!string.IsNullOrWhiteSpace(Latitude) && !string.IsNullOrWhiteSpace(Longitude) ? string.Format("{0},{1}", Latitude.Trim(), Longitude.Trim()) : null;

		/// <summary>
		/// True when a pin was typed or placed but is not a real coordinate: either half missing, not a decimal number, or out
		/// of range (latitude -90 to 90, longitude -180 to 180).
		/// </summary>
		public bool HasInvalidPin() =>
			(!string.IsNullOrWhiteSpace(Latitude) || !string.IsNullOrWhiteSpace(Longitude)) &&
			(!LocationHelpers.IsValidLatitude(Latitude) || !LocationHelpers.IsValidLongitude(Longitude));
	}
}