// Runs every *.test.cjs in this folder in its own node process and reports a summary.
//
//   npm test                      (from Tests/Resgrid.Tests/Web, after `npm ci`)
//   node browser-tests.cjs        (same; optional arguments filter by file name substring)
//
// `dotnet test` runs the same scripts through BrowserScriptTests.cs, one NUnit case per script, so
// they sit alongside the C# suite locally and in CI. See browser-launch.cjs for the environment
// variables that pick the Playwright install and the browser channel.
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const filters = process.argv.slice(2);
const scripts = fs.readdirSync(__dirname)
    .filter((name) => name.endsWith('.test.cjs'))
    .filter((name) => filters.length === 0 || filters.some((filter) => name.includes(filter)))
    .sort();

if (scripts.length === 0) {
    console.error('No browser test scripts matched.');
    process.exit(1);
}

let failed = 0;
for (const script of scripts) {
    console.log(`\n=== ${script}`);
    const result = spawnSync(process.execPath, [path.join(__dirname, script)], { stdio: 'inherit', cwd: __dirname });
    if (result.status !== 0) {
        failed++;
        console.error(`--- ${script} FAILED (exit ${result.status === null ? result.signal : result.status})`);
    }
}

console.log(`\n${scripts.length - failed} of ${scripts.length} browser test scripts passed.`);
process.exit(failed === 0 ? 0 : 1);
