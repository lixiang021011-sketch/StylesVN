/*
 * 把网页版剧本（js/story.js + js/data.js）编译成 Unity 的 JSON 内容库。
 * 用法: node tools/convert_from_js.js [源目录] [输出目录]
 *   默认源目录: ../../styles/js   （即网页原型）
 *   默认输出:   ../Assets/StreamingAssets/content
 */
const fs = require('fs');
const path = require('path');

const SRC = path.resolve(__dirname, process.argv[2] || '../../styles/js');
const OUT = path.resolve(__dirname, process.argv[3] || '../Assets/StreamingAssets/content');
const BG_PREFIX = 'styles_';

const code = ['data', 'story'].map(f => fs.readFileSync(path.join(SRC, f + '.js'), 'utf8')).join('\n');
const mod = { exports: {} };
new Function('module', code + '\nmodule.exports={CAST,CLUES,SCENES,ASK,STORY,ACCUSE,FAITH};')(mod);
const { CAST, CLUES, SCENES, ASK, STORY, ACCUSE, FAITH } = mod.exports;

const bg = (id) => (!id ? id : (String(id).startsWith(BG_PREFIX) ? id : BG_PREFIX + id));
const fig = (f) => ({ id: f[0], pos: f[1] || 'mid', emotion: f[2] || 'neutral', flip: f[3] || false, dim: false });
// Noto Serif SC 不含 emoji 字形：把 emoji 图标统一换成中文字体里存在的几何符号，避免游戏内出现方框
const ICON = (s) => (!s ? '' : (/[\u25A0-\u25FF\u2190-\u21FF\u3000-\u303F\uFF00-\uFFEF]/.test(s) && !/[\u{1F000}-\u{1FAFF}\u2600-\u27BF]/u.test(s) ? s : '◆'));

// 知识卡的正文在原稿里叫 html，而且是网页标签：TMP 只认 <b>/<i>/<color> 和换行，
// 所以 <p> 要变成换行、<br> 要变成换行，否则游戏里会把标签原样打出来。
const TMP = (s) => (!s ? '' : String(s)
  .replace(/<\/p>\s*<p>/gi, '\n')
  .replace(/<p>/gi, '')
  .replace(/<\/p>/gi, '\n')
  .replace(/<br\s*\/?>/gi, '\n')
  .replace(/<strong>/gi, '<b>').replace(/<\/strong>/gi, '</b>')
  .replace(/<em>/gi, '<i>').replace(/<\/em>/gi, '</i>')
  .replace(/\n{3,}/g, '\n\n')
  .trim());

// ---------- 章节拆分 ----------
const chapters = [];
let cur = null;
for (const b of STORY) {
  if (b.t === 'chapter') {
    cur = { id: 'ch' + String(chapters.length + 1).padStart(2, '0'), act: b.tag || '', title: b.title || '', summary: b.sub || '', beats: [] };
    chapters.push(cur);
    continue;
  }
  if (!cur) { cur = { id: 'ch01', act: '序章', title: '斯泰尔斯', summary: '', beats: [] }; chapters.push(cur); }
  const o = { t: b.t };
  if (b.who) o.who = b.who;
  if (b.text) o.text = b.text;
  if (b.figs) o.figs = b.figs.map(fig);
  if (b.fx) o.fx = b.fx;
  // 台词上挂的「念到这句就把某条证据记入笔记本」——以前没映射，
  // 于是 4 条证据在游戏里永远发不出来（当时是直接改 JSON 补的）
  if (b.give) o.give = b.give;
  if (b.tag) o.tag = b.tag;
  if (b.title) o.title = b.title;
  if (b.sub) o.sub = b.sub;
  if (b.icon) o.icon = ICON(b.icon);
  if (b.body) o.body = b.body;
  if (b.q || b.prompt) o.prompt = b.prompt || b.q;
  switch (b.t) {
    case 'bg': o.id = bg(b.id); break;
    case 'say': o.id = undefined; break;
    case 'choice':
      o.options = (b.opts || []).map(x => ({ text: x.t, hint: x.hint || '', score: x.s || 0, give: x.give || '', goto: x.goto || '' }));
      delete o.prompt; o.prompt = b.q || '';
      break;
    case 'deduce':
      o.question = b.q; o.answers = b.opts; o.answer = b.a; o.explain = b.why || '';
      o.wrong = b.wrong || []; o.give = b.give || ''; o.insight = b.insight || ''; o.score = b.score || 0;
      o.tag = b.tag || '推理';
      break;
    case 'inv':
      o.t = 'investigate'; o.scene = b.scene; break;
    case 'ask': o.chars = b.chars; o.need = b.need; o.title = b.title; break;
    // 演出特效：原稿写的是 FX('shake'|'fade'|'flash')，这里以前漏了映射，
    // 结果转换出来的 fx 指令没有参数（游戏里就是三条空指令）。
    case 'fx': o.fx = b.name; break;
    // 知识卡正文：原稿字段是 html，Unity 端读的是 body（这里以前整段丢了，卡片只剩标题和图标）
    case 'note': o.body = TMP(b.html || b.body || ''); break;
    case 'accuse': case 'ending': break;
    default: break;
  }
  if (o.id === undefined) delete o.id;
  cur.beats.push(o);
}

// ---------- 数据库 ----------
const characters = Object.keys(CAST).map(id => ({
  id, name: CAST[id].n, role: CAST[id].r, description: CAST[id].d, emotions: ['neutral']
}));

const evidence = Object.keys(CLUES).map(id => ({
  id, name: CLUES[id].n, category: CLUES[id].cat, source: CLUES[id].src, description: CLUES[id].d, insight: '', icon: ''
}));

const investigations = Object.keys(SCENES).map(id => ({
  id: BG_PREFIX + 'scene_' + id,
  bg: bg(SCENES[id].bg),
  title: SCENES[id].title,
  hint: SCENES[id].hint,
  intro: SCENES[id].intro,
  need: SCENES[id].need,
  spots: SCENES[id].spots.map(s => ({ x: s.x, y: s.y, icon: ICON(s.ic), clue: s.clue, text: s.t }))
}));

const interrogations = Object.keys(ASK).map(id => ({
  id,
  name: ASK[id].name,
  role: ASK[id].role,
  topics: ASK[id].topics.map(t => ({
    id: t.id, label: t.label, need: t.need || '', give: t.give || '',
    lines: t.lines.map(l => ({ who: l.w, text: l.x }))
  }))
}));

const accusation = ACCUSE.map(q => ({ question: q.q, options: q.opts, answer: q.a, explain: q.why }));
const faith = FAITH.map(r => ({ item: r[0], source: r[1], kind: r[2] }));

// inv 场景引用修正：剧本里的 scene 名 -> 调查表里的 id
for (const ch of chapters)
  for (const b of ch.beats)
    if (b.t === 'investigate' && b.scene) b.scene = BG_PREFIX + 'scene_' + b.scene;

// ---------- 写出 ----------
fs.mkdirSync(path.join(OUT, 'chapters'), { recursive: true });
const write = (file, obj) => fs.writeFileSync(path.join(OUT, file), JSON.stringify(obj, null, 2), 'utf8');

const index = {
  chapters: [], characters: ['characters.json'], evidence: ['evidence.json'],
  investigations: ['investigations.json'], interrogations: ['interrogations.json']
};
for (const ch of chapters) {
  const file = 'chapters/' + ch.id + '.json';
  write(file, ch);
  index.chapters.push(file);
}
write('index.json', index);
write('characters.json', characters);
write('evidence.json', evidence);
write('investigations.json', investigations);
write('interrogations.json', interrogations);
write('accusation.json', accusation);
write('faith.json', faith);

// ---------- 统计 ----------
let chars = 0, says = 0, beats = 0;
for (const ch of chapters) for (const b of ch.beats) { beats++; if (b.t === 'say') { says++; chars += (b.text || '').replace(/<[^>]+>/g, '').length; } }
let askChars = 0, askLines = 0;
for (const a of interrogations) for (const t of a.topics) for (const l of t.lines) { askLines++; askChars += l.text.length; }
const stats = {
  chapters: chapters.length, beats, says, textChars: chars, askChars, askLines,
  minutesAt250: Math.round((chars + askChars) / 250),
  evidence: evidence.length, characters: characters.length, investigations: investigations.length,
  accusations: accusation.length,
  backgroundsNeeded: Array.from(new Set([...investigations.map(i => i.bg), ...chapters.flatMap(c => c.beats.filter(b => b.t === 'bg').map(b => b.id))])).sort(),
  figuresNeeded: Array.from(new Set(chapters.flatMap(c => c.beats.flatMap(b => (b.figs || []).map(f => f.id + '|' + f.emotion))))).sort()
};
write('stats.json', stats);

console.log('已输出到 ' + OUT);
console.log(JSON.stringify(stats, null, 2));
