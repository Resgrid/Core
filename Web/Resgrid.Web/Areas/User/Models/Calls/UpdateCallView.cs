using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Calls
{
	public class UpdateCallView: BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public Call Call { get; set; }
		public string Message { get; set; }
		public List<DepartmentGroup> Groups { get; set; }
		public List<IdentityUser> UnGroupedUsers { get; set; }
		public int CallPriority { get; set; }
		public SelectList CallPriorities { get; set; }
		public string Latitude { get; set; }
		public string Longitude { get; set; }
		public SelectList CallTypes { get; set; }
		public Coordinates CenterCoordinates { get; set; }
		public W3W What3Words { get; set; }
		public string What3Word { get; set; }
		public string MapCenterLatitude { get; set; }
		public string MapCenterLongitude { get; set; }
		public bool RebroadcastCall { get; set; }
		public List<Unit> Units { get; set; }
		public List<UnitState> UnitStates { get; set; }
		public List<Contact> Contacts { get; set; }
		public SelectList ContactsList { get; set; }
		public string PrimaryContact { get; set; }
		public List<string> AdditionalContacts { get; set; }
	}
}
