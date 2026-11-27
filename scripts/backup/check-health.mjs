import { appendFileSync } from 'node:fs';
import { assessBackup } from './health.mjs';

if (process.env.GITHUB_REPOSITORY !== 'anish689/OnlyIPO.Scheduler' || !process.env.GH_TOKEN)
  throw new Error('Backup watchdog configuration missing.');
async function api(path, method = 'GET', body) {
  const response = await fetch(`https://api.github.com/repos/anish689/OnlyIPO.Scheduler/${path}`, {
    method, redirect: 'error', headers: { Authorization: `Bearer ${process.env.GH_TOKEN}`,
      Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28', 'Content-Type': 'application/json' },
    body: body ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(30000)
  });
  if (!response.ok) throw new Error(`Backup watchdog GitHub HTTP ${response.status}`);
  return response.json();
}
const { workflow_runs: runs } = await api('actions/workflows/production-backup.yml/runs?branch=main&per_page=30');
const jobs = {};
for (const run of runs.slice(0, 10)) jobs[run.id] = (await api(`actions/runs/${run.id}/jobs?per_page=100`)).jobs;
const result = assessBackup(runs.slice(0, 10), jobs, new Date());
const marker = '<!-- youripo-production-backup-watchdog-v1 -->';
const title = '[operations] Production backup needs attention';
let incident;
for (let page = 1; page <= 10; page++) {
  const issues = await api(`issues?state=open&per_page=100&page=${page}`);
  incident = issues.find(x => !x.pull_request && x.title === title && x.body?.includes(marker) && x.user?.login === 'github-actions[bot]');
  if (incident || issues.length < 100) break;
  if (page === 10) throw new Error('Issue search incomplete; refusing duplicate incident.');
}
console.log(JSON.stringify(result));
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY,
  `Production backup: ${result.reason} Due: ${result.due}. This is execution evidence, not a guarantee of future recovery or source-job completeness.\n`);
if (!result.healthy) {
  const body = `${marker}\n${result.reason} Due: ${result.due}.\n\nInspect the [backup workflow](https://github.com/anish689/OnlyIPO.Scheduler/actions/workflows/production-backup.yml), dedicated database permissions and private Drive authorization. Keep existing backups. Never paste keys, tokens, account data or dump output into this public issue.\n\nSubscribe for GitHub notifications. Delivery is not guaranteed; independently check the backup history.`;
  if (!incident) await api('issues', 'POST', { title, body });
  else if (incident.body !== body) await api(`issues/${incident.number}`, 'PATCH', { body });
  process.exitCode = 1;
} else if (incident) {
  await api(`issues/${incident.number}/comments`, 'POST', { body: 'Recovered: a production backup completed its isolated restore and offsite download verification in the due window.' });
  await api(`issues/${incident.number}`, 'PATCH', { state: 'closed', state_reason: 'completed' });
}
