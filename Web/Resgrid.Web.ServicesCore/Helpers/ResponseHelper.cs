using Resgrid.Web.Services.Models.v4;
using System;

namespace Resgrid.Web.Services.Helpers
{
	public static class ResponseHelper
	{
		public static void PopulateV4ResponseData(StandardApiResponseV4Base data)
		{
			data.Timestamp = DateTime.UtcNow;
			data.Version = "v4";
			data.Node = Environment.MachineName;
			data.RequestId = System.Diagnostics.Activity.Current.Id;
			data.Environment = Enum.GetName(Config.SystemBehaviorConfig.Environment);
		}

		public static void PopulateV4ResponseNotFound(StandardApiResponseV4Base data)
		{
			data.PageSize = 0;
			data.Status = ResponseHelper.NotFound;
			PopulateV4ResponseData(data);
		}

		public const string Success = "success";
		public const string Failure = "failure";
		public const string NotFound = "not_found";
		public const string Created = "created";
		public const string Updated = "updated";
		public const string Deleted = "deleted";
		public const string Queued = "queued";
	}
}
