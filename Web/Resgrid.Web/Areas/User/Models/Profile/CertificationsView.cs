using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Profile
{
	public class CertificationsView
	{
		public Department Department { get; set; }
		public List<PersonnelCertification> Certifications { get; set; }
		public bool Self { get; set; }
		public string Name { get; set; }
		public string UserId { get; set; }
	}
}