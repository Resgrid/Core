using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Types
{
	public class EditCallTypeView
	{
		public string Message { get; set; }
		public CallType CallType { get; set; }
		public int CallTypeIcon { get; set; }

		/// <summary>An uploaded custom icon image; replaces any earlier upload.</summary>
		public IFormFile IconFile { get; set; }

		/// <summary>Removes the uploaded custom icon so the type falls back to its map icon.</summary>
		public bool RemoveCustomIcon { get; set; }
	}
}
