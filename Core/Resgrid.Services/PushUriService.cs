using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class PushUriService : IPushUriService
	{
		private readonly IPushUriRepository _pushUriRepository;

		public PushUriService(IPushUriRepository pushUriRepository)
		{
			_pushUriRepository = pushUriRepository;
		}

		public PushUri GetPushUriById(int pushUriId)
		{
			return _pushUriRepository.GetPushUriById(pushUriId);
		}

		public PushUri SavePushUri(PushUri pushUri)
		{
			if (pushUri.PushUriId == 0)
			{
				var uri = GetPushUriByPlatformDeviceId((Platforms)pushUri.PlatformType, pushUri.DeviceId);

				if (uri != null)
					return uri;
			}

			pushUri.CreatedOn = DateTime.UtcNow;
			_pushUriRepository.SaveOrUpdate(pushUri);

			return pushUri;
		}

		public List<PushUri> GetPushUrisByUserId(string userId)
		{
			List<PushUri> pushUris = new List<PushUri>();

			try
			{
				var savedPushUris = from pu in _pushUriRepository.GetAll()
									where pu.UserId == userId
									select pu;

				return pushUris = new List<PushUri>(savedPushUris);
			}
			catch { }

			return pushUris;
		}

		public List<PushUri> GetPushUrisByUserIdPlatform(string userId, Platforms platform)
		{
			var pushUris = from pu in _pushUriRepository.GetAll()
						   where pu.UserId == userId && pu.PlatformType == (int)platform
						   select pu;

			return pushUris.ToList();
		}

		public PushUri GetPushUriByPlatformDeviceId(Platforms platform, string deviceId)
		{
			return _pushUriRepository.GetPushUriByPlatformDeviceId((int)platform, deviceId);
		}

		public PushUri GetPushUriByDeviceId(string deviceId)
		{
			var pushUris = from pu in _pushUriRepository.GetAll()
						   where pu.DeviceId == deviceId
						   select pu;

			return pushUris.FirstOrDefault();
		}

		public void DeletePushUri(PushUri pushUri)
		{
			_pushUriRepository.DeleteOnSubmit(pushUri);
		}

		public void DeletePushUrisForUser(string userId)
		{
			var pushUri = (from pu in _pushUriRepository.GetAll()
						   where pu.UserId == userId
						   select pu).ToList();

			foreach (var uri in pushUri)
			{
				_pushUriRepository.DeleteOnSubmit(uri);
			}
		}

		public void DeleteAllPushUrisByPlatformDevice(Platforms platform, string deviceId)
		{
			var pushUri = (from pu in _pushUriRepository.GetAll()
						   where pu.PlatformType == (int)platform && pu.DeviceId == deviceId
						   select pu).ToList();

			foreach (var uri in pushUri)
			{
				_pushUriRepository.DeleteOnSubmit(uri);
			}
		}
	}
}