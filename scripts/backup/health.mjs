// A completed workflow alone is insufficient: the upload step must have passed.
export function assessBackup(runs, jobs, now) {
  const due = new Date(now);
  due.setUTCHours(19, 43, 0, 0);
  if (now.getTime() < due.getTime() + 2 * 60 * 60 * 1000) due.setUTCDate(due.getUTCDate() - 1);
  const valid = runs.filter(run => run.head_branch === 'main'
    && ['schedule', 'workflow_dispatch'].includes(run.event)
    && new Date(run.created_at) >= due && new Date(run.created_at) <= now);
  const verified = valid.find(run => run.conclusion === 'success'
    && (jobs[run.id] || []).some(job => job.name === 'backup' && job.conclusion === 'success'
      && job.steps?.some(step => step.name === 'Export, restore-check, encrypt and verify private Drive copy'
        && step.conclusion === 'success')));
  return { healthy: !!verified, due: due.toISOString(),
    reason: verified ? 'Verified offsite backup completed in the due window.' : 'No verified offsite backup in the due window.',
    runId: verified?.id ?? null };
}
