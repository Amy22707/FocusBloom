export const palettes = [
  { id:'fog-blue', name:'莫兰迪 · 雾蓝', light:'#627f90', dark:'#9cafbf' },
  { id:'sage', name:'莫兰迪 · 鼠尾草绿', light:'#718578', dark:'#a2b69e' },
  { id:'cream', name:'马卡龙 · 奶油杏', light:'#ad8566', dark:'#d7b599' },
  { id:'pink', name:'马卡龙 · 樱花粉', light:'#ae748d', dark:'#d3a7bb' },
  { id:'mint', name:'马卡龙 · 薄荷绿', light:'#668b7e', dark:'#9ac5b3' },
  { id:'lilac', name:'马卡龙 · 淡紫', light:'#927daf', dark:'#b7a4d0' }
];

export function applyTheme(preferences) {
  const palette = palettes.find(item => item.id === preferences.palette) || palettes[0];
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
}
