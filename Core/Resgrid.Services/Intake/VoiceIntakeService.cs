using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Intake
{
	/// <summary>
	/// Enhanced AI voice intake (plan D2, D6). Routing: a member of the number's department gets the member IVR; anyone else gets
	/// guided intake when the number has voice intake on, the department may use Ai.VoiceIntake (paid add-on, or the operator's
	/// self-hosted list) and is under DedicatedNumberAddonConfig.MonthlyVoiceIntakeLimit; otherwise the "please text" message.
	/// Guided intake asks Nature → Location → Caller name → Callback, re-asking up to IntakeConfig.VoiceMaxRetries; only Nature is
	/// required (an unanswered later question is skipped). One session per CallSid, so a Twilio retry or a stale action resumes the
	/// question the caller is on. The answers go to IIntakeService as a labelled transcript, which creates the pending call
	/// (VoiceIntake) and queues the AI; the session's transcript is then cleared.
	/// </summary>
	public class VoiceIntakeService : IVoiceIntakeService
	{
		private static readonly VoiceIntakeSteps[] Questions = { VoiceIntakeSteps.Nature, VoiceIntakeSteps.Location, VoiceIntakeSteps.CallerName, VoiceIntakeSteps.Callback };
		private const int MaxAnswerLength = 600;

		private readonly IVoiceIntakeSessionsRepository _sessions;
		private readonly IIntakeChannelsRepository _channels;
		private readonly IIntakeService _intake;
		private readonly IVoiceIntakeTranscriber _transcriber;
		private readonly IEnhancedAiAccessService _enhancedAi;
		private readonly IFeatureToggleService _flags;
		private readonly IUserProfileService _profiles;
		private readonly IDepartmentsService _departments;
		private readonly IIntakeBlockedSendersRepository _blocked;
		private readonly TimeProvider _clock;

		public VoiceIntakeService(IVoiceIntakeSessionsRepository sessions, IIntakeChannelsRepository channels, IIntakeService intake, IVoiceIntakeTranscriber transcriber,
			IEnhancedAiAccessService enhancedAi, IFeatureToggleService flags, IUserProfileService profiles, IDepartmentsService departments,
			IIntakeBlockedSendersRepository blocked, TimeProvider clock)
		{
			_blocked = blocked;
			_sessions = sessions;
			_channels = channels;
			_intake = intake;
			_transcriber = transcriber;
			_enhancedAi = enhancedAi;
			_flags = flags;
			_profiles = profiles;
			_departments = departments;
			_clock = clock;
		}

		public async Task<VoiceIntakeRouting> RouteAsync(DepartmentPhoneNumber number, string fromNumber, DateTime nowUtc)
		{
			if (number == null)
				return new VoiceIntakeRouting { Route = VoiceIntakeRoutes.NumberInactive, Reason = "NoNumber" };

			var routing = new VoiceIntakeRouting { DepartmentId = number.DepartmentId };

			// A lapsed number is held, not in service: everyone hears that it no longer takes calls, members too (their own IVR stays on the
			// master number), as a text to it gets the lapsed refusal whoever sends it.
			if (number.Status != (int)DepartmentPhoneNumberStatuses.Active)
				return Route(routing, VoiceIntakeRoutes.NumberInactive, "NumberNotActive");

			// Members keep the IVR on their department's number, in that department (not the one their profile defaults to).
			var memberUserId = await MemberUserIdAsync(number.DepartmentId, fromNumber);
			if (memberUserId != null)
			{
				routing.Route = VoiceIntakeRoutes.MemberMenu;
				routing.MemberUserId = memberUserId;
				return routing;
			}

			// M0326: a caller the department blocked (dismiss as spam) hears that the line cannot take the call; no session, no limit used.
			if (await IsBlockedAsync(number.DepartmentId, fromNumber, nowUtc))
				return Route(routing, VoiceIntakeRoutes.Blocked, "SenderBlocked");
			if (!IntakeConfig.Enabled || !number.VoiceIntakeEnabled || !number.VoiceCapable)
				return Route(routing, VoiceIntakeRoutes.TextOnly, "VoiceIntakeOff");

			try
			{
				if (!await _flags.IsEnabledAsync(FeatureFlagKeys.IntakeChannels, number.DepartmentId))
					return Route(routing, VoiceIntakeRoutes.TextOnly, "FeatureOff");
				if (!await CanUseVoiceIntakeAsync(number.DepartmentId))
					return Route(routing, VoiceIntakeRoutes.TextOnly, "NotEntitled");

				var channel = await _channels.GetLiveByAddressAsync((int)IntakeChannelTypes.Voice, IntakeChannelAddresses.PhoneNumber(number.PhoneNumber));
				if (channel == null || !channel.IsEnabled || channel.DepartmentId != number.DepartmentId)
					return Route(routing, VoiceIntakeRoutes.TextOnly, "NoVoiceChannel");

				var limit = DedicatedNumberAddonConfig.MonthlyVoiceIntakeLimit;
				if (limit <= 0 || await _intake.GetMonthlyCountAsync(number.DepartmentId, IntakeChannelTypes.Voice, nowUtc) >= limit)
					return Route(routing, VoiceIntakeRoutes.TextOnly, "OverLimit");
			}
			catch (Exception ex)
			{
				// A flag or billing read that fails keeps the line safe: the caller is told to text instead.
				Framework.Logging.LogException(ex, $"Voice intake routing failed for department {number.DepartmentId}.");
				return Route(routing, VoiceIntakeRoutes.TextOnly, "RoutingFailed");
			}

			return Route(routing, VoiceIntakeRoutes.Intake, null);
		}

		public async Task<VoiceIntakeTurn> StartAsync(DepartmentPhoneNumber number, string providerCallId, string fromNumber, CancellationToken cancellationToken = default)
		{
			if (number == null || string.IsNullOrWhiteSpace(providerCallId))
				return Closing(VoiceIntakeSteps.Abandoned, TwilioVoicePromptCatalog.VoiceIntakeFailed, null);

			var existing = await _sessions.GetByProviderCallIdAsync(providerCallId);
			if (existing != null)
				return Resume(existing);

			var session = new VoiceIntakeSession
			{
				VoiceIntakeSessionId = Guid.NewGuid().ToString("D"),
				DepartmentId = number.DepartmentId,
				ProviderCallId = Cap(providerCallId.Trim(), 64),
				FromNumber = Cap(IntakeChannelAddresses.PhoneNumber(fromNumber) ?? fromNumber, 32),
				ToNumber = Cap(IntakeChannelAddresses.PhoneNumber(number.PhoneNumber) ?? number.PhoneNumber, 32),
				Step = (int)VoiceIntakeSteps.Nature,
				AnswersJson = "{}",
				StartedOn = _clock.GetUtcNow().UtcDateTime
			};

			if (!await _sessions.TryInsertAsync(session, cancellationToken))
			{
				// Twilio retried the webhook while the first attempt was inserting: continue that session.
				existing = await _sessions.GetByProviderCallIdAsync(providerCallId);
				return existing != null ? Resume(existing) : Closing(VoiceIntakeSteps.Abandoned, TwilioVoicePromptCatalog.VoiceIntakeFailed, null);
			}

			return Ask(VoiceIntakeSteps.Nature, TwilioVoicePromptCatalog.VoiceIntakeGreeting);
		}

		public async Task<VoiceIntakeTurn> AnswerAsync(VoiceIntakeAnswer answer, CancellationToken cancellationToken = default)
		{
			if (answer == null || string.IsNullOrWhiteSpace(answer.ProviderCallId))
				return Closing(VoiceIntakeSteps.Abandoned, TwilioVoicePromptCatalog.VoiceIntakeFailed, null);

			var session = await _sessions.GetByProviderCallIdAsync(answer.ProviderCallId.Trim());
			if (session == null)
				return Closing(VoiceIntakeSteps.Abandoned, TwilioVoicePromptCatalog.VoiceIntakeFailed, null);

			var step = (VoiceIntakeSteps)session.Step;
			if (IsTerminal(step))
				return Resume(session);

			// A retried or out-of-order action answers a question the caller is no longer on: ask the current one again, unchanged.
			if (answer.Step != session.Step)
				return Ask(step);

			// Every write below applies only while the stored session is still the one read here: an overlapping callback for the same call
			// (a Twilio retry) that moved it on, or completed it, wins, and this one answers from what it wrote.
			var read = (Step: session.Step, Retries: session.Retries);
			var text = await AnswerTextAsync(step, answer, session, cancellationToken);
			var answers = ReadAnswers(session.AnswersJson);

			if (string.IsNullOrWhiteSpace(text))
			{
				session.Retries++;
				if (session.Retries <= Math.Max(0, IntakeConfig.VoiceMaxRetries))
				{
					if (!await SaveAsync(session, answers, read, cancellationToken))
						return await ResumeCurrentAsync(session.ProviderCallId);
					return Ask(step, TwilioVoicePromptCatalog.VoiceIntakeNotHeard);
				}

				if (step == VoiceIntakeSteps.Nature)
				{
					session.Step = (int)VoiceIntakeSteps.Abandoned;
					session.CompletedOn = _clock.GetUtcNow().UtcDateTime;
					if (!await SaveAsync(session, null, read, cancellationToken))
						return await ResumeCurrentAsync(session.ProviderCallId);
					return Closing(VoiceIntakeSteps.Abandoned, TwilioVoicePromptCatalog.VoiceIntakeNothingHeard, null);
				}
			}
			else
				answers[step.ToString()] = text;

			session.Retries = 0;
			var next = Next(step);
			if (next != VoiceIntakeSteps.Completed)
			{
				session.Step = (int)next;
				if (!await SaveAsync(session, answers, read, cancellationToken))
					return await ResumeCurrentAsync(session.ProviderCallId);
				return Ask(next);
			}

			return await CompleteAsync(session, answers, read, cancellationToken);
		}

		/// <summary>A write lost to an overlapping callback: continue from the session as it is stored now.</summary>
		private async Task<VoiceIntakeTurn> ResumeCurrentAsync(string providerCallId)
		{
			var current = await _sessions.GetByProviderCallIdAsync(providerCallId);
			return current != null ? Resume(current) : Closing(VoiceIntakeSteps.Abandoned, TwilioVoicePromptCatalog.VoiceIntakeFailed, null);
		}

		/// <summary>The labelled transcript the pending call (and the AI) reads: one "Label: answer" line per question answered.</summary>
		public static string Transcript(IDictionary<string, string> answers)
		{
			var text = new StringBuilder();
			foreach (var question in Questions)
			{
				if (answers != null && answers.TryGetValue(question.ToString(), out var value) && !string.IsNullOrWhiteSpace(value))
					text.Append(Label(question)).Append(": ").Append(value.Trim()).Append('\n');
			}

			return text.ToString().TrimEnd('\n');
		}

		public static string Label(VoiceIntakeSteps step)
		{
			switch (step)
			{
				case VoiceIntakeSteps.Nature:
					return "Nature";
				case VoiceIntakeSteps.Location:
					return "Location";
				case VoiceIntakeSteps.CallerName:
					return "Caller name";
				case VoiceIntakeSteps.Callback:
					return "Callback number";
				default:
					return step.ToString();
			}
		}

		private async Task<VoiceIntakeTurn> CompleteAsync(VoiceIntakeSession session, Dictionary<string, string> answers, (int Step, int Retries) read,
			CancellationToken cancellationToken)
		{
			var now = _clock.GetUtcNow().UtcDateTime;
			var result = await _intake.ReceiveAsync(new IntakeEnvelope
			{
				ChannelType = IntakeChannelTypes.Voice,
				ChannelAddress = session.ToNumber,
				ProviderMessageId = session.ProviderCallId,
				Sender = session.FromNumber,
				Body = Transcript(answers),
				ReceivedOnUtc = now,
				Answers = answers
			}, cancellationToken);

			// An overlapping callback for the same call (a Twilio retry of this answer) owns the intake and is still creating the call: not a
			// result. This one writes nothing (the owner records Completed or Abandoned) and answers from the stored session: finished by now,
			// or still on this question, which is asked again; intake, keyed on the CallSid, then returns the call it made instead of a second.
			if (result != null && result.State == IntakeMessageStates.Duplicate && !result.CallId.HasValue &&
				result.Reason == IntakeResultReasons.DuplicateInProgress)
				return await ResumeCurrentAsync(session.ProviderCallId);

			session.CompletedOn = now;
			// AddedToCall (M0326): a caller past the hourly limit was recorded as a note on their open call, which still counts as received.
			if (result?.CallId != null && (result.State == IntakeMessageStates.CallCreated || result.State == IntakeMessageStates.Duplicate ||
				result.State == IntakeMessageStates.AddedToCall))
			{
				session.Step = (int)VoiceIntakeSteps.Completed;
				session.CallId = result.CallId;
				// The transcript now lives on the pending call (where ADP protects it); the session keeps none of it. The call exists either way
				// (intake keys it on the CallSid), so a write lost to an overlapping callback still tells the caller it was received.
				await SaveAsync(session, null, read, cancellationToken);
				return Closing(VoiceIntakeSteps.Completed, TwilioVoicePromptCatalog.VoiceIntakeCompleted, result.CallId);
			}

			// Kept until the retention purge so an operator can recover a message the intake path refused or failed on.
			session.Step = (int)VoiceIntakeSteps.Abandoned;
			if (!await SaveAsync(session, answers, read, cancellationToken))
				return await ResumeCurrentAsync(session.ProviderCallId);
			return Closing(VoiceIntakeSteps.Abandoned, TwilioVoicePromptCatalog.VoiceIntakeFailed, null);
		}

		private async Task<string> AnswerTextAsync(VoiceIntakeSteps step, VoiceIntakeAnswer answer, VoiceIntakeSession session, CancellationToken cancellationToken)
		{
			var digits = (answer.Digits ?? string.Empty).Trim().TrimEnd('#');
			if (step == VoiceIntakeSteps.Callback)
			{
				if (digits == "*")
					return session.FromNumber;
				if (digits.Length >= 7 && digits.All(char.IsDigit))
					return digits;
			}

			VoiceTranscript transcript = null;
			try
			{
				transcript = await _transcriber.TranscribeAsync(answer, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Framework.Logging.LogException(ex, "Voice intake transcription failed; re-asking.");
			}

			var text = Clean(transcript?.Text);
			if (step == VoiceIntakeSteps.Callback && text != null && Regex.IsMatch(text, @"^(the\s+)?(same|this)(\s+(one|number|phone))?\.?$", RegexOptions.IgnoreCase))
				return session.FromNumber;

			return text;
		}

		private async Task<bool> CanUseVoiceIntakeAsync(int departmentId)
		{
			if (AiOperatorSettings.IsListedDepartment(AiConfig.SelfHostedDepartmentIds, departmentId))
				return await _enhancedAi.IsEnabledAsync(departmentId) &&
					(await _flags.EvaluateFreshAsync(FeatureFlagKeys.AiVoiceIntake, departmentId))?.IsEnabled == true;

			return await _enhancedAi.CanUseAsync(departmentId, FeatureFlagKeys.AiVoiceIntake);
		}

		private async Task<string> MemberUserIdAsync(int departmentId, string fromNumber)
		{
			var digits = new string((fromNumber ?? string.Empty).Where(char.IsDigit).ToArray());
			if (digits.Length < 7)
				return null;

			try
			{
				// The member menu acts as that member (status, staffing), so the number must be one they verified: caller id is easy to set,
				// and the lookups fall back to an unverified match. An unverified match is a member of the public here (sms-unverified routing:
				// an unverified number never authorizes actions).
				// One number can be a verified mobile on one profile and a verified home number on another (a household, a shared line): the
				// verified mobile match is tried first and settles it when it is an active member of this department; the home number is only
				// looked up when it is not (so a failing home lookup never turns a member calling from their verified mobile into the public).
				var mobile = await _profiles.GetProfileByMobileNumberAsync(digits);
				var mobileUserId = mobile != null && mobile.MobileNumberVerified == true && !string.IsNullOrWhiteSpace(mobile.UserId) ? mobile.UserId : null;
				if (mobileUserId != null && await IsActiveMemberAsync(mobileUserId, departmentId))
					return mobileUserId;

				var home = await _profiles.GetProfileByHomeNumberAsync(digits);
				if (home != null && home.HomeNumberVerified == true && !string.IsNullOrWhiteSpace(home.UserId) &&
					!string.Equals(home.UserId, mobileUserId, StringComparison.OrdinalIgnoreCase) && await IsActiveMemberAsync(home.UserId, departmentId))
					return home.UserId;

				return null;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, "Voice intake member lookup failed.");
				return null;
			}
		}

		private async Task<bool> IsActiveMemberAsync(string userId, int departmentId)
		{
			var member = await _departments.GetDepartmentMemberAsync(userId, departmentId);
			return member != null && member.DepartmentId == departmentId && !member.IsDeleted && member.IsDisabled != true;
		}

		/// <summary>False when the stored session moved on since <paramref name="read"/> (another callback's write won; nothing was written).</summary>
		private async Task<bool> SaveAsync(VoiceIntakeSession session, Dictionary<string, string> answers, (int Step, int Retries) read, CancellationToken cancellationToken)
		{
			session.AnswersJson = answers == null ? null : JsonConvert.SerializeObject(answers);
			return await _sessions.UpdateProgressAsync(session, read.Step, read.Retries, cancellationToken);
		}

		private static VoiceIntakeTurn Resume(VoiceIntakeSession session)
		{
			var step = (VoiceIntakeSteps)session.Step;
			if (step == VoiceIntakeSteps.Completed)
				return Closing(step, TwilioVoicePromptCatalog.VoiceIntakeCompleted, session.CallId);
			if (step == VoiceIntakeSteps.Abandoned)
				return Closing(step, session.CallId.HasValue ? TwilioVoicePromptCatalog.VoiceIntakeCompleted : TwilioVoicePromptCatalog.VoiceIntakeNothingHeard, session.CallId);
			if (step == VoiceIntakeSteps.Greeting)
				return Ask(VoiceIntakeSteps.Nature, TwilioVoicePromptCatalog.VoiceIntakeGreeting);

			return Ask(step);
		}

		private static VoiceIntakeTurn Ask(VoiceIntakeSteps step, string before = null)
		{
			var turn = new VoiceIntakeTurn { Step = step, Gather = true, AcceptDigits = step == VoiceIntakeSteps.Callback };
			if (before != null)
				turn.Prompts.Add(before);
			turn.Prompts.Add(Question(step));
			return turn;
		}

		private static VoiceIntakeTurn Closing(VoiceIntakeSteps step, string prompt, int? callId) =>
			new VoiceIntakeTurn { Step = step, HangUp = true, CallId = callId, Prompts = new List<string> { prompt } };

		private static string Question(VoiceIntakeSteps step)
		{
			switch (step)
			{
				case VoiceIntakeSteps.Location:
					return TwilioVoicePromptCatalog.VoiceIntakeAskLocation;
				case VoiceIntakeSteps.CallerName:
					return TwilioVoicePromptCatalog.VoiceIntakeAskCallerName;
				case VoiceIntakeSteps.Callback:
					return TwilioVoicePromptCatalog.VoiceIntakeAskCallback;
				default:
					return TwilioVoicePromptCatalog.VoiceIntakeAskNature;
			}
		}

		private static VoiceIntakeSteps Next(VoiceIntakeSteps step)
		{
			var index = Array.IndexOf(Questions, step);
			return index >= 0 && index < Questions.Length - 1 ? Questions[index + 1] : VoiceIntakeSteps.Completed;
		}

		private static bool IsTerminal(VoiceIntakeSteps step) => step == VoiceIntakeSteps.Completed || step == VoiceIntakeSteps.Abandoned;

		private static Dictionary<string, string> ReadAnswers(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new Dictionary<string, string>(StringComparer.Ordinal);

			try
			{
				return JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(StringComparer.Ordinal);
			}
			catch (JsonException)
			{
				return new Dictionary<string, string>(StringComparer.Ordinal);
			}
		}

		private static string Clean(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var text = Regex.Replace(new string(value.Where(c => !char.IsControl(c)).ToArray()), @"\s+", " ").Trim();
			return text.Length == 0 ? null : Cap(text, MaxAnswerLength);
		}

		/// <summary>The caller is on the department's blocked list; a list that cannot be read lets the call through.</summary>
		private async Task<bool> IsBlockedAsync(int departmentId, string fromNumber, DateTime nowUtc)
		{
			var key = IntakeBlockedSenders.Normalize(fromNumber);
			if (key == null)
				return false;

			try
			{
				return await _blocked.GetInForceAsync(departmentId, key, nowUtc) != null;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Voice intake blocked-caller check failed for department {departmentId}; the call is let through.");
				return false;
			}
		}

		private static VoiceIntakeRouting Route(VoiceIntakeRouting routing, VoiceIntakeRoutes route, string reason)
		{
			routing.Route = route;
			routing.Reason = reason;
			return routing;
		}

		private static string Cap(string value, int max) => value == null ? null : value.Length <= max ? value : value.Substring(0, max);
	}

	/// <summary>The D2 transcriber: Twilio already ran speech recognition (&lt;Gather input="speech"&gt;) and posted SpeechResult.</summary>
	public class TwilioSpeechTranscriber : IVoiceIntakeTranscriber
	{
		public Task<VoiceTranscript> TranscribeAsync(VoiceIntakeAnswer answer, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(new VoiceTranscript { Text = answer?.SpeechResult, Confidence = answer?.Confidence });
		}
	}
}
