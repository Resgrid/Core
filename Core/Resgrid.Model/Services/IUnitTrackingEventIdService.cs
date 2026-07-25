namespace Resgrid.Model.Services
{
	public interface IUnitTrackingEventIdService
	{
		string CreateForHttps(string unitTrackingDeviceId, string callerEventId);
	}
}
