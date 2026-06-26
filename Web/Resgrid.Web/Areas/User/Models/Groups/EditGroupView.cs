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

		[StringLength(500, ErrorMessage = "Street address cannot exceed 500 characters.")]
		public string Address1 { get; set; }

		[StringLength(150, ErrorMessage = "City cannot exceed 150 characters.")]
		public string City { get; set; }

		[StringLength(100, ErrorMessage = "State/Province cannot exceed 100 characters.")]
		public string State { get; set; }

		[StringLength(32, ErrorMessage = "Postal code cannot exceed 32 characters.")]
		public string PostalCode { get; set; }

		[StringLength(100, ErrorMessage = "Country cannot exceed 100 characters.")]
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
