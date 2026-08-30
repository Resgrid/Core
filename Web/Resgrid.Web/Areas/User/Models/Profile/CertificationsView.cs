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

		/// <summary>
		/// True when this department's certification values are enveloped, so the page renders the
		/// step-up banner and the reveal wiring instead of plain text (ADP plan 5.1/7.2).
		/// </summary>
		public bool IsProtected { get; set; }
	}
}