namespace Resgrid.Model.Providers
{
	public interface IWeatherAlertProviderFactory
	{
		IWeatherAlertProvider GetProvider(WeatherAlertSourceType sourceType);
	}
}
