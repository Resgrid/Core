using ProtoBuf;
using System.Collections.Generic;


namespace Resgrid.Model
{
	[ProtoContract]
	public class VisibilityPayloadUsers
	{
		[ProtoMember(1)]
		public bool EveryoneNoGroupLock { get; set; }

		[ProtoMember(2)]
		public Dictionary<string, List<string>> Users { get; set; }
	}
}
