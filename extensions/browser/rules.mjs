export function isAllowed(address, restricted, websites, mode = 'Whitelist') {
  if (!restricted) return true;
  try {
    const url = new URL(address);
    if (!['http:', 'https:'].includes(url.protocol)) return true;
    const host = url.hostname.toLowerCase().replace(/\.$/, '');
    const listed = websites.some(rule => host === rule.domain || (rule.includeSubdomains && host.endsWith('.' + rule.domain)));
    return mode === 'Blacklist' ? !listed : listed;
  } catch { return true; }
}

export function createRules(restricted, websites, blockedPageUrl, mode = 'Whitelist') {
  if (!restricted) return [];
  const redirect = { type: 'redirect', redirect: { regexSubstitution: blockedPageUrl + '#\\0' } };
  const rules = mode === 'Blacklist' ? [] : [{ id: 1, priority: 1,
    action: { type: 'redirect', redirect: { regexSubstitution: blockedPageUrl + '#\\0' } },
    condition: { regexFilter: '^https?://.*', resourceTypes: ['main_frame'] }
  }];
  for (let index = 0; index < websites.length; index++) {
    const rule = websites[index];
    const escaped = rule.domain.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const subdomains = rule.includeSubdomains ? '(?:[a-z0-9_-]+\\.)*' : '';
    rules.push({ id: index + 2, priority: 2, action: mode === 'Blacklist' ? redirect : { type: 'allow' },
      condition: { regexFilter: '^https?://' + subdomains + escaped + '(?::[0-9]+)?(?:/|$)',
        isUrlFilterCaseSensitive: false, resourceTypes: ['main_frame'] }
    });
  }
  return rules;
}
