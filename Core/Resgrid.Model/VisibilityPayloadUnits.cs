using ProtoBuf;
using System;
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

		/// <summary>
		/// When the payload was built. A unit created after this point is not in <see cref="Units"/>,
		/// which is indistinguishable from "no restriction" unless we know the payload's age -- so
		/// consumers use this to tell a stale matrix from a permissive one and trigger a rebuild.
		/// </summary>
		[ProtoMember(3)]
		public DateTime GeneratedOn { get; set; }
	}
}
