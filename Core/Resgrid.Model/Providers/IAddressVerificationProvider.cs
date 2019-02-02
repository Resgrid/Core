namespace Resgrid.Model.Providers
{
	public interface IAddressVerificationProvider
	{
		AddressVerificationResult VerifyAddress(Address address);
	}
}