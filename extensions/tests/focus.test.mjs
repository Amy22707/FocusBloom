import test from 'node:test';
import assert from 'node:assert/strict';
import { createModel, start, pause, resume, finish, settle, elapsed, getView, nextDeadline, normalizeWebsite } from '../browser/focus.mjs';

test('count-up is manual, pause excludes time and auto-resume uses the scheduled instant', () => {
  const model = createModel();
  start(model, { timerMode:'CountUp', pauseMinutes:5 }, 0);
  assert.equal(elapsed(model.session, 30 * 60000), 30 * 60000);
  assert.equal(settle(model, 30 * 60000), false);
  pause(model, 30 * 60000);
  assert.equal(getView(model, 31 * 60000).effective, true);
  assert.equal(elapsed(model.session, 31 * 60000), 30 * 60000);
  settle(model, 36 * 60000);
  assert.equal(elapsed(model.session, 36 * 60000), 31 * 60000);
  finish(model, 36 * 60000);
  assert.equal(getView(model, 36 * 60000).effective, false);
});

test('rest and countdown settle at real deadlines after a suspended worker or browser restart', () => {
  const model = createModel();
  start(model, { timerMode:'Countdown', durationMinutes:2, pauseMinutes:1, restMinutes:1 }, 0);
  pause(model, 60000);
  const restored = JSON.parse(JSON.stringify(model));
  settle(restored, 181000);
  assert.equal(restored.session.state, 'Resting');
  assert.equal(restored.session.endedAt, 180000);
  assert.equal(restored.session.elapsedMs, 120000);
  assert.equal(nextDeadline(restored.session), 240000);
  assert.equal(getView(restored, 181000).effective, false);
  settle(restored, 300000);
  assert.equal(restored.session.state, 'Ended');
  assert.equal(nextDeadline(restored.session), null);
});

test('manual resume preserves elapsed time and session rules remain the start snapshot', () => {
  const model = createModel();
  model.websiteMode = 'Blacklist';
  model.lists.Blacklist = [{ domain:'distraction.test', includeSubdomains:true }];
  start(model, { timerMode:'Countdown', durationMinutes:2 }, 0);
  model.lists.Blacklist[0].domain = 'other.test'; model.websiteMode = 'Whitelist';
  pause(model, 60000);
  resume(model, 90000);
  assert.equal(nextDeadline(model.session), 150000);
  assert.equal(getView(model, 90000).state.timer, '00:01:00');
  assert.equal(getView(model, 90000).state.websiteMode, 'Blacklist');
  assert.equal(model.session.websites[0].domain, 'distraction.test');
  model.enabled = false;
  assert.equal(getView(model, 90000).effective, false);
  model.enabled = true;
  assert.equal(getView(model, 90000).effective, true);
});

test('website entry accepts normal domains and URLs and rejects unsupported schemes', () => {
  assert.equal(normalizeWebsite(' https://Library.Example.edu:8443/lesson?q=1 '), 'library.example.edu');
  assert.equal(normalizeWebsite('example.edu'), 'example.edu');
  for (const address of ['', 'not a domain', 'file:///tmp/lesson', 'chrome://settings']) assert.throws(() => normalizeWebsite(address));
});
