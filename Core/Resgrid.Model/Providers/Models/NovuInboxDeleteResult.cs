namespace Resgrid.Model.Providers;

/// <summary>
/// The outcome of deleting a message from a subscriber's own Novu inbox.
/// </summary>
public enum NovuInboxDeleteResult
{
	Deleted = 0,

	/// <summary>
	/// No such message in that subscriber's inbox: it never existed, was already deleted, or belongs to
	/// another subscriber. Novu does not say which, and callers should not either.
	/// </summary>
	NotFound = 1,

	Failed = 2
}
