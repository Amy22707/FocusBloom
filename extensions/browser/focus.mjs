export const defaultPreferences = {
  project: '', timerMode: 'CountUp', durationMinutes: 25, pauseMinutes: 5, restMinutes: 0,
  palette: 'fog-blue', appearance: 'System'
};

export function createModel() {
  return { enabled: true, preferences: { ...defaultPreferences }, websiteMode: 'Whitelist',
    lists: { Whitelist: [], Blacklist: [] }, session: null };
}

export function isActive(session) {
  return session?.state === 'Focusing' || session?.state === 'Paused';
}

export function elapsed(session, now) {
  return session ? session.elapsedMs + (session.state === 'Focusing' ? Math.max(0, now - session.phaseStartedAt) : 0) : 0;
}

export function nextDeadline(session) {
  if (!session) return null;
  if (session.state === 'Focusing') return session.timerMode === 'Countdown'
    ? session.phaseStartedAt + Math.max(0, session.durationMs - session.elapsedMs) : null;
  return ['Paused', 'Resting'].includes(session.state) ? session.deadline : null;
}

export function settle(model, now) {
  const session = model.session;
  let changed = false;
  if (session?.state === 'Paused' && now >= session.deadline) {
    session.state = 'Focusing'; session.phaseStartedAt = session.deadline; session.deadline = null; changed = true;
  }
  const due = nextDeadline(session);
  if (session?.state === 'Focusing' && due !== null && now >= due) {
    finish(model, due); changed = true;
  }
  if (session?.state === 'Resting' && now >= session.deadline) {
    session.state = 'Ended'; session.deadline = null; changed = true;
  }
  return changed;
}

export function validatePreferences(fields, previous = defaultPreferences) {
  const preferences = { ...previous, ...fields };
  if (!['CountUp', 'Countdown'].includes(preferences.timerMode)) throw new Error('请选择正计时或倒计时。');
  for (const key of ['durationMinutes', 'pauseMinutes', 'restMinutes']) {
    preferences[key] = Number(preferences[key]);
    if (!Number.isFinite(preferences[key]) || preferences[key] < 0 || (key !== 'restMinutes' && preferences[key] === 0))
      throw new Error('专注与暂停时长需大于零；休息时长可以为零。');
  }
  preferences.project = String(preferences.project).trim();
  return preferences;
}

export function start(model, fields, now) {
  if (isActive(model.session) || model.session?.state === 'Resting') throw new Error('请先结束当前专注或休息。');
  model.preferences = validatePreferences(fields, model.preferences);
  const preferences = model.preferences;
  model.session = { state: 'Focusing', project: preferences.project || '这一刻，专注学习',
    timerMode: preferences.timerMode, durationMs: preferences.durationMinutes * 60000,
    pauseMs: preferences.pauseMinutes * 60000, restMs: preferences.restMinutes * 60000,
    elapsedMs: 0, phaseStartedAt: now, deadline: null,
    websiteMode: model.websiteMode, websites: model.lists[model.websiteMode].map(rule => ({ ...rule })) };
}

export function pause(model, now) {
  if (model.session?.state !== 'Focusing') throw new Error('当前没有正在进行的专注。');
  const session = model.session;
  session.elapsedMs = elapsed(session, now); session.state = 'Paused'; session.deadline = now + session.pauseMs;
}

export function resume(model, now) {
  if (model.session?.state !== 'Paused') throw new Error('当前专注没有暂停。');
  model.session.state = 'Focusing'; model.session.phaseStartedAt = now; model.session.deadline = null;
}

export function finish(model, now) {
  if (!isActive(model.session)) throw new Error('当前没有可结束的专注。');
  const session = model.session;
  session.elapsedMs = elapsed(session, now); session.endedAt = now;
  session.state = session.restMs > 0 ? 'Resting' : 'Ended';
  session.deadline = session.restMs > 0 ? now + session.restMs : null;
}

export function formatTime(milliseconds, roundUp = false) {
  const seconds = Math.max(0, (roundUp ? Math.ceil : Math.floor)(milliseconds / 1000));
  return [Math.floor(seconds / 3600), Math.floor(seconds / 60) % 60, seconds % 60]
    .map(value => String(value).padStart(2, '0')).join(':');
}

export function getView(model, now) {
  const session = model.session;
  const active = isActive(session);
  const effective = model.enabled && active;
  const state = session?.state || 'Ready';
  const spent = elapsed(session, now);
  const display = session?.timerMode === 'Countdown' && active ? Math.max(0, session.durationMs - spent) : spent;
  const remaining = ['Paused', 'Resting'].includes(state) ? Math.max(0, session.deadline - now) : 0;
  return { enabled: model.enabled, effective, preferences: model.preferences,
    state: { state, stateText: { Ready:'准备开始', Focusing:'专注中', Paused:'已暂停', Resting:'休息中', Ended:'本次已结束' }[state],
      project: session?.project || '这一刻，专注学习', timer: formatTime(display, session?.timerMode === 'Countdown' && active),
      phaseText: state === 'Paused' ? `${formatTime(remaining, true)} 后自动继续`
        : state === 'Resting' ? `休息剩余 ${formatTime(remaining, true)}` : `有效专注 ${formatTime(spent)}`,
      canEditRules: !active, canConfigure: ['Ready', 'Ended'].includes(state),
      websiteMode: active ? session.websiteMode : model.websiteMode, savedWebsiteMode: model.websiteMode,
      websites: model.lists[model.websiteMode], sessionWebsites: session?.websites || [] } };
}

export function normalizeWebsite(address) {
  try {
    const input = address.trim();
    const url = new URL(input.includes('://') ? input : 'https://' + input);
    if (!['https:', 'http:'].includes(url.protocol) || !url.hostname) throw new Error();
    return url.hostname.toLowerCase().replace(/\.$/, '');
  } catch { throw new Error('请输入有效域名或 http／https 网址。'); }
}
