using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Messages;

/// <summary>
/// Gets the messages for a user
/// </summary>
public class GetRecipientsResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public List<RecipientsResultData> Data { get; set; }

	/// <summary>
	/// Default constructor
	/// </summary>
	public GetRecipientsResult()
	{
		Data = new List<RecipientsResultData>();
	}
}

/// <summary>
/// Holds Resgrid message data
/// </summary>
public class RecipientsResultData
{
	/// <summary>
	/// Identifier of the recipient
	/// </summary>
	public string Id { get; set; }

	/// <summary>
	/// Type of the recipient
	/// </summary>
	public string Type { get; set; }

	/// <summary>
	/// The name of the recipient
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Is the recipient selected or not
	/// </summary>
	public bool Selected { get; set; }

	/// <summary>
	/// Unit rows: absolute URL of the department's uploaded icon for the unit's type; Roles rows: the role's uploaded map
	/// icon (a 128 px square PNG, anonymous GET, immutable). Null for other rows and when there is no uploaded icon.
	/// </summary>
	public string TypeIconUrl { get; set; }

	/// <summary>
	/// Unit rows only: built-in icon name the unit's type picked (e.g. "ambulance"); null when none.
	/// </summary>
	public string TypeMapIcon { get; set; }
}
