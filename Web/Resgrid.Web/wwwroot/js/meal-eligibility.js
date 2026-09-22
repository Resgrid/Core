(function () {
    'use strict';
    var rows = document.getElementById('meal-eligibility-rows');
    var add = document.getElementById('add-meal-eligibility');
    var template = document.getElementById('meal-eligibility-template');
    if (!rows || !add || !template) return;

    function reindex() {
        var items = rows.querySelectorAll('.meal-eligibility-row');
        items.forEach(function (row, index) {
            row.querySelectorAll('[data-meal-field]').forEach(function (input) {
                var field = input.dataset.mealField;
                input.name = 'MealEligibility[' + index + '].' + field;
                input.id = 'MealEligibility_' + index + '__' + field;
                var container = input.parentElement;
                container.querySelector('label').htmlFor = input.id;
                var error = container.querySelector('[data-valmsg-for]');
                if (error) error.setAttribute('data-valmsg-for', input.name);
            });
        });
        document.getElementById('meal-eligibility-empty').hidden = items.length > 0;
    }

    add.addEventListener('click', function () {
        rows.appendChild(template.content.cloneNode(true));
        reindex();
        rows.lastElementChild.querySelector('input').focus();
    });

    rows.addEventListener('click', function (event) {
        var remove = event.target.closest('[data-remove-meal-eligibility]');
        if (!remove) return;
        var row = remove.closest('.meal-eligibility-row');
        var next = row.nextElementSibling || row.previousElementSibling;
        row.remove();
        reindex();
        (next ? next.querySelector('input') : add).focus();
    });

    reindex();
}());
