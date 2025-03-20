using System;
using Resgrid.Model;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Connect
{
	public class DepartmentProfile
	{
		public int ProfileId { get; set; }
		public int Did { get; set; }
		public string Name { get; set; }
		public string ShortName { get; set; }
		public string Description { get; set; }
		public string InCaseOfEmergency { get; set; }
		public string ServiceArea { get; set; }
		public string ServicesProvided { get; set; }
		public DateTime? Founded { get; set; }
		public byte[] Logo { get; set; }
		public string Keywords { get; set; }
		public bool InviteOnly { get; set; }
		public bool AllowMessages { get; set; }
		public bool VolunteerPositionsAvailable { get; set; }
		public bool ShareStats { get; set; }
		public string VolunteerKeywords { get; set; }
		public string VolunteerDescription { get; set; }
		public string VolunteerContactName { get; set; }
		public string VolunteerContactInfo { get; set; }
		public string Geofence { get; set; }
		public Address Address { get; set; }
		public string Latitude { get; set; }
		public string Longitude { get; set; }
		public string What3Words { get; set; }
		public string Facebook { get; set; }
		public string Twitter { get; set; }
		public string GooglePlus { get; set; }
		public string LinkedIn { get; set; }
		public string Instagram { get; set; }
		public string YouTube { get; set; }
		public string Website { get; set; }
		public string PhoneNumber { get; set; }
		public bool Following { get; set; }
	}
}