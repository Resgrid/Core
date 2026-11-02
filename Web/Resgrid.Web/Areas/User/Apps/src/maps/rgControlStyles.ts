let added = false;

/** The pressed look for the kit's map buttons (draw tools, traffic): MapLibre's control CSS has none. Added once per page. */
export function ensureControlStyles(): void {
  if (added || typeof document === 'undefined') {
    return;
  }
  added = true;

  const style = document.createElement('style');
  style.textContent =
    '.maplibregl-ctrl-group button.active{background-color:#dbe9f7;color:#1c6fb5}' +
    '.maplibregl-ctrl-group button.active:hover{background-color:#cfe1f3}';
  document.head.appendChild(style);
}
