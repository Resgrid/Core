using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Tests.Helpers
{
	public static class WorkflowHelpers
	{
		public static Workflow CreateTestWorkflow(
			int departmentId = 1,
			WorkflowTriggerEventType triggerType = WorkflowTriggerEventType.CallAdded)
		{
			var workflowId = Guid.NewGuid().ToString();
			return new Workflow
			{
				WorkflowId             = workflowId,
				DepartmentId           = departmentId,
				Name                   = "Test Workflow",
				Description            = "A test workflow for unit tests",
				TriggerEventType       = (int)triggerType,
				IsEnabled              = true,
				MaxRetryCount          = 3,
				RetryBackoffBaseSeconds = 5,
				CreatedByUserId        = "test-user-id",
				CreatedOn              = DateTime.UtcNow.AddDays(-1),
				Steps                  = new List<WorkflowStep>
				{
					CreateTestWorkflowStep(workflowId)
				}
			};
		}

		public static WorkflowStep CreateTestWorkflowStep(
			string workflowId = "test-workflow-id",
			WorkflowActionType actionType = WorkflowActionType.SendEmail)
		{
			return new WorkflowStep
			{
				WorkflowStepId       = Guid.NewGuid().ToString(),
				WorkflowId           = workflowId,
				ActionType           = (int)actionType,
				StepOrder            = 1,
				OutputTemplate       = "Hello {{ department.name }}, call: {{ call.name }}",
				ActionConfig         = "{\"to\":\"test@example.com\",\"subject\":\"Test Alert\"}",
				IsEnabled            = true
			};
		}

		public static WorkflowCredential CreateTestWorkflowCredential(
			int departmentId = 1,
			WorkflowCredentialType type = WorkflowCredentialType.Smtp)
		{
			return new WorkflowCredential
			{
				WorkflowCredentialId = Guid.NewGuid().ToString(),
				DepartmentId         = departmentId,
				Name                 = "Test SMTP Credential",
				CredentialType       = (int)type,
				EncryptedData        = "ENCRYPTED_BLOB",
				CreatedByUserId      = "test-user-id",
				CreatedOn            = DateTime.UtcNow.AddDays(-1)
			};
		}

		public static WorkflowRun CreateTestWorkflowRun(string workflowId = "test-workflow-id")
		{
			return new WorkflowRun
			{
				WorkflowRunId    = Guid.NewGuid().ToString(),
				WorkflowId       = workflowId,
				DepartmentId     = 1,
				Status           = (int)WorkflowRunStatus.Pending,
				TriggerEventType = (int)WorkflowTriggerEventType.CallAdded,
				InputPayload     = "{\"call\":{\"callId\":1,\"name\":\"Structure Fire\"}}",
				StartedOn        = DateTime.UtcNow,
				QueuedOn         = DateTime.UtcNow,
				AttemptNumber    = 1
			};
		}

		public static Call CreateTestCall()
		{
			return new Call
			{
				CallId            = 1,
				DepartmentId      = 1,
				Name              = "Structure Fire",
				NatureOfCall      = "Residential structure fire with possible entrapment",
				Notes             = "Caller reports smoke visible from street",
				Address           = "123 Main St, Springfield, IL 62701",
				GeoLocationData   = "39.7817,-89.6501",
				Type              = "Structure Fire",
				IncidentNumber    = "INC-2026-001",
				Priority          = 3,
				IsCritical        = true,
				State             = 0,
				CallSource        = 0,
				LoggedOn          = DateTime.UtcNow.AddMinutes(-30),
				ReportingUserId   = "test-user-id",
				ContactName       = "Jane Doe",
				ContactNumber     = "555-0100",
				W3W               = "///word.word.word"
			};
		}

		public static Note CreateTestNote()
		{
			return new Note
			{
				NoteId      = 1,
				DepartmentId = 1,
				Title       = "Test Note",
				Body        = "This is the body of a test note.",
				Color       = "yellow",
				Category    = "Operations",
				IsAdminOnly = false,
				AddedOn     = DateTime.UtcNow.AddHours(-2),
				UserId      = "test-user-id"
			};
		}

		public static Document CreateTestDocument()
		{
			return new Document
			{
				DepartmentId = 1,
				Name         = "SOG-001",
				Category     = "SOGs",
				Description  = "Standard Operating Guideline #1",
				Type         = "PDF",
				Filename     = "SOG-001.pdf",
				AdminsOnly   = false,
				AddedOn      = DateTime.UtcNow.AddDays(-3),
				UserId       = "test-user-id",
				Data         = Array.Empty<byte>()
			};
		}

		public static DepartmentGroup CreateTestDepartmentGroup()
		{
			return new DepartmentGroup
			{
				DepartmentGroupId = 1,
				DepartmentId      = 1,
				Name              = "Station 1",
				Type              = 1,
				DispatchEmail     = "station1@dept.example.com",
				Latitude          = "39.7817",
				Longitude         = "-89.6501"
			};
		}

		public static UserProfile CreateTestUserProfile(string userId = "test-user-id")
		{
			return new UserProfile
			{
				UserId               = userId,
				FirstName            = "John",
				LastName             = "Smith",
				MobileNumber         = "555-0199",
				HomeNumber           = "555-0198",
				IdentificationNumber = "FF-001",
				TimeZone             = "Eastern Standard Time"
			};
		}

		public static Department CreateTestDepartmentWithAddress()
		{
			return new Department
			{
				DepartmentId = 1,
				Name         = "Sample Fire Department",
				Code         = "SFD1",
				TimeZone     = "Eastern Standard Time",
				CreatedOn    = DateTime.UtcNow.AddYears(-2),
				Address      = new Address
				{
					Address1   = "100 Fire Station Road",
					City       = "Springfield",
					State      = "IL",
					PostalCode = "62701",
					Country    = "US"
				}
			};
		}

		public static Training CreateTestTraining()
		{
			return new Training
			{
				TrainingId        = 1,
				DepartmentId      = 1,
				Name              = "Hazmat Awareness",
				Description       = "Annual Hazmat Awareness training",
				MinimumScore      = 80.0,
				CreatedOn         = DateTime.UtcNow.AddDays(-7),
				ToBeCompletedBy   = DateTime.UtcNow.AddDays(30),
				CreatedByUserId   = "test-user-id"
			};
		}

		public static PersonnelCertification CreateTestCertification()
		{
			return new PersonnelCertification
			{
				PersonnelCertificationId = 1,
				DepartmentId             = 1,
				UserId                   = "test-user-id",
				Name                     = "EMT-Basic",
				Number                   = "EMT-12345",
				Type                     = "Medical",
				Area                     = "EMS",
				IssuedBy                 = "State EMS Office",
				ExpiresOn                = DateTime.UtcNow.AddDays(14),
				RecievedOn               = DateTime.UtcNow.AddYears(-2)
			};
		}
	}
}


