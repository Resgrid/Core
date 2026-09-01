using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Which client applications a department has exempted from the Advanced Data Protection
	/// step-up prompt (ADP plan section 3.3).
	///
	/// The default is zero — nothing exempt, every app prompts for a second factor before a
	/// protected value is revealed — and it stays that way until a department deliberately turns an
	/// app off. That direction matters: a department that never touches this setting keeps the
	/// stronger behaviour, and every weakening is an explicit, audited act by its managing member.
	///
	/// The reason to allow it at all is operational rather than theoretical. A dispatcher working a
	/// live incident cannot stop to read a code off a phone, and a prompt that lands mid-call is a
	/// safety problem, not a security win — people work around it in ways far worse than the
	/// exemption. So the choice is offered per app: a department can leave the prompt on for the web
	/// site, where someone is doing administrative work at a desk, and take it off the dispatch
	/// console, where seconds count.
	///
	/// An exemption does NOT remove the grant. The caller still has to be signed in, still gets a
	/// tenant-bound grant with an expiry and a policy epoch, and every read is still audited — what
	/// changes is only whether a second factor is demanded before that grant is minted. Grants issued
	/// this way are marked, so an auditor can tell them apart.
	///
	/// Values mirror <see cref="UserSessionClientApplication"/> so the two never drift.
	/// </summary>
	[Flags]
	public enum AdpStepUpExemptClients
	{
		/// <summary>The default and the recommendation: every client prompts.</summary>
		None = 0,

		/// <summary>Core web site.</summary>
		Web = 1 << UserSessionClientApplication.Web,

		Responder = 1 << UserSessionClientApplication.Responder,

		Unit = 1 << UserSessionClientApplication.Unit,

		/// <summary>The dispatch console — the case this setting exists for.</summary>
		Dispatch = 1 << UserSessionClientApplication.Dispatch,

		/// <summary>Incident Command.</summary>
		Command = 1 << UserSessionClientApplication.Command,

		/// <summary>
		/// Direct API callers. Deliberately offered last and separately: an API integration is not a
		/// person under time pressure, so the operational argument for exempting it is much weaker.
		/// </summary>
		Api = 1 << UserSessionClientApplication.Api
	}

	/// <summary>Maps a client application onto its exemption flag.</summary>
	public static class AdpStepUpExemptClientsExtensions
	{
		/// <summary>
		/// True when this department has exempted the given client from the step-up prompt.
		///
		/// BigBoard and MCP are never exemptable and always return false. BigBoard is an unattended
		/// wall display with no one to prompt and no business seeing protected values at all — it
		/// steps DOWN to safe projections (plan 7.3) rather than up. MCP is automated. An unknown or
		/// legacy client is likewise never exempt: a client that cannot identify itself must not
		/// inherit somebody else's exemption.
		/// </summary>
		public static bool IsExempt(this AdpStepUpExemptClients exemptions, UserSessionClientApplication client)
		{
			switch (client)
			{
				case UserSessionClientApplication.Web:
				case UserSessionClientApplication.Responder:
				case UserSessionClientApplication.Unit:
				case UserSessionClientApplication.Dispatch:
				case UserSessionClientApplication.Command:
				case UserSessionClientApplication.Api:
					return (exemptions & (AdpStepUpExemptClients)(1 << (int)client)) != 0;

				default:
					return false;
			}
		}

		/// <summary>Strips any bit that does not map to an exemptable client, so a stored value cannot
		/// carry meaning nothing reads — and cannot quietly acquire it if the enum grows.</summary>
		public static AdpStepUpExemptClients Sanitize(this AdpStepUpExemptClients exemptions)
		{
			var allowed = AdpStepUpExemptClients.Web | AdpStepUpExemptClients.Responder |
				AdpStepUpExemptClients.Unit | AdpStepUpExemptClients.Dispatch |
				AdpStepUpExemptClients.Command | AdpStepUpExemptClients.Api;

			return exemptions & allowed;
		}
	}
}
