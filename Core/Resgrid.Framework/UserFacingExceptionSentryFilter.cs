using System;
using System.Linq;
using System.Reflection;
using Sentry.Extensibility;

namespace Resgrid.Framework
{
	/// <summary>
	/// Drops <see cref="IUserFacingException"/>s from Sentry: their message goes to the user, so an event for them is noise
	/// nobody can action. Added to every SDK init (the hosts' UseSentry/SentrySdk.Init and the Serilog sink behind
	/// <see cref="Logging.LogException"/>), so neither an unhandled one nor a logged one becomes an issue.
	/// </summary>
	public sealed class UserFacingExceptionSentryFilter : IExceptionFilter
	{
		public bool Filter(Exception ex) => IsUserFacing(ex);

		/// <summary>True for a user-facing exception, including one only wrapped by Task.Wait or reflection.</summary>
		public static bool IsUserFacing(Exception exception)
		{
			switch (exception)
			{
				case null:
					return false;
				case IUserFacingException _:
					return true;
				case AggregateException aggregate:
					var inner = aggregate.Flatten().InnerExceptions;
					return inner.Count > 0 && inner.All(IsUserFacing);
				case TargetInvocationException invocation:
					return IsUserFacing(invocation.InnerException);
				default:
					return false;
			}
		}
	}
}
