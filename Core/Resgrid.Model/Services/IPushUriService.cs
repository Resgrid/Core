using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IPushUriService
	{
		PushUri GetPushUriById(int pushUriId);
		PushUri SavePushUri(PushUri pushUri);
		List<PushUri> GetPushUrisByUserId(string userId);
		List<PushUri> GetPushUrisByUserIdPlatform(string userId, Platforms platform);
		void DeletePushUri(PushUri pushUri);
		void DeletePushUrisForUser(string userId);
	    PushUri GetPushUriByPlatformDeviceId(Platforms platform, string deviceId);
	    void DeleteAllPushUrisByPlatformDevice(Platforms platform, string deviceId);
		PushUri GetPushUriByDeviceId(string deviceId);
	}
}