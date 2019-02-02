using System;
using System.Collections.Generic;

namespace Resgrid.Model.Repositories
{
	public interface IPushUriRepository : IRepository<PushUri>
	{
		PushUri GetPushUriById(int pushUriId);
		PushUri GetPushUriByPlatformDeviceId(int platform, string deviceId);
	}
}