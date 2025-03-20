using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Voice
{
	public class DepartmentAudioResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<DepartmentAudioResultStreamData> Data { get; set; }
	}

	public class DepartmentAudioResultStreamData
	{
		public string Id { get; set; }

		public string Name { get; set; }

		public string Url { get; set; }

		public string Type { get; set; }
	}
}
