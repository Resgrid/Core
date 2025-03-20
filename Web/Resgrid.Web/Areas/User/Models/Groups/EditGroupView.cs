using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Groups
{
	public class EditGroupView : BaseUserModel
	{
		public string Message { get; set; }
		public List<DepartmentGroup> Groups { get; set; }
		public DepartmentGroup EditGroup { get; set; }
		public List<IdentityUser> Users { get; set; }
		public SelectList StationGroups { get; set; }

		public string Address1 { get; set; }

		[MaxLength(100)]
		public string City { get; set; }

		[MaxLength(50)]
		public string State { get; set; }

		[MaxLength(50)]
		public string PostalCode { get; set; }

		[MaxLength(100)]
		public string Country { get; set; }

		public string InternalDispatchEmail { get; set; }

		public string Latitude { get; set; }

		public string Longitude { get; set; }
		public string What3Word { get; set; }
		public string PrinterName { get; set; }
		public string PrinterId { get; set; }
		public string PrinterApiKey { get; set; }
	}
}
