
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var callData;
        (function (callData) {
            $(document).ready(function () {
                marker = null;
                getCallNotes();
            });
            function addCallNote() {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Dispatch/AddCallNote',
                    data: JSON.stringify({
                        CallId: callId,
                        Note: $('#note-box').val()
                    }),
                    contentType: 'application/json',
                    type: 'POST'
                }).done(function (data) {
                    $('#note-box').val('');
                    getCallNotes();
                });
            }
            callData.addCallNote = addCallNote;
            function getCallNotes() {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCallNotes?callId=' + callId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (result) {
                    var notesHtml = $('#note-messages-inner');
                    if (result) {
                        notesHtml.empty();
                        for (var i = 0; i < result.length; i++) {
                            notesHtml.append('<p class="show"><span class="msg-block" ><strong>' + result[i].Name + '</strong><span class="time"> on ' + result[i].Timestamp + '</span><span class="msg">' + result[i].Note + '</span></span></p>');
                        }
                    }
                });
            }
            callData.getCallNotes = getCallNotes;
        })(callData = dispatch.callData || (dispatch.callData = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
