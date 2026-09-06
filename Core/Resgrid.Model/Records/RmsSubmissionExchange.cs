using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>Append-only delivery journal. Started, Response, and Applied entries share ExchangeId; no response is overwritten.</summary>
	public class RmsSubmissionExchange : IEntity
	{
		public string RmsSubmissionExchangeId { get; set; }
		public int DepartmentId { get; set; }
		public string SubmissionId { get; set; }
		public string RecordId { get; set; }
		public string RevisionId { get; set; }
		public string ExchangeId { get; set; }
		public string Stage { get; set; }
		public string Operation { get; set; }
		public string DestinationIdentity { get; set; }
		public string PayloadChecksum { get; set; }
		public string OutcomeJson { get; set; }
		public string OutcomeChecksum { get; set; }
		public int AttemptNumber { get; set; }
		public DateTime OccurredOn { get; set; }
		public object IdValue { get => RmsSubmissionExchangeId; set => RmsSubmissionExchangeId = (string)value; }
		public string TableName => "RmsSubmissionExchanges";
		public string IdName => "RmsSubmissionExchangeId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
