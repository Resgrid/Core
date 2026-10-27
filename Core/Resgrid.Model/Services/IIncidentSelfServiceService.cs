using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.IncidentLogistics;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Token-scoped self-service pages (large-incident plan §9.28, D17): a participant opens a link to see their own incident
	/// information — never anyone else's. Only the SHA-256 hash of the token is stored; the token is shown once, when issued. Lodging
	/// and travel detail need a one-time code delivered to the participant when the department's policy says so.
	/// </summary>
	public interface IIncidentSelfServiceService
	{
		/// <summary>Issues a link for one participant and returns the token (shown once, never stored).</summary>
		Task<IncidentSelfServiceIssue> IssueAsync(IncidentActor actor, string participantId, IncidentSelfServicePurposes purposes, CancellationToken cancellationToken = default);

		Task RevokeAsync(IncidentActor actor, string linkId, CancellationToken cancellationToken = default);

		/// <summary>The page for a token, or an unavailable page (unknown, revoked or expired token, module off) — never an error that says which.</summary>
		Task<IncidentSelfServicePage> GetPageAsync(string token, CancellationToken cancellationToken = default);

		/// <summary>Sends a one-time code to the participant's own member account; false when there is nowhere to send it.</summary>
		Task<bool> SendCodeAsync(string token, CancellationToken cancellationToken = default);

		Task<bool> VerifyCodeAsync(string token, string code, CancellationToken cancellationToken = default);
	}
}

namespace Resgrid.Model.IncidentLogistics
{
	public sealed class IncidentSelfServiceIssue
	{
		public string LinkId { get; set; }
		public string Token { get; set; }
		public DateTime ExpiresOn { get; set; }
	}

	/// <summary>What a self-service page shows: the participant's own support information and nothing protected.</summary>
	public sealed class IncidentSelfServicePage
	{
		public bool Available { get; set; }
		public string DepartmentName { get; set; }
		public string IncidentName { get; set; }
		public string ShortCode { get; set; }
		public bool IsExercise { get; set; }
		public string TimeZoneId { get; set; }
		public string ParticipantName { get; set; }
		public int SupportStatus { get; set; }
		public string CheckInCode { get; set; }
		public DateTime? ExpectedArrivalOn { get; set; }
		public DateTime? CheckedInOn { get; set; }
		public List<string> Positions { get; set; } = new List<string>();
		public List<IncidentSelfServiceMeal> Meals { get; set; } = new List<IncidentSelfServiceMeal>();
		public bool LodgingAllowed { get; set; }
		public bool LodgingNeedsCode { get; set; }
		public bool CanSendCode { get; set; }
		public List<IncidentSelfServiceLodging> Lodging { get; set; } = new List<IncidentSelfServiceLodging>();
		public DateTime ExpiresOn { get; set; }
	}

	public sealed class IncidentSelfServiceMeal
	{
		public string SiteName { get; set; }
		public int MealType { get; set; }
		public DateTime WindowStart { get; set; }
		public DateTime WindowEnd { get; set; }
	}

	public sealed class IncidentSelfServiceLodging
	{
		public string PropertyName { get; set; }
		public string Address { get; set; }
		public DateTime FirstNight { get; set; }
		public DateTime LastNight { get; set; }
		public string CheckInHours { get; set; }
	}
}
