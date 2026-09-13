using System;
using System.Collections.Generic;
using Styles.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Styles.UI
{
    /// <summary>通用弹层：暗底 + 金边卡片 + 标题 + 内容区 + 底部按钮。</summary>
    public class OverlayPanel
    {
        public readonly RectTransform Root, Card, Body, Footer;
        readonly TMP_Text _title;
        readonly RectTransform _titleBar;
        public bool IsOpen { get { return Root.gameObject.activeSelf; } }

        public OverlayPanel(RectTransform parent)
        {
            Root = UiFactory.Stretch("Overlay", parent);
            var dim = UiFactory.StretchImage("Dim", Root, new Color(0, 0, 0, .78f));
            dim.raycastTarget = true;

            Card = UiFactory.Rect("Card", Root, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(-760, -470), new Vector2(760, 470));
            var cardBg = Card.gameObject.AddComponent<Image>();
            cardBg.color = UiTheme.Hex("#101B17");
            Deco.Border(Card, UiTheme.Gold, 2f, 0f);

            _titleBar = UiFactory.Panel("TitleBar", Card, UiTheme.WithAlpha(UiTheme.Ink, .9f),
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -84), new Vector2(0, 0)).rectTransform;
            _title = UiFactory.Text("Title", _titleBar, "", 36, UiTheme.GoldLight, TextAlignmentOptions.Left, 1.2f);
            _title.rectTransform.offsetMin = new Vector2(32, 0);
            _title.rectTransform.offsetMax = new Vector2(-120, 0);

            var close = UiFactory.Button("Close", _titleBar, "×", 28, Close, UiTheme.WithAlpha(UiTheme.Ink, .6f), UiTheme.PaperDim);
            var cr = close.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(1, .5f); cr.anchorMax = new Vector2(1, .5f);
            cr.offsetMin = new Vector2(-84, -30); cr.offsetMax = new Vector2(-16, 30);
            cr.pivot = new Vector2(1, .5f);
            UiFactory.Size(close.gameObject, 60f, 68f);

            Body = UiFactory.Rect("Body", Card, Vector2.zero, Vector2.one, new Vector2(36, 108), new Vector2(-36, -104));
            Footer = UiFactory.Rect("Footer", Card, new Vector2(0, 0), new Vector2(1, 0), new Vector2(36, 24), new Vector2(-36, 100));
            var fh = Footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            fh.spacing = 14; fh.childAlignment = TextAnchor.MiddleCenter;
            fh.childForceExpandWidth = false; fh.childForceExpandHeight = false;
            fh.childControlWidth = true; fh.childControlHeight = true;

            Root.gameObject.SetActive(false);
        }

        public void Open(string title)
        {
            // 标题画面是在 BuildCanvas 之后才建出来的，天然盖在弹层之上；
            // 把整个弹层提到最前，才不至于「点了没反应」。
            if (Root.parent != null) Root.parent.SetAsLastSibling();
            // 换页时必须清空旧内容：否则菜单的按钮会留在存档页/设置页上，还能点。
            UiFactory.Clear(Body);
            UiFactory.Clear(Footer);
            _title.text = title;
            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
        }

        public void Close()
        {
            Root.gameObject.SetActive(false);
            UiFactory.Clear(Body);
            UiFactory.Clear(Footer);
        }

        public Button AddFooterButton(string label, Action onClick, bool primary = false, float width = 260f,
                                      int fontSize = 0)
        {
            var b = UiFactory.Button("Btn_" + label, Footer, label,
                fontSize > 0 ? fontSize : UiTheme.FontButton, onClick,
                primary ? UiTheme.Gold : UiTheme.WithAlpha(UiTheme.Ink, .9f),
                primary ? UiTheme.Ink : UiTheme.Paper);
            UiFactory.Size(b.gameObject, 78f, width);
            return b;
        }
    }

    /// <summary>章节卡：全屏标题动画。</summary>
    public class ChapterCard
    {
        readonly RectTransform _root, _card;
        readonly TMP_Text _tag, _title, _sub;
        readonly CanvasGroup _cg;
        readonly MonoBehaviour _host;
        bool _cancelled;                 // 卡片演到一半被 Hide() 掉了，结束时不能再推剧情

        public ChapterCard(MonoBehaviour host, RectTransform parent)
        {
            _host = host;
            _root = UiFactory.Stretch("ChapterCard", parent);
            var dim = UiFactory.StretchImage("Dim", _root, new Color(0, 0, 0, .92f));
            dim.raycastTarget = true;
            _card = UiFactory.Rect("Card", _root, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(-780, -260), new Vector2(780, 260));
            _cg = _card.gameObject.AddComponent<CanvasGroup>();
            _tag = UiFactory.Text("Tag", _card, "", UiTheme.FontSmall + 2, UiTheme.Gold, TextAlignmentOptions.Center, 1.2f);
            Anchor(_tag, .62f, .78f);
            _title = UiFactory.Text("Title", _card, "", UiTheme.FontChapter + 8, UiTheme.Paper, TextAlignmentOptions.Center, 1.25f);
            Anchor(_title, .38f, .66f);
            _sub = UiFactory.Text("Sub", _card, "", UiTheme.FontSmall + 4, UiTheme.PaperDim, TextAlignmentOptions.Center, 1.3f);
            Anchor(_sub, .18f, .38f);
            _root.gameObject.SetActive(false);
        }

        static void Anchor(TMP_Text t, float y0, float y1)
        {
            t.rectTransform.anchorMin = new Vector2(0, y0);
            t.rectTransform.anchorMax = new Vector2(1, y1);
            t.rectTransform.offsetMin = new Vector2(40, 0);
            t.rectTransform.offsetMax = new Vector2(-40, 0);
        }

        public void Show(Beat b, Action next)
        {
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _cancelled = false;
            _tag.text = b.tag ?? "";
            _title.text = b.title ?? "";
            _sub.text = string.IsNullOrEmpty(b.sub) ? "" : "— " + b.sub + " —";
            _host.StartCoroutine(Animate(next));
        }

        System.Collections.IEnumerator Animate(Action next)
        {
            float t = 0f; _cg.alpha = 0f;
            while (t < .5f) { t += Time.unscaledDeltaTime; _cg.alpha = Mathf.Clamp01(t / .5f); yield return null; }
            yield return new WaitForSecondsRealtime(1.9f);
            t = 0f;
            while (t < .4f) { t += Time.unscaledDeltaTime; _cg.alpha = 1f - Mathf.Clamp01(t / .4f); yield return null; }
            _root.gameObject.SetActive(false);
            // 演出中途玩家返回标题 / 读档：卡片已经作废，不能再把剧情往下带
            if (_cancelled) yield break;
            next();
        }

        public void Hide() { _cancelled = true; _root.gameObject.SetActive(false); }
    }

    /// <summary>笔记本 / 人物档案 / 回顾 / 菜单 / 结算，全部渲染进 OverlayPanel。</summary>
    public class Overlays
    {
        readonly OverlayPanel _panel;
        readonly MonoBehaviour _host;
        readonly RectTransform _layer;
        ContentDatabase _db;
        GameState _state;
        Action _onBackToTitle;
        Action _onRestart;
        Action<GameState> _onLoadState;
        string _tab = "全部";

        public Overlays(MonoBehaviour host, RectTransform layer)
        {
            _host = host; _layer = layer;
            _panel = new OverlayPanel(layer);
        }

        public void Bind(ContentDatabase db, GameState state, Action backToTitle, Action restart,
                         Action<GameState> onLoadState = null)
        {
            _db = db; _state = state; _onBackToTitle = backToTitle; _onRestart = restart;
            if (onLoadState != null) _onLoadState = onLoadState;
        }

        public bool IsOpen { get { return _panel.IsOpen; } }
        public void Close() { _panel.Close(); }

        // ---------------- 知识卡（医学 / 法律 / 化学说明） ----------------
        public void OpenNote(Beat b, Action done)
        {
            _panel.Open(b.title ?? "笔记");
            var t = UiFactory.Text("Note", _panel.Body,
                "<size=48><color=#F0DDA0>" + (b.icon ?? "🔎") + "</color></size>\n\n" + (b.body ?? ""),
                UiTheme.FontBody, UiTheme.Paper, TextAlignmentOptions.TopLeft, 1.55f);
            t.rectTransform.offsetMin = new Vector2(24, 24);
            t.rectTransform.offsetMax = new Vector2(-24, -24);
            t.overflowMode = TextOverflowModes.Overflow;
            _panel.AddFooterButton("明白了", () => { Close(); done(); }, true);
        }

        // ---------------- 笔记本 ----------------
        public void ShowNotebook()
        {
            _panel.Open("证据笔记本　" + _state.clues.Count + " / " + _db.Evidence.Count);
            var tabs = UiFactory.Rect("Tabs", _panel.Body, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -76), new Vector2(0, 0));
            var h = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12; h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            h.childControlWidth = true; h.childControlHeight = true;
            foreach (var cat in new[] { "全部", "物证", "证词", "推论" })
            {
                var c = cat;
                var b = UiFactory.Button("Tab_" + c, tabs, c, UiTheme.FontSmall + 2, () => { _tab = c; ShowNotebook(); },
                    _tab == c ? UiTheme.WithAlpha(UiTheme.Gold, .85f) : UiTheme.WithAlpha(UiTheme.Ink, .8f),
                    _tab == c ? UiTheme.Ink : UiTheme.PaperDim);
                UiFactory.Size(b.gameObject, 64f, 170f);
            }

            var listRect = UiFactory.Rect("List", _panel.Body, new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(0, 0), new Vector2(700, -96));
            RectTransform content;
            UiFactory.Scroll("Scroll", listRect, out content, 12f, new RectOffset(8, 8, 8, 8));
            foreach (var id in _state.clues)
            {
                EvidenceInfo e;
                if (!_db.Evidence.TryGetValue(id, out e)) continue;
                if (_tab != "全部" && e.category != _tab) continue;
                var b = UiFactory.Button("Clue_" + id, content, "【" + e.category + "】" + e.name + "\n<size=20><color=#BFB49A>" + e.source + "</color></size>",
                    UiTheme.FontSmall + 4, () => ShowClueDetail(e), UiTheme.WithAlpha(UiTheme.InkSoft, .92f), UiTheme.Paper, TextAlignmentOptions.Left);
                UiFactory.Size(b.gameObject, 104f);
            }
            if (_state.clues.Count == 0)
            {
                var t = UiFactory.Text("Empty", content, "还没有收集到任何证据。", UiTheme.FontSmall + 4, UiTheme.PaperDim);
                UiFactory.Size(t.gameObject, 60f);
            }

            var detail = UiFactory.Panel("Detail", _panel.Body, UiTheme.WithAlpha(UiTheme.Ink, .6f),
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(716, 0), new Vector2(0, -96));
            Deco.Border(detail.rectTransform, UiTheme.WithAlpha(UiTheme.Gold, .5f), 1.5f, 8f);
            var dt = UiFactory.Text("DetailText", detail.rectTransform,
                "<color=#BFB49A>从左侧选一条证据。</color>", UiTheme.FontSmall + 4, UiTheme.PaperDim,
                TextAlignmentOptions.TopLeft, 1.5f);
            dt.rectTransform.offsetMin = new Vector2(24, 24);
            dt.rectTransform.offsetMax = new Vector2(-24, -24);
            dt.overflowMode = TextOverflowModes.Overflow;
            dt.gameObject.name = "DetailText";
        }

        void ShowClueDetail(EvidenceInfo e)
        {
            var t = _panel.Body.Find("Detail/DetailText");
            var txt = t ? t.GetComponent<TMP_Text>() : null;
            if (txt == null) return;
            var meaning = string.IsNullOrEmpty(e.insight)
                ? "<color=#808070>（这一条你还没有想通。）</color>"
                : "<color=#C9A227>波洛的批注：</color>" + e.insight;
            txt.text = "<size=34><color=#F0DDA0>" + e.name + "</color></size>\n" +
                       "<size=22><color=#BFB49A>" + e.category + " · " + e.source + "</color></size>\n\n" +
                       e.description + "\n\n" + meaning;
        }

        // ---------------- 人物档案 ----------------
        public void ShowCodex()
        {
            _panel.Open("人物档案 · 关系图");
            RectTransform content;
            var scrollRect = UiFactory.Rect("ScrollHost", _panel.Body, Vector2.zero, Vector2.one, new Vector2(0, 0), new Vector2(-740, 0));
            UiFactory.Scroll("Scroll", scrollRect, out content, 14f, new RectOffset(8, 8, 8, 8));
            foreach (var kv in _db.Characters)
            {
                var c = kv.Value;
                var row = UiFactory.Rect("Char_" + c.id, content, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
                var img = row.gameObject.AddComponent<Image>();
                img.color = UiTheme.WithAlpha(UiTheme.InkSoft, .9f);
                UiFactory.Size(row.gameObject, 190f);

                var face = UiFactory.Panel("Face", row, Color.white, new Vector2(0, 0), new Vector2(0, 1),
                    new Vector2(10, 10), new Vector2(150, -10));
                face.sprite = AssetService.Portrait(c.id, "neutral");
                face.preserveAspect = true;
                if (face.sprite == AssetService.Missing())
                {
                    // 没有立绘的配角（如律师威尔斯、无名者）不要显示成一块紫色占位，
                    // 用一个姓氏首字的徽记代替，看起来是设计而不是缺图。
                    face.sprite = null;
                    face.color = UiTheme.WithAlpha(UiTheme.Wine, .85f);
                    Deco.Border(face.rectTransform, UiTheme.WithAlpha(UiTheme.Gold, .6f), 2f, 0f);
                    var mono = UiFactory.Text("Mono", face.rectTransform,
                        string.IsNullOrEmpty(c.name) ? "？" : c.name.Substring(0, 1),
                        UiTheme.FontChapter, UiTheme.GoldLight, TextAlignmentOptions.Center, 1.1f);
                    mono.rectTransform.offsetMin = Vector2.zero;
                    mono.rectTransform.offsetMax = Vector2.zero;
                }
                else face.color = Color.white;

                var t = UiFactory.Text("Info", row,
                    "<size=30><color=#F0DDA0>" + c.name + "</color></size>\n" +
                    "<size=21><color=#C9A227>" + c.role + "</color></size>\n<size=22>" + c.description + "</size>",
                    UiTheme.FontSmall + 2, UiTheme.Paper, TextAlignmentOptions.TopLeft, 1.45f);
                t.rectTransform.offsetMin = new Vector2(170, 12);
                t.rectTransform.offsetMax = new Vector2(-14, -12);
                t.overflowMode = TextOverflowModes.Overflow;
            }

            // 关系图
            var map = UiFactory.Panel("RelMap", _panel.Body, UiTheme.WithAlpha(UiTheme.Ink, .55f),
                new Vector2(1, 0), new Vector2(1, 1), new Vector2(-720, 0), new Vector2(0, 0));
            Deco.Border(map.rectTransform, UiTheme.WithAlpha(UiTheme.Gold, .4f), 1.5f, 8f);
            BuildRelationMap(map.rectTransform);
        }

        // 关系图：位置是手排的「语义种子」（女主人与丈夫在上、波洛与黑斯廷斯在下），
        // 但必须跑一次松弛，保证每个人的「方框 + 名字」互不重叠 ——
        // 以前节点间距只有 ~100px，名字标签却固定 210px 宽，必然糊成一团。
        void BuildRelationMap(RectTransform host)
        {
            var seeds = new Dictionary<string, Vector2>
            {
                { "emily",      new Vector2(0.46f, 0.90f) },
                { "alfred",     new Vector2(0.74f, 0.92f) },
                { "evelyn",     new Vector2(0.94f, 0.62f) },
                { "john",       new Vector2(0.14f, 0.74f) },
                { "mary",       new Vector2(0.06f, 0.42f) },
                { "lawrence",   new Vector2(0.30f, 0.52f) },
                { "cynthia",    new Vector2(0.24f, 0.94f) },
                { "dorcas",     new Vector2(0.60f, 0.50f) },
                { "bauerstein", new Vector2(0.90f, 0.92f) },
                { "japp",       new Vector2(0.86f, 0.26f) },
                { "poirot",     new Vector2(0.46f, 0.14f) },
                { "hastings",   new Vector2(0.12f, 0.12f) }
            };
            var edges = new[]
            {
                new[] { "emily", "alfred", "配偶" },
                new[] { "alfred", "evelyn", "表亲·同谋" },
                new[] { "emily", "john", "继母" },
                new[] { "emily", "lawrence", "继母" },
                new[] { "john", "mary", "夫妻" },
                new[] { "emily", "cynthia", "养母" },
                new[] { "mary", "bauerstein", "来往密切" },
                new[] { "poirot", "emily", "调查者" },
                new[] { "hastings", "poirot", "朋友" },
                new[] { "hastings", "john", "旧友" },
                new[] { "japp", "poirot", "合作" }
            };

            var mapRect = host.rect;
            float w = mapRect.width > 1f ? mapRect.width : 720f;
            float h = mapRect.height > 1f ? mapRect.height : 688f;
            const float box = 112f;        // 节点头像框
            const float nameW = 150f;      // 名字底宽
            const float nameGap = 86f;     // 名字中心相对节点中心的偏移（方框半高 56 + 名字半高 23 + 余量）
            const float halfBox = box * 0.5f, halfName = 23f;
            // 两个节点「不打架」的判据：横向拉开到名字宽以上，或纵向拉开到方框 + 名字高以上
            const float sepX = 176f, sepY = 200f;
            const float bottomBand = 46f;  // 左下角留给图例

            var keys = new List<string>();
            foreach (var k in seeds.Keys) keys.Add(k);
            var px = new List<Vector2>();
            foreach (var k in keys) px.Add(new Vector2(seeds[k].x * w, seeds[k].y * h));
            for (int iter = 0; iter < 400; iter++)
            {
                bool moved = false;
                for (int a = 0; a < px.Count; a++)
                    for (int b = a + 1; b < px.Count; b++)
                    {
                        float dx = px[b].x - px[a].x, dy = px[b].y - px[a].y;
                        if (Mathf.Abs(dx) >= sepX || Mathf.Abs(dy) >= sepY) continue;
                        var va = px[a]; var vb = px[b];
                        if (sepX - Mathf.Abs(dx) <= sepY - Mathf.Abs(dy))
                        {
                            float s = dx >= 0f ? 1f : -1f;
                            float need = (sepX - Mathf.Abs(dx)) * 0.5f + 1f;
                            va.x -= need * s; vb.x += need * s;
                        }
                        else
                        {
                            float s = dy >= 0f ? 1f : -1f;
                            float need = (sepY - Mathf.Abs(dy)) * 0.5f + 1f;
                            va.y -= need * s; vb.y += need * s;
                        }
                        px[a] = va; px[b] = vb;
                        moved = true;
                    }
                for (int a = 0; a < px.Count; a++)
                    px[a] = new Vector2(Mathf.Clamp(px[a].x, halfBox + 10f, w - halfBox - 10f),
                                        Mathf.Clamp(px[a].y, bottomBand + halfName + 4f, h - halfBox - 12f));
                if (!moved) break;
            }
            var pos = new Dictionary<string, Vector2>();
            for (int i = 0; i < keys.Count; i++) pos[keys[i]] = px[i];

            // 连线先画（压在节点下面）
            foreach (var e in edges)
            {
                var a = pos[e[0]];
                var b = pos[e[1]];
                var delta = b - a;
                bool red = e[2].Contains("同谋");
                var line = UiFactory.Panel("Edge", host, UiTheme.Gold,
                    new Vector2(0, 0), new Vector2(0, 0), a, a);
                var lr = line.rectTransform;
                lr.sizeDelta = new Vector2(delta.magnitude, red ? 3f : 1.5f);
                lr.pivot = new Vector2(0, .5f);
                lr.anchoredPosition = a;
                lr.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                line.color = UiTheme.WithAlpha(red ? UiTheme.Hex("#C95B5B") : UiTheme.Gold, red ? .95f : .4f);
                line.raycastTarget = false;
            }

            // 节点：方框 + 居中的头像 + 方框下方的名字（名字带底衬，免得被连线穿过看不清）
            foreach (var id in keys)
            {
                var p = pos[id];
                var dot = UiFactory.Panel("Node_" + id, host, UiTheme.WithAlpha(UiTheme.Ink, .85f),
                    new Vector2(0, 0), new Vector2(0, 0), p, p);
                dot.rectTransform.sizeDelta = new Vector2(box, box);
                dot.rectTransform.anchoredPosition = p;
                dot.raycastTarget = false;
                Deco.Border(dot.rectTransform, UiTheme.WithAlpha(UiTheme.Gold, .55f), 2f, 0f);
                var face = UiFactory.Panel("Face", dot.rectTransform, Color.white, Vector2.zero, Vector2.one,
                    new Vector2(4, 4), new Vector2(-4, -4));
                face.sprite = AssetService.Portrait(id, "neutral");
                face.preserveAspect = true;
                face.color = face.sprite == AssetService.Missing() ? UiTheme.WithAlpha(UiTheme.Gold, .35f) : Color.white;
                face.raycastTarget = false;

                var chip = UiFactory.Panel("NameChip_" + id, host, new Color(0f, 0f, 0f, .55f),
                    new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, Vector2.zero);
                chip.rectTransform.sizeDelta = new Vector2(nameW, halfName * 2f);
                chip.rectTransform.anchoredPosition = p + new Vector2(0, -nameGap);
                chip.raycastTarget = false;

                var label = UiFactory.Text("Name", chip.rectTransform, _db.Character(id).name, 17, UiTheme.Paper,
                    TextAlignmentOptions.Center, 1.05f);
                label.enableWordWrapping = true;
                label.enableAutoSizing = true;
                label.fontSizeMin = 12f;
                label.fontSizeMax = 17f;
                label.rectTransform.offsetMin = new Vector2(3, 2);
                label.rectTransform.offsetMax = new Vector2(-3, -2);
            }

            var legend = UiFactory.Text("Legend", host,
                "<size=15><color=#BFB49A>线＝人物关系　</color><color=#C95B5B>红线＝同谋</color></size>",
                15, UiTheme.PaperDim, TextAlignmentOptions.BottomLeft, 1.1f);
            legend.rectTransform.anchorMin = new Vector2(0, 0);
            legend.rectTransform.anchorMax = new Vector2(0, 0);
            legend.rectTransform.pivot = new Vector2(0, 0);
            legend.rectTransform.sizeDelta = new Vector2(340, 30);
            legend.rectTransform.anchoredPosition = new Vector2(12, 18);   // 让开 8px 的内边框线
        }

        // ---------------- 回顾 ----------------
        public void ShowBacklog()
        {
            _panel.Open("对话回顾");
            RectTransform content;
            var hostRect = UiFactory.Rect("Host", _panel.Body, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            UiFactory.Scroll("Scroll", hostRect, out content, 10f, new RectOffset(10, 10, 10, 10));
            var log = _state.log;
            int start = Mathf.Max(0, log.Count - 120);
            for (int i = start; i < log.Count; i++)
            {
                var l = log[i];
                var head = (string.IsNullOrEmpty(l.who) || l.who == "narr") ? "记录" : _db.Character(l.who).name;
                var t = UiFactory.Text("Log" + i, content,
                    "<size=21><color=#C9A227>" + head + "</color></size>\n" + l.text,
                    UiTheme.FontSmall + 4, UiTheme.Paper, TextAlignmentOptions.TopLeft, 1.4f);
                t.overflowMode = TextOverflowModes.Overflow;
                var le = t.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 40 + Mathf.CeilToInt(l.text.Length / 46f) * 40f;
            }
        }

        // ---------------- 菜单 ----------------
        public void ShowMenu()
        {
            _panel.Open("菜单");
            var col = UiFactory.LayoutVertical("MenuCol", _panel.Body, 14f, new RectOffset(320, 320, 10, 10), TextAnchor.UpperCenter);
            // 菜单有 7 颗按钮，按默认宽度（260）排一行会超出卡片右边缘，最后两颗直接被切掉。
            // 这里统一收窄字号与宽度，保证 7 颗都落在卡片内。
            var fh = _panel.Footer.GetComponent<HorizontalLayoutGroup>();
            if (fh != null) fh.spacing = 10f;
            const float w = 188f;
            const int fs = 24;
            _panel.AddFooterButton("继续游戏", Close, true, w, fs);
            _panel.AddFooterButton("保存进度", ShowSaveSlots, false, w, fs);
            _panel.AddFooterButton("读取存档", ShowLoadSlots, false, w, fs);
            _panel.AddFooterButton("设置", ShowSettings, false, w, fs);
            _panel.AddFooterButton("证据笔记本", ShowNotebook, false, w, fs);
            _panel.AddFooterButton("人物档案", ShowCodex, false, w, fs);
            _panel.AddFooterButton("返回标题", () => { Close(); if (_onBackToTitle != null) _onBackToTitle(); }, false, w, fs);
            var info = UiFactory.Text("Info", col, "《斯泰尔斯庄园奇案》Unity 版 · 自动存档中", UiTheme.FontSmall, UiTheme.PaperDim,
                TextAlignmentOptions.Center, 1.3f);
            UiFactory.Size(info.gameObject, 40f);
        }

        void ShowSaveSlots() { Slots(true); }
        void ShowLoadSlots() { Slots(false); }

        void Slots(bool save)
        {
            _panel.Open(save ? "保存进度" : "读取存档");
            RectTransform content;
            var hostRect = UiFactory.Rect("Host", _panel.Body, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            UiFactory.Scroll("Scroll", hostRect, out content, 12f, new RectOffset(10, 10, 10, 10));
            for (int i = 0; i < SaveSystem.SlotCount; i++)
            {
                var slot = i;
                var meta = SaveSystem.Meta(i);
                var label = (i == SaveSystem.AutoSlot ? "自动存档　" : "存档槽 " + i + "　") +
                            (meta.used ? meta.time + "　线索 " + meta.clues + "　得分 " + meta.score : "空");
                var b = UiFactory.Button("Slot" + i, content, label, UiTheme.FontButton, () =>
                {
                    if (save)
                    {
                        SaveSystem.Save(slot, _state);
                        Slots(true);
                    }
                    else
                    {
                        var st = SaveSystem.Load(slot);
                        if (st != null)
                        {
                            Close();
                            // 从标题画面直接读档时 GameDirector 还没被创建，
                            // 以前这里会空引用崩溃（点「读取存档」→ 选中存档 → 报错）。
                            if (_onLoadState != null) _onLoadState(st);
                            else if (GameDirector.Instance != null) GameDirector.Instance.LoadState(st);
                        }
                        else Slots(false);
                    }
                }, UiTheme.WithAlpha(UiTheme.InkSoft, .92f), meta.used ? UiTheme.Paper : UiTheme.PaperDim, TextAlignmentOptions.Left);
                UiFactory.Size(b.gameObject, 92f);
            }
        }

        void ShowSettings()
        {
            _panel.Open("设置");
            RectTransform content;
            var hostRect = UiFactory.Rect("Host", _panel.Body, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            UiFactory.Scroll("Scroll", hostRect, out content, 18f, new RectOffset(20, 20, 10, 10));
            var s = SettingsService.Current;

            AddSlider(content, "文字速度", 0.004f, 0.06f, s.textSpeed, v => { s.textSpeed = v; SettingsService.Save(); });
            AddSlider(content, "自动播放间隔", 0.4f, 4f, s.autoDelay, v => { s.autoDelay = v; SettingsService.Save(); });
            AddSlider(content, "背景音乐", 0f, 1f, s.bgmVolume, v => { s.bgmVolume = v; SettingsService.Save(); });
            AddSlider(content, "音效", 0f, 1f, s.sfxVolume, v => { s.sfxVolume = v; SettingsService.Save(); });

            var row = UiFactory.Rect("Row", content, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            UiFactory.Size(row.gameObject, 84f);
            var t = UiFactory.Text("Label", row, "画质", UiTheme.FontButton, UiTheme.Paper, TextAlignmentOptions.Left, 1.2f);
            t.rectTransform.offsetMin = new Vector2(6, 0);
            t.rectTransform.offsetMax = new Vector2(-700, 0);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10; h.childAlignment = TextAnchor.MiddleRight;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false; h.childControlWidth = true; h.childControlHeight = true;
            foreach (var name in new[] { "低", "中", "高", "极高" })
            {
                var n = name; var q = Array.IndexOf(new[] { "低", "中", "高", "极高" }, n);
                var b = UiFactory.Button("Q" + n, row, n, UiTheme.FontSmall + 2, () => { s.quality = q; SettingsService.Save(); ShowSettings(); },
                    s.quality == q ? UiTheme.Gold : UiTheme.WithAlpha(UiTheme.Ink, .8f), s.quality == q ? UiTheme.Ink : UiTheme.PaperDim);
                UiFactory.Size(b.gameObject, 64f, 130f);
            }
            var full = UiFactory.Button("Fullscreen", content, s.fullscreen ? "全屏：开" : "全屏：关", UiTheme.FontButton,
                () => { s.fullscreen = !s.fullscreen; SettingsService.Save(); ShowSettings(); },
                UiTheme.WithAlpha(UiTheme.InkSoft, .9f), UiTheme.Paper, TextAlignmentOptions.Left);
            UiFactory.Size(full.gameObject, 84f);
        }

        void AddSlider(RectTransform parent, string label, float min, float max, float value, Action<float> onChange)
        {
            var row = UiFactory.Rect("Row_" + label, parent, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            UiFactory.Size(row.gameObject, 84f);
            var t = UiFactory.Text("Label", row, label, UiTheme.FontButton, UiTheme.Paper, TextAlignmentOptions.Left, 1.2f);
            t.rectTransform.offsetMin = new Vector2(6, 0);
            t.rectTransform.offsetMax = new Vector2(-860, 0);
            var val = UiFactory.Text("Value", row, value.ToString("0.00"), UiTheme.FontSmall + 2, UiTheme.Gold,
                TextAlignmentOptions.Right, 1.2f);
            val.rectTransform.offsetMin = new Vector2(-140, 0);
            val.rectTransform.offsetMax = new Vector2(-8, 0);

            var sliderRect = UiFactory.Rect("Slider", row, new Vector2(0, .5f), new Vector2(1, .5f),
                new Vector2(300, -18), new Vector2(-160, 18));
            var bg = sliderRect.gameObject.AddComponent<Image>();
            bg.color = UiTheme.WithAlpha(UiTheme.Ink, .85f);
            var slider = sliderRect.gameObject.AddComponent<Slider>();
            var fillArea = UiFactory.Rect("FillArea", sliderRect, Vector2.zero, Vector2.one, new Vector2(0, 0), new Vector2(0, 0));
            var fill = UiFactory.StretchImage("Fill", fillArea, UiTheme.Gold);
            slider.fillRect = fill.rectTransform;
            var handleArea = UiFactory.Rect("HandleArea", sliderRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var handle = UiFactory.Panel("Handle", handleArea, UiTheme.GoldLight, new Vector2(0, .5f), new Vector2(0, .5f), Vector2.zero, Vector2.zero);
            handle.rectTransform.sizeDelta = new Vector2(26, 44);
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = min; slider.maxValue = max; slider.value = value;
            slider.onValueChanged.AddListener(v => { val.text = v.ToString("0.00"); onChange(v); });
        }

        // ---------------- 结算 ----------------
        public void ShowEnding(GameState st, ContentDatabase db, Action onAgain)
        {
            int grade = st.Grade(db.Accusation.Count);
            string letter = GameState.GradeLetter(grade);
            _panel.Open("结案报告");
            var head = UiFactory.Text("Head", _panel.Body,
                "<align=center><size=26><color=#C9A227>" + (grade == 4 ? "分歧结局" : "终局") + "</color></size>\n" +
                "<size=110><color=#F0DDA0>" + letter + "</color></size>\n" +
                "<size=24><color=#BFB49A>推理得分 " + st.score + "　失误 " + st.mistakes + " 次　证据 " + st.clues.Count + "/" + db.Evidence.Count +
                "　耗时 " + Mathf.FloorToInt(st.playSeconds / 60f) + " 分钟</color></size></align>",
                UiTheme.FontBody, UiTheme.Paper, TextAlignmentOptions.Top, 1.3f);
            Head(head.rectTransform);

            RectTransform content;
            var hostRect = UiFactory.Rect("Host", _panel.Body, new Vector2(0, 0), new Vector2(1, .62f), Vector2.zero, Vector2.zero);
            UiFactory.Scroll("Scroll", hostRect, out content, 14f, new RectOffset(10, 10, 10, 10));

            var title1 = UiFactory.Text("T1", content, "<size=26><color=#F0DDA0>你的六次指认</color></size>", UiTheme.FontSmall, UiTheme.Paper, TextAlignmentOptions.Left, 1.2f);
            UiFactory.Size(title1.gameObject, 46f);
            foreach (var a in st.accuse)
            {
                var t = UiFactory.Text("A", content,
                    "<size=22>" + a.question + "</size>\n" +
                    (a.ok ? "<color=#7FE0A8>你的判断：" + a.pick + "</color>" : "<color=#E08A8A>你的判断：" + a.pick + "（错误）</color>") + "\n" +
                    "<color=#F0DDA0>事实：" + a.truth + "</color>\n<color=#BFB49A>" + a.why + "</color>",
                    UiTheme.FontSmall, UiTheme.Paper, TextAlignmentOptions.TopLeft, 1.35f);
                t.overflowMode = TextOverflowModes.Overflow;
                var le = t.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 150f + Mathf.CeilToInt(a.why.Length / 48f) * 34f;
            }
            var title2 = UiFactory.Text("T2", content, "<size=26><color=#F0DDA0>与原著对照</color></size>", UiTheme.FontSmall, UiTheme.Paper, TextAlignmentOptions.Left, 1.2f);
            UiFactory.Size(title2.gameObject, 46f);
            foreach (var f in db.Faith)
            {
                var t = UiFactory.Text("F", content, "<size=21>" + f.item + "　<color=#BFB49A>（" + f.source + " · " + f.kind + "）</color></size>",
                    UiTheme.FontSmall, UiTheme.Paper, TextAlignmentOptions.TopLeft, 1.35f);
                t.overflowMode = TextOverflowModes.Overflow;
                var le = t.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 36f + Mathf.CeilToInt(f.item.Length / 42f) * 32f;
            }
            _panel.AddFooterButton("重新调查", onAgain, true);
            _panel.AddFooterButton("人物档案", ShowCodex);
            _panel.AddFooterButton("返回标题", () => { Close(); if (_onBackToTitle != null) _onBackToTitle(); });
        }

        static void Head(RectTransform r)
        {
            r.anchorMin = new Vector2(0, .62f);
            r.anchorMax = new Vector2(1, 1);
            r.offsetMin = new Vector2(20, 0);
            r.offsetMax = new Vector2(-20, -10);
        }
    }
}

