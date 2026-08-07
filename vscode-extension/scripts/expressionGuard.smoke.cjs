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
// eslint-disable-next-line no-new-func
Function('exports', 'module', 'require', outputText)(exported.exports, exported, require);
const { validateReadOnlyExpression } = exported.exports;

function assertBlocked(expression, label = expression) {
  const result = validateReadOnlyExpression(expression);
  assert.strictEqual(result.ok, false, `expected blocked: ${label}`);
}

function assertAllowed(expression, label = expression) {
  const result = validateReadOnlyExpression(expression);
  assert.strictEqual(result.ok, true, `expected allowed: ${label} (${result.reason ?? ''})`);
}

assertBlocked('db.Products.ExecuteDeleteAsync()');
assertBlocked('EntityFrameworkQueryableExtensions.ExecuteDeleteAsync<Product>(db.Products)');
assertBlocked('EntityFrameworkQueryableExtensions.ExecuteUpdateAsync<Product>(db.Products, s => s.SetProperty(p => p.Name, "x"))');
assertBlocked('db.Database.EnsureDeleted()');
assertBlocked('db.Database.EnsureDeletedAsync()');
assertBlocked('db.Database.GetService<IRelationalDatabaseCreator>().EnsureDeleted()');
assertBlocked('db.GetService<IRelationalDatabaseCreator>().EnsureDeleted()');
assertBlocked('db.Database.GetService<IMigrator>().Migrate()');
assertBlocked('db.GetService<IRelationalDatabaseCreator>().Delete()');
assertBlocked('db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM T")');
assertBlocked('db.SaveChangesAsync()');

assertAllowed('db.Products.Where(p => p.Id > 0).ToList()');
assertAllowed('db.Products.Count()');
assertAllowed('db.Products.Where(p => p.Name == "ExecuteDelete").ToList()');

console.log('expressionGuard.smoke.cjs: ok');
