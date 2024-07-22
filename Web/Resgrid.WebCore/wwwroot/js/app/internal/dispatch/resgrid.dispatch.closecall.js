
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var closecall;
        (function (closecall) {
            $(document).ready(function () {
                let quill = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#closeCallForm', function () {
                    $('#ClosedCallNotes').val(quill.root.innerHTML);

                    return true;
                });
            });
        })(closecall = dispatch.closecall || (dispatch.closecall = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
