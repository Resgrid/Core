namespace Resgrid.Model.Events
{
	public class CertificationExpiringEvent
	{
		public int DepartmentId { get; set; }
		public PersonnelCertification Certification { get; set; }
		public int DaysUntilExpiry { get; set; }
	}
}

