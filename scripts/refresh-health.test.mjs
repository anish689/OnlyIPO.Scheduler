import test from 'node:test';
import assert from 'node:assert/strict';
import { assessRefresh, dueCycle, partitions } from './refresh-health.mjs';

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
