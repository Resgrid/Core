namespace Resgrid.Model
{
	/// <summary>What accepting an invitation does (Invites.Kind, M0280).</summary>
	public enum InviteKinds
	{
		/// <summary>Creates a new account and membership (sent at creation).</summary>
		Classic = 0,

		/// <summary>An imported person sets a password on their dormant account, which activates it (Department Import 15.4).</summary>
		ImportAccount = 1,

		/// <summary>An existing Resgrid account is invited to join; the membership is created only when they accept (12.3).</summary>
		ImportJoin = 2
	}

	/// <summary>The batched sender's progress for an invitation (Invites.SendState, M0280).</summary>
	public enum InviteSendStates
	{
		Sent = 0,
		Pending = 1,
		Failed = 2,
		Revoked = 3
	}
}
