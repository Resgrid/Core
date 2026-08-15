using ProtoBuf;
using System;
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

		/// <summary>
		/// When the payload was built. A user who joined the department after this point is not in
		/// <see cref="Users"/>, which is indistinguishable from "no restriction" unless we know the
		/// payload's age -- so consumers use this to tell a stale matrix from a permissive one.
		/// </summary>
		[ProtoMember(3)]
		public DateTime GeneratedOn { get; set; }
	}
}
