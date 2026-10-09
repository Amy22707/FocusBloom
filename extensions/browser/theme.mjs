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
  const colors = dark ? { background:'#20262c', card:'#2a323a', text:'#eef0f2', muted:'#b0b8c0', border:'#414b55', primary:'#20262c' }
    : { background:'#f6f7f8', card:'#ffffff', text:'#303944', muted:'#707986', border:'#e6ebed', primary:'#ffffff' };
  colors.accent = dark ? palette.dark : palette.light;
  for (const [key, value] of Object.entries(colors)) document.documentElement.style.setProperty('--' + key, value);
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light';
}
