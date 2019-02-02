namespace Resgrid.Model
{
	public class PhoneNumberResult
	{
		public string LocalNumber { get; set; }
		public string InternationalNumber { get; set; }
		public bool IsValid { get; set; }
		public string CountryCode { get; set; }
		public string ErrorMessage { get; set; }
	}
}