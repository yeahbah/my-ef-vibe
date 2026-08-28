'use strict';

const assert = require('assert');
const path = require('path');
const fs = require('fs');
const ts = require('typescript');

const sourcePath = path.join(__dirname, '..', 'src', 'expressionGuard.ts');
const source = fs.readFileSync(sourcePath, 'utf8');
const { outputText } = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
});

const exported = { exports: {} };
Function('exports', 'module', 'require', outputText)(exported.exports, exported, require);
const { validateReadOnlyExpression } = exported.exports;

function assertBlocked(expression, label = expression) {
  const result = validateReadOnlyExpression(expression);
  assert.strictEqual(result.ok, false, `expected blocked: ${label} (${result.reason ?? ''})`);
}

function assertAllowed(expression, label = expression) {
  const result = validateReadOnlyExpression(expression);
  assert.strictEqual(result.ok, true, `expected allowed: ${label} (${result.reason ?? ''})`);
}

assertBlocked('db.SaveChanges()');
assertBlocked('await db.SaveChangesAsync()');
assertBlocked('db.Products.ExecuteDelete()');
assertBlocked('await db.Products.ExecuteDeleteAsync()');
assertBlocked('#load "Helpers.csx"');

assertBlocked(
  'db.Products.Where(p => p.Url == "http://example.com").ExecuteDelete()',
  'URL literal must not hide ExecuteDelete',
);
assertBlocked(
  'await db.Orders.Where(o => o.CallbackUrl == "https://legacy.example.com").ExecuteDeleteAsync()',
  'https URL literal must not hide ExecuteDeleteAsync',
);
assertBlocked(
  'db.Products.Where(p => p.Url == @"http://example.com").ExecuteUpdate(s => s.SetProperty(p => p.Name, "x"))',
  'verbatim URL literal must not hide ExecuteUpdate',
);
assertBlocked(
  'db.Products.Where(p => p.Url == $"http://{host}").SaveChanges()',
  'interpolated URL literal must not hide SaveChanges',
);
assertBlocked(
  'db.Products.Where(p => p.Url == """http://example.com""").ExecuteDelete()',
  'raw string URL literal must not hide ExecuteDelete',
);
assertBlocked(
  '$"ok {db.SaveChanges()}"',
  'SaveChanges inside an interpolation hole must stay visible',
);
assertBlocked(
  'var s = "/*"; db.SaveChanges(); var t = "*/";',
  'block-comment markers inside strings must not hide SaveChanges',
);

assertAllowed('db.Products.Where(p => p.Id > 0).Take(10).ToList()');
assertAllowed('await db.Products.CountAsync()');
assertAllowed(
  'db.Products.Where(p => p.Url == "http://example.com").ToList()',
  'URL filter without mutation stays allowed',
);
assertAllowed(
  'db.Products.ToList() // SaveChanges is not executed',
  'real line comments should still be ignored',
);
assertAllowed(
  'db.Products.ToList() /* SaveChanges() */',
  'real block comments should still be ignored',
);
assertAllowed(
  'db.Products.Where(p => p.Name == "ExecuteDelete").ToList()',
  'method name inside a string stays allowed',
);

console.log('expressionGuard.smoke.cjs: ok');
