namespace Resgrid.Web.Services.Models.v4.UserDefinedFields
{
	/// <summary>Response containing the React Native JSON schema for UDF fields.</summary>
	public class UdfSchemaResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// JSON string produced by IUdfRenderingService.GenerateReactNativeSchema.
		/// Contains definitionId, entityType, version, and the fields array with
		/// type, label, placeholder, validation rules, current value, and options.
		/// </summary>
		public string Data { get; set; }
	}
}

