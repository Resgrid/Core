// Role order on Personnel > Roles: drag a row or use its arrows, then save. The order picks a person's map icon (the
// first role they hold, in this order, that has an icon), so it is posted as the role ids from top to bottom.
(function () {
    'use strict';

    function start() {
        var table = document.getElementById('rolesTable');
        var form = document.getElementById('roleOrderForm');
        if (!table || !form)
            return;

        var body = table.tBodies[0];
        var unsaved = document.getElementById('roleOrderUnsaved');
        var dragged = null;
        var orderBeforeDrag = '';

        function rows() {
            return Array.prototype.slice.call(body.querySelectorAll('tr[data-role-id]'));
        }

        function order() {
            return rows().map(function (row) { return row.getAttribute('data-role-id'); }).join(',');
        }

        function refresh() {
            var all = rows();
            all.forEach(function (row, index) {
                var position = row.querySelector('[data-role-position]');
                if (position)
                    position.textContent = String(index + 1);

                var up = row.querySelector('[data-role-move="up"]');
                var down = row.querySelector('[data-role-move="down"]');
                if (up)
                    up.disabled = index === 0;
                if (down)
                    down.disabled = index === all.length - 1;
            });
        }

        function changed() {
            refresh();
            if (unsaved)
                unsaved.hidden = false;
        }

        body.addEventListener('click', function (event) {
            var button = event.target.closest ? event.target.closest('[data-role-move]') : null;
            if (!button || button.disabled)
                return;

            var row = button.closest('tr');
            var direction = button.getAttribute('data-role-move');
            if (direction === 'up' && row.previousElementSibling)
                body.insertBefore(row, row.previousElementSibling);
            else if (direction === 'down' && row.nextElementSibling)
                body.insertBefore(row.nextElementSibling, row);
            else
                return;

            changed();
            // Keep the keyboard on the moved row; fall back to the other arrow when this one is now disabled.
            var again = row.querySelector('[data-role-move="' + direction + '"]');
            var other = row.querySelector('[data-role-move="' + (direction === 'up' ? 'down' : 'up') + '"]');
            (again && !again.disabled ? again : other).focus();
        });

        body.addEventListener('dragstart', function (event) {
            var row = event.target.closest ? event.target.closest('tr[data-role-id]') : null;
            if (!row)
                return;

            dragged = row;
            orderBeforeDrag = order();
            row.style.opacity = '0.5';
            if (event.dataTransfer) {
                event.dataTransfer.effectAllowed = 'move';
                event.dataTransfer.setData('text/plain', row.getAttribute('data-role-id'));
            }
        });

        body.addEventListener('dragover', function (event) {
            if (!dragged)
                return;

            var row = event.target.closest ? event.target.closest('tr[data-role-id]') : null;
            event.preventDefault();
            if (!row || row === dragged)
                return;

            var box = row.getBoundingClientRect();
            var after = event.clientY > box.top + box.height / 2;
            body.insertBefore(dragged, after ? row.nextElementSibling : row);
        });

        body.addEventListener('drop', function (event) {
            if (dragged)
                event.preventDefault();
        });

        body.addEventListener('dragend', function () {
            if (!dragged)
                return;

            dragged.style.opacity = '';
            dragged = null;
            if (order() !== orderBeforeDrag)
                changed();
        });

        form.addEventListener('submit', function () {
            Array.prototype.slice.call(form.querySelectorAll('input[name="roleIds"]')).forEach(function (input) {
                form.removeChild(input);
            });

            rows().forEach(function (row) {
                var input = document.createElement('input');
                input.type = 'hidden';
                input.name = 'roleIds';
                input.value = row.getAttribute('data-role-id');
                form.appendChild(input);
            });
        });

        refresh();
    }

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', start);
    else
        start();
})();
