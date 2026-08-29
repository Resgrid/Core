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

		/// <summary>
		/// Label for the staffing level the member was on when the run was built: the name the
		/// department had configured at the time, the raw level when a run predates that snapshot or
		/// the level has since been deleted, and "-" when the member had never set one.
		/// </summary>
		public static string GetStaffingLevelDisplayText(this CommunicationTestResult result)
		{
			if (result == null)
				return "-";

			if (!string.IsNullOrWhiteSpace(result.StaffingLevelText))
				return result.StaffingLevelText;

			return result.StaffingLevel.HasValue ? result.StaffingLevel.Value.ToString() : "-";
		}

		/// <summary>
		/// The member's own on/off election for this channel. Falls back to <paramref name="liveValue"/>
		/// -- what their profile says right now -- only for runs built before the election was
		/// recorded, so an older report reads as unknown-but-plausible rather than claiming every
		/// channel in it was switched off.
		/// </summary>
		public static bool GetChannelElection(this CommunicationTestResult result, bool liveValue)
		{
			return result?.ChannelEnabled ?? liveValue;
		}
	}
}
