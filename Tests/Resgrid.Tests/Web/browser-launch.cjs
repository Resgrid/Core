// Shared browser launch for the headless script tests in this folder.
//
// Playwright resolves from RESGRID_PLAYWRIGHT_PATH, then this folder's own node_modules
// (`npm ci` here installs the pinned version), then a global install. RESGRID_PLAYWRIGHT_CHANNEL
// picks the browser: msedge (default, uses the Edge already installed on a developer machine) or
// chromium (Playwright's bundled build, which is what CI installs with `playwright install chromium`).
const path = require('node:path');

function playwright() {
    const candidates = [process.env.RESGRID_PLAYWRIGHT_PATH, path.join(__dirname, 'node_modules', 'playwright'), 'playwright'].filter(Boolean);
    let lastError;
    for (const candidate of candidates) {
        try { return require(candidate); } catch (error) { lastError = error; }
    }
    throw lastError;
}

function launchOptions() {
    const channel = process.env.RESGRID_PLAYWRIGHT_CHANNEL || 'msedge';
    return channel === 'chromium' ? { headless: true } : { channel: channel, headless: true };
}

module.exports = { playwright, launchOptions };
