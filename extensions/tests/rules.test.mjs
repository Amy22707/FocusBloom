import test from 'node:test';
import assert from 'node:assert/strict';
import { createRules, isAllowed } from '../browser/rules.mjs';

test('session rules redirect only top level pages, exact and subdomain rules agree with access decisions', () => {
  const websites = [{ domain:'example.edu', includeSubdomains:true }, { domain:'login.edu', includeSubdomains:false }];
  const rules = createRules(true, websites, 'chrome-extension://test/blocked.html');
  assert.equal(rules[0].action.redirect.regexSubstitution, 'chrome-extension://test/blocked.html#\\0');
  assert.deepEqual(rules[0].condition.resourceTypes, ['main_frame']);
  for (const url of ['https://example.edu/', 'https://library.example.edu:8443/lesson', 'https://example.edu.other.edu/', 'https://login.edu/', 'https://other.login.edu/']) {
    const allowedRule = rules.slice(1).some(rule => new RegExp(rule.condition.regexFilter, 'i').test(url));
    assert.equal(allowedRule, isAllowed(url, true, websites), url);
  }
  assert.equal(isAllowed('https://other.edu/', false, websites), true);
  assert.equal(isAllowed('https://example.edu/', true, []), false);
  assert.deepEqual(createRules(false, websites, ''), []);
});

test('blacklist redirects listed page domains and allows all other page domains', () => {
  const websites = [{ domain:'example.edu', includeSubdomains:true }, { domain:'login.edu', includeSubdomains:false }];
  const rules = createRules(true, websites, 'chrome-extension://test/blocked.html', 'Blacklist');
  assert.equal(rules.length, 2);
  for (const url of ['https://example.edu/', 'https://library.example.edu:8443/lesson', 'https://example.edu.other.edu/', 'https://login.edu/', 'https://other.login.edu/']) {
    const blockedRule = rules.some(rule => new RegExp(rule.condition.regexFilter, 'i').test(url));
    assert.equal(blockedRule, !isAllowed(url, true, websites, 'Blacklist'), url);
  }
  assert.deepEqual(createRules(true, [], '', 'Blacklist'), []);
});
