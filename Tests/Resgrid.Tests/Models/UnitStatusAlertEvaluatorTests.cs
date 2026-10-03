using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// The server's copy of the Big Board's "is this unit overdue, and does an acknowledgement still cover it"
	/// rules. The cases that matter to dispatchers: a new status ends the acknowledgement, escalation brings the
	/// alert back, and a mute that runs out falls back to acknowledged rather than vanishing.
	/// </summary>
	[TestFixture]
	public class UnitStatusAlertEvaluatorTests
	{
		private static readonly DateTime Now = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

		private static UnitStatusThresholds Dispatched(int warnSeconds, int alertSeconds) => new UnitStatusThresholds
		{
			Thresholds = new List<UnitStatusThreshold>
			{
				new UnitStatusThreshold { BaseType = (int)ActionBaseTypes.Dispatched, WarnSeconds = warnSeconds, AlertSeconds = alertSeconds }
			}
		};

		private static UnitStatusAlertAcknowledgement Ack(int unitStateId = 77, UnitStatusAlertLevels level = UnitStatusAlertLevels.Warn,
			UnitStatusAlertAcknowledgementModes mode = UnitStatusAlertAcknowledgementModes.Acknowledged, DateTime? mutedUntil = null) =>
			new UnitStatusAlertAcknowledgement { UnitStateId = unitStateId, Level = (int)level, Mode = (int)mode, MutedUntil = mutedUntil };

		[Test]
		public void Nothing_is_overdue_without_thresholds()
		{
			UnitStatusAlertEvaluator.Evaluate(new UnitStatusThresholds(), (int)ActionBaseTypes.Dispatched, Now.AddHours(-3), Now)
				.Should().Be(UnitStatusAlertLevels.None);
		}

		[Test]
		public void Nothing_is_overdue_when_the_status_has_no_base_type()
		{
			UnitStatusAlertEvaluator.Evaluate(Dispatched(240, 480), null, Now.AddHours(-3), Now).Should().Be(UnitStatusAlertLevels.None);
		}

		[Test]
		public void A_status_without_a_threshold_is_never_overdue()
		{
			UnitStatusAlertEvaluator.Evaluate(Dispatched(240, 480), (int)ActionBaseTypes.OnScene, Now.AddHours(-3), Now).Should().Be(UnitStatusAlertLevels.None);
		}

		[TestCase(239, UnitStatusAlertLevels.None)]
		[TestCase(240, UnitStatusAlertLevels.Warn)]
		[TestCase(479, UnitStatusAlertLevels.Warn)]
		[TestCase(480, UnitStatusAlertLevels.Alert)]
		[TestCase(4000, UnitStatusAlertLevels.Alert)]
		public void Levels_follow_the_thresholds(int secondsInStatus, UnitStatusAlertLevels expected)
		{
			UnitStatusAlertEvaluator.Evaluate(Dispatched(240, 480), (int)ActionBaseTypes.Dispatched, Now.AddSeconds(-secondsInStatus), Now)
				.Should().Be(expected);
		}

		[Test]
		public void An_alert_only_threshold_skips_the_warning()
		{
			UnitStatusAlertEvaluator.Evaluate(Dispatched(0, 300), (int)ActionBaseTypes.Dispatched, Now.AddSeconds(-299), Now).Should().Be(UnitStatusAlertLevels.None);
			UnitStatusAlertEvaluator.Evaluate(Dispatched(0, 300), (int)ActionBaseTypes.Dispatched, Now.AddSeconds(-300), Now).Should().Be(UnitStatusAlertLevels.Alert);
		}

		[Test]
		public void A_status_timestamped_in_the_future_is_not_overdue()
		{
			UnitStatusAlertEvaluator.Evaluate(Dispatched(240, 480), (int)ActionBaseTypes.Dispatched, Now.AddMinutes(5), Now).Should().Be(UnitStatusAlertLevels.None);
		}

		[Test]
		public void An_acknowledgement_covers_its_own_episode()
		{
			UnitStatusAlertEvaluator.Resolve(Ack(), 77, UnitStatusAlertLevels.Warn, Now).Should().Be(UnitStatusAlertAcknowledgementModes.Acknowledged);
		}

		[Test]
		public void A_new_status_ends_the_acknowledgement()
		{
			UnitStatusAlertEvaluator.Resolve(Ack(unitStateId: 77), 78, UnitStatusAlertLevels.Warn, Now).Should().BeNull();
		}

		[Test]
		public void Escalating_past_the_acknowledged_level_brings_the_alert_back()
		{
			UnitStatusAlertEvaluator.Resolve(Ack(level: UnitStatusAlertLevels.Warn), 77, UnitStatusAlertLevels.Alert, Now).Should().BeNull();
		}

		[Test]
		public void An_alert_level_acknowledgement_covers_the_alert()
		{
			UnitStatusAlertEvaluator.Resolve(Ack(level: UnitStatusAlertLevels.Alert), 77, UnitStatusAlertLevels.Alert, Now)
				.Should().Be(UnitStatusAlertAcknowledgementModes.Acknowledged);
		}

		[Test]
		public void A_mute_without_an_end_lasts_for_the_episode()
		{
			UnitStatusAlertEvaluator.Resolve(Ack(mode: UnitStatusAlertAcknowledgementModes.Muted), 77, UnitStatusAlertLevels.Warn, Now)
				.Should().Be(UnitStatusAlertAcknowledgementModes.Muted);
		}

		[Test]
		public void A_running_mute_is_muted()
		{
			UnitStatusAlertEvaluator.Resolve(Ack(mode: UnitStatusAlertAcknowledgementModes.Muted, mutedUntil: Now.AddMinutes(1)), 77, UnitStatusAlertLevels.Warn, Now)
				.Should().Be(UnitStatusAlertAcknowledgementModes.Muted);
		}

		[Test]
		public void An_expired_mute_falls_back_to_acknowledged()
		{
			UnitStatusAlertEvaluator.Resolve(Ack(mode: UnitStatusAlertAcknowledgementModes.Muted, mutedUntil: Now), 77, UnitStatusAlertLevels.Warn, Now)
				.Should().Be(UnitStatusAlertAcknowledgementModes.Acknowledged);
		}

		[Test]
		public void A_mute_does_not_survive_escalation()
		{
			UnitStatusAlertEvaluator.Resolve(Ack(mode: UnitStatusAlertAcknowledgementModes.Muted), 77, UnitStatusAlertLevels.Alert, Now).Should().BeNull();
		}

		[Test]
		public void A_cleared_acknowledgement_covers_nothing()
		{
			var cleared = Ack();
			cleared.ClearedOn = Now.AddMinutes(-1);

			UnitStatusAlertEvaluator.Resolve(cleared, 77, UnitStatusAlertLevels.Warn, Now).Should().BeNull();
		}

		[Test]
		public void A_unit_with_no_status_record_matches_no_acknowledgement()
		{
			UnitStatusAlertEvaluator.Resolve(Ack(unitStateId: 0), 0, UnitStatusAlertLevels.Warn, Now).Should().BeNull();
		}
	}
}
