using System.Collections.Generic;
using Resgrid.Config;

namespace Resgrid.Web.Services.Models.v4.Configs
{
	/// <summary>
	/// Gets Configuration Information for the Resgrid System
	/// </summary>
	public class GetSystemConfigResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public GetSystemConfigResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public GetSystemConfigResult()
		{
			Data = new GetSystemConfigResultData();
		}
	}

	/// <summary>
	/// Information about the Resgrid System
	/// </summary>
	public class GetSystemConfigResultData
	{
		/// <summary>
		/// Resgrid Datacenter Locations
		/// </summary>
		public List<ResgridSystemLocation> Locations { get; set; }

		public GetSystemConfigResultData()
		{
			Locations = InfoConfig.Locations;
		}
	}
}
