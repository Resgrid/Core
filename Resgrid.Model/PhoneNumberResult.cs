namespace Resgrid.Model
{
	public class PhoneNumberResult
	{
		public string LocalNumber { get; set; }
		public string InternationalNumber { get; set; }
		public bool IsValid { get; set; }
		public string CountryCode { get; set; }
		public string ErrorMessage { get; set; }

		/// <summary>
		/// ISO region the number actually parsed as ("GB", "AU"), which is not necessarily the region
		/// that was passed in - an E.164 number carries its own. Lets a caller learn the region a set of
		/// numbers belongs to and reuse it for the ones that arrived in national format.
		/// </summary>
		public string Region { get; set; }
	}
}
