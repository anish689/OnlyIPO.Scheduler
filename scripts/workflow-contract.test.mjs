import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const workflow = name => readFileSync(new URL(`../.github/workflows/${name}.yml`, import.meta.url), 'utf8');

for (const [name, selector] of [['production-refresh', 'inputs.task'], ['staging-refresh', 'matrix.status']]) {
  test(`${name} bounds listed refresh at 20 minutes without changing other task budgets`, () => {
    const expected = `    timeout-minutes: \${{ ${selector} == 'financials' && 50 || ${selector} == 'tracking' && 30 || ${selector} == 'listed' && 20 || 10 }}`;
    // This is an exact workflow-source contract, not a general YAML parser.
    assert.ok(workflow(name).split('\n').includes(expected));
  });

  test(`${name} retains non-cancelling concurrency and disables document enrichment`, () => {
    const source = workflow(name);
    assert.match(source, /^  cancel-in-progress: false$/m);
    assert.match(source, /^          DocumentEnrichment__Enabled: 'false'$/m);
    assert.match(source, /^          Financials__SkipFresh: 'true'$/m);
    assert.match(source, /^          Tracking__IncludeWatchlisted: 'false'$/m);
  });
}

test('production stays manual-only, main-only and isolated from staging credentials', () => {
  const source = workflow('production-refresh');
  assert.match(source, /^  workflow_dispatch:$/m);
  assert.doesNotMatch(source, /^\s*(?:schedule:|- cron:)/m);
  assert.match(source, /^    if: github.ref == 'refs\/heads\/main'$/m);
  assert.match(source, /^    environment: production$/m);
  assert.match(source, /^  group: youripo-production-refresh$/m);
  assert.ok(source.includes('secrets.PRODUCTION_DATABASE_CONNECTION'));
  assert.ok(source.includes('secrets.PRODUCTION_UPSTOX_TOKEN'));
  assert.doesNotMatch(source, /secrets\.STAGING_/);
});

test('staging remains serial with independent concurrency and credentials', () => {
  const source = workflow('staging-refresh');
  assert.match(source, /^      max-parallel: 1$/m);
  assert.match(source, /^      fail-fast: false$/m);
  assert.match(source, /^    environment: staging$/m);
  assert.match(source, /^  group: onlyipo-staging-refresh$/m);
  assert.ok(source.includes('secrets.STAGING_DATABASE_CONNECTION'));
  assert.doesNotMatch(source, /secrets\.PRODUCTION_/);
});
