using System;
using System.Runtime.Serialization;

namespace Resgrid.Web.Services.Models.Services.Results.V1
{
	[Serializable]
	[DataContract]
	public class Message
	{
		[DataMember]
		public int MessageId { get; set; }

		[DataMember]
		public string Subject { get; set; }

		[DataMember]
		public string SendingUser { get; set; }

		[DataMember]
		public string Body { get; set; }

		[DataMember]
		public DateTime SentOn { get; set; }
	}
}