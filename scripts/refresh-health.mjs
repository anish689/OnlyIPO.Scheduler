export const partitions = ['open', 'upcoming', 'closed', 'listed', 'tracking', 'financials'];

// Allow two hours for dispatch and bounded serial work before declaring a missed cycle.
export function dueCycle(now) {
  const cutoff = new Date(now.getTime() - 2 * 60 * 60 * 1000);
  const day = Date.UTC(cutoff.getUTCFullYear(), cutoff.getUTCMonth(), cutoff.getUTCDate());
  return new Date([day + 13 * 3600000 + 17 * 60000, day + 4 * 3600000 + 17 * 60000,
    day - 86400000 + 13 * 3600000 + 17 * 60000].find(time => time <= cutoff.getTime()));
}

export function assessRefresh(runs, jobs, now = new Date()) {
  const due = dueCycle(now);
  const scheduled = runs.filter(run => run.event === 'schedule' && run.head_branch === 'main'
    && Date.parse(run.created_at) <= now.getTime()).sort((a, b) => Date.parse(b.created_at) - Date.parse(a.created_at));
  return partitions.map(partition => {
    const name = `refresh (${partition})`;
    const latestJob = (jobs[scheduled[0]?.id] ?? []).find(job => job.name === name);
    const success = scheduled.find(run => Date.parse(run.created_at) >= due.getTime()
      && (jobs[run.id] ?? []).some(job => job.name === name && job.conclusion === 'success'
        && Date.parse(job.completed_at) <= now.getTime()));
    const failed = latestJob && ['failure', 'cancelled', 'timed_out', 'action_required'].includes(latestJob.conclusion);
    return { partition, healthy: Boolean(success) && !failed,
      reason: failed ? latestJob.conclusion : success ? 'completed' : 'no successful scheduled completion in the due window',
      url: scheduled[0]?.html_url ?? null, due: due.toISOString() };
  });
}
