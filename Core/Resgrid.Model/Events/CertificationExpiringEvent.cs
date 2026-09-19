namespace Resgrid.Model.Events
{
	public class CertificationExpiringEvent
	{
		public int DepartmentId { get; set; }
		public PersonnelCertification Certification { get; set; }
		public int DaysUntilExpiry { get; set; }

		// Phase D (plan D7): typed catalog metadata; null for a legacy free-text record.
		public string TypeCode { get; set; }
		public string TypeName { get; set; }
	}
}

