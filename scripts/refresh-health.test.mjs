import test from 'node:test';
import assert from 'node:assert/strict';
import { assessRefresh, dueCycle, partitions, refreshPolicy } from './refresh-health.mjs';

const now = new Date('2026-10-13T16:00:00Z');
const run = { id: 1, event: 'schedule', head_branch: 'main', created_at: '2026-10-13T13:20:00Z', html_url: 'https://github.com/example/run/1' };
const successful = partitions.map(p => ({ name: `refresh (${p})`, conclusion: 'success', completed_at: '2026-10-13T14:00:00Z' }));
test('due cycle observes grace and UTC day boundary', () => {
  assert.equal(dueCycle(new Date('2026-10-13T06:16:00Z')).toISOString(), '2026-10-12T13:17:00.000Z');
  assert.equal(dueCycle(new Date('2026-10-13T06:17:00Z')).toISOString(), '2026-10-13T04:17:00.000Z');
  assert.equal(dueCycle(now).toISOString(), '2026-10-13T13:17:00.000Z');
});
test('each partition needs success, not just a green catalogue', () => {
  const jobs = successful.map(j => j.name === 'refresh (financials)' ? { ...j, conclusion: 'failure' } : j);
  assert.deepEqual(assessRefresh([run], { 1: jobs }, now).filter(x => !x.healthy).map(x => x.partition), ['financials']);
});
test('manual and unrelated branches cannot hide a missed schedule', () => {
  for (const patch of [{ event: 'workflow_dispatch' }, { head_branch: 'feature' }])
    assert.ok(assessRefresh([{ ...run, ...patch }], { 1: successful }, now).every(x => !x.healthy));
});
test('old successful runs do not satisfy freshness', () => {
  assert.ok(assessRefresh([{ ...run, created_at: '2026-10-12T13:20:00Z' }], { 1: successful }, now).every(x => !x.healthy));
});
test('successful partitions recover and empty history alerts', () => {
  assert.ok(assessRefresh([run], { 1: successful }, now).every(x => x.healthy));
  assert.ok(assessRefresh([], {}, now).every(x => !x.healthy));
});
test('current failure is not hidden by an earlier successful run', () => {
  const latest = { ...run, id: 2, created_at: '2026-10-13T15:00:00Z' };
  assert.equal(assessRefresh([run, latest], { 1: successful, 2: [{ name: 'refresh (open)', conclusion: 'failure' }] }, now)[0].healthy, false);
});
test('in-progress completion and future data are not successful evidence', () => {
  assert.ok(assessRefresh([run], { 1: successful.map(j => ({ ...j, completed_at: '2026-10-14T00:00:00Z' })) }, now).every(x => !x.healthy));
});

const production = refreshPolicy('production');
test('environment configuration fails closed and separates incidents/workflows', () => {
  assert.throws(() => refreshPolicy('prod'), /Unknown/);
  assert.equal(refreshPolicy().workflow, 'staging-refresh.yml');
  assert.equal(production.workflow, 'production-refresh.yml');
  assert.notEqual(production.marker, refreshPolicy().marker);
});
test('production due cycles use their own UTC schedule and three-hour grace', () => {
  for (const [at, expected] of [
    ['2026-11-22T10:06:59Z', '2026-11-21T16:07:00.000Z'],
    ['2026-11-22T10:07:00Z', '2026-11-22T07:07:00.000Z'],
    ['2026-11-22T19:06:59Z', '2026-11-22T07:07:00.000Z'],
    ['2026-11-22T19:07:00Z', '2026-11-22T16:07:00.000Z'],
    ['2026-11-23T00:00:00Z', '2026-11-22T16:07:00.000Z']
  ]) assert.equal(dueCycle(new Date(at), production).toISOString(), expected);
});
test('production reports each failed/missing partition without hiding it behind catalogue success', () => {
  const at = new Date('2026-11-22T11:00:00Z');
  const current = { ...run, created_at: '2026-11-22T07:10:00Z' };
  const completed = successful.map(job => ({ ...job, completed_at: '2026-11-22T09:00:00Z' }));
  assert.ok(assessRefresh([current], { 1: completed }, at, production).every(x => x.healthy));
  for (const conclusion of ['failure', 'cancelled', 'timed_out', 'skipped', null]) {
    const jobs = completed.map(job => job.name === 'refresh (financials)' ? { ...job, conclusion } : job);
    assert.deepEqual(assessRefresh([current], { 1: jobs }, at, production)
      .filter(x => !x.healthy).map(x => x.partition), ['financials']);
  }
  for (const patch of [{ event: 'workflow_dispatch' }, { head_branch: 'feature' },
    { created_at: '2026-11-21T16:10:00Z' }]) {
    assert.ok(assessRefresh([{ ...current, ...patch }], { 1: completed }, at, production).every(x => !x.healthy));
  }
  assert.ok(assessRefresh([], {}, at, production).every(x => !x.healthy));
});
