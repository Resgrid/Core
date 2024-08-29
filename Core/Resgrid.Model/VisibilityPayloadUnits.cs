using ProtoBuf;
using System.Collections.Generic;

namespace Resgrid.Model
{
	[ProtoContract]
	public class VisibilityPayloadUnits
	{
		[ProtoMember(1)]
		public bool EveryoneNoGroupLock { get; set; }

		[ProtoMember(2)]
		public Dictionary<int, List<string>> Units { get; set; }
	}
}
