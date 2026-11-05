export const palettes = [
  { id:'fog-blue', name:'水色', light:'#487a8b', dark:'#9ecbd7' },
  { id:'ink-blue', name:'墨蓝', light:'#4c668d', dark:'#adc5eb' },
  { id:'sage', name:'鼠尾草绿', light:'#718578', dark:'#a2b69e' },
  { id:'mint', name:'薄荷绿', light:'#668b7e', dark:'#9ac5b3' },
  { id:'pine', name:'松针绿', light:'#467467', dark:'#9ccfba' },
  { id:'cream', name:'奶油杏', light:'#ad8566', dark:'#d7b599' },
  { id:'ochre', name:'琥珀金', light:'#896329', dark:'#e4c17c' },
  { id:'tea', name:'茶棕', light:'#8c604e', dark:'#d9ae99' },
  { id:'pink', name:'樱花粉', light:'#ae748d', dark:'#d3a7bb' },
  { id:'rosewood', name:'玫瑰木', light:'#925a60', dark:'#e2aab0' },
  { id:'lilac', name:'淡紫', light:'#927daf', dark:'#b7a4d0' },
  { id:'plum', name:'梅子紫', light:'#775883', dark:'#d0b0dd' }
];

export function applyTheme(preferences) {
  const custom = preferences.customColor;
  const mixCustom = (target, amount) => '#' + [1, 3, 5].map(index => {
    const channel = Number.parseInt(custom.slice(index, index + 2), 16);
    return Math.round(channel + (target - channel) * amount).toString(16).padStart(2, '0');
  }).join('');
  const palette = preferences.palette === 'custom' && /^#[0-9a-f]{6}$/i.test(custom)
    ? { light:mixCustom(0, .24), dark:mixCustom(255, .38) }
    : palettes.find(item => item.id === preferences.palette) || palettes[0];
  const dark = preferences.appearance === 'Dark' || preferences.appearance === 'System' && matchMedia('(prefers-color-scheme: dark)').matches;
  const colors = dark ? { card:'#242832', text:'#e5e9f0', muted:'#a6afbe', primary:'#1b2430' }
    : { card:'#ffffff', text:'#303944', muted:'#76808d', primary:'#ffffff' };
  colors.accent = dark ? palette.dark : palette.light;
  const mix = amount => '#' + [1,3,5].map(start => {
    const base = parseInt(colors.card.slice(start,start+2),16);
    return Math.round(base + (parseInt(colors.accent.slice(start,start+2),16)-base)*amount).toString(16).padStart(2,'0');
  }).join('');
  colors.background = dark ? '#191d25' : mix(.035);
  colors.border = mix(dark ? .23 : .13);
  colors.soft = mix(.08);
  colors.selected = mix(dark ? .20 : .17);
  for (const [key, value] of Object.entries(colors)) document.documentElement.style.setProperty('--' + key, value);
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light';
  document.documentElement.style.setProperty('--timer-scale', (preferences.timerScale || 100) / 100);
}
