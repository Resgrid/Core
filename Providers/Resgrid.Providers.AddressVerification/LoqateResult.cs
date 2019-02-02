using System.Collections.Generic;

namespace Resgrid.Providers.AddressVerification
{
	public class LoqateAddressVerificationResult
	{
		public string status { get; set; }
		public List<LoqateAddressVerificationResultItem> results { get; set; }
	}

	public class LoqateAddressVerificationResultItem
	{
		public string AVC { get; set; }
		public string Address1 { get; set; }
		public string Address2 { get; set; }
		public string AdministrativeArea { get; set; }
		public string CountryName { get; set; }
		public string DeliveryAddress { get; set; }
		public string DeliveryAddress1 { get; set; }
		public string Locality { get; set; }
		public string MatchRuleLabel { get; set; }
		public string PostalCode { get; set; }
		public string PostalCodePrimary { get; set; }
		public string PostalCodeSecondary { get; set; }
		public string Premise { get; set; }
		public string PremiseNumber { get; set; }
		public string SubAdministrativeArea { get; set; }
		public string Thoroughfare { get; set; }
		public string ThoroughfareName { get; set; }
		public string ThoroughfareTrailingType { get; set; }
		public string ThoroughfareType { get; set; }
	}

	public class VerificationCode
	{
		public string AVC { get; set; }

		public VerificationCode(string avc)
		{
			AVC = avc;
		}

		public bool WasAddressVerified()
		{
			if (AVC[0] == char.Parse("V"))
				return true;
			else if (AVC[0] == char.Parse("P"))
				return true;

			return false;
		}

		public bool WasAddressedParsed()
		{
			if (AVC[4] == char.Parse("I"))
				return true;

			return false;
		}

		public bool VerifiedToPremises()
		{
			if (int.Parse(AVC[1].ToString()) >= 4)
			return true;

			return false;
		}

		public int Score
		{
			get { return int.Parse(string.Format("{0}{1}{2}", AVC[11], AVC[12], AVC[13])); }
		}
	}
}
