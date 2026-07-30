'use strict';

const assert = require('node:assert/strict');
const { validateReadOnlyExpression } = require('../out/expressionGuard');

const blocked = [
  'await db.Database.EnsureDeletedAsync()',
  'db.Database.EnsureDeleted()',
  'await db.Database.EnsureCreatedAsync()',
  'db.Database.EnsureCreated()',
  'await db.Database.MigrateAsync()',
  'db.Database.Migrate()',
  'await db.SaveChangesAsync()',
];

const allowed = [
  'db.Products.Where(p => p.Id > 0).Take(10).ToList()',
  'await db.Products.CountAsync()',
];

for (const expression of blocked) {
  const result = validateReadOnlyExpression(expression);
  assert.equal(result.ok, false, `expected blocked: ${expression}`);
}

for (const expression of allowed) {
  const result = validateReadOnlyExpression(expression);
  assert.equal(result.ok, true, `expected allowed: ${expression}`);
}

console.log('expressionGuard smoke checks passed');
