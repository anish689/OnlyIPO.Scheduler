export const partitions = ['open', 'upcoming', 'closed', 'listed', 'tracking', 'financials'];

export function refreshPolicy(environment = 'staging') {
  if (!['staging', 'production'].includes(environment)) throw new Error('Unknown refresh environment.');
  const production = environment === 'production';
  return {
    environment,
    label: production ? 'Production' : 'Staging',
    workflow: `${environment}-refresh.yml`,
    hours: production ? [7, 16] : [4, 13],
    minute: production ? 7 : 17,
    // Production's six serial job budgets total 130 minutes, plus dispatch grace.
    graceHours: production ? 3 : 2,
    marker: `<!-- youripo-${environment}-refresh-watchdog -->`
  };
}

export function dueCycle(now, policy = refreshPolicy()) {
  const cutoff = new Date(now.getTime() - policy.graceHours * 60 * 60 * 1000);
  const day = Date.UTC(cutoff.getUTCFullYear(), cutoff.getUTCMonth(), cutoff.getUTCDate());
  const candidates = [day, day - 86400000].flatMap(date => policy.hours.map(hour =>
    date + hour * 3600000 + policy.minute * 60000));
  return new Date(Math.max(...candidates.filter(time => time <= cutoff.getTime())));
}

export function assessRefresh(runs, jobs, now = new Date(), policy = refreshPolicy()) {
  const due = dueCycle(now, policy);
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
