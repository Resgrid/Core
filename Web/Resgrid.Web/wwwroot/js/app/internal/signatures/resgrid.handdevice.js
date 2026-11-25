/*
 * "Hand device to signer" (e-Signature S2): a drawn signature on a canvas (pointer events: finger, pen or mouse), or the
 * printed name adopted as a typed signature (the keyboard path). On submit the canvas is posted as a PNG data URL.
 */
(function () {
	'use strict';

	var form = document.getElementById('rg-handdevice-form');
	var canvas = document.getElementById('rg-handdevice-canvas');
	if (!form || !canvas || !canvas.getContext) {
		return;
	}

	var image = document.getElementById('rg-handdevice-image');
	var nameInput = document.getElementById('rg-handdevice-name');
	var error = document.getElementById('rg-handdevice-error');
	var context = canvas.getContext('2d');
	var drawing = false;
	var drawn = false;
	var last = null;

	function reset() {
		context.fillStyle = '#ffffff';
		context.fillRect(0, 0, canvas.width, canvas.height);
		context.strokeStyle = '#111111';
		context.lineWidth = 2.5;
		context.lineCap = 'round';
		context.lineJoin = 'round';
		drawn = false;
	}

	function point(event) {
		var rect = canvas.getBoundingClientRect();
		return {
			x: (event.clientX - rect.left) * (canvas.width / rect.width),
			y: (event.clientY - rect.top) * (canvas.height / rect.height)
		};
	}

	canvas.addEventListener('pointerdown', function (event) {
		drawing = true;
		last = point(event);
		if (canvas.setPointerCapture) {
			canvas.setPointerCapture(event.pointerId);
		}
		event.preventDefault();
	});

	canvas.addEventListener('pointermove', function (event) {
		if (!drawing) {
			return;
		}
		var next = point(event);
		context.beginPath();
		context.moveTo(last.x, last.y);
		context.lineTo(next.x, next.y);
		context.stroke();
		last = next;
		drawn = true;
		event.preventDefault();
	});

	function stop() {
		drawing = false;
		last = null;
	}

	canvas.addEventListener('pointerup', stop);
	canvas.addEventListener('pointercancel', stop);
	canvas.addEventListener('pointerleave', stop);

	document.getElementById('rg-handdevice-clear').addEventListener('click', function () {
		reset();
	});

	document.getElementById('rg-handdevice-typed').addEventListener('click', function () {
		var name = (nameInput && nameInput.value || '').trim();
		if (!name) {
			if (nameInput) {
				nameInput.focus();
			}
			return;
		}
		reset();
		context.fillStyle = '#111111';
		context.font = 'italic 44px "Brush Script MT", "Segoe Script", cursive';
		context.textBaseline = 'middle';
		context.fillText(name, 20, canvas.height / 2, canvas.width - 40);
		drawn = true;
	});

	form.addEventListener('submit', function (event) {
		if (!drawn) {
			event.preventDefault();
			if (error) {
				error.hidden = false;
			}
			canvas.focus();
			return;
		}
		if (error) {
			error.hidden = true;
		}
		image.value = canvas.toDataURL('image/png');
	});

	reset();
})();
