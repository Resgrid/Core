namespace Resgrid.Model.Services
{
	public interface IUnitTrackingIdentifierService
	{
		string Normalize(string identifier);
		string Mask(string identifier);
	}
}
