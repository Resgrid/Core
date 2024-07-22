
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var deletecall;
        (function (deletecall) {
            $(document).ready(function () {
                let quill = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#deleteCallForm', function () {
                    $('#DeleteCallNotes').val(quill.root.innerHTML);

                    return true;
                });
            });
        })(deletecall = dispatch.deletecall || (dispatch.deletecall = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
