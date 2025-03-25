using System;
using System.Runtime.Serialization;
using Resgrid.Model;

namespace Resgrid.Web.Services.Models.Services.Results.V1
{
	[Serializable]
	[DataContract]
	public class CallResult
	{
		[DataMember]
		public int CallId { get; set; }

		[DataMember]
		public string ReportingUserName { get; set; }

		[DataMember]
		public int Priority { get; set; }

		[DataMember]
		public bool IsCritical { get; set; }

		[DataMember]
		public string Name { get; set; }

		[DataMember]
		public string NatureOfCall { get; set; }

		[DataMember]
		public string MapPage { get; set; }

		[DataMember]
		public string Notes { get; set; }

		[DataMember]
		public string Address { get; set; }

		[DataMember]
		public string GeoLocationData { get; set; }

		[DataMember]
		public DateTime LoggedOn { get; set; }

		[DataMember]
		public int State { get; set; }
	}
}