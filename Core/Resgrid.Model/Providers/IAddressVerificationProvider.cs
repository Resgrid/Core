using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IAddressVerificationProvider
	{
		Task<AddressVerificationResult> VerifyAddressAsync(Address address);
	}
}
