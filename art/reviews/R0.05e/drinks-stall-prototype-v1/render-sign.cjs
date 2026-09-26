const sharp = require('C:/Users/99wil/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
sharp(__dirname + '/drinks-sign.svg').png().toFile(__dirname + '/drinks-sign.png').catch(e => { console.error(e); process.exitCode = 1; });
