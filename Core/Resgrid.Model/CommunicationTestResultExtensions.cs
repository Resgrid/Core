namespace Resgrid.Model
{
	/// <summary>
	/// Presentation helpers for communication test results. These live here rather than in a view so
	/// the web report, the v4 API and the apps all describe a channel's verification state with the
	/// same words — a report that says "Pending" in one place and "Unverified" in another is the sort
	/// of drift that makes an administrator distrust the whole report.
	/// </summary>
	public static class CommunicationTestResultExtensions
	{
		/// <summary>
		/// Label shown for a result's contact verification state. Push has no verifiable contact
		/// method — delivery depends on a registered device, not on a confirmed address or number —
		/// so it reports "N/A" instead of borrowing a verification word that would be meaningless.
		/// </summary>
		public static string GetVerificationDisplayText(this CommunicationTestResult result)
		{
			if (result == null)
				return "-";

			if (result.Channel == (int)CommunicationTestChannel.Push)
				return "N/A";

			return ((ContactVerificationStatus)result.VerificationStatus).ToDisplayText();
		}

		/// <summary>
		/// <c>true</c> when this result's channel carries a real verification state, i.e. anything
		/// except push. Use this before counting a result into a verification summary.
		/// </summary>
		public static bool HasVerifiableContactMethod(this CommunicationTestResult result)
		{
			return result != null && result.Channel != (int)CommunicationTestChannel.Push;
		}
	}
}
