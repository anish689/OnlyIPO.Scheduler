import test from 'node:test';
import assert from 'node:assert/strict';
import { assessBackup } from './health.mjs';

const now = new Date('2026-11-27T22:13:00Z');
const run = { id: 123, head_branch: 'main', event: 'schedule', created_at: '2026-11-27T19:44:00Z', conclusion: 'success' };
const jobs = { 123: [{ name: 'backup', conclusion: 'success', steps: [
  { name: 'Export, restore-check, encrypt and verify private Drive copy', conclusion: 'success' }]}] };
test('verified daily backup is healthy', () => assert.equal(assessBackup([run], jobs, now).healthy, true));
test('a skipped workflow or upload is not a backup', () => {
  assert.equal(assessBackup([run], {}, now).healthy, false);
  assert.equal(assessBackup([run], {123: [{name: 'backup', conclusion: 'skipped'}]}, now).healthy, false);
});
test('missing, failed, stale and future runs remain unhealthy', () => {
  for (const change of [{conclusion: 'failure'}, {created_at: '2026-11-26T19:44:00Z'},
    {created_at: '2026-11-28T19:44:00Z'}, {head_branch: 'feature'}, {event: 'pull_request'}])
    assert.equal(assessBackup([{...run, ...change}], jobs, now).healthy, false);
});
test('grace period evaluates previous daily deadline', () => {
  const result = assessBackup([], {}, new Date('2026-11-27T20:00:00Z'));
  assert.equal(result.due, '2026-11-26T19:43:00.000Z');
});
test('manual verified recovery can satisfy the due window', () =>
  assert.equal(assessBackup([{...run,event: 'workflow_dispatch'}], jobs, now).healthy, true));
