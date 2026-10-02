using System;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Security;

namespace Resgrid.Services
{
	/// <summary>
	/// Chooses the grant contract version an issuer emits (passkey plan section 8.2). Every reader accepts version 2 since
	/// slice 3; an issuer emits it only once <c>PasskeyConfig.EmitGrantV2</c> is on, and only for a caller whose session
	/// was validated on this request, whose facts it then binds. Otherwise the request stays version 1, as before.
	/// </summary>
	public static class ProtectedGrantIssueRequests
	{
		public static ProtectedDataGrantIssueRequest ForSession(ProtectedDataGrantIssueRequest request, ProtectedGrantSessionContext session,
			string mfaMethod) => ForSession(request, session, mfaMethod, PasskeyConfig.EmitGrantV2);

		public static ProtectedDataGrantIssueRequest ForSession(ProtectedDataGrantIssueRequest request, ProtectedGrantSessionContext session,
			string mfaMethod, bool emitVersionTwo)
		{
			ArgumentNullException.ThrowIfNull(request);
			if (!emitVersionTwo || session == null || string.IsNullOrWhiteSpace(session.SessionId))
				return request;

			// The binding comes from the validated session, never from token claims or the client.
			request.Version = 2;
			request.SessionId = session.SessionId;
			request.ClientApp = session.ClientApplication;
			request.AuthenticationGeneration = session.AuthenticationGeneration;
			request.SessionLockVersion = session.SessionLockVersion;
			request.FederatedFirstFactor = session.FederatedFirstFactor;
			request.MfaMethod = request.StepUpExempt ? ProtectedDataGrantMfaMethods.None : mfaMethod;
			if (request.StepUpExempt)
				request.MfaAtUtc = default;
			return request;
		}
	}
}
