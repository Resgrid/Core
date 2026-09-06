using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	public sealed class RecordsUdfService : IRecordsUdfService
	{
		private readonly IRmsUdfDefinitionsRepository _definitions;
		private readonly IUdfFieldRepository _fields;
		private readonly IUdfFieldValueRepository _values;
		private readonly IRecordsAuthorizationService _auth;
		private readonly IDepartmentGroupsService _groups;
		private readonly IUnitOfWork _unit;
		private readonly IDepartmentDataProtectionService _protection;
		public RecordsUdfService(IRmsUdfDefinitionsRepository definitions, IUdfFieldRepository fields, IUdfFieldValueRepository values,
			IRecordsAuthorizationService auth, IDepartmentGroupsService groups, IUnitOfWork unit, IDepartmentDataProtectionService protection)
		{ _definitions=definitions; _fields=fields; _values=values; _auth=auth; _groups=groups; _unit=unit; _protection=protection; }
		private async Task RequireSupportedProtectionAsync(int department)
		{
			if (await _protection.GetStateAsync(department, true) != DepartmentDataProtectionState.Disabled)
				throw new InvalidOperationException("RMS custom fields require the protected record read/write integration before use during Advanced Data Protection enrollment or operation.");
		}
		private static T Copy<T>(T item) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(item));
		private async Task RequireDesigner(int department, string user, string key, int version)
		{
			await RequireSupportedProtectionAsync(department);
			if (!await _auth.IsDepartmentAdminAsync(user, department) || !await _auth.HasPermissionAsync(user, department, PermissionTypes.ManageRecordDefinitions)) throw new UnauthorizedAccessException();
			if (version != RmsDefinitionKeys.LockedDefinitionVersion || !(RmsDefinitionKeys.LockedTypes.ContainsKey(key ?? "") || key == RmsDefinitionKeys.NerisIncidentReport)) throw new ArgumentException("Choose a published record definition and version.");
		}
		public async Task<UdfDefinition> GetForDesignerAsync(int departmentId, string userId, string key, int version)
		{
			await RequireDesigner(departmentId,userId,key,version);
			var definition=Copy(await _definitions.GetActiveAsync(departmentId,key,version));
			if (definition != null) definition.Fields=(await _fields.GetFieldsByDefinitionIdAsync(definition.UdfDefinitionId)).Select(Copy).ToList();
			await RequireDesigner(departmentId,userId,key,version); return definition;
		}
		public async Task<UdfDefinition> PublishAsync(int departmentId, string userId, string key, int version, string expectedDefinitionId, List<UdfField> fields, CancellationToken ct=default)
		{
			await RequireDesigner(departmentId,userId,key,version);
			fields=Copy(fields ?? new List<UdfField>());
			if (fields.Count>100 || fields.Any(f=>f==null)) throw new ArgumentException("A record extension supports at most 100 fields.");
			var errors=UdfValidationHelper.ValidateFieldNamesUnique(fields);
			foreach(var field in fields)
			{
				if (string.IsNullOrWhiteSpace(field.Label) || field.Label.Length>200 || field.Name.Length>200 || field.Description?.Length>500 || field.Placeholder?.Length>200 || field.GroupName?.Length>100 || field.DefaultValue?.Length>16000 || field.ValidationRules?.Length>16000) errors.Add("A field label or setting is missing or too long.");
				if (!Enum.IsDefined(typeof(UdfFieldDataType),field.FieldDataType) || field.Visibility<0 || field.Visibility>2 || field.RmsClassification is not (0 or 1)) errors.Add("Choose a supported type, visibility and explicit information classification.");
				if (!string.IsNullOrWhiteSpace(field.ValidationRules))
				{
					var rules=JsonConvert.DeserializeObject<UdfValidationRules>(field.ValidationRules) ?? throw new ArgumentException("Validation rules are invalid.");
					if (rules.Regex != null) _ = new System.Text.RegularExpressions.Regex(rules.Regex, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(250));
				}
				var optional=Copy(field); optional.IsRequired=false; errors.AddRange(UdfValidationHelper.ValidateFieldValue(optional,field.DefaultValue));
				if (field.IsRequired && field.IsReadOnly && string.IsNullOrWhiteSpace(field.DefaultValue)) errors.Add("A required read-only field needs a valid default value.");
			}
			if (errors.Count>0) throw new ArgumentException(string.Join(" ",errors.Distinct()));
			_unit.CreateOrGetConnection();
			try
			{
				await _definitions.LockDepartmentAsync(departmentId,ct);
				var active=await _definitions.GetActiveAsync(departmentId,key,version);
				if ((active?.UdfDefinitionId ?? "") != (expectedDefinitionId ?? "")) throw new InvalidOperationException("This form was republished. Reload before publishing changes.");
				await RequireDesigner(departmentId,userId,key,version);
				await _definitions.DeactivateAsync(departmentId,key,version,ct);
				var definition=new UdfDefinition { UdfDefinitionId=Guid.NewGuid().ToString(), DepartmentId=departmentId, EntityType=(int)UdfEntityType.Record, RecordDefinitionKey=key, RecordDefinitionVersion=version, Version=(active?.Version??0)+1, IsActive=true, CreatedBy=userId, CreatedOn=DateTime.UtcNow };
				await _definitions.InsertAsync(definition,ct,true);
				for(var i=0;i<fields.Count;i++)
				{
					var field=fields[i]; field.UdfFieldId=Guid.NewGuid().ToString(); field.UdfDefinitionId=definition.UdfDefinitionId; field.SortOrder=i;
					await _fields.InsertAsync(field,ct,true);
				}
				_unit.CommitChanges(); definition.Fields=fields; return definition;
			}
			catch { _unit.DiscardChanges(); throw; }
		}
		public async Task<RecordUdfSection> CaptureAsync(int departmentId, string recordId, string key, int version, string definitionId)
		{
			if (string.IsNullOrWhiteSpace(definitionId)) return null;
			await RequireSupportedProtectionAsync(departmentId);
			var definition=await _definitions.GetScopedAsync(departmentId,definitionId,key,version) ?? throw new InvalidOperationException("The captured custom-field definition is unavailable.");
			var fields=(await _fields.GetFieldsByDefinitionIdAsync(definitionId)).Where(f=>f.IsEnabled).OrderBy(f=>f.SortOrder).ToList();
			var values=(await _values.GetFieldValuesByEntityAsync((int)UdfEntityType.Record,recordId,definitionId)).ToDictionary(v=>v.UdfFieldId,v=>v.Value,StringComparer.Ordinal);
			return new RecordUdfSection { DefinitionId=definitionId,RecordDefinitionKey=key,RecordDefinitionVersion=version,ExtensionVersion=definition.Version,
				Fields=fields.Select(f=>new RecordUdfField {Field=Copy(f),Value=values.TryGetValue(f.UdfFieldId,out var value)?value:f.DefaultValue}).ToList() };
		}
		public async Task<RecordUdfSection> GetNewFormAsync(int departmentId, string userId, string key, int version)
		{
			if (!await _auth.HasPermissionAsync(userId,departmentId,PermissionTypes.CreateRecord)) throw new UnauthorizedAccessException();
			var definition=await _definitions.GetActiveAsync(departmentId,key,version);
			var section=definition==null?null:await CaptureAsync(departmentId,null,key,version,definition.UdfDefinitionId);
			return await ProjectAsync(departmentId,userId,section);
		}
		public async Task<RecordUdfSection> ProjectAsync(int departmentId, string userId, RecordUdfSection section, bool mobile=false, bool reportLayout=false)
		{
			if (section==null) return null;
			await RequireSupportedProtectionAsync(departmentId);
			if (!await _auth.IsActiveMemberAsync(userId,departmentId)) throw new UnauthorizedAccessException();
			var restricted=await _auth.HasPermissionAsync(userId,departmentId,PermissionTypes.ViewRestrictedRecords);
			var admin=await _auth.IsDepartmentAdminAsync(userId,departmentId);
			var group=(await _groups.GetGroupForUserAsync(userId,departmentId))?.IsUserGroupAdmin(userId)==true;
			var projected=Copy(section);
			// Intersect the initial and final live grants; role changes during projection cannot reveal a value.
			restricted = restricted && await _auth.HasPermissionAsync(userId,departmentId,PermissionTypes.ViewRestrictedRecords);
			admin = admin && await _auth.IsDepartmentAdminAsync(userId,departmentId);
			group = group && (await _groups.GetGroupForUserAsync(userId,departmentId))?.IsUserGroupAdmin(userId)==true;
			if (!await _auth.IsActiveMemberAsync(userId,departmentId)) throw new UnauthorizedAccessException();
			projected.Fields=projected.Fields.Where(v=>CanSee(v.Field,restricted,admin,group) && (!mobile || v.Field.IsVisibleOnMobile) && (!reportLayout || v.Field.IsVisibleOnReports)).ToList();
			return projected;
		}
		public async Task<int> GetVisibilityLevelAsync(int departmentId, string userId)
		{
			await RequireSupportedProtectionAsync(departmentId);
			if (!await _auth.IsActiveMemberAsync(userId,departmentId)) throw new UnauthorizedAccessException();
			if (await _auth.IsDepartmentAdminAsync(userId,departmentId)) return 2;
			return (await _groups.GetGroupForUserAsync(userId,departmentId))?.IsUserGroupAdmin(userId)==true ? 1 : 0;
		}
		public static bool CanSee(UdfField field, bool restricted, bool admin, bool group) => field!=null && (field.RmsClassification==0 || restricted) && (field.Visibility==0 || field.Visibility==1 && (admin||group) || field.Visibility==2 && admin);
		public async Task<string> SaveInTransactionAsync(int departmentId, string userId, string recordId, string key, int version, string pinnedDefinitionId, RecordUdfInput input, CancellationToken ct)
		{
			// The calling aggregate owns both the transaction and header version fence. Never commit it here.
			await _definitions.GuardRecordAsync(departmentId,recordId,ct);
			var definition=pinnedDefinitionId==null ? await _definitions.GetActiveAsync(departmentId,key,version) : await _definitions.GetScopedAsync(departmentId,pinnedDefinitionId,key,version);
			if (definition==null)
			{
				if (pinnedDefinitionId!=null || input?.DefinitionId!=null || input?.Values?.Count>0) throw new ArgumentException("The custom-field definition does not match this record.");
				return null;
			}
			if (input!=null && input.DefinitionId!=definition.UdfDefinitionId) throw new ArgumentException("This record uses a different custom-field version. Reload its form.");
			var section=await CaptureAsync(departmentId,recordId,key,version,definition.UdfDefinitionId);
			var visible=await ProjectAsync(departmentId,userId,section);
			var allowed=visible.Fields.ToDictionary(f=>f.Field.UdfFieldId);
			foreach(var value in input?.Values ?? new Dictionary<string,string>())
			{
				if (!allowed.TryGetValue(value.Key,out var field) || field.Field.IsReadOnly) throw new UnauthorizedAccessException("A custom field cannot be edited under your current permissions.");
				if (value.Value?.Length>16000) throw new ArgumentException("A custom-field value is too long.");
				var optional=Copy(field.Field); optional.IsRequired=false;
				var errors=UdfValidationHelper.ValidateFieldValue(optional,value.Value); if(errors.Count>0) throw new ArgumentException(string.Join(" ",errors));
				section.Fields.Single(f=>f.Field.UdfFieldId==value.Key).Value=value.Value;
			}
			await ReplaceValues(departmentId,recordId,section,userId,ct); return definition.UdfDefinitionId;
		}
		public async Task RestoreInTransactionAsync(int departmentId, string recordId, string key, int version, RecordUdfSection section, string userId, CancellationToken ct)
		{
			await _definitions.GuardRecordAsync(departmentId,recordId,ct);
			if (section!=null && (section.RecordDefinitionKey!=key || section.RecordDefinitionVersion!=version || await _definitions.GetScopedAsync(departmentId,section.DefinitionId,key,version)==null)) throw new InvalidOperationException("The revision custom-field definition is invalid.");
			await _definitions.DeleteRecordValuesAsync(departmentId,recordId,ct);
			if (section!=null) await ReplaceValues(departmentId,recordId,section,userId,ct);
		}
		private async Task ReplaceValues(int department, string recordId, RecordUdfSection section, string user, CancellationToken ct)
		{
			await RequireSupportedProtectionAsync(department);
			if (section.Fields.Any(f=>ProtectedDataEnvelope.HasEnvelopePrefix(f.Value) || f.Value==ProtectedDataEnvelope.RedactionValue)) throw new InvalidOperationException("Protected custom-field values must retain their original protected identity.");
			await _values.DeleteFieldValuesByEntityAndDefinitionAsync((int)UdfEntityType.Record,recordId,section.DefinitionId,ct);
			foreach(var field in section.Fields)
				await _values.InsertAsync(new UdfFieldValue { UdfFieldValueId=Guid.NewGuid().ToString(), UdfFieldId=field.Field.UdfFieldId, UdfDefinitionId=section.DefinitionId, EntityId=recordId,EntityType=(int)UdfEntityType.Record,Value=field.Value,CreatedOn=DateTime.UtcNow,CreatedBy=user },ct,true);
		}
		public void ValidateForFinalization(RecordUdfSection section)
		{
			var errors=(section?.Fields ?? new List<RecordUdfField>()).SelectMany(f=>UdfValidationHelper.ValidateFieldValue(f.Field,f.Value)).ToList();
			if (errors.Count>0) throw new ArgumentException("Custom fields must be completed before finalization. An authorized editor must complete any required fields outside your access.");
		}
	}
}
