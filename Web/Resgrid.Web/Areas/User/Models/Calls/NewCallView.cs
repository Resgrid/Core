using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Calls
{
	public class NewCallView : BaseUserModel, ICallLocationsForm
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
		/// <summary>The units the form may dispatch; the post accepts only these. The page lists them from Units/GetUnitsForCallGrid.</summary>
		public List<Unit> Units { get; set; }
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

		/// <summary>Save the call to Pending Calls without notifying anyone; a dispatcher sends it later.</summary>
		public bool SaveAsPending { get; set; }

		/// <summary>The department's POIs the call location can be picked from (M0322); empty hides the picker.</summary>
		public List<CallLocationPoiOption> LocationPois { get; set; } = new List<CallLocationPoiOption>();

		/// <summary>The route plans the call can follow (M0322); empty hides the route picker.</summary>
		public List<CallRoutePlanOption> RoutePlans { get; set; } = new List<CallRoutePlanOption>();

		/// <summary>Destination POI points by id, for the destination marker on the call map.</summary>
		public Dictionary<string, double[]> DestinationPoiPoints { get; set; } = new Dictionary<string, double[]>();

		/// <summary>
		/// The free destination point (M0322) as typed, kept as text and parsed invariant (see <see cref="CallLocationsForm.PostedPoint"/>):
		/// the form never posts Call.DestinationGeoLocationData.
		/// </summary>
		public string DestinationLatitude { get; set; }

		public string DestinationLongitude { get; set; }

		/// <summary>A plus code to find the destination point with; never stored.</summary>
		public string DestinationPlusCode { get; set; }

		/// <summary>The member may open Routes (the route preview link).</summary>
		public bool CanViewRoutes { get; set; }

		/// <summary>
		/// The department's new-call field policy: which built-in fields the form renders and which it marks required.
		/// Never null; an empty policy is stock behaviour (everything shown, nothing required).
		/// </summary>
		public NewCallFieldPolicy FieldPolicy { get; set; }

		public NewCallView()
		{
			What3Words = new W3W();
			AdditionalContacts = new List<string>();
			DestinationPois = new List<SelectListItem>();
			FieldPolicy = new NewCallFieldPolicy();
		}

		/// <summary>
		/// "All Call": dispatch everyone in the department. Offered only when the department hides every recipient list;
		/// left off, the call pages nobody (an informational call).
		/// </summary>
		public bool AllCall { get; set; }

		public bool IsFieldVisible(string key) => FieldPolicy == null || FieldPolicy.IsVisible(key);

		public bool IsFieldRequired(string key) => FieldPolicy != null && FieldPolicy.IsRequired(key);

		/// <summary>Every recipient list is hidden, so the form offers the optional All Call instead.</summary>
		public bool OffersAllCall => FieldPolicy != null && FieldPolicy.AreAllDispatchTargetsHidden;

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