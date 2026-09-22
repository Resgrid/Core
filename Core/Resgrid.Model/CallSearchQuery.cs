using System;

namespace Resgrid.Model
{
	/// <summary>A bounded page of call candidates; callers must authorize each result before displaying it.</summary>
	public sealed class CallSearchQuery
	{
		public string Term { get; set; }
		public bool? Closed { get; set; }
		public DateTime? FromUtc { get; set; }
		public DateTime? UntilUtc { get; set; }
		public int Offset { get; set; }
		public int Take { get; set; } = 26;
		public bool IncludeText { get; set; }
	}
}
