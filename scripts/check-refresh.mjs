import { assessRefresh } from './refresh-health.mjs';
import { appendFileSync } from 'node:fs';

const repo = process.env.GITHUB_REPOSITORY;
if (!repo || !process.env.GH_TOKEN) throw new Error('GitHub repository and token are required.');
async function api(path, method = 'GET', body) {
  const response = await fetch(`https://api.github.com/repos/${repo}/${path}`, {
    method, headers: { Authorization: `Bearer ${process.env.GH_TOKEN}`, Accept: 'application/vnd.github+json',
      'X-GitHub-Api-Version': '2022-11-28', 'Content-Type': 'application/json' },
    body: body ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(30000)
  });
  if (!response.ok) throw new Error(`GitHub request failed: HTTP ${response.status}`);
  return response.json();
}
const { workflow_runs: runs } = await api('actions/workflows/staging-refresh.yml/runs?branch=main&event=schedule&per_page=30');
const jobs = {};
for (const run of runs.slice(0, 6)) jobs[run.id] = (await api(`actions/runs/${run.id}/jobs?per_page=100`)).jobs;
const assessment = assessRefresh(runs.slice(0, 6), jobs);
const failures = assessment.filter(x => !x.healthy);
const title = '[operations] Staging refresh needs attention';
const marker = '<!-- youripo-staging-refresh-watchdog -->';
const issues = [];
for (let page = 1; page <= 10; page++) {
  const batch = await api(`issues?state=open&per_page=100&page=${page}`);
  issues.push(...batch);
  if (batch.length < 100) break;
  if (page === 10) throw new Error('Issue search limit reached; refusing to create a possible duplicate.');
}
const incident = issues.find(x => !x.pull_request && x.title === title && x.body?.includes(marker)
  && x.user?.login === 'github-actions[bot]');
const report = assessment.map(x => `- ${x.partition}: ${x.reason}`).join('\n');
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY,
  `## Staging scheduled refresh\n\n${report}\n\nThis checks execution, not upstream data completeness.\n`);
if (failures.length) {
  const body = `${marker}\nScheduled refresh requires investigation. Latest due cycle: ${assessment[0].due}.\n\n${report}\n\n${runs[0]?.html_url ?? 'No scheduled runs found.'}\n\nExisting data must be retained. Inspect source failures; do not bypass authentication, rate limits or document access restrictions.\n\nSubscribe to this issue/repository for GitHub notifications. No email delivery guarantee is implied.`;
  if (!incident) await api('issues', 'POST', { title, body });
  else if (incident.body !== body) await api(`issues/${incident.number}`, 'PATCH', { body });
  process.exitCode = 1;
} else if (incident) {
  await api(`issues/${incident.number}/comments`, 'POST', { body: 'Recovered: all six partitions have successful scheduled completion in the due window. Source completeness remains a separate check.' });
  await api(`issues/${incident.number}`, 'PATCH', { state: 'closed', state_reason: 'completed' });
}
