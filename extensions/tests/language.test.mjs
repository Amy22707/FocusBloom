import test from 'node:test';
import assert from 'node:assert/strict';
import { createModel, getView, start } from '../browser/focus.mjs';
import { translator } from '../browser/language.mjs';

test('English translates timer labels while keeping saved project names', () => {
  const model = createModel();
  model.preferences.language = 'en';
  assert.equal(getView(model, 1000).state.stateText, 'Ready');
  start(model, { project:'阅读' }, 1000);
  const running = getView(model, 2000);
  assert.equal(running.state.stateText, 'Focusing');
  assert.equal(running.state.project, '阅读');
  model.preferences.language = 'zh-CN';
  assert.equal(getView(model, 2000).state.stateText, '专注中');
});

test('extension language supports interpolated labels and empty messages', () => {
  const en = translator('en');
  assert.equal(en('{0} 次专注 · 合计 {1}', 2, '00:31:00'), 'Sessions: 2 · Total: 00:31:00');
  assert.equal(en(''), '');
});
