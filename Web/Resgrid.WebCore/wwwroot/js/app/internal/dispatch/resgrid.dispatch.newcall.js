
var resgrid;
(function (resgrid) {
	var dispatch;
	(function (dispatch) {
		var newcall;
		(function (newcall) {
			$(document).ready(function () {
				marker = null;
				userSuppliedAddress = false;
				resgrid.dispatch.newcall.protocolCount = 0;
				resgrid.dispatch.newcall.protocolData = {};

				$("#NatureOfCall").kendoEditor();
				$("#CallNotes").kendoEditor();
				$("#Call_Address").bind("keypress", function (event) {
					if (event.keyCode == 13) {
						$("#searchButton").click();
						return false;
					}

					userSuppliedAddress = true;
				});
				$("#What3Word").bind("keypress", function (event) {
					if (event.keyCode == 13) {
						$("#findw3wButton").click();
						return false;
					}
				});

				$("#CallPriority").change(function () {
					checkForProtocols();
				});

				$("#Call_Type").change(function () {
					checkForProtocols();
                });

                var newCallForm = $('#fb-template').formRender({
                    dataType: 'json',
                    formData: newCallFormData
                });

                $("#saveNewCallFrom").click(function (evt) {
                    var data = JSON.stringify(newCallForm.userData);
                    $("#Call_CallFormData").val(data);
                });

                //$('#UnitCustomStatesId').select2();
                //$('#CallTemplateId').select2();

                //$.fn.modal.Constructor.prototype._enforceFocus = function () { };
                $("#CallTemplateId").select2({
                    dropdownParent: $("#callQuickTemplateWindow")
                });

				var mapCenter = new google.maps.LatLng(centerLat, centerLng);
				var mapOptions = {
					zoom: 11,
					center: mapCenter,
					scrollwheel: false
				};
				map = new google.maps.Map(document.getElementById('callMap'), mapOptions);
				google.maps.event.addListener(map, 'click', function (clickEvent) {
					if (marker)
						marker.setMap(null);
					marker = new google.maps.Marker({
						position: clickEvent.latLng,
						map: map,
						title: 'Call Location',
						animation: google.maps.Animation.DROP,
						draggable: true,
						bounds: false
					});
					$("#Latitude").val(clickEvent.latLng.lat().toString());
					$("#Longitude").val(clickEvent.latLng.lng().toString());
					refreshPersonnelGrid();
					google.maps.event.addListener(marker, 'dragend', function () {
						findLocation(marker.getPosition());
					});
					var geocoder = new google.maps.Geocoder();
					geocoder.geocode({
						latLng: clickEvent.latLng
					}, function (results, status) {
						if (status == google.maps.GeocoderStatus.OK) {
							if (!userSuppliedAddress) {
								$("#Call_Address").val(results[0].formatted_address);
							}
						}
						else {
							alert("Geocode was not successful for the following reason: " + status);
						}
					});
				});
				navigator.geolocation.getCurrentPosition(foundLocation, noLocation, { timeout: 10000 });
				$("#searchButton").click(function (evt) {
					var where = jQuery.trim($("#Call_Address").val());
					if (where.length < 1)
						return;
					var geocoder = new google.maps.Geocoder();
					geocoder.geocode({ 'address': where }, function (results, status) {
						if (status == google.maps.GeocoderStatus.OK) {
							map.setCenter(results[0].geometry.location);
							$("#Latitude").val(results[0].geometry.location.lat().toString());
							$("#Longitude").val(results[0].geometry.location.lng().toString());
							refreshPersonnelGrid();
							if (marker)
								marker.setMap(null);
							marker = new google.maps.Marker({
								position: results[0].geometry.location,
								map: map,
								title: 'Call Location',
								animation: google.maps.Animation.DROP,
								draggable: true,
								bounds: false
							});
						}
						else {
							alert("Geocode was not successful for the following reason: " + status);
						}
					});
					evt.preventDefault();
				});
				$("#findw3wButton").click(function (evt) {
					var word = jQuery.trim($("#What3Word").val());
					if (word.length < 1)
						return;
					$.ajax({
						url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCoordinatesFromW3W?words=' + word,
						contentType: 'application/json',
						type: 'GET'
					}).done(function (data) {
						if (data && data.Latitude && data.Longitude) {
							map.setCenter({ lat: data.Latitude, lng: data.Longitude });
							$("#Latitude").val(data.Latitude);
							$("#Longitude").val(data.Longitude);
							refreshPersonnelGrid();
							if (marker)
								marker.setMap(null);
							marker = new google.maps.Marker({
								position: { lat: data.Latitude, lng: data.Longitude },
								map: map,
								title: 'W3W Area Center',
								animation: google.maps.Animation.DROP,
								draggable: true,
								bounds: false
							});
						}
						else {
							alert("What3Words was unable to find a location for those words. Ensure its 3 words separated by periods.");
						}
					});
					evt.preventDefault();
				});
				$("#personnelGrid").kendoGrid({
					dataSource: {
						type: "json",
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForCallGrid?callLat=' + $("#Latitude").val() + '&callLong=' + $("#Longitude").val()
						},
						schema: {
							model: {
								fields: {
									UserId: { type: "string" },
									Name: { type: "string" },
									Eta: { type: "string" },
									Status: { type: "string" },
									StatusColor: { type: "string" },
									Staffing: { type: "string" },
									StaffingColor: { type: "string" },
									Group: { type: "string" },
									Roles: { type: "string" }
								}
							}
						},
						//pageSize: 50,
						serverPaging: false,
						serverFiltering: false,
						serverSorting: false
					},
					height: 600,
					width: 210,
					filterable: true,
					sortable: true,
					pageable: false,
					columns: [
						{
							field: "UserId",
							title: "",
							width: 9,
							filterable: false,
							sortable: false,
							headerTemplate: '<label><input type="checkbox" id="checkAllPersonnel"/></label>',
							template: "<input type=\"checkbox\" id=\"dispatchUser_#=UserId#\" name=\"dispatchUser_#=UserId#\" />"
						},
						{
							field: "Name",
							title: "Name",
							width: 50
						},
						{
							field: "Eta",
							title: "Eta",
							width: 18
						},
						{
							field: "Status",
							title: "Status",
							width: 30,
							template: "<span style='color:#=StatusColor#;'>#=Status#</span>"
						},
						{
							field: "Staffing",
							title: "Staffing",
							width: 30,
							template: "<span style='color:#=StaffingColor#;'>#=Staffing#</span>"
						},
						{
							field: "Group",
							title: "Group",
							width: 50
						},
						{
							field: "Roles",
							title: "Roles",
							width: 100
						}
					]
				});
				$("#groupsGrid").kendoGrid({
					dataSource: {
						type: "json",
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Groups/GetGroupsForCallGrid'
						},
						schema: {
							model: {
								fields: {
									GroupId: { type: "number" },
									Name: { type: "string" },
									Count: { type: "number" }
								}
							}
						},
						//pageSize: 50,
						serverPaging: false,
						serverFiltering: false,
						serverSorting: false
					},
					height: 600,
					width: 210,
					filterable: true,
					sortable: true,
					pageable: false,
					columns: [
						{
							field: "GroupId",
							title: "",
							width: 28,
							filterable: false,
							sortable: false,
							headerTemplate: '<label><input type="checkbox" id="checkAllGroups"/></label>',
							template: "<input type=\"checkbox\" id=\"dispatchGroup_#=GroupId#\" name=\"dispatchGroup_#=GroupId#\" />"
						},
						"Name",
						{
							field: "Count",
							title: "Personnel Count"
						}
					]
				});
				$("#rolesGrid").kendoGrid({
					dataSource: {
						type: "json",
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Personnel/GetRolesForCallGrid'
						},
						schema: {
							model: {
								fields: {
									RoleId: { type: "number" },
									Name: { type: "string" },
									Count: { type: "number" }
								}
							}
						},
						//pageSize: 50,
						serverPaging: false,
						serverFiltering: false,
						serverSorting: false
					},
					height: 600,
					width: 210,
					filterable: true,
					sortable: true,
					pageable: false,
					columns: [
						{
							field: "RoleId",
							title: "",
							width: 28,
							filterable: false,
							sortable: false,
							headerTemplate: '<label><input type="checkbox" id="checkAllRoles"/></label>',
							template: "<input type=\"checkbox\" id=\"dispatchRole_#=RoleId#\" name=\"dispatchRole_#=RoleId#\" />"
						},
						"Name",
						{
							field: "Count",
							title: "Personnel Count"
						}
					]
				});

				$('#protocolQuestionWindow').on('show.bs.modal', function (event) {
					var protocolId = $(event.relatedTarget).data('protocolid');
					//var protocolId = button.data('protocolId');

					var protocol = null;

					for (var i = 0; i < resgrid.dispatch.newcall.protocolData.length; i++) {
						if (resgrid.dispatch.newcall.protocolData[i].Id === protocolId) {
							protocol = resgrid.dispatch.newcall.protocolData[i];
							break;
						}
					}

					var modal = $(this);
					modal.find('.modal-title').text(`Questions for ${protocol.Name}`);

					var questionHtml = "";
					for (var t = 0; t < protocol.Questions.length; t++) {
						var question = protocol.Questions[t];
						questionHtml = questionHtml + `<div class="form-group"><label class=" control-label">${question.Question}</label><div class="controls"><select id="questionAnswer_${question.Id}" name="questionAnswer_${question.Id}">`;

						for (var r = 0; r < protocol.Questions[t].Answers.length; r++) {
							var answer = protocol.Questions[t].Answers[r];

							if (r === 0) {
								questionHtml = questionHtml + `<option selected="selected" value="${answer.Weight}">${answer.Answer}</option>`;
							} else {
								questionHtml = questionHtml + `<option value="${answer.Weight}">${answer.Answer}</option>`;
							}
						}

						questionHtml = questionHtml + '</select></div></div>';
					}
					modal.find('.modal-body').empty();
					modal.find('.modal-body').append(questionHtml);

					$('#processQuestionAnswers').removeAttr("data-protocolid");
					$('#processQuestionAnswers').attr('data-protocolid', protocol.Id);
				});

				$('#processQuestionAnswers').click(function () {
					var buttonProtocolId = $('#processQuestionAnswers').attr('data-protocolid');
					$('#protocolQuestionWindow').modal('hide');

					var protocol = null;
					for (var i = 0; i < resgrid.dispatch.newcall.protocolData.length; i++) {
						if (resgrid.dispatch.newcall.protocolData[i].Id === Number(buttonProtocolId)) {
							protocol = resgrid.dispatch.newcall.protocolData[i];
							break;
						}
					}

					var totalAnswerWeight = 0;
					for (var t = 0; t < protocol.Questions.length; t++) {
						var question = protocol.Questions[t];

						var answerWeight = $(`#questionAnswer_${question.Id}`).val();

						if (answerWeight) {
							totalAnswerWeight = totalAnswerWeight + Number(answerWeight);
						}
					}

					$(`#answerProcotolQuestions_${protocol.Id}`).removeClass("btn-warning btn-success btn-inverse");

					if (totalAnswerWeight >= protocol.MinimumWeight) {
						$(`#pendingProtocol_${protocol.Id}`).val('1');
						$(`#answerProcotolQuestions_${protocol.Id}`).addClass("btn-success");
					} else {
						$(`#answerProcotolQuestions_${protocol.Id}`).addClass("btn-inverse");
					}
				});

				function fillCallTemplate() {
					var templateId = $('#CallTemplateId').val();

					if (templateId && templateId > 0) {
						$.ajax({
							url: resgrid.absoluteBaseUrl + '/User/Templates/GetTemplate?id=' + templateId,
							contentType: 'application/json',
							type: 'GET'
						}).done(function (data) {
							if (data) {
								if (data.CallName && data.CallName.length > 0) {
									$('#Call_Name').val(data.CallName);
								}

								if (data.CallNature && data.CallNature.length > 0) {
									$('#Call_NatureOfCall').val(data.CallNature);
								}

								if (data.CallType && data.CallType.length > 0) {
									$('#Call_Type').val(data.CallType);
								}

								if (data.CallPriority && data.CallPriority >= 0) {
									$('#CallPriority').val(data.CallPriority);
								}
							}
						});
					}
				}
				newcall.fillCallTemplate = fillCallTemplate;

				$('#checkAllPersonnel').on('click', function () {
					$('#personnelGrid').find(':checkbox').prop('checked', this.checked);
				});
				$('#checkAllGroups').on('click', function () {
					$('#groupsGrid').find(':checkbox').prop('checked', this.checked);
				});
				$('#checkAllRoles').on('click', function () {
					$('#rolesGrid').find(':checkbox').prop('checked', this.checked);
				});
				$('a[data-toggle="tab"]').on('shown.bs.tab', function (e) {
					if (e.target && e.target.textContent === "Personnel") {
						var personnelsGrid = $('#personnelGrid');
						var personnelDataArea = personnelsGrid.find('.k-grid-content');
						personnelDataArea.height(556);
						personnelsGrid.height(600);
					}
					else if (e.target && e.target.textContent === "Groups") {
						var groupsGrid = $('#groupsGrid');
						var groupsDataArea = groupsGrid.find('.k-grid-content');
						groupsDataArea.height(556);
						groupsGrid.height(600);
					}
					else if (e.target && e.target.textContent === "Roles") {
						var rolesGrid = $('#rolesGrid');
						var rolesDataArea = rolesGrid.find('.k-grid-content');
						rolesDataArea.height(556);
						rolesGrid.height(600);
					}
				});
				centerMap();
			});
			function centerMap() {
				if (centerLat && centerLng) {
					var center = new google.maps.LatLng(centerLat, centerLng);
					// using global variable:
					map.panTo(center);
				}
			}
			newcall.centerMap = centerMap;
			function foundLocation(position) {
				var center = new google.maps.LatLng(position.coords.latitude, position.coords.longitude);
				map.setCenter(center);
			}
			newcall.foundLocation = foundLocation;
			function noLocation() {
				map.setCenter(new google.maps.LatLng(-34.397, 150.644));
			}
			newcall.noLocation = noLocation;
			function findLocation(pos) {
				var geocoder = new google.maps.Geocoder();
				geocoder.geocode({
					latLng: pos
				}, function (results, status) {
					if (status == google.maps.GeocoderStatus.OK) {
						$("#Call_Address").val(results[0].formatted_address);
					}
					else {
						alert("Geocode was not successful for the following reason: " + status);
					}
				});
				$("#Latitude").val(pos.lat().toString());
				$("#Longitude").val(pos.lng().toString());
			}
			newcall.findLocation = findLocation;
			function refreshPersonnelGrid() {
				var personnelGrid = $('#personnelGrid').data('kendoGrid');
				personnelGrid.dataSource.transport.options.read.url = resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForCallGrid?callLat=' + $("#Latitude").val() + '&callLong=' + $("#Longitude").val();
				personnelGrid.dataSource.read();
				personnelGrid.refresh();
			}
			newcall.refreshPersonnelGrid = refreshPersonnelGrid;
			function checkAllUnits(gridName, item) {
				$('#' + gridName).find(':checkbox').trigger('click');//.prop('checked', item.value);
			}
			newcall.checkAllUnits = checkAllUnits;
			function checkForProtocols() {
				var callPriorityVal = $('#CallPriority').val();
				var callTypeVal = $('#Call_Type').val();

				$("#protocols tr").remove();

				$.ajax({
					url: resgrid.absoluteBaseUrl + `/User/Protocols/GetProtocolsForPrioType?priority=${callPriorityVal}&type=${callTypeVal}`,
					contentType: 'application/json',
					type: 'GET'
				}).done(function (data) {
					if (data) {
						resgrid.dispatch.newcall.protocolCount = 0;

						if (data) {
							resgrid.dispatch.newcall.protocolData = data;
							for (var i = 0; i < data.length; i++) {
								var pendingProtocol = data[i];

								if (pendingProtocol.State === 1 || pendingProtocol.State === 2) {
									resgrid.dispatch.newcall.addProtocol(pendingProtocol.Id, pendingProtocol.Name, pendingProtocol.Code, pendingProtocol.State);
								}
								
							}
						}
					}
				});
			}
			newcall.checkForProtocols = checkForProtocols;
			function addProtocol(id, name, code, state) {
				resgrid.dispatch.newcall.protocolCount++;
				$('#protocols tbody').first().append(`<tr>
					<td style='max-width: 50px;'>${code}</td>
					<td>${name}</td>"
					<td>${resgrid.dispatch.newcall.getStatusField(id, state, code)}</td>"
				</tr>`);
			}
			newcall.addProtocol = addProtocol;

			function getStatusField(id, state, code) {
				if (state === 0) {
					return "Inactive";
				} else if (state === 1) {
					return `Active <input type='text' id='activeProtocol_${id}' name='activeProtocol_${id}' style='display:none;' value='1'></input><input type='text' id='protocolCode_${id}' name='protocolCode_${id}' style='display:none;' value='${code}'></input>`;
				} else if (state === 2) {
					return `<a id="answerProcotolQuestions_${id}" class="btn btn-warning btn-xs" data-toggle="modal" data-target="#protocolQuestionWindow" data-protocolId="${id}">Answer Questions</a> <input type='text' id='pendingProtocol_${id}' name='pendingProtocol_${id}' style='display:none;' value='0'></input><input type='text' id='protocolCode_${id}' name='protocolCode_${id}' style='display:none;' value='${code}'></input>`;
				} else {
					return "Unknown";
				}
			}
			newcall.getStatusField = getStatusField;

			checkForProtocols();
		})(newcall = dispatch.newcall || (dispatch.newcall = {}));
	})(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
