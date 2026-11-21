using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class RouteInstance : IEntity
	{
		public string RouteInstanceId { get; set; }

		public string RoutePlanId { get; set; }

		public string RouteScheduleId { get; set; }

		public int UnitId { get; set; }

		public int DepartmentId { get; set; }

		public int Status { get; set; }

		public string StartedByUserId { get; set; }

		public DateTime? ScheduledStartOn { get; set; }

		public DateTime? ActualStartOn { get; set; }

		public DateTime? ActualEndOn { get; set; }

		public string EndedByUserId { get; set; }

		public double? TotalDistanceMeters { get; set; }

		public double? TotalDurationSeconds { get; set; }

		public string ActualRouteGeometry { get; set; }

		/// <summary>The plan's normalized route at start, so later plan edits do not move the line under a running unit (M0308).</summary>
		public string PlannedDirectionsJson { get; set; }

		public DateTime? LastRoutedOn { get; set; }

		public int StopsCompleted { get; set; }

		public int StopsTotal { get; set; }

		public string Notes { get; set; }

		public DateTime AddedOn { get; set; }

		/// <summary>The call this run was started for (M0322, <see cref="Call.RoutePlanId"/>); null for an ordinary route run.</summary>
		public int? CallId { get; set; }

		/// <summary>
		/// When the unit reached the plan's start point (M0322). A call route's live directions lead to the plan start first until this is
		/// set (or a stop has been worked); ordinary runs start where the unit is and never set it.
		/// </summary>
		public DateTime? StartReachedOn { get; set; }

		[NotMapped]
		public string TableName => "RouteInstances";

		[NotMapped]
		public string IdName => "RouteInstanceId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RouteInstanceId; }
			set { RouteInstanceId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
