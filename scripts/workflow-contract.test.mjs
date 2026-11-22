import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const workflow = name => readFileSync(new URL(`../.github/workflows/${name}.yml`, import.meta.url), 'utf8');

for (const [name, selector] of [['production-refresh', 'matrix.status'], ['staging-refresh', 'matrix.status']]) {
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

test('production is opt-in, main-only and isolated from staging credentials', () => {
  const source = workflow('production-refresh');
  assert.match(source, /^  workflow_dispatch:$/m);
  assert.ok(source.includes("- cron: '7 7,16 * * *'"));
  assert.ok(source.includes("if: github.ref == 'refs/heads/main' && (github.event_name == 'workflow_dispatch' || vars.PRODUCTION_SCHEDULER_ENABLED == 'true')"));
  assert.match(source, /^      max-parallel: 1$/m);
  assert.match(source, /^      fail-fast: false$/m);
  assert.ok(source.includes('options: [all, open, upcoming, closed, listed, tracking, financials]'));
  assert.ok(source.includes("github.event_name == 'schedule' && '[\"open\",\"upcoming\",\"closed\",\"listed\",\"tracking\",\"financials\"]'"));
  assert.match(source, /^    environment: production$/m);
  assert.match(source, /^  group: youripo-production-refresh$/m);
  assert.ok(source.includes('secrets.PRODUCTION_DATABASE_CONNECTION'));
  assert.ok(source.includes('secrets.PRODUCTION_UPSTOX_TOKEN'));
  assert.doesNotMatch(source, /secrets\.STAGING_/);
});

test('production watchdog cannot confuse staging success or read production secrets', () => {
  const source = workflow('production-refresh-watchdog');
  assert.ok(source.includes("if: github.ref == 'refs/heads/main' && vars.PRODUCTION_SCHEDULER_ENABLED == 'true'"));
  assert.ok(source.includes("workflows: ['Production data refresh (opt-in)']"));
  assert.ok(source.includes('REFRESH_ENVIRONMENT: production'));
  assert.ok(source.includes('group: production-refresh-watchdog'));
  assert.ok(source.includes('persist-credentials: false'));
  assert.doesNotMatch(source, /secrets\.|environment: production|curl|onrender\.com/);
  assert.match(source, /^    timeout-minutes: 5$/m);
  const checker = readFileSync(new URL('./check-refresh.mjs', import.meta.url), 'utf8');
  assert.ok(checker.includes('actions/workflows/${policy.workflow}/runs?branch=main&event=schedule'));
  assert.ok(checker.includes('const marker = policy.marker'));
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
