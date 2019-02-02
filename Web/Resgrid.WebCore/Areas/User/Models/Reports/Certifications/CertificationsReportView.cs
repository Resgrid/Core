using System;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Reports.Certifications
{
	public class CertificationsReportRow
    {
		public string Name { get; set; }
		public string ID { get; set; }
		public string Group { get; set; }
		public List<CertificationsReportSubRow> SubRows { get; set; }
    }

	public class CertificationsReportSubRow
	{
		public string Name { get; set; }

		public string Number { get; set; }

		public string Type { get; set; }

		public string Area { get; set; }

		public string IssuedBy { get; set; }

		public string ExpiresOn { get; set; }
	}

	public class CertificationsReportView
    {
        public DateTime RunOn { get; set; }
		public List<CertificationsReportRow> Rows { get; set; }
    }
}