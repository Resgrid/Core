using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Areas.User.Models.Profile
{
	public class AddCertificationView
	{
		public string UserId { get; set; }
		public string Message { get; set; }
		public SelectList CertificationTypes { get; set; }

		[Required]
		public string Name { get; set; }

		public string Number { get; set; }

		public string Type { get; set; }

		public string Area { get; set; }

		public string IssuedBy { get; set; }

		public DateTime? ExpiresOn { get; set; }

		public DateTime? RecievedOn { get; set; }
	}
}
