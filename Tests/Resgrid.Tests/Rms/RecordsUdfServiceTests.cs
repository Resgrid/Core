using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	[TestFixture]
	public class RecordsUdfServiceTests
	{
		private RecordsUdfService _service;
		private Mock<IRmsUdfDefinitionsRepository> _definitions;
		private Mock<IUdfFieldRepository> _fields;
		private Mock<IUdfFieldValueRepository> _values;
		private Mock<IRecordsAuthorizationService> _auth;
		private Mock<IUnitOfWork> _unit;
		private Mock<IDepartmentDataProtectionService> _protection;
		private List<UdfDefinition> _published;
		private List<UdfField> _fieldRows;
		private List<UdfFieldValue> _valueRows;
		private bool _admin, _restricted;
		private const string Key=RmsDefinitionKeys.Training;
		[SetUp]
		public void Setup()
		{
			_published=new(); _fieldRows=new(); _valueRows=new(); _admin=true; _restricted=true;
			_definitions=new(); _fields=new(); _values=new(); _auth=new(); _unit=new();
			_auth.Setup(a=>a.IsActiveMemberAsync("officer",11)).ReturnsAsync(true);
			_auth.Setup(a=>a.IsDepartmentAdminAsync("officer",11)).ReturnsAsync(()=>_admin);
			_auth.Setup(a=>a.HasPermissionAsync("officer",11,It.IsAny<PermissionTypes>())).ReturnsAsync((string u,int d,PermissionTypes p)=>p!=PermissionTypes.ViewRestrictedRecords || _restricted);
			_definitions.Setup(r=>r.GetActiveAsync(It.IsAny<int>(),It.IsAny<string>(),It.IsAny<int>())).ReturnsAsync((int d,string k,int v)=>_published.SingleOrDefault(x=>x.DepartmentId==d && x.RecordDefinitionKey==k && x.RecordDefinitionVersion==v && x.IsActive));
			_definitions.Setup(r=>r.GetScopedAsync(It.IsAny<int>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<int>())).ReturnsAsync((int d,string id,string k,int v)=>_published.SingleOrDefault(x=>x.DepartmentId==d && x.UdfDefinitionId==id && x.RecordDefinitionKey==k && x.RecordDefinitionVersion==v));
			_definitions.Setup(r=>r.DeactivateAsync(It.IsAny<int>(),It.IsAny<string>(),It.IsAny<int>(),It.IsAny<CancellationToken>())).Returns((int d,string k,int v,CancellationToken ct)=>{ foreach(var x in _published.Where(x=>x.DepartmentId==d && x.RecordDefinitionKey==k && x.RecordDefinitionVersion==v)) x.IsActive=false; return Task.CompletedTask; });
			_definitions.Setup(r=>r.InsertAsync(It.IsAny<UdfDefinition>(),It.IsAny<CancellationToken>(),true)).ReturnsAsync((UdfDefinition x,CancellationToken c,bool b)=>{_published.Add(x);return x;});
			_fields.Setup(r=>r.InsertAsync(It.IsAny<UdfField>(),It.IsAny<CancellationToken>(),true)).ReturnsAsync((UdfField x,CancellationToken c,bool b)=>{_fieldRows.Add(x);return x;});
			_fields.Setup(r=>r.GetFieldsByDefinitionIdAsync(It.IsAny<string>())).ReturnsAsync((string id)=>_fieldRows.Where(f=>f.UdfDefinitionId==id));
			_values.Setup(r=>r.GetFieldValuesByEntityAsync(4,It.IsAny<string>(),It.IsAny<string>())).ReturnsAsync((int t,string id,string def)=>_valueRows.Where(v=>v.EntityId==id && v.UdfDefinitionId==def));
			_values.Setup(r=>r.DeleteFieldValuesByEntityAndDefinitionAsync(4,It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>())).ReturnsAsync((int t,string id,string def,CancellationToken c)=>{_valueRows.RemoveAll(v=>v.EntityId==id && v.UdfDefinitionId==def);return true;});
			_values.Setup(r=>r.InsertAsync(It.IsAny<UdfFieldValue>(),It.IsAny<CancellationToken>(),true)).ReturnsAsync((UdfFieldValue x,CancellationToken c,bool b)=>{_valueRows.Add(x);return x;});
			_protection=new();
			_service=new(_definitions.Object,_fields.Object,_values.Object,_auth.Object,Mock.Of<IDepartmentGroupsService>(),_unit.Object,_protection.Object);
		}
		private UdfField Field(string name="score", int? classification=0, int visibility=0) => new() { Name=name,Label="Captured "+name,FieldDataType=(int)UdfFieldDataType.Number,RmsClassification=classification,Visibility=visibility,IsEnabled=true,IsVisibleOnMobile=true,IsVisibleOnReports=true };
		[TestCase(DepartmentDataProtectionState.EnrollmentQueued)]
		[TestCase(DepartmentDataProtectionState.Enabled)]
		[TestCase(DepartmentDataProtectionState.Failed)]
		[TestCase(DepartmentDataProtectionState.Decrypting)]
		public async Task Protection_enrollment_and_operation_fail_closed_before_plaintext_capture_or_value_replacement(DepartmentDataProtectionState state)
		{
			var definition=await Publish(Field());await Save(definition,new(){{definition.Fields.Single().UdfFieldId,"7"}});
			var before=JsonConvert.SerializeObject(_valueRows);var section=await _service.CaptureAsync(11,"record",Key,1,definition.UdfDefinitionId);
			_protection.Setup(p=>p.GetStateAsync(11,true)).ReturnsAsync(state);
			foreach(var action in new Func<Task>[] {
				()=>_service.CaptureAsync(11,"record",Key,1,definition.UdfDefinitionId),
				()=>_service.ProjectAsync(11,"officer",section),
				()=>Save(definition,new(){{definition.Fields.Single().UdfFieldId,"8"}},definition.UdfDefinitionId),
				()=>_service.GetVisibilityLevelAsync(11,"officer") })
				await action.Should().ThrowAsync<InvalidOperationException>();
			JsonConvert.SerializeObject(_valueRows).Should().Be(before);
		}
		private async Task<UdfDefinition> Publish(params UdfField[] fields) => await _service.PublishAsync(11,"officer",Key,1,null,fields.ToList());
		private Task<string> Save(UdfDefinition def, Dictionary<string,string> values=null, string pin=null) => _service.SaveInTransactionAsync(11,"officer","record",Key,1,pin,new RecordUdfInput {DefinitionId=def.UdfDefinitionId,Values=values??new()},CancellationToken.None);
		[Test]
		public async Task Republish_allocates_fresh_field_ids_and_does_not_rewrite_captured_metadata_or_values()
		{
			var original=Field(); original.UdfFieldId="forged-existing-id";
			var first=await Publish(original); first.Fields.Single().UdfFieldId.Should().NotBe(original.UdfFieldId);
			var id=first.Fields.Single().UdfFieldId; await Save(first,new(){{id,"7"}});
			var captured=await _service.CaptureAsync(11,"record",Key,1,first.UdfDefinitionId);
			var snapshot=RecordSnapshotSerializer.Serialize(RecordSnapshotSerializer.Build(new RecordAggregate {Record=new(){DefinitionKey=Key,DefinitionVersion=1},CustomFields=captured}));
			var edited=JsonConvert.DeserializeObject<UdfField>(JsonConvert.SerializeObject(first.Fields.Single())); edited.Label="New label";
			var second=await _service.PublishAsync(11,"officer",Key,1,first.UdfDefinitionId,new(){edited});
			second.Version.Should().Be(2); second.Fields.Single().UdfFieldId.Should().NotBe(id);
			(await _service.CaptureAsync(11,"record",Key,1,first.UdfDefinitionId)).Fields.Single().Value.Should().Be("7");
			snapshot.Should().Contain("Captured score").And.Contain("\"Value\":\"7\"").And.NotContain("New label");
			Func<Task> stale=()=>_service.PublishAsync(11,"officer",Key,1,first.UdfDefinitionId,new(){Field()}); await stale.Should().ThrowAsync<InvalidOperationException>();
		}
		[Test]
		public async Task Foreign_definition_and_form_version_forgery_never_modify_values()
		{
			var def=await Publish(Field()); var before=_valueRows.Count;
			Func<Task> foreign=()=>_service.SaveInTransactionAsync(12,"officer","record",Key,1,def.UdfDefinitionId,null,CancellationToken.None);
			await foreign.Should().ThrowAsync<ArgumentException>();
			Func<Task> wrongType=()=>_service.SaveInTransactionAsync(11,"officer","record",RmsDefinitionKeys.Run,1,def.UdfDefinitionId,null,CancellationToken.None);
			await wrongType.Should().ThrowAsync<ArgumentException>();
			Func<Task> wrongVersion=()=>_service.SaveInTransactionAsync(11,"officer","record",Key,2,def.UdfDefinitionId,null,CancellationToken.None);
			await wrongVersion.Should().ThrowAsync<ArgumentException>(); _valueRows.Count.Should().Be(before);
		}
		[Test]
		public async Task Hidden_and_readonly_values_survive_omission_and_reject_forged_writes()
		{
			var restricted=Field("restricted",1); restricted.DefaultValue="9";
			var readOnly=Field("readonly"); readOnly.IsReadOnly=true; readOnly.DefaultValue="8";
			var def=await Publish(Field(),restricted,readOnly); await Save(def);
			_admin=false;_restricted=false;
			var section=await _service.CaptureAsync(11,"record",Key,1,def.UdfDefinitionId);
			var id=def.Fields.Single(f=>f.Name=="score").UdfFieldId; await Save(def,new(){{id,"2"}},def.UdfDefinitionId);
			_valueRows.Single(v=>v.UdfFieldId==def.Fields.Single(f=>f.Name=="restricted").UdfFieldId).Value.Should().Be("9");
			foreach(var name in new[]{"restricted","readonly"})
			{
				Func<Task> forge=()=>Save(def,new(){{def.Fields.Single(f=>f.Name==name).UdfFieldId,"0"}},def.UdfDefinitionId);
				await forge.Should().ThrowAsync<UnauthorizedAccessException>();
			}
			(await _service.ProjectAsync(11,"officer",section)).Fields.Select(v=>v.Field.Name).Should().NotContain("restricted");
			section.Fields.Should().HaveCount(3); _unit.Verify(u=>u.CommitChanges(),Times.Once,"only publishing owns a commit; aggregate saves remain atomic with the parent");
		}
		[TestCase(0,0,true)]
		[TestCase(1,0,false)]
		[TestCase(null,0,false)]
		[TestCase(0,1,false)]
		[TestCase(0,2,false)]
		public async Task Projection_applies_classification_and_role_to_the_whole_field(int? classification,int visibility,bool visible)
		{
			_admin=false; _restricted=false;
			var section=new RecordUdfSection {Fields=new(){new(){Field=Field("secret",classification,visibility),Value="17"}}};
			var projected=await _service.ProjectAsync(11,"officer",section);
			projected.Fields.Count.Should().Be(visible?1:0); section.Fields.Should().HaveCount(1);
		}
		[Test]
		public async Task Required_fields_allow_a_partial_draft_and_block_finalization_until_completed()
		{
			var field=Field();field.IsRequired=true;var def=await Publish(field);await Save(def);
			var draft=await _service.CaptureAsync(11,"record",Key,1,def.UdfDefinitionId);
			Action finalize=()=>_service.ValidateForFinalization(draft);finalize.Should().Throw<ArgumentException>();
			await Save(def,new(){{def.Fields.Single().UdfFieldId,"3"}},def.UdfDefinitionId);
			_service.ValidateForFinalization(await _service.CaptureAsync(11,"record",Key,1,def.UdfDefinitionId));
		}
		[Test]
		public async Task Publish_requires_live_department_admin_and_explicit_classification()
		{
			_admin=false;Func<Task> unauthorized=()=>Publish(Field());await unauthorized.Should().ThrowAsync<UnauthorizedAccessException>();
			_admin=true;Func<Task> unclassified=()=>Publish(Field(classification:null));await unclassified.Should().ThrowAsync<ArgumentException>();
			_published.Should().BeEmpty();
		}
		[Test]
		public async Task Generic_non_record_publication_cannot_move_or_mutate_an_RMS_field_by_submitted_id()
		{
			var published=await Publish(Field());var existing=_fieldRows.Single();var original=JsonConvert.SerializeObject(existing);
			var definitions=new Mock<IUdfDefinitionRepository>();
			definitions.Setup(d=>d.SaveOrUpdateAsync(It.IsAny<UdfDefinition>(),It.IsAny<CancellationToken>(),It.IsAny<bool>())).ReturnsAsync((UdfDefinition d,CancellationToken c,bool b)=>{d.UdfDefinitionId="ordinary-new-definition";return d;});
			UdfField saved=null;
			_fields.Setup(f=>f.SaveOrUpdateAsync(It.IsAny<UdfField>(),It.IsAny<CancellationToken>(),It.IsAny<bool>())).ReturnsAsync((UdfField f,CancellationToken c,bool b)=>{saved=f;return f;});
			var generic=new Resgrid.Services.UserDefinedFieldsService(definitions.Object,_fields.Object,_values.Object,_unit.Object);
			var forged=Field();forged.UdfFieldId=existing.UdfFieldId;forged.Label="Replace historical RMS label";
			await generic.SaveDefinitionAsync(12,(int)UdfEntityType.Call,new(){forged},"attacker");
			saved.UdfFieldId.Should().BeNull("the repository must allocate a new ID instead of updating the submitted RMS row");
			JsonConvert.SerializeObject(existing).Should().Be(original);forged.UdfFieldId.Should().Be(existing.UdfFieldId);
			Func<Task> wrongRoute=()=>generic.GetActiveDefinitionAsync(11,4);await wrongRoute.Should().ThrowAsync<UnauthorizedAccessException>();
		}
		[Test]
		public async Task Required_readonly_defaults_and_mobile_or_layout_visibility_use_the_published_settings()
		{
			var field=Field();field.IsVisibleOnMobile=false;field.IsVisibleOnReports=false;
			var def=await Publish(field);await Save(def,new(){{def.Fields.Single().UdfFieldId,"4"}});
			var section=await _service.CaptureAsync(11,"record",Key,1,def.UdfDefinitionId);
			(await _service.ProjectAsync(11,"officer",section,mobile:true)).Fields.Should().BeEmpty();
			(await _service.ProjectAsync(11,"officer",section,reportLayout:true)).Fields.Should().BeEmpty();
			(await _service.ProjectAsync(11,"officer",section)).Fields.Single().Value.Should().Be("4","the complete department record ignores optional layout/mobile omissions");
		}
	}
}
