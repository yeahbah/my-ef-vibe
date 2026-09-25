const assert = require('assert');
const fs = require('fs');
const { spawnSync } = require('child_process');
const {
  detectShellFamily,
  joinShellCommand,
  quoteForIntegratedTerminal,
} = require('../out/shellQuote.js');

const connectionString = 'Host=localhost;Database=shop;Username=postgres;Password=secret';

assert.equal(detectShellFamily('/bin/bash'), 'posix');
assert.equal(detectShellFamily('C:\\Program Files\\Git\\bin\\bash.exe', 'win32'), 'posix');
assert.equal(detectShellFamily('C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe'), 'powershell');
assert.equal(detectShellFamily('/usr/bin/pwsh'), 'powershell');
assert.equal(detectShellFamily('C:\\Windows\\System32\\cmd.exe'), 'cmd');
assert.equal(detectShellFamily('', 'win32'), 'powershell');
assert.equal(detectShellFamily('', 'linux'), 'posix');

assert.equal(
  quoteForIntegratedTerminal(connectionString, '/bin/bash'),
  `'${connectionString}'`,
);
assert.equal(
  quoteForIntegratedTerminal("O'Brien", '/bin/bash'),
  `'O'\\''Brien'`,
);
assert.equal(
  quoteForIntegratedTerminal("O'Brien", 'powershell.exe'),
  "'O''Brien'",
);
assert.equal(
  quoteForIntegratedTerminal('db.Users.Count()', 'cmd.exe'),
  '"db.Users.Count()"',
);
assert.equal(
  quoteForIntegratedTerminal('say "hi"', 'cmd.exe'),
  '"say ""hi"""',
);

function bashArg(quoted) {
  const result = spawnSync('bash', ['-c', `set -- ${quoted}; printf %s "$1"`], {
    encoding: 'utf8',
  });
  assert.equal(result.status, 0, result.stderr);
  return result.stdout;
}

assert.equal(bashArg(quoteForIntegratedTerminal(connectionString, '/bin/bash')), connectionString);
assert.equal(bashArg(quoteForIntegratedTerminal('db.Users.Count()', '/bin/bash')), 'db.Users.Count()');
assert.equal(bashArg(quoteForIntegratedTerminal("O'Brien", '/bin/bash')), "O'Brien");

const marker = `/tmp/efvibe-quote-pwned-${process.pid}`;
try {
  fs.rmSync(marker, { force: true });
  const payload = `$(touch ${marker})`;
  const printed = bashArg(quoteForIntegratedTerminal(payload, '/bin/bash'));
  assert.equal(printed, payload);
  assert.equal(fs.existsSync(marker), false);
} finally {
  fs.rmSync(marker, { force: true });
}

const command = joinShellCommand(
  ['node', '-e', 'process.stdout.write(process.argv[1] ?? "")', connectionString],
  '/bin/bash',
);
const executed = spawnSync('bash', ['-c', command], { encoding: 'utf8' });
assert.equal(executed.status, 0, executed.stderr);
assert.equal(executed.stdout, connectionString);

console.log('shellQuote smoke: ok');
