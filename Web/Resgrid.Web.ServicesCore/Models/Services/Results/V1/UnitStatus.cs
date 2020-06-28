using System;
using System.Runtime.Serialization;

namespace Resgrid.Web.Services.Models.Services.Results.V1
{
	[Serializable]
	[DataContract]
	public class UnitStatus
	{
		[DataMember]
		public int UnitId { get; set; }

		[DataMember]
		public string Name { get; set; }

		[DataMember]
		public int StatusTypeId { get; set; }

		[DataMember]
		public DateTime Timestamp { get; set; }
	}
}