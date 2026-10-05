var resgrid;
(function (resgrid) {
    var contacts;
    (function (contacts) {
        var categories;
        (function (categories) {
            $(document).ready(function () {
                // DeleteCategory is POST + antiforgery: confirm, then submit the page's token form. The buttons
                // render disabled so a click before this binding cannot fall through.
                $(document).on('click', '.contact-category-delete', function (e) {
                    e.preventDefault();

                    var message = $(this).attr('data-delete-confirm');
                    if (message && !window.confirm(message))
                        return;

                    var form = document.getElementById('deleteCategoryForm');
                    form.elements.namedItem('categoryId').value = $(this).attr('data-category-id');
                    form.submit();
                });
                $('.contact-category-delete').prop('disabled', false);
            });
        })(categories = contacts.categories || (contacts.categories = {}));
    })(contacts = resgrid.contacts || (resgrid.contacts = {}));
})(resgrid || (resgrid = {}));
