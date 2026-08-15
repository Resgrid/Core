using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// How long a unit may sit in a status before the board flags it.
	/// </summary>
	/// <remarks>
	/// Keyed by <see cref="ActionBaseTypes"/> rather than by the department's own status ids, because
	/// departments name and colour their statuses freely: "a unit has been dispatched for more than
	/// four minutes without reporting that it has departed" is a statement about the *meaning* of the
	/// status, not its label.
	/// </remarks>
	[ProtoContract]
	public class UnitStatusThreshold
	{
		/// <summary>The <see cref="ActionBaseTypes"/> value this threshold applies to.</summary>
		[ProtoMember(1)]
		public int BaseType { get; set; }

		/// <summary>Seconds after which the unit is highlighted. 0 disables the warning.</summary>
		[ProtoMember(2)]
		public int WarnSeconds { get; set; }

		/// <summary>
		/// Seconds after which the unit is escalated to a high-priority alert. 0 disables it. A value at
		/// or below <see cref="WarnSeconds"/> is treated as "alert only" -- see
		/// <see cref="UnitStatusThresholds.Normalize"/>.
		/// </summary>
		[ProtoMember(3)]
		public int AlertSeconds { get; set; }

		/// <summary>True when this row would never fire and is not worth storing.</summary>
		public bool IsEmpty => WarnSeconds <= 0 && AlertSeconds <= 0;
	}

	/// <summary>
	/// A department's time-in-status thresholds, driving the board's "this unit has been sitting here
	/// too long" highlighting.
	/// </summary>
	/// <remarks>
	/// No configuration means no highlighting at all -- exactly how the board behaved before this
	/// existed, so a department that never opens the screen sees no change.
	/// </remarks>
	[ProtoContract]
	public class UnitStatusThresholds
	{
		[ProtoMember(1)]
		public List<UnitStatusThreshold> Thresholds { get; set; } = new List<UnitStatusThreshold>();

		public bool IsEmpty => Thresholds == null || Thresholds.Count == 0;

		public UnitStatusThreshold Find(int baseType) => Thresholds?.FirstOrDefault(x => x.BaseType == baseType);

		/// <summary>
		/// Drops rows that would never fire, collapses duplicates, and clamps an alert that is not
		/// actually later than its warning.
		/// </summary>
		public UnitStatusThresholds Normalize()
		{
			Thresholds = (Thresholds ?? new List<UnitStatusThreshold>())
				.Where(x => x != null)
				.GroupBy(x => x.BaseType)
				.Select(g => g.Last())
				.Select(x => new UnitStatusThreshold
				{
					BaseType = x.BaseType,
					WarnSeconds = Math.Max(0, x.WarnSeconds),
					// An alert that fires no later than the warning is not an escalation; treating it as
					// alert-only keeps the two levels meaningful instead of showing both at once.
					AlertSeconds = Math.Max(0, x.AlertSeconds)
				})
				.Select(x =>
				{
					if (x.AlertSeconds > 0 && x.WarnSeconds > 0 && x.AlertSeconds <= x.WarnSeconds)
						x.WarnSeconds = 0;

					return x;
				})
				.Where(x => !x.IsEmpty)
				.OrderBy(x => x.BaseType)
				.ToList();

			return this;
		}
	}
}
