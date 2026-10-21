import test from 'node:test';
import assert from 'node:assert/strict';
import { createModel, start, pause, resume, finish, settle, elapsed, getView, nextDeadline, normalizeWebsite, validatePreferences } from '../browser/focus.mjs';

test('countdown accepts ten seconds through twenty-four hours and rejects invalid preferences before start', () => {
  const model = createModel();
  for (const durationMinutes of [0.01, 0.1, 1440.01]) {
    assert.throws(() => validatePreferences({timerMode:'Countdown',durationMinutes}));
    assert.throws(() => start(model,{timerMode:'Countdown',durationMinutes},0));
    assert.equal(model.session,null);
  }
  for (const durationMinutes of [10/60,1440]) assert.equal(validatePreferences({timerMode:'Countdown',durationMinutes}).durationMinutes,durationMinutes);
});

test('less than ten effective seconds is discarded and exactly ten seconds counts', () => {
  for (const [duration,discarded] of [[9999,true],[10000,false]]) {
    const model=createModel(); start(model,{timerMode:'CountUp'},0); pause(model,8000); resume(model,68000); finish(model,68000+duration-8000);
    assert.equal(model.session.elapsedMs,duration); assert.equal(model.session.discarded,discarded);
    assert.equal(getView(model,100000).state.shortFocus,discarded); assert.equal(getView(model,100000).effective,false);
    if(discarded) assert.match(getView(model,100000).state.phaseText,/未计入/);
  }
  const model=createModel(); start(model,{timerMode:'Countdown',durationMinutes:10/60,restMinutes:1},0);
  finish(model,9000); assert.equal(model.session.state,'Resting'); assert.match(getView(model,9500).state.phaseText,/未计入/);
  settle(model,70000); assert.equal(getView(model,70000).state.stateText,'本次未计入');
});

test('count-up ends at twenty-four effective hours, releases websites and preserves a notice across restart', () => {
  const model=createModel(); const hour=3600000;
  start(model,{timerMode:'CountUp',restMinutes:15},0); pause(model,23*hour); resume(model,23*hour+60000);
  assert.equal(nextDeadline(model.session),24*hour+60000);
  settle(model,30*hour); assert.equal(model.session.state,'Ended'); assert.equal(model.session.elapsedMs,24*hour);
  assert.equal(getView(model,30*hour).effective,false); assert.equal(model.session.endReason,'DurationLimit');
  assert.equal(nextDeadline(model.session),null); assert.equal(model.limitNotice,24*hour+60000);
  const restored=JSON.parse(JSON.stringify(model)); settle(restored,72*hour); assert.equal(restored.session.state,'Ended');
  assert.equal(getView(restored,72*hour).limitNotice,24*hour+60000);
  start(restored,{},72*hour); assert.equal(restored.session.elapsedMs,0);
});

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
