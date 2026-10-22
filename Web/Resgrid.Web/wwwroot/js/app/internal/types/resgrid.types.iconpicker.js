// Custom icon picker for unit types, call types, POI types and personnel roles (Areas/User/Views/Shared/_TypeIconPicker.cshtml).
// Checks the chosen file in the browser with the server's limits (TypeIconRenditions) and messages, then previews it
// the way the server stores it: scaled so the long edge is 128 px and centred on a transparent 128 px square, drawn at
// 32 px on a light and a dark map, 20 px in a list and as the stored image. The server check still runs on every post.
(function () {
    'use strict';

    var ACCEPTED_TYPES = ['image/png', 'image/jpeg', 'image/pjpeg', 'image/gif', 'image/webp'];
    var ACCEPTED_EXTENSIONS = /\.(png|jpe?g|gif|webp)$/i;

    function format(template, args) {
        return String(template || '').replace(/\{(\d+)\}/g, function (match, index) {
            var value = args[parseInt(index, 10)];
            return value === undefined ? match : String(value);
        });
    }

    function formatBytes(bytes) {
        if (bytes >= 1048576)
            return (bytes / 1048576).toFixed(1) + ' MB';

        return Math.max(1, Math.round(bytes / 1024)) + ' KB';
    }

    function each(nodes, callback) {
        Array.prototype.forEach.call(nodes, callback);
    }

    function fill(list, messages, itemTag) {
        while (list.firstChild)
            list.removeChild(list.firstChild);

        each(messages, function (message) {
            var item = document.createElement(itemTag);
            item.textContent = message;
            list.appendChild(item);
        });
    }

    function onChange(element, handler) {
        // Through jQuery when the page has it, so selects driven by a jQuery plugin still report their changes.
        if (window.jQuery)
            window.jQuery(element).on('change', handler);
        else
            element.addEventListener('change', handler);
    }

    function init(root) {
        if (root.getAttribute('data-icon-ready') === 'true')
            return;

        var input = root.querySelector('input[type="file"]');
        if (!input)
            return;

        root.setAttribute('data-icon-ready', 'true');

        var data = root.dataset;
        var removeBox = root.querySelector('[data-icon-remove]');
        var errorBox = root.querySelector('[data-icon-error]');
        var warningList = root.querySelector('[data-icon-warnings]');
        var stateText = root.querySelector('[data-icon-state]');
        var mapImages = root.querySelectorAll('[data-icon-map]');
        var placeholders = root.querySelectorAll('[data-icon-placeholder]');
        var listImage = root.querySelector('[data-icon-list]');
        var savedTile = root.querySelector('[data-icon-saved-tile]');
        var savedImage = root.querySelector('[data-icon-saved]');
        var names = root.querySelectorAll('[data-icon-name]');

        var maxBytes = parseInt(data.maxBytes, 10);
        var minEdge = parseInt(data.minEdge, 10);
        var maxEdge = parseInt(data.maxEdge, 10);
        var storedSize = parseInt(data.storedSize, 10);

        var currentUrl = data.currentUrl || null;
        var defaultUrl = data.defaultUrl || null;
        var pendingUrl = null;
        var objectUrl = null;
        var token = 0;

        function setErrors(messages) {
            fill(errorBox, messages, 'div');
        }

        function setWarnings(messages) {
            fill(warningList, messages, 'li');
            warningList.hidden = messages.length === 0;
        }

        // Uploaded icons are square (32 x 32 on the map); the built-in pins are 32 x 37.
        function show(url, square, state) {
            each(mapImages, function (image) {
                if (url) {
                    image.src = url;
                    image.width = 32;
                    image.height = square ? 32 : 37;
                    image.hidden = false;
                } else {
                    image.removeAttribute('src');
                    image.hidden = true;
                }
            });
            each(placeholders, function (placeholder) {
                placeholder.hidden = !!url;
            });

            if (url) {
                listImage.src = url;
                listImage.hidden = false;
            } else {
                listImage.removeAttribute('src');
                listImage.hidden = true;
            }

            if (url && square) {
                savedImage.src = url;
                savedTile.hidden = false;
            } else {
                savedImage.removeAttribute('src');
                savedTile.hidden = true;
            }

            stateText.textContent = state || '';
        }

        function render() {
            if (pendingUrl)
                show(pendingUrl, true, data.msgStateNew);
            else if (removeBox && removeBox.checked)
                show(defaultUrl, false, data.msgStateRemoved);
            else if (currentUrl)
                show(currentUrl, true, data.msgStateCurrent);
            else
                show(defaultUrl, false, defaultUrl ? data.msgStateDefault : data.msgStateNone);
        }

        function reject(messages) {
            input.value = '';
            pendingUrl = null;
            setWarnings([]);
            setErrors(messages);
            render();
        }

        // The stored rendition: long edge scaled to the square, centred on a transparent canvas (server: TypeIconRenditions.Build).
        function normalize(image, width, height) {
            var canvas = document.createElement('canvas');
            canvas.width = storedSize;
            canvas.height = storedSize;

            var context = canvas.getContext('2d');
            var scale = storedSize / Math.max(width, height);
            var drawWidth = Math.max(1, Math.round(width * scale));
            var drawHeight = Math.max(1, Math.round(height * scale));
            var left = Math.floor((storedSize - drawWidth) / 2);
            var top = Math.floor((storedSize - drawHeight) / 2);

            context.imageSmoothingEnabled = true;
            if ('imageSmoothingQuality' in context)
                context.imageSmoothingQuality = 'high';
            context.drawImage(image, left, top, drawWidth, drawHeight);

            var result = { url: objectUrl, opaque: false };
            try {
                var corners = [[left, top], [left + drawWidth - 1, top], [left, top + drawHeight - 1], [left + drawWidth - 1, top + drawHeight - 1]];
                result.opaque = corners.every(function (corner) {
                    return context.getImageData(corner[0], corner[1], 1, 1).data[3] === 255;
                });
                result.url = canvas.toDataURL('image/png');
            } catch (e) {
                // A local file never taints the canvas; if a browser refuses anyway, preview the file as chosen.
            }

            return result;
        }

        function onFile() {
            var file = input.files && input.files[0];
            var current = ++token;

            pendingUrl = null;
            setErrors([]);
            setWarnings([]);

            if (!file) {
                render();
                return;
            }

            var typeKnown = !!file.type;
            var typeAccepted = typeKnown ? ACCEPTED_TYPES.indexOf(file.type.toLowerCase()) >= 0 : ACCEPTED_EXTENSIONS.test(file.name || '');
            if (!typeAccepted) {
                reject([data.msgFormat]);
                return;
            }

            if (file.size > maxBytes) {
                reject([data.msgBytes + ' ' + format(data.msgFileSize, [formatBytes(file.size)])]);
                return;
            }

            if (objectUrl && window.URL)
                window.URL.revokeObjectURL(objectUrl);
            objectUrl = window.URL.createObjectURL(file);

            var image = new Image();
            image.onload = function () {
                if (current !== token)
                    return;

                var width = image.naturalWidth;
                var height = image.naturalHeight;
                var size = format(data.msgImageSize, [width, height]);

                if (!width || !height) {
                    reject([data.msgUnreadable]);
                    return;
                }

                if (width > maxEdge || height > maxEdge) {
                    reject([data.msgTooLarge + ' ' + size]);
                    return;
                }

                if (Math.max(width, height) < minEdge) {
                    reject([data.msgTooSmall + ' ' + size]);
                    return;
                }

                var rendition = normalize(image, width, height);
                var warnings = [];
                if (Math.max(width, height) < storedSize)
                    warnings.push(format(data.msgWarnSmall, [width, height]));
                if (width !== height)
                    warnings.push(data.msgWarnSquare);
                if (rendition.opaque)
                    warnings.push(data.msgWarnOpaque);

                pendingUrl = rendition.url;
                if (removeBox)
                    removeBox.checked = false;

                setWarnings(warnings);
                render();
            };
            image.onerror = function () {
                if (current === token)
                    reject([data.msgUnreadable]);
            };
            image.src = objectUrl;
        }

        var sampleInput = data.sampleInput ? document.getElementById(data.sampleInput) : null;
        function syncName() {
            var typed = sampleInput && sampleInput.value ? sampleInput.value.trim() : '';
            var text = typed || data.sampleName || '';
            each(names, function (name) {
                name.textContent = text;
            });
        }

        var defaultSelect = data.defaultSelect ? document.getElementById(data.defaultSelect) : null;
        function syncDefault() {
            var option = defaultSelect.options[defaultSelect.selectedIndex];
            var value = defaultSelect.value === '' ? -1 : parseInt(defaultSelect.value, 10);

            defaultUrl = option && value >= 0
                ? '/images/Mapping/' + option.text.replace(/\s/g, '').toLowerCase() + '.png'
                : (data.baseUrl || null);
            render();
        }

        input.addEventListener('change', onFile);
        if (removeBox)
            removeBox.addEventListener('change', render);
        if (sampleInput) {
            sampleInput.addEventListener('input', syncName);
            syncName();
        }

        if (defaultSelect) {
            onChange(defaultSelect, syncDefault);
            syncDefault();
        } else {
            render();
        }

        // A file kept by the browser across a back/forward navigation is checked like a new choice.
        if (input.files && input.files.length)
            onFile();
    }

    function start() {
        each(document.querySelectorAll('[data-rg-icon-picker]'), init);
    }

    window.resgrid = window.resgrid || {};
    window.resgrid.types = window.resgrid.types || {};
    window.resgrid.types.iconpicker = { init: init, start: start };

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', start);
    else
        start();
})();
