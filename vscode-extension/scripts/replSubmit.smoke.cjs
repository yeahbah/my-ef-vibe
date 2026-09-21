'use strict';

const assert = require('node:assert/strict');
const {
  collapseSnippetWhitespace,
  formatSnippetForReplSubmit,
} = require('../out/snippetWhitespace.js');

function check(name, actual, expected) {
  assert.equal(actual, expected, `${name}\n  actual:   ${JSON.stringify(actual)}\n  expected: ${JSON.stringify(expected)}`);
}

check(
  'preserves double spaces in a regular string filter',
  formatSnippetForReplSubmit('db.Users.Where(u => u.Name == "John  Smith").ToList()'),
  'db.Users.Where(u => u.Name == "John  Smith").ToList();',
);

check(
  'flattens multiline LINQ without touching the filter literal',
  formatSnippetForReplSubmit(`db.Users
    .Where(u => u.Name == "John  Smith")
    .ToList()`),
  'db.Users .Where(u => u.Name == "John  Smith") .ToList();',
);

check(
  'preserves verbatim string padding',
  formatSnippetForReplSubmit('db.Users.Where(u => u.Name == @"John  Smith").ToList()'),
  'db.Users.Where(u => u.Name == @"John  Smith").ToList();',
);

check(
  'preserves escaped quotes and spaces',
  formatSnippetForReplSubmit('db.Users.Where(u => u.Name == "John \\"  Smith").ToList()'),
  'db.Users.Where(u => u.Name == "John \\"  Smith").ToList();',
);

check(
  'does not add a second semicolon',
  formatSnippetForReplSubmit('db.Users.Count();'),
  'db.Users.Count();',
);

check(
  'collapses code whitespace outside strings',
  collapseSnippetWhitespace('db.Users.  Where(u => u.Id > 0)'),
  'db.Users. Where(u => u.Id > 0)',
);

check(
  'empty snippet',
  formatSnippetForReplSubmit('   \n  '),
  '',
);

check(
  'preserves spaces in interpolated literals',
  formatSnippetForReplSubmit('db.Users.Where(u => u.Name == $"John  {suffix}").ToList()'),
  'db.Users.Where(u => u.Name == $"John  {suffix}").ToList();',
);

check(
  'does not treat verbatim identifiers as strings',
  formatSnippetForReplSubmit('db.Users.Where(u => u.Name == @name).ToList()'),
  'db.Users.Where(u => u.Name == @name).ToList();',
);

check(
  'preserves a space char literal',
  collapseSnippetWhitespace("filter == ' ' && name == \"A  B\""),
  "filter == ' ' && name == \"A  B\"",
);

console.log('replSubmit.smoke: ok');
