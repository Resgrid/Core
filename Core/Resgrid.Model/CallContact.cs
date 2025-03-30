using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;
using Resgrid.Framework;

namespace Resgrid.Model
{
	[ProtoContract]
	public class CallContact : IEntity
	{
		[Required]
		[ProtoMember(1)]
		public string CallContactId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		[Required]
		[ProtoMember(3)]
		public int CallId { get; set; }

		[ProtoMember(4)]
		public string ContactId { get; set; }

		[ProtoMember(5)]
		public int CallContactType { get; set; } // 0 = Primary

		public string GetContactTypeName()
		{
			if (CallContactType == 0)
				return "Primary";

			return "Additional";
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallContactId; }
			set { CallContactId = (string)value; }
		}

		[NotMapped]
		public string TableName => "CallContacts";

		[NotMapped]
		public string IdName => "CallContactId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
