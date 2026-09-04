using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	public static class NerisEnvironments
	{
		public const string Production = "production";
		public const string Sandbox = "sandbox";
	}

	public static class NerisGrantTypes
	{
		public const string ClientCredentials = "client_credentials";
		public const string Password = "password";
	}

	/// <summary>
	/// A department's NERIS reporting profile (registry M0166): its NERIS entity ID, environment, and integration
	/// credential (encrypted per department through IEncryptionService). Records is useful without one (plan
	/// decision 21); the profile only turns on submission.
	/// </summary>
	public class RmsNerisProfile : IEntity
	{
		public string RmsNerisProfileId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		/// <summary>NERIS entity ID (neris_id_entity) the department files under.</summary>
		public string NerisEntityId { get; set; }
		public string EntityName { get; set; }
		/// <summary><see cref="NerisEnvironments"/>.</summary>
		public string Environment { get; set; }
		/// <summary>Optional base URL override (sandbox or a test double); the config default otherwise.</summary>
		public string BaseUrlOverride { get; set; }
		/// <summary><see cref="NerisGrantTypes"/>.</summary>
		public string GrantType { get; set; }
		/// <summary>Department-encrypted JSON: {"client_id","client_secret"} or {"username","password"}.</summary>
		public string EncryptedCredentialJson { get; set; }
		/// <summary>Pinned contract version the department maps against.</summary>
		public string ContractVersion { get; set; }
		public bool AutoSubmitOnFinalize { get; set; }
		public bool IsEnabled { get; set; }
		public DateTime? LastTokenIssuedOn { get; set; }
		public DateTime? LastSuccessfulCallOn { get; set; }
		public string LastError { get; set; }
		public string UpdatedByUserId { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public bool HasCredential => !string.IsNullOrWhiteSpace(EncryptedCredentialJson);

		public object IdValue { get => RmsNerisProfileId; set => RmsNerisProfileId = (string)value; }
		public string TableName => "RmsNerisProfiles";
		public string IdName => "RmsNerisProfileId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "HasCredential" };
	}

	/// <summary>One code of one NERIS value set at one contract version (seeded from the pinned snapshot; global reference data).</summary>
	public class RmsNerisValueSetEntry : IEntity
	{
		public int RmsNerisValueSetEntryId { get; set; }
		public string ContractVersion { get; set; }
		/// <summary>Snapshot set key, e.g. incident_type, action_tactic, aid_type.</summary>
		public string SetKey { get; set; }
		public string Code { get; set; }
		public string Label { get; set; }
		public string ParentCode { get; set; }
		public int SortOrder { get; set; }
		public bool IsRetired { get; set; }
		public DateTime CreatedOn { get; set; }

		public object IdValue { get => RmsNerisValueSetEntryId; set => RmsNerisValueSetEntryId = (int)value; }
		public string TableName => "RmsNerisValueSets";
		public string IdName => "RmsNerisValueSetEntryId";
		public int IdType => 0;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public static class NerisCrosswalkSources
	{
		public const string CallType = "CallType";
		public const string CallPriority = "CallPriority";
		public const string UnitType = "UnitType";
	}

	/// <summary>Department-owned crosswalk from a Resgrid/CAD code to a NERIS value; the original code is always kept beside the mapped one.</summary>
	public class RmsNerisCrosswalk : IEntity
	{
		public string RmsNerisCrosswalkId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string ContractVersion { get; set; }
		public string SetKey { get; set; }
		/// <summary><see cref="NerisCrosswalkSources"/>.</summary>
		public string LocalSource { get; set; }
		public string LocalCode { get; set; }
		public string NerisCode { get; set; }
		public bool IsDefault { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsNerisCrosswalkId; set => RmsNerisCrosswalkId = (string)value; }
		public string TableName => "RmsNerisCrosswalks";
		public string IdName => "RmsNerisCrosswalkId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
