using System;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	public class PersonName
	{
		private string _userId;

		[ProtoMember(1)]
		public string UserId { get { return _userId.ToUpper(); } set { _userId = value; } }


		public string Name => $"{FirstName} {LastName}";

		[ProtoMember(3)]
		public string FirstName { get; set; }

		[ProtoMember(4)]
		public string LastName { get; set; }
	}
}
