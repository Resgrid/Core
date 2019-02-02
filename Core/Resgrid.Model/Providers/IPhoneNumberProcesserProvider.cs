namespace Resgrid.Model.Providers
{
	public interface IPhoneNumberProcesserProvider
	{
		PhoneNumberResult Process(string phoneNumber, string countryCode = null);
	}
}