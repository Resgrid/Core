namespace Resgrid.Framework
{
	/// <summary>
	/// An exception whose message is meant for the person using the app (a Records module that is not enabled for their
	/// department, ...) rather than a fault anyone on the backend can act on. Callers report it to that person;
	/// <see cref="UserFacingExceptionSentryFilter"/> keeps it out of Sentry wherever it still ends up logged.
	/// </summary>
	public interface IUserFacingException
	{
	}
}
