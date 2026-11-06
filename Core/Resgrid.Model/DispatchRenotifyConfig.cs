using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// The channels a dispatch re-notification can go out on, stored as flags in <see cref="DispatchRenotifyConfig.Channels"/>.
	/// Every one is built (CommunicationService.SendCallRenotificationAsync). While <c>DispatchConfig.RenotifyChannelsLocked</c>
	/// is on, push is the only one a department may use, whatever is stored or posted.
	/// </summary>
	[Flags]
	public enum DispatchRenotifyChannels
	{
		None = 0,
		Push = 1,
		Sms = 2,
		Email = 4,
		Voice = 8,
		/// <summary>Outbound chat platforms (Discord, Slack, Teams) through the chatbot.</summary>
		ChatPlatform = 16
	}

	/// <summary>
	/// ProtoBuf-serialized <see cref="DepartmentSettingTypes.DispatchRenotifyConfig"/> (Dispatch Settings): re-notify personnel
	/// and units that have not acknowledged a dispatch. Any status a person or unit sets after the dispatch, of any kind and with
	/// or without a destination, acknowledges it; statuses Resgrid applies itself (dispatch automation, schedules, maintenance) do
	/// not. A missing row means off.
	/// </summary>
	/// <remarks>
	/// protobuf-net does not write a zero value, so a stored 0 reads back as the initializer below. The effective values are
	/// always read through <see cref="GetEffectiveIntervalMinutes"/>, <see cref="GetEffectiveMaxRetries"/> and
	/// <see cref="GetEffectiveChannels"/>, which apply the system locks, clamp, and fall back to the defaults; every save goes
	/// through <see cref="ApplyLocks"/>.
	/// </remarks>
	[ProtoContract]
	public class DispatchRenotifyConfig
	{
		public const int DefaultIntervalMinutes = 5;
		public const int DefaultMaxRetries = 3;
		public const int MinimumIntervalMinutes = 1;
		public const int MaximumIntervalMinutes = 60;
		public const int MinimumMaxRetries = 1;
		public const int MaximumMaxRetries = 10;

		/// <summary>The only channel while the channels are locked, and the fallback when none is chosen.</summary>
		public const DispatchRenotifyChannels DefaultChannels = DispatchRenotifyChannels.Push;

		/// <summary>The channels a unit can be reached on: the unit apps' push. A unit has no phone, email or chat identity.</summary>
		public const DispatchRenotifyChannels UnitChannels = DispatchRenotifyChannels.Push;

		/// <summary>Every channel, in the order the settings page lists them.</summary>
		public static readonly DispatchRenotifyChannels[] AllChannels =
		{
			DispatchRenotifyChannels.Push, DispatchRenotifyChannels.Sms, DispatchRenotifyChannels.Email,
			DispatchRenotifyChannels.Voice, DispatchRenotifyChannels.ChatPlatform
		};

		/// <summary>All channel flags together; any other bit in a stored or posted value is dropped.</summary>
		public static readonly DispatchRenotifyChannels SupportedChannels = AllChannels.Aggregate(DispatchRenotifyChannels.None, (all, channel) => all | channel);

		[ProtoMember(1)]
		public bool Enabled { get; set; }

		/// <summary>Minutes between re-notifications. Only honoured while the timing options are unlocked.</summary>
		[ProtoMember(2)]
		public int IntervalMinutes { get; set; } = DefaultIntervalMinutes;

		/// <summary>How many re-notifications follow the original dispatch. Only honoured while the timing options are unlocked.</summary>
		[ProtoMember(3)]
		public int MaxRetries { get; set; } = DefaultMaxRetries;

		/// <summary><see cref="DispatchRenotifyChannels"/> flags. Only push is honoured while the channels are locked.</summary>
		[ProtoMember(4)]
		public int Channels { get; set; } = (int)DefaultChannels;

		public int GetEffectiveIntervalMinutes(bool timingLocked)
		{
			if (timingLocked || IntervalMinutes <= 0)
				return DefaultIntervalMinutes;

			return Math.Min(MaximumIntervalMinutes, Math.Max(MinimumIntervalMinutes, IntervalMinutes));
		}

		public int GetEffectiveMaxRetries(bool timingLocked)
		{
			if (timingLocked || MaxRetries <= 0)
				return DefaultMaxRetries;

			return Math.Min(MaximumMaxRetries, Math.Max(MinimumMaxRetries, MaxRetries));
		}

		/// <summary>The channels re-notification goes out on: push only while locked, otherwise the stored ones (push when none).</summary>
		public DispatchRenotifyChannels GetEffectiveChannels(bool channelsLocked)
		{
			if (channelsLocked)
				return DefaultChannels;

			var channels = (DispatchRenotifyChannels)Channels & SupportedChannels;
			return channels == DispatchRenotifyChannels.None ? DefaultChannels : channels;
		}

		/// <summary>
		/// The setting as it may be stored: the defaults for whatever is locked (5 minutes, 3 re-notifications, push only), and
		/// the rest clamped to its limits. Every writer stores this, so a locked option can never be turned on from a request.
		/// </summary>
		public DispatchRenotifyConfig ApplyLocks(bool timingLocked, bool channelsLocked)
		{
			return new DispatchRenotifyConfig
			{
				Enabled = Enabled,
				IntervalMinutes = GetEffectiveIntervalMinutes(timingLocked),
				MaxRetries = GetEffectiveMaxRetries(timingLocked),
				Channels = (int)GetEffectiveChannels(channelsLocked)
			};
		}

		/// <summary>Channel flags from posted checkbox values: only the value of a single listed channel counts; anything else is dropped.</summary>
		public static int ChannelsFrom(IEnumerable<int> values)
		{
			return (int)(values ?? Enumerable.Empty<int>())
				.Select(value => (DispatchRenotifyChannels)value)
				.Where(channel => AllChannels.Contains(channel))
				.Aggregate(DispatchRenotifyChannels.None, (all, channel) => all | channel);
		}
	}
}
