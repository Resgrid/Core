using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Transactions;
using System.Configuration;
using System.Data;
using Dapper;

namespace Resgrid.Repositories.DataRepository
{
	public class PushUriRepository : IPushUriRepository
	{
		public PushUri GetPushUriById(int pushUriId)
		{
			//using (IDbConnection db = new SqlConnection(connectionString))
			//{
			//	return db.Query<PushUri>($"SELECT * FROM PushUris WHERE PushUriId = @pushUriId", new { pushUriId = pushUriId }).FirstOrDefault();
			//}

			return null;
		}

		public PushUri GetPushUriByPlatformDeviceId(int platform, string deviceId)
		{
			//using (IDbConnection db = new SqlConnection(connectionString))
			//{
			//	return db.Query<PushUri>($"SELECT * FROM PushUris WHERE DeviceId = @deviceId AND PlatformType = @platformType", new { deviceId = deviceId, platformType = platform }).FirstOrDefault();
			//}

			return null;
		}
	}
}
