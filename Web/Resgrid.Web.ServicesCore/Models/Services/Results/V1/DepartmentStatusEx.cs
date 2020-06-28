using System;
using System.Runtime.Serialization;

namespace Resgrid.Web.Services.Models.Services.Results.V1
{
	[Serializable]
	[DataContract]
	public class DepartmentStatusEx
	{
		[DataMember]
		public string UserId { get; set; }

		[DataMember]
		public string Name { get; set; }

		[DataMember]
		public int ActionTypeId { get; set; }

		[DataMember]
		public DateTime Timestamp { get; set; }
	}
}