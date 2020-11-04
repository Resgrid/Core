namespace Resgrid.Model.Repositories
{
	public interface IPushUriRepository
	{
		PushUri GetPushUriById(int pushUriId);
		PushUri GetPushUriByPlatformDeviceId(int platform, string deviceId);
	}
}
