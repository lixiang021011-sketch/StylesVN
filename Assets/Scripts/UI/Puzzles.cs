using System;
using System.Collections.Generic;
using Styles.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Styles.UI
{
    /// <summary>调查现场、推理题、询问、最终指认——四种可交互玩法。</summary>
    public class Puzzles
    {
        readonly MonoBehaviour _host;
        readonly RectTransform _layer;
        readonly DialogueView _dialogue;
        readonly Func<GameState> _state;
        readonly Func<ContentDatabase> _db;
        AskCharacter _askCurrent;      // 询问面板当前在看谁，返回话题列表时用它还原

        public Puzzles(MonoBehaviour host, RectTransform layer, DialogueView dialogue, Func<GameState> state, Func<ContentDatabase> db)
        {
            _host = host; _layer = layer; _dialogue = dialogue; _state = state; _db = db;
        }

        // ============================================================
        // 现场调查
        // ============================================================
        public void ShowInvestigate(InvestigationScene sc, string sceneKey, Action done)
        {
            _dialogue.SetVisible(false);
            var root = UiFactory.Stretch("Investigate", _layer);
            var st = _state();

            var head = UiFactory.Panel("Head", root, UiTheme.WithAlpha(UiTheme.Ink, .72f), new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -132), new Vector2(0, 0));
            var title = UiFactory.Text("Title", head.rectTransform, "<size=34><color=#F0DDA0>" + sc.title + "</color></size>\n<size=22><color=#BFB49A>" + sc.hint + "</color></size>",
                UiTheme.FontBody, UiTheme.Paper, TextAlignmentOptions.Center, 1.3f);
            title.rectTransform.offsetMin = new Vector2(40, 10);
            title.rectTransform.offsetMax = new Vector2(-40, -10);

            // 开场提示：挂在顶部信息条下面。原来放在左下角，会盖住一片热点。
            var intro = UiFactory.Panel("Intro", root, UiTheme.Glass, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(60, -312), new Vector2(60 + 900, -142));
            Deco.Border(intro.rectTransform, UiTheme.Gold, 2f, 6f);
            var introText = UiFactory.Text("T", intro.rectTransform, sc.intro, UiTheme.FontSmall + 4, UiTheme.Paper, TextAlignmentOptions.TopLeft, 1.45f);
            introText.rectTransform.offsetMin = new Vector2(24, 20);
            introText.rectTransform.offsetMax = new Vector2(-24, -20);
            introText.overflowMode = TextOverflowModes.Overflow;
            var closer = intro.gameObject.AddComponent<Button>();
            closer.onClick.AddListener(() => intro.gameObject.SetActive(false));
            _host.StartCoroutine(AutoHide(intro.gameObject, 7f));

            // 跳出按钮
            var exit = UiFactory.Button("Exit", root, "结束搜寻 · 继续剧情 →", UiTheme.FontButton, () =>
            {
                UnityEngine.Object.Destroy(root.gameObject);
                done();
            }, UiTheme.WithAlpha(UiTheme.Gold, .92f), UiTheme.Ink);
            var er = exit.GetComponent<RectTransform>();
            er.anchorMin = er.anchorMax = new Vector2(1, 1);
            er.pivot = new Vector2(1, 1);
            er.sizeDelta = new Vector2(420, 84);
            er.anchoredPosition = new Vector2(-40, -150);
            exit.gameObject.SetActive(false);

            var counter = UiFactory.Text("Counter", root, "", UiTheme.FontSmall + 4, UiTheme.GoldLight,
                TextAlignmentOptions.Right, 1.2f);
            counter.rectTransform.anchorMin = counter.rectTransform.anchorMax = new Vector2(1, 1);
            counter.rectTransform.sizeDelta = new Vector2(420, 40);
            counter.rectTransform.anchoredPosition = new Vector2(-40, -246);

            int found = 0;
            // 热点坐标来自剧本的百分比。数据里有些点挨得非常近（甚至重叠），
            // 叠在一起就等于「有一个点永远点不到」；这里做一次轻微推开 + 夹进安全区。
            var placed = new List<Vector2>();
            var hotPos = new List<Vector2>();
            for (int i = 0; i < sc.spots.Count; i++)
            {
                var raw = new Vector2(sc.spots[i].x / 100f, 1f - sc.spots[i].y / 100f);
                raw.x = Mathf.Clamp(raw.x, 0.055f, 0.945f);
                raw.y = Mathf.Clamp(raw.y, 0.12f, 0.80f);
                hotPos.Add(raw);
            }
            // 松弛必须在「画布真实尺寸」下做，不能拿 1920×1080 的参考尺寸算：
            // 热点是按百分比锚定的，画布不是 16:9 时横向会被压扁（4:3 时只剩 86%），
            // 参考尺寸下量出来的 108px 间距到 4:3 就变成 93px —— 比热区本身（96px）还小，
            // 两个方框照样叠在一起。按当前画布尺寸算，任何分辨率/比例都不会叠。
            // 判据是「切比雪夫距离」（横、纵里较大的那个），不是直线距离：
            // 热区是 96×96 的方框，两个点斜着相隔 108px 时方框的角仍会压在一起，
            // 被压住的那一角归上面那个点 —— 下面那个点就有一小块永远点不到。
            // 108 > 96 才会保证任意两个方框之间一定留缝。
            const float box = 96f;
            const float minGap = 108f;
            float cw = _layer != null && _layer.rect.width > 1f ? _layer.rect.width : UiTheme.RefWidth;
            float ch = _layer != null && _layer.rect.height > 1f ? _layer.rect.height : UiTheme.RefHeight;
            var px = new List<Vector2>();
            foreach (var p in hotPos) px.Add(new Vector2(p.x * cw, p.y * ch));
            // 迭代次数给足：一次只把一对推开，别的对又可能把它挤回来，
            // 24 次在 4:3 这种画布上收敛不完（实测还剩 2px 的重叠）。
            for (int iter = 0; iter < 400; iter++)
            {
                bool moved = false;
                for (int a = 0; a < px.Count; a++)
                    for (int b = a + 1; b < px.Count; b++)
                    {
                        float dx = px[b].x - px[a].x, dy = px[b].y - px[a].y;
                        float sep = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                        if (sep >= minGap) continue;
                        float need = (minGap - sep) * 0.5f + 0.5f;   // 各退一半，这一对一次就到位
                        var va = px[a]; var vb = px[b];
                        if (Mathf.Abs(dx) >= Mathf.Abs(dy))
                        {
                            float s = dx >= 0f ? 1f : -1f;   // 左右推开
                            va.x -= need * s; vb.x += need * s;
                        }
                        else
                        {
                            float s = dy >= 0f ? 1f : -1f;   // 上下推开
                            va.y -= need * s; vb.y += need * s;
                        }
                        px[a] = va; px[b] = vb;
                        moved = true;
                    }
                for (int a = 0; a < px.Count; a++)
                    px[a] = new Vector2(Mathf.Clamp(px[a].x, box * 0.5f + 8f, cw - box * 0.5f - 8f),
                                        Mathf.Clamp(px[a].y, 170f, Mathf.Max(220f, ch - 240f)));
                if (!moved) break;
            }
            for (int i = 0; i < px.Count; i++) placed.Add(px[i]);

            for (int i = 0; i < sc.spots.Count; i++)
            {
                var spot = sc.spots[i];
                var key = sceneKey + "_" + i;
                var pos = new Vector2(placed[i].x / cw, placed[i].y / ch);
                var btn = UiFactory.Panel("Hot" + i, root, UiTheme.WithAlpha(UiTheme.Gold, .28f),
                    pos, pos, Vector2.zero, Vector2.zero);
                btn.rectTransform.sizeDelta = new Vector2(box, box);
                btn.rectTransform.pivot = new Vector2(.5f, .5f);
                Deco.Border(btn.rectTransform, UiTheme.GoldLight, 2f, 0f);
                var mark = UiFactory.Text("Icon", btn.rectTransform, string.IsNullOrEmpty(spot.icon) ? "◆" : spot.icon,
                    34, UiTheme.Paper, TextAlignmentOptions.Center, 1.1f);
                mark.rectTransform.offsetMin = new Vector2(6, 6);
                mark.rectTransform.offsetMax = new Vector2(-6, -6);
                var button = btn.gameObject.AddComponent<Button>();
                button.targetGraphic = btn;
                var index = i;
                bool already = st.spots.ContainsKey(key) && st.spots[key];
                if (already)
                {
                    found++;
                    btn.color = UiTheme.WithAlpha(UiTheme.Gold, .12f);
                    mark.color = UiTheme.WithAlpha(UiTheme.PaperDim, .5f);
                }
                button.onClick.AddListener(() =>
                {
                    if (!st.spots.ContainsKey(key) || !st.spots[key])
                    {
                        st.spots[key] = true;
                        st.AddClue(spot.clue);
                        found++;
                        btn.color = UiTheme.WithAlpha(UiTheme.Gold, .12f);
                        mark.color = UiTheme.WithAlpha(UiTheme.PaperDim, .5f);
                        _dialogue.Toast("＋ 已记入笔记本：" + (_db().Evidence.ContainsKey(spot.clue) ? _db().Evidence[spot.clue].name : spot.clue));
                        counter.text = "已发现 " + found + " / " + sc.spots.Count;
                        if (found >= sc.need) exit.gameObject.SetActive(true);
                    }
                    ShowSpotInfo(root, spot, index);
                });
            }
            counter.text = "已发现 " + found + " / " + sc.spots.Count;
            if (found >= sc.need) exit.gameObject.SetActive(true);
        }

        void ShowSpotInfo(RectTransform root, Hotspot spot, int index)
        {
            var old = root.Find("SpotInfo");
            if (old) UnityEngine.Object.Destroy(old.gameObject);
            var info = UiFactory.Panel("SpotInfo", root, UiTheme.Glass, new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(60, UiTheme.DialogueMargin), new Vector2(60 + 1000, UiTheme.DialogueMargin + 220));
            Deco.Border(info.rectTransform, UiTheme.Gold, 2f, 6f);
            EvidenceInfo e;
            var name = _db().Evidence.TryGetValue(spot.clue, out e) ? e.name : spot.clue;
            var t = UiFactory.Text("T", info.rectTransform,
                "<size=26><color=#F0DDA0>" + name + "</color></size>\n<size=23>" + spot.text + "</size>",
                UiTheme.FontSmall + 2, UiTheme.Paper, TextAlignmentOptions.TopLeft, 1.45f);
            t.rectTransform.offsetMin = new Vector2(24, 20);
            t.rectTransform.offsetMax = new Vector2(-24, -20);
            t.overflowMode = TextOverflowModes.Overflow;
            _host.StartCoroutine(AutoHide(info.gameObject, 8f));
        }

        System.Collections.IEnumerator AutoHide(GameObject go, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (go != null) go.SetActive(false);
        }

        // ============================================================
        // 推理题（灰色小细胞）
        // ============================================================
        public void ShowDeduce(Beat b, Action<int> resolve, Action<int> onWrongCounted)
        {
            _dialogue.SetVisible(false);
            var root = UiFactory.Stretch("Deduce", _layer);
            var dim = UiFactory.StretchImage("Dim", root, new Color(0, 0, 0, .55f));
            dim.raycastTarget = true;
            var card = UiFactory.Rect("Card", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(-700, -430), new Vector2(700, 470));
            var bg = card.gameObject.AddComponent<Image>();
            bg.color = UiTheme.WithAlpha(UiTheme.Ink, .96f);
            Deco.Border(card, UiTheme.Gold, 2f, 0f);

            var tag = UiFactory.Text("Tag", card, "◎ ◎ ◎ 灰 色 小 细 胞 ◎ ◎ ◎", UiTheme.FontSmall, UiTheme.Gold, TextAlignmentOptions.Center, 1.2f);
            tag.rectTransform.anchorMin = new Vector2(0, 1); tag.rectTransform.anchorMax = new Vector2(1, 1);
            tag.rectTransform.offsetMin = new Vector2(30, -70); tag.rectTransform.offsetMax = new Vector2(-30, -18);
            var q = UiFactory.Text("Q", card, "<size=26><color=#C9A227>" + (b.tag ?? "推理") + "</color></size>\n" + b.question,
                UiTheme.FontButton + 4, UiTheme.Paper, TextAlignmentOptions.TopLeft, 1.4f);
            q.rectTransform.anchorMin = new Vector2(0, 1); q.rectTransform.anchorMax = new Vector2(1, 1);
            q.rectTransform.offsetMin = new Vector2(40, -280); q.rectTransform.offsetMax = new Vector2(-40, -84);
            q.overflowMode = TextOverflowModes.Overflow;

            var listHost = UiFactory.Rect("Opts", card, new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(40, 170), new Vector2(-40, -300));
            var v = listHost.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 12; v.childAlignment = TextAnchor.UpperLeft;
            v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            v.childControlHeight = true; v.childControlWidth = true;

            var verdict = UiFactory.Text("Verdict", card, "", UiTheme.FontSmall + 2, UiTheme.Paper, TextAlignmentOptions.TopLeft, 1.4f);
            verdict.rectTransform.anchorMin = new Vector2(0, 0); verdict.rectTransform.anchorMax = new Vector2(1, 0);
            verdict.rectTransform.offsetMin = new Vector2(40, 110); verdict.rectTransform.offsetMax = new Vector2(-40, 160);
            verdict.overflowMode = TextOverflowModes.Overflow;

            var next = UiFactory.Button("Next", card, "继续 →", UiTheme.FontButton, null, UiTheme.Gold, UiTheme.Ink);
            var nr = next.GetComponent<RectTransform>();
            nr.anchorMin = nr.anchorMax = new Vector2(.5f, 0);
            nr.pivot = new Vector2(.5f, 0);
            nr.sizeDelta = new Vector2(300, 76);
            nr.anchoredPosition = new Vector2(0, 26);
            next.gameObject.SetActive(false);

            var answers = b.answers ?? new List<string>();
            bool solved = false;
            for (int i = 0; i < answers.Count; i++)
            {
                var idx = i;
                var btn = UiFactory.Button("A" + i, listHost, answers[i], UiTheme.FontButton, null,
                    UiTheme.WithAlpha(UiTheme.InkSoft, .92f), UiTheme.Paper, TextAlignmentOptions.Left);
                var h = 84 + Mathf.Max(0, answers[i].Length - 22) / 22 * 30;
                UiFactory.Size(btn.gameObject, h);
                btn.onClick.AddListener(() =>
                {
                    if (solved) return;
                    if (idx == b.answer)
                    {
                        solved = true;
                        var img = btn.GetComponent<Image>();
                        img.color = new Color(.16f, .42f, .28f, .95f);
                        verdict.text = "<color=#7FE0A8>波洛点了点头。</color>" + (b.explain ?? "");
                        verdict.color = UiTheme.Paper;
                        next.gameObject.SetActive(true);
                        next.onClick.RemoveAllListeners();
                        next.onClick.AddListener(() =>
                        {
                            UnityEngine.Object.Destroy(root.gameObject);
                            resolve(idx);
                        });
                    }
                    else
                    {
                        var img = btn.GetComponent<Image>();
                        img.color = new Color(.42f, .14f, .18f, .9f);
                        btn.interactable = false;
                        var wrongs = b.wrong;
                        var msg = (wrongs != null && idx < wrongs.Count && !string.IsNullOrEmpty(wrongs[idx]))
                            ? wrongs[idx] : "波洛摇了摇头：“再想一想，我的朋友。”";
                        verdict.text = "<color=#E08A8A>× 波洛摇了摇头。</color>" + msg;
                        if (onWrongCounted != null) onWrongCounted(idx);
                    }
                });
            }
        }

        // ============================================================
        // 询问
        // ============================================================
        public void ShowAsk(List<AskCharacter> chars, int need, string title, Action done)
        {
            _dialogue.SetVisible(false);
            var st = _state();
            var root = UiFactory.Stretch("Ask", _layer);
            var dim = UiFactory.StretchImage("Dim", root, new Color(0, 0, 0, .72f));
            dim.raycastTarget = true;
            var card = UiFactory.Rect("Card", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(-760, -470), new Vector2(760, 470));
            var bg = card.gameObject.AddComponent<Image>();
            bg.color = UiTheme.Hex("#101B17");
            Deco.Border(card, UiTheme.Gold, 2f, 0f);

            var head = UiFactory.Panel("Head", card, UiTheme.WithAlpha(UiTheme.Ink, .9f), new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -84), new Vector2(0, 0));
            var t = UiFactory.Text("Title", head.rectTransform, title, 34, UiTheme.GoldLight, TextAlignmentOptions.Left, 1.2f);
            t.rectTransform.offsetMin = new Vector2(32, 0);
            t.rectTransform.offsetMax = new Vector2(-32, 0);

            var people = UiFactory.Rect("People", card, new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(28, 108), new Vector2(408, -100));
            var pv = people.gameObject.AddComponent<VerticalLayoutGroup>();
            pv.spacing = 10; pv.childAlignment = TextAnchor.UpperLeft;
            pv.childForceExpandHeight = false; pv.childForceExpandWidth = true;
            pv.childControlHeight = true; pv.childControlWidth = true;

            var bodyHost = UiFactory.Rect("Body", card, new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(424, 108), new Vector2(-28, -100));

            var footer = UiFactory.Rect("Footer", card, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(28, 22), new Vector2(-28, 92));
            var fh = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            fh.spacing = 12; fh.childAlignment = TextAnchor.MiddleRight;
            fh.childForceExpandWidth = false; fh.childForceExpandHeight = false;
            fh.childControlWidth = true; fh.childControlHeight = true;

            int askedTotal = 0;
            foreach (var c in chars)
                foreach (var topic in c.topics)
                    if (st.topics.ContainsKey(c.id + "_" + topic.id) && st.topics[c.id + "_" + topic.id]) askedTotal++;

            var counter = UiFactory.Text("Counter", footer, "", UiTheme.FontSmall + 2, UiTheme.PaperDim, TextAlignmentOptions.Left, 1.2f);
            UiFactory.Size(counter.gameObject, 60f, 420f);
            var finish = UiFactory.Button("Finish", footer, "结束询问 · 继续剧情 →", UiTheme.FontButton, () =>
            {
                UnityEngine.Object.Destroy(root.gameObject);
                done();
            }, UiTheme.Gold, UiTheme.Ink);
            UiFactory.Size(finish.gameObject, 72f, 420f);

            // 顶部关闭（未问够时只是提示）
            var close = UiFactory.Button("Close", head.rectTransform, "×", 26, () =>
            {
                if (askedTotal >= need) { UnityEngine.Object.Destroy(root.gameObject); done(); }
                else _dialogue.Toast("再多问 " + (need - askedTotal) + " 个问题吧");
            }, UiTheme.WithAlpha(UiTheme.Ink, .6f), UiTheme.PaperDim);
            var cr = close.GetComponent<RectTransform>();
            cr.anchorMin = cr.anchorMax = new Vector2(1, .5f);
            cr.pivot = new Vector2(1, .5f);
            cr.sizeDelta = new Vector2(66, 60);
            cr.anchoredPosition = new Vector2(-16, 0);

            Action refresh = null;
            refresh = () =>
            {
                UiFactory.Clear(people); UiFactory.Clear(bodyHost);
                int total = 0;
                foreach (var c in chars)
                {
                    int mine = 0;
                    foreach (var topic in c.topics)
                        if (st.topics.ContainsKey(c.id + "_" + topic.id) && st.topics[c.id + "_" + topic.id]) mine++;
                    total += mine;
                    var cc = c;
                    var b = UiFactory.Button("P_" + c.id, people,
                        "<size=28>" + c.name + "</size>\n<size=20><color=#BFB49A>" + c.role + "　已问 " + mine + "</color></size>",
                        UiTheme.FontSmall, () => ShowTopics(cc, bodyHost, refresh), UiTheme.WithAlpha(UiTheme.InkSoft, .92f), UiTheme.Paper, TextAlignmentOptions.Left);
                    UiFactory.Size(b.gameObject, 104f);
                }
                askedTotal = total;
                counter.text = askedTotal >= need ? "已问 " + askedTotal + " 个问题" : "还需再问 " + (need - askedTotal) + " 个问题";
                finish.gameObject.SetActive(askedTotal >= need);
                var show = _askCurrent != null && chars.Contains(_askCurrent) ? _askCurrent : chars[0];
                if (chars.Count > 0) ShowTopics(show, bodyHost, refresh);
            };
            refresh();
        }

        void ShowTopics(AskCharacter c, RectTransform host, Action refresh)
        {
            UiFactory.Clear(host);
            _askCurrent = c;
            var st = _state();
            var list = UiFactory.Rect("List", host, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            RectTransform content;
            UiFactory.Scroll("Scroll", list, out content, 10f, new RectOffset(8, 8, 8, 8));
            foreach (var topic in c.topics)
            {
                var t = topic;
                bool locked = !string.IsNullOrEmpty(t.need) && !st.HasClue(t.need);
                string name = "";
                if (locked)
                {
                    EvidenceInfo e;
                    name = _db().Evidence.TryGetValue(t.need, out e) ? e.name : t.need;
                }
                bool already = st.topics.ContainsKey(c.id + "_" + t.id) && st.topics[c.id + "_" + t.id];
                var label = (already ? "✓ " : "") + t.label + (locked ? "\n<size=20><color=#8A8270>需要先弄清：" + name + "</color></size>" : "");
                var b = UiFactory.Button("T_" + t.id, content, label, UiTheme.FontSmall + 4, () =>
                {
                    if (locked) { _dialogue.Toast("你还没有掌握足以提出这个问题的证据"); return; }
                    st.topics[c.id + "_" + t.id] = true;
                    if (!string.IsNullOrEmpty(t.give)) st.AddClue(t.give);
                    // 念完台词回到列表时必须走一次 refresh：
                    // 否则「已问 N 个」计数与「结束询问」按钮不会更新，玩家会以为问不完。
                    PlayTopic(c, t, host, refresh);
                }, UiTheme.WithAlpha(UiTheme.InkSoft, .92f), already ? UiTheme.PaperDim : UiTheme.Paper, TextAlignmentOptions.Left);
                UiFactory.Size(b.gameObject, locked ? 108f : 82f);
            }
        }

        void PlayTopic(AskCharacter c, AskTopic t, RectTransform host, Action refresh)
        {
            UiFactory.Clear(host);
            var panel = UiFactory.Stretch("Lines", host);
            RectTransform content;
            UiFactory.Scroll("Scroll", panel, out content, 14f, new RectOffset(10, 10, 10, 10));
            foreach (var line in t.lines)
            {
                var isNarr = string.IsNullOrEmpty(line.who) || line.who == "narr";
                var head = isNarr ? "记录" : _db().Character(line.who).name;
                var txt = UiFactory.Text("L", content,
                    "<size=21><color=#C9A227>" + head + "</color></size>\n<size=24>" + line.text + "</size>",
                    UiTheme.FontSmall + 2, UiTheme.Paper, TextAlignmentOptions.TopLeft, 1.45f);
                txt.overflowMode = TextOverflowModes.Overflow;
                var le = txt.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 40 + Mathf.CeilToInt(line.text.Length / 24f) * 38f;
            }
            var back = UiFactory.Button("Back", host, "← 换一个问题", UiTheme.FontSmall + 2,
                () => { if (refresh != null) refresh(); else ShowTopics(c, host, refresh); },
                UiTheme.WithAlpha(UiTheme.Ink, .9f), UiTheme.PaperDim);
            var br = back.GetComponent<RectTransform>();
            br.anchorMin = br.anchorMax = new Vector2(0, 0);
            br.pivot = new Vector2(0, 0);
            br.sizeDelta = new Vector2(280, 64);
            br.anchoredPosition = new Vector2(6, 6);
        }

        // ============================================================
        // 最终指认
        // ============================================================
        public void ShowAccusation(List<AccusationQuestion> questions, Action<List<AccuseRecord>> done)
        {
            _dialogue.SetVisible(false);
            var records = new List<AccuseRecord>();
            var root = UiFactory.Stretch("Accuse", _layer);
            var dim = UiFactory.StretchImage("Dim", root, new Color(0, 0, 0, .8f));
            dim.raycastTarget = true;
            var card = UiFactory.Rect("Card", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(-720, -450), new Vector2(720, 450));
            var bg = card.gameObject.AddComponent<Image>();
            bg.color = UiTheme.WithAlpha(UiTheme.Ink, .97f);
            Deco.Border(card, UiTheme.Gold, 2f, 0f);

            var head = UiFactory.Text("Head", card, "", 34, UiTheme.GoldLight, TextAlignmentOptions.Left, 1.2f);
            head.rectTransform.anchorMin = new Vector2(0, 1); head.rectTransform.anchorMax = new Vector2(1, 1);
            head.rectTransform.offsetMin = new Vector2(40, -96); head.rectTransform.offsetMax = new Vector2(-40, -24);
            var qtext = UiFactory.Text("Q", card, "", UiTheme.FontButton + 4, UiTheme.Paper, TextAlignmentOptions.TopLeft, 1.4f);
            qtext.rectTransform.anchorMin = new Vector2(0, 1); qtext.rectTransform.anchorMax = new Vector2(1, 1);
            qtext.rectTransform.offsetMin = new Vector2(40, -230); qtext.rectTransform.offsetMax = new Vector2(-40, -110);
            qtext.overflowMode = TextOverflowModes.Overflow;

            var listHost = UiFactory.Rect("Opts", card, new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(40, 190), new Vector2(-40, -250));
            var v = listHost.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 12; v.childAlignment = TextAnchor.UpperLeft;
            v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            v.childControlHeight = true; v.childControlWidth = true;

            var verdict = UiFactory.Text("Verdict", card, "", UiTheme.FontSmall + 2, UiTheme.Paper, TextAlignmentOptions.TopLeft, 1.4f);
            verdict.rectTransform.anchorMin = new Vector2(0, 0); verdict.rectTransform.anchorMax = new Vector2(1, 0);
            verdict.rectTransform.offsetMin = new Vector2(40, 110); verdict.rectTransform.offsetMax = new Vector2(-40, 170);
            verdict.overflowMode = TextOverflowModes.Overflow;

            var nx = UiFactory.Button("Next", card, "继续 →", UiTheme.FontButton, null, UiTheme.Gold, UiTheme.Ink);
            var nr = nx.GetComponent<RectTransform>();
            nr.anchorMin = nr.anchorMax = new Vector2(.5f, 0);
            nr.pivot = new Vector2(.5f, 0);
            nr.sizeDelta = new Vector2(300, 76);
            nr.anchoredPosition = new Vector2(0, 24);
            nx.gameObject.SetActive(false);

            int index = 0;
            Action step = null;
            step = () =>
            {
                if (index >= questions.Count)
                {
                    UnityEngine.Object.Destroy(root.gameObject);
                    done(records);
                    return;
                }
                var q = questions[index];
                head.text = "最终指认 · " + (index + 1) + " / " + questions.Count;
                qtext.text = q.question;
                verdict.text = "";
                nx.gameObject.SetActive(false);
                UiFactory.Clear(listHost);
                for (int i = 0; i < q.options.Count; i++)
                {
                    var i2 = i;
                    var btn = UiFactory.Button("O" + i, listHost, q.options[i], UiTheme.FontButton, null,
                        UiTheme.WithAlpha(UiTheme.InkSoft, .92f), UiTheme.Paper, TextAlignmentOptions.Left);
                    UiFactory.Size(btn.gameObject, 78f + Mathf.Max(0, q.options[i].Length - 20) / 20 * 26);
                    btn.onClick.AddListener(() =>
                    {
                        bool ok = i2 == q.answer;
                        var rec = new AccuseRecord
                        {
                            question = q.question,
                            pick = q.options[i2],
                            truth = q.options[q.answer],
                            ok = ok,
                            why = q.explain
                        };
                        records.Add(rec);
                        var img = btn.GetComponent<Image>();
                        img.color = ok ? new Color(.16f, .42f, .28f, .95f) : new Color(.42f, .14f, .18f, .9f);
                        for (int k = 0; k < listHost.childCount; k++)
                        {
                            var b2 = listHost.GetChild(k).GetComponent<Button>();
                            if (b2) b2.interactable = false;
                        }
                        if (!ok)
                        {
                            var correct = listHost.GetChild(q.answer).GetComponent<Image>();
                            if (correct) correct.color = new Color(.16f, .42f, .28f, .95f);
                        }
                        verdict.text = ok ? "<color=#7FE0A8>波洛微微颔首。</color>这一处，你想对了。" :
                                            "<color=#E08A8A>波洛摇了摇头。</color>答案不是这个——先记住你的判断，他会在最后解释。";
                        nx.gameObject.SetActive(true);
                        nx.onClick.RemoveAllListeners();
                        nx.onClick.AddListener(() => { index++; step(); });
                    });
                }
            };
            step();
        }
    }
}

