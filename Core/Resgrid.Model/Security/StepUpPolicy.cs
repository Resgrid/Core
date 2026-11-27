using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// Where a department can ask a member to prove who they are again before an action (owner decision 2026-11-26). Each area has one
	/// rule: whether step-up is required, and which kinds of proof count. Persisted inside setting 153 as the number: append only.
	/// </summary>
	public enum StepUpArea
	{
		/// <summary>L2 signatures: protocol version approvals and review stamps, translation and course approvals, certification
		/// verification, checklist witnesses, multi-signer request steps and LMS skill evaluations.</summary>
		Signatures = 1,

		/// <summary>Read-and-sign acknowledgments. Whether step-up is required is the Protocols setting <c>ReadAndSignRequiresStepUp</c>.</summary>
		Acknowledgments = 2,

		/// <summary>Approval chain decisions (purchase, work order, payroll and the other approvable subjects).</summary>
		Approvals = 3,

		/// <summary>The Advanced Data Protection reveal grant. Whether a client is prompted stays the ADP exemptions' decision.</summary>
		ProtectedData = 4
	}

	/// <summary>The kinds of proof a step-up can be completed with. Persisted as flags: append only.</summary>
	[Flags]
	public enum StepUpMethodClasses
	{
		None = 0,

		/// <summary>A second factor: an authenticator code, a passkey for this app, a Responder approval, or the provider's MFA where the
		/// department's MFA switches accept it. Always allowed.</summary>
		Mfa = 1,

		/// <summary>A fresh sign-in at the department's identity provider (prompt=login, max_age, auth_time checked). Not a second factor.</summary>
		Sso = 2,

		/// <summary>The account password entered again. Not a second factor.</summary>
		Password = 4
	}

	public static class StepUpAreas
	{
		public const string SignaturesName = "signatures";
		public const string AcknowledgmentsName = "acknowledgments";
		public const string ApprovalsName = "approvals";
		public const string ProtectedDataName = "protected_data";

		public static IReadOnlyList<StepUpArea> All { get; } = new[] { StepUpArea.Signatures, StepUpArea.Acknowledgments, StepUpArea.Approvals, StepUpArea.ProtectedData };

		public static bool IsKnown(StepUpArea area) => All.Contains(area);

		public static string Name(StepUpArea area) => area switch
		{
			StepUpArea.Signatures => SignaturesName,
			StepUpArea.Acknowledgments => AcknowledgmentsName,
			StepUpArea.Approvals => ApprovalsName,
			StepUpArea.ProtectedData => ProtectedDataName,
			_ => null
		};

		public static bool TryParse(string name, out StepUpArea area)
		{
			area = name switch
			{
				SignaturesName => StepUpArea.Signatures,
				AcknowledgmentsName => StepUpArea.Acknowledgments,
				ApprovalsName => StepUpArea.Approvals,
				ProtectedDataName => StepUpArea.ProtectedData,
				_ => 0
			};
			return area != 0;
		}

		/// <summary>The area a signature for <paramref name="use"/> belongs to when the caller does not name one.</summary>
		public static StepUpArea ForUse(SignatureUse use) =>
			use?.SubjectKind == SignatureSubjectKinds.ProtocolAcknowledgment ? StepUpArea.Acknowledgments : StepUpArea.Signatures;

		/// <summary>Which department MFA switches decide the acceptable second factors for the area (passkey plan section 7.6).</summary>
		public static MfaMethodScope MfaScope(StepUpArea area) => area == StepUpArea.ProtectedData ? MfaMethodScope.Adp : MfaMethodScope.Login;
	}

	public static class StepUpMethodNames
	{
		public const string Mfa = "mfa";
		public const string Sso = "sso";
		public const string Password = "password";

		/// <summary>The classes as wire names, strongest first.</summary>
		public static List<string> Of(StepUpMethodClasses classes)
		{
			var names = new List<string>();
			if (classes.HasFlag(StepUpMethodClasses.Mfa))
				names.Add(Mfa);
			if (classes.HasFlag(StepUpMethodClasses.Sso))
				names.Add(Sso);
			if (classes.HasFlag(StepUpMethodClasses.Password))
				names.Add(Password);
			return names;
		}

		/// <summary>The class a piece of verification evidence belongs to; None for anything that is not a step-up (a recovery code).</summary>
		public static StepUpMethodClasses ClassOf(MfaEvidenceMethod method) => method switch
		{
			MfaEvidenceMethod.Totp or MfaEvidenceMethod.Passkey or MfaEvidenceMethod.PasskeyApproval or MfaEvidenceMethod.Federated => StepUpMethodClasses.Mfa,
			MfaEvidenceMethod.Sso => StepUpMethodClasses.Sso,
			MfaEvidenceMethod.Password => StepUpMethodClasses.Password,
			_ => StepUpMethodClasses.None
		};

		/// <summary>The method name the v4 <c>StepUp/Verify</c> takes for a piece of evidence (the MFA names, plus <c>password</c> and <c>sso</c>).</summary>
		public static string From(MfaEvidenceMethod method) => method switch
		{
			MfaEvidenceMethod.Password => Password,
			MfaEvidenceMethod.Sso => Sso,
			_ => MfaMethodNames.From(method)
		};
	}

	/// <summary>
	/// The department's stored step-up choices (setting 153, <see cref="DepartmentSettingTypes.StepUpPolicy"/>). A department with no row, an
	/// area with no entry, and an unreadable row all mean the defaults, which are today's behaviour: step-up required with a second factor
	/// for signatures and approvals, read-and-sign as the Protocols setting says, protected data as ADP decides. Stored as "optional" and
	/// "extra methods" so a default never depends on a serialized true (protobuf drops a member equal to its default).
	/// </summary>
	[ProtoContract]
	public class DepartmentStepUpPolicy
	{
		[ProtoMember(1)]
		public List<StepUpAreaSetting> Areas { get; set; } = new List<StepUpAreaSetting>();

		public StepUpAreaSetting For(StepUpArea area) => Areas?.FirstOrDefault(a => a != null && a.Area == (int)area);

		/// <summary>Known areas only, one entry each, and only the method bits that exist (a second factor is never stored: always allowed).</summary>
		public DepartmentStepUpPolicy Normalize()
		{
			const int alternatives = (int)(StepUpMethodClasses.Sso | StepUpMethodClasses.Password);
			Areas = (Areas ?? new List<StepUpAreaSetting>())
				.Where(a => a != null && StepUpAreas.IsKnown((StepUpArea)a.Area))
				.GroupBy(a => a.Area)
				.Select(g => g.Last())
				.Select(a => new StepUpAreaSetting { Area = a.Area, Optional = a.Optional, AlternativeMethods = a.AlternativeMethods & alternatives })
				.OrderBy(a => a.Area)
				.ToList();
			return this;
		}
	}

	[ProtoContract]
	public class StepUpAreaSetting
	{
		[ProtoMember(1)]
		public int Area { get; set; }

		/// <summary>The department turned step-up off for this area (signatures and approvals only; the other two have their own switch).</summary>
		[ProtoMember(2)]
		public bool Optional { get; set; }

		/// <summary><see cref="StepUpMethodClasses.Sso"/> and/or <see cref="StepUpMethodClasses.Password"/> allowed besides a second factor.</summary>
		[ProtoMember(3)]
		public int AlternativeMethods { get; set; }
	}

	/// <summary>One area's effective rule, resolved from setting 153 and the switches that own "required" for two of the areas.</summary>
	public sealed class StepUpRule
	{
		public StepUpArea Area { get; init; }

		/// <summary>Whether this area asks for a step-up at all. For protected data: true, and the ADP exemptions still decide per app.</summary>
		public bool Required { get; init; }

		/// <summary>The allowed kinds of proof; always includes <see cref="StepUpMethodClasses.Mfa"/>.</summary>
		public StepUpMethodClasses Methods { get; init; } = StepUpMethodClasses.Mfa;

		/// <summary>How long one verification serves the area.</summary>
		public int WindowMinutes { get; init; }

		/// <summary>True when the rule was read from defaults because the stored policy could not be read (fails closed).</summary>
		public bool FromFallback { get; init; }

		public bool Allows(StepUpMethodClasses methodClass) => methodClass != StepUpMethodClasses.None && (Methods & methodClass) == methodClass;

		public static StepUpRule Default(StepUpArea area, int windowMinutes, bool required = true, bool fromFallback = false) => new()
		{
			Area = area,
			Required = required,
			Methods = StepUpMethodClasses.Mfa,
			WindowMinutes = windowMinutes,
			FromFallback = fromFallback
		};
	}

	/// <summary>A change to one area from the settings page or API.</summary>
	public sealed class StepUpRuleInput
	{
		public StepUpArea Area { get; init; }
		public bool Required { get; init; }
		public bool AllowSso { get; init; }
		public bool AllowPassword { get; init; }
	}

	public enum StepUpPolicySaveOutcome
	{
		Saved = 0,
		Unchanged = 1,
		InvalidRequest = 2,

		/// <summary>Only the department's managing member may change how protected data is verified.</summary>
		ManagingMemberRequired = 3,

		Failed = 4
	}

	/// <summary>
	/// Who is stepping up, from the server's validated session only: the account, the active department, the evidence key of the tracked
	/// session (Web key or <c>sid:</c>) and the account's current authentication generation.
	/// </summary>
	public sealed class StepUpSubject
	{
		public string UserId { get; init; }
		public int DepartmentId { get; init; }
		public string SessionKey { get; init; }
		public long AuthenticationGeneration { get; init; }
	}

	/// <summary>What the session can show for an area now.</summary>
	public sealed class StepUpEvaluation
	{
		public StepUpRule Rule { get; init; }

		/// <summary>The area does not ask for a step-up (the action goes ahead at its unproven level).</summary>
		public bool NotRequired { get; init; }

		/// <summary>A qualifying verification exists for this session.</summary>
		public bool Satisfied { get; init; }

		public StepUpMethodClasses MethodClass { get; init; }
		public MfaEvidenceMethod? Method { get; init; }
		public DateTime? VerifiedOnUtc { get; init; }
		public DateTime? ExpiresOnUtc { get; init; }

		/// <summary>True when the action may go ahead: not required, or satisfied.</summary>
		public bool Allowed => NotRequired || Satisfied;
	}

	/// <summary>What a member has for each kind of proof, gathered server-side (never from the client).</summary>
	public sealed class StepUpCapabilities
	{
		public bool TotpEnrolled { get; init; }
		public bool PasskeyForClient { get; init; }
		public bool ApprovalAvailable { get; init; }
		public bool ProviderMfaAvailable { get; init; }

		/// <summary>The account has a password and the department lets it sign in with one.</summary>
		public bool PasswordAvailable { get; init; }

		/// <summary>The department has a brokered identity provider and the account is linked to it.</summary>
		public bool SsoAvailable { get; init; }

		public List<string> MfaMethods()
		{
			var methods = new List<string>();
			if (TotpEnrolled)
				methods.Add(MfaMethodNames.Totp);
			if (PasskeyForClient)
				methods.Add(MfaMethodNames.Passkey);
			if (ApprovalAvailable)
				methods.Add(MfaMethodNames.PasskeyApproval);
			if (ProviderMfaAvailable)
				methods.Add(MfaMethodNames.Federated);
			return methods;
		}

		public StepUpMethodClasses Classes =>
			(TotpEnrolled || PasskeyForClient || ApprovalAvailable || ProviderMfaAvailable ? StepUpMethodClasses.Mfa : StepUpMethodClasses.None) |
			(SsoAvailable ? StepUpMethodClasses.Sso : StepUpMethodClasses.None) |
			(PasswordAvailable ? StepUpMethodClasses.Password : StepUpMethodClasses.None);
	}

	/// <summary>
	/// The one description of a step-up every surface shows (the v4 <c>step_up_required</c> refusal and <c>StepUp/Options</c>): the area,
	/// whether it is required, the allowed kinds, the kinds this member can use, and the second factors among them.
	/// </summary>
	public sealed class StepUpRequirement
	{
		public StepUpArea Area { get; init; }
		public bool Required { get; init; }
		public List<string> AllowedMethods { get; init; } = new List<string>();
		public List<string> UsableMethods { get; init; } = new List<string>();

		/// <summary>The member's usable second factors (<c>totp</c>, <c>passkey</c>, <c>passkey_approval</c>, <c>federated</c>) when MFA is usable.</summary>
		public List<string> MfaMethods { get; init; } = new List<string>();

		public int WindowMinutes { get; init; }

		/// <summary>True when the member can use at least one allowed kind of proof.</summary>
		public bool CanComplete => UsableMethods.Count > 0;

		public static StepUpRequirement For(StepUpRule rule, StepUpCapabilities capabilities)
		{
			if (rule == null)
				throw new ArgumentNullException(nameof(rule));
			capabilities ??= new StepUpCapabilities();
			var usable = rule.Methods & capabilities.Classes;
			return new StepUpRequirement
			{
				Area = rule.Area,
				Required = rule.Required,
				AllowedMethods = StepUpMethodNames.Of(rule.Methods),
				UsableMethods = StepUpMethodNames.Of(usable),
				MfaMethods = usable.HasFlag(StepUpMethodClasses.Mfa) ? capabilities.MfaMethods() : new List<string>(),
				WindowMinutes = rule.WindowMinutes
			};
		}
	}

	/// <summary>Why a member cannot complete an area's step-up (the settings warning).</summary>
	public enum StepUpGapReasons
	{
		/// <summary>No second factor enrolled, and no other allowed kind of proof.</summary>
		NoSecondFactor = 1,

		/// <summary>No second factor, the department allows the password, but this member signs in only through the provider.</summary>
		NoPassword = 2,

		/// <summary>No second factor, the department allows SSO, but this member is not linked to the provider.</summary>
		NoSsoLink = 3
	}

	public sealed class StepUpMemberGap
	{
		public string UserId { get; init; }
		public string Name { get; init; }
		public StepUpGapReasons Reason { get; init; }
	}

	/// <summary>One area's readiness: the members who could not complete its step-up as the policy stands.</summary>
	public sealed class StepUpReadiness
	{
		public StepUpRule Rule { get; init; }
		public List<StepUpMemberGap> Members { get; init; } = new List<StepUpMemberGap>();
		public int MemberCount { get; init; }

		/// <summary>The warning shows: the area is required and at least one member could not complete it.</summary>
		public bool ShowWarning => Rule != null && Rule.Required && Members.Count > 0;
	}
}
