using System;

namespace Resgrid.Model
{
	/// <summary>Department opt-in; every change advances the store version and revokes existing support sessions.</summary>
	public sealed class AdpSupportConsent
	{
		public bool Enabled { get; set; }
		public string UserId { get; set; }
		public DateTime UpdatedUtc { get; set; }
		public static string Key(int departmentId) => "support-consent:" + departmentId;
	}
}
