using System;
using System.Collections;
using System.Collections.Generic;
using Styles.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Styles.UI
{
    /// <summary>对话框、姓名牌、打字机、选项条、HUD 与提示。</summary>
    public class DialogueView
    {
        public RectTransform Root;          // 对话层根节点
        public bool Typing { get; private set; }
        public bool AutoMode;
        public bool SkipMode;
        /// <summary>有弹层（笔记本 / 菜单 / 知识卡…）盖在上面时为 true：自动翻页必须让路。</summary>
        public Func<bool> AutoBlocked;

        readonly MonoBehaviour _host;
        readonly RectTransform _advanceLayer;
        readonly Image _box, _namePlate;
        readonly TMP_Text _nameText, _bodyText, _hint, _chapterTag, _chapterTitle, _clueCount;
        readonly RectTransform _choiceRoot, _toastRoot;
        readonly List<TMP_Text> _toasts = new List<TMP_Text>();
        Coroutine _typing;
        Coroutine _auto;
        Action _onAdvance;
        TMP_Text _continueHint;

        public DialogueView(MonoBehaviour host, RectTransform parent, RectTransform advanceLayer,
                            RectTransform hud, RectTransform choiceRoot, RectTransform toastRoot,
                            out TMP_Text chapterTag, out TMP_Text chapterTitle, out TMP_Text clueCount)
        {
            _host = host;
            _advanceLayer = advanceLayer;
            _choiceRoot = choiceRoot;
            _toastRoot = toastRoot;

            // ---- 对话底板 ----
            Root = UiFactory.Rect("Dialogue", parent, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(UiTheme.DialogueMargin, UiTheme.DialogueMargin),
                new Vector2(-UiTheme.DialogueMargin, UiTheme.DialogueMargin + UiTheme.DialogueHeight));
            _box = Root.gameObject.AddComponent<Image>();
            _box.color = UiTheme.Glass;
            // 对话框本体不参与射线检测：否则点「点击继续」的位置会被它挡住，
            // 点上去毫无反应（只有对话框以外的区域能推进剧情）。
            _box.raycastTarget = false;
            Deco.Border(Root, UiTheme.Gold, 2f);

            // 姓名牌
            _namePlate = UiFactory.Panel("NamePlate", Root, UiTheme.Gold, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -74), new Vector2(28 + 320, -12));
            _namePlate.raycastTarget = false;
            // 统一用「锚点(0,1) + 轴心(0,1)」定位：anchoredPosition 就是左上角相对对话框左上角的偏移。
            // 先设偏移再改轴心会把整块牌挪走（v1 就是这么飘到对话框外的）。
            var plateRt = _namePlate.rectTransform;
            plateRt.pivot = new Vector2(0f, 1f);
            plateRt.anchoredPosition = new Vector2(28f, -12f);
            plateRt.sizeDelta = new Vector2(320f, 62f);
            _nameText = UiFactory.Text("NameText", _namePlate.rectTransform, "", 30, UiTheme.Ink,
                TextAlignmentOptions.Center, 1.1f);
            _nameText.rectTransform.offsetMin = new Vector2(12, 4);
            _nameText.rectTransform.offsetMax = new Vector2(-12, -4);

            // 正文
            _bodyText = UiFactory.Text("Body", Root, "", UiTheme.FontBody, UiTheme.Paper, TextAlignmentOptions.TopLeft, 1.45f);
            _bodyText.rectTransform.anchorMin = new Vector2(0, 0);
            _bodyText.rectTransform.anchorMax = new Vector2(1, 1);
            _bodyText.rectTransform.offsetMin = new Vector2(52, 42);
            _bodyText.rectTransform.offsetMax = new Vector2(-52, -78);
            // 长台词自动缩号而不是被省略号截断：默认是 Ellipsis，超过 4 行就看不见后半句了。
            _bodyText.overflowMode = TextOverflowModes.Overflow;
            _bodyText.enableAutoSizing = true;
            _bodyText.fontSizeMin = 20f;
            _bodyText.fontSizeMax = UiTheme.FontBody;

            // 继续指示
            _continueHint = UiFactory.Text("ContinueHint", Root, "→ 点击继续", UiTheme.FontSmall, UiTheme.GoldLight,
                TextAlignmentOptions.BottomRight, 1.1f);
            _continueHint.rectTransform.anchorMin = new Vector2(0, 0);
            _continueHint.rectTransform.anchorMax = new Vector2(1, 0);
            _continueHint.rectTransform.offsetMin = new Vector2(0, 10);
            _continueHint.rectTransform.offsetMax = new Vector2(-28, 44);

            // 小按钮条
            var tools = UiFactory.Rect("Tools", Root, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-470, -8), new Vector2(-12, 34));
            tools.pivot = new Vector2(1, 0);
            var h = tools.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8; h.childAlignment = TextAnchor.MiddleRight;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false; h.childControlWidth = true; h.childControlHeight = true;
            // 这两个开关以前只弹一条提示，没有任何东西读它们 —— 也就是「按了没反应」。
            // 现在它们真的会让剧情自己往前走（间隔取设置里的「自动播放间隔」）。
            AddTool(tools, "自动", () =>
            {
                AutoMode = !AutoMode;
                if (AutoMode) SkipMode = false;
                Toast(AutoMode ? "自动播放：开" : "自动播放：关");
                ScheduleAuto();
            });
            AddTool(tools, "快进", () =>
            {
                SkipMode = !SkipMode;
                if (SkipMode) AutoMode = false;
                Toast(SkipMode ? "快进：开" : "快进：关");
                ScheduleAuto();
            });

            // ---- HUD ----
            var bar = UiFactory.Panel("Hud", hud, UiTheme.WithAlpha(UiTheme.Ink, .55f), new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -96), new Vector2(0, 0));
            bar.rectTransform.offsetMin = new Vector2(0, -96);
            chapterTag = UiFactory.Text("Tag", bar.rectTransform, "序章", UiTheme.FontHud, UiTheme.Gold,
                TextAlignmentOptions.Left, 1.1f);
            chapterTag.rectTransform.anchorMin = new Vector2(0, 0);
            chapterTag.rectTransform.anchorMax = new Vector2(0, 1);
            chapterTag.rectTransform.offsetMin = new Vector2(40, 0);
            chapterTag.rectTransform.offsetMax = new Vector2(200, 0);
            chapterTitle = UiFactory.Text("Title", bar.rectTransform, "", UiTheme.FontHud, UiTheme.Paper,
                TextAlignmentOptions.Left, 1.1f);
            chapterTitle.rectTransform.anchorMin = new Vector2(0, 0);
            chapterTitle.rectTransform.anchorMax = new Vector2(1, 1);
            chapterTitle.rectTransform.offsetMin = new Vector2(210, 0);
            chapterTitle.rectTransform.offsetMax = new Vector2(-700, 0);
            clueCount = UiFactory.Text("Clues", bar.rectTransform, "笔记本 0", UiTheme.FontHud, UiTheme.PaperDim,
                TextAlignmentOptions.Right, 1.1f);
            clueCount.rectTransform.anchorMin = new Vector2(1, 0);
            clueCount.rectTransform.anchorMax = new Vector2(1, 1);
            // 右上角是「笔记本 / 人物 / 回顾 / 菜单」四个按钮（x 从 -640 起），
            // 计数器原来落在同一块区域里，会被按钮整个盖住 —— 往左让开。
            clueCount.rectTransform.offsetMin = new Vector2(-1060, 0);
            clueCount.rectTransform.offsetMax = new Vector2(-660, 0);

            // ---- 提示 ----
            _hint = UiFactory.Text("Hint", parent, "", UiTheme.FontSmall + 4, UiTheme.Paper, TextAlignmentOptions.Center, 1.2f);
            _hint.rectTransform.anchorMin = new Vector2(0, 0);
            _hint.rectTransform.anchorMax = new Vector2(1, 0);
            _hint.rectTransform.offsetMin = new Vector2(200, UiTheme.DialogueMargin - 46);
            _hint.rectTransform.offsetMax = new Vector2(-200, UiTheme.DialogueMargin + 10);
            _hint.gameObject.SetActive(false);
        }

        void AddTool(RectTransform parent, string label, Action onClick)
        {
            var b = UiFactory.Button("Tool_" + label, parent, label, UiTheme.FontSmall, onClick,
                UiTheme.WithAlpha(UiTheme.Ink, .8f), UiTheme.PaperDim);
            UiFactory.Size(b.gameObject, 60f, 108f);
        }

        public void SetVisible(bool on)
        {
            Root.gameObject.SetActive(on);
            if (_advanceLayer != null) _advanceLayer.gameObject.SetActive(on);
            // 对话框收起来（选项 / 解谜 / 弹层）时自动翻页必须停手，
            // 否则会在玩家看不见的地方把剧情一路点过去。
            if (!on) CancelAuto();
        }

        /// <summary>停止待触发的自动翻页（玩家自己点了、对话框收了、或者开关关掉了）。</summary>
        public void CancelAuto()
        {
            if (_auto != null) { _host.StopCoroutine(_auto); _auto = null; }
        }

        /// <summary>一句台词念完之后，若开着「自动 / 快进」，安排自动翻到下一句。</summary>
        void ScheduleAuto()
        {
            CancelAuto();
            if (!AutoMode && !SkipMode) return;
            if (!Root.gameObject.activeSelf || _onAdvance == null) return;
            if (AutoBlocked != null && AutoBlocked()) return;
            float delay = SkipMode ? 0.06f : Mathf.Clamp(SettingsService.Current.autoDelay, 0.4f, 8f);
            _auto = _host.StartCoroutine(AutoRoutine(delay));
        }

        IEnumerator AutoRoutine(float delay)
        {
            float t = 0f;
            while (t < delay)
            {
                // 中途情况变了就放弃：还在打字 / 对话框收起了 / 已经没得推进了 / 玩家开了弹层
                if (Typing || !Root.gameObject.activeSelf || _onAdvance == null ||
                    (AutoBlocked != null && AutoBlocked())) { _auto = null; yield break; }
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            _auto = null;
            if (!Typing && Root.gameObject.activeSelf && _onAdvance != null &&
                (AutoBlocked == null || !AutoBlocked())) Advance();
        }

        public void ShowHint(string text)
        {
            _hint.text = text ?? "";
            _hint.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        public void ShowSay(Beat b, ContentDatabase db, Action next)
        {
            SetVisible(true);
            _choiceRoot.gameObject.SetActive(false);
            _onAdvance = next;
            var isNarr = string.IsNullOrEmpty(b.who) || b.who == "narr";
            var info = db.Character(b.who);
            _namePlate.gameObject.SetActive(true);
            _namePlate.color = isNarr ? UiTheme.Hex("#2E4A41") : UiTheme.Gold;
            _nameText.color = isNarr ? UiTheme.Paper : UiTheme.Ink;
            _nameText.text = isNarr ? "记录" : info.name;
            var plateW = Mathf.Max(200f, Mathf.Min(520f, _nameText.text.Length * 34f + 90f));
            _namePlate.rectTransform.sizeDelta = new Vector2(plateW, 62f);
            StartTyping(b.text);
        }

        public void StartTyping(string content)
        {
            if (_typing != null) _host.StopCoroutine(_typing);
            _typing = _host.StartCoroutine(TypeRoutine(content));
        }

        IEnumerator TypeRoutine(string content)
        {
            _bodyText.text = content ?? "";
            _bodyText.maxVisibleCharacters = 0;
            _bodyText.ForceMeshUpdate();
            int total = _bodyText.textInfo.characterCount;
            _continueHint.text = "→";
            if (SkipMode)
            {
                _bodyText.maxVisibleCharacters = total; Typing = false;
                _continueHint.text = "→ 点击继续";
                ScheduleAuto();
                yield break;
            }
            Typing = true;
            float per = Mathf.Max(0.002f, SettingsService.Current.textSpeed);
            for (int i = 1; i <= total; i++)
            {
                _bodyText.maxVisibleCharacters = i;
                float wait = per;
                var ch = _bodyText.textInfo.characterInfo.Length > i - 1 ? _bodyText.textInfo.characterInfo[i - 1].character : ' ';
                if (ch == '。' || ch == '，' || ch == '；' || ch == '？' || ch == '！') wait = per * 4.5f;
                yield return new WaitForSecondsRealtime(wait);
            }
            Typing = false;
            _continueHint.text = "→ 点击继续";
            ScheduleAuto();
        }

        /// <summary>返回 true 表示本次点击只是把文字显示完。</summary>
        public bool Advance()
        {
            CancelAuto();
            if (Typing)
            {
                if (_typing != null) _host.StopCoroutine(_typing);
                _bodyText.ForceMeshUpdate();
                _bodyText.maxVisibleCharacters = _bodyText.textInfo.characterCount;
                Typing = false;
                _continueHint.text = "→ 点击继续";
                return true;
            }
            var n = _onAdvance; _onAdvance = null;
            if (n != null) n();
            return false;
        }

        public void ShowChoices(Beat b, Action<Option> pick)
        {
            SetVisible(false);
            _choiceRoot.gameObject.SetActive(true);
            UiFactory.Clear(_choiceRoot);
            if (!string.IsNullOrEmpty(b.prompt))
            {
                var q = UiFactory.Text("Prompt", _choiceRoot, b.prompt, UiTheme.FontButton + 6, UiTheme.GoldLight,
                    TextAlignmentOptions.Center, 1.3f);
                UiFactory.Size(q.gameObject, 92f);
            }
            var list = b.options ?? new List<Option>();
            for (int i = 0; i < list.Count; i++)
            {
                var opt = list[i];
                var text = opt.text + (string.IsNullOrEmpty(opt.hint) ? "" : "\n<size=22><color=#BFB49A>" + opt.hint + "</color></size>");
                var btn = UiFactory.Button("Opt" + i, _choiceRoot, text, UiTheme.FontButton, () =>
                {
                    _choiceRoot.gameObject.SetActive(false);
                    pick(opt);
                }, UiTheme.WithAlpha(UiTheme.InkSoft, .94f), UiTheme.Paper, TextAlignmentOptions.Left);
                UiFactory.Size(btn.gameObject, string.IsNullOrEmpty(opt.hint) ? 92f : 122f);
            }
        }

        public void Toast(string msg)
        {
            var t = UiFactory.Text("Toast" + _toasts.Count, _toastRoot, msg, UiTheme.FontSmall + 2, UiTheme.GoldLight,
                TextAlignmentOptions.Right, 1.2f);
            UiFactory.Size(t.gameObject, 44f);
            _toasts.Add(t);
            _host.StartCoroutine(FadeToast(t));
        }

        IEnumerator FadeToast(TMP_Text t)
        {
            var c = t.color; c.a = 0f; t.color = c;
            float a = 0f;
            while (a < 1f) { a += Time.unscaledDeltaTime * 4f; c.a = a; t.color = c; yield return null; }
            yield return new WaitForSecondsRealtime(2.2f);
            while (a > 0f) { a -= Time.unscaledDeltaTime * 2f; c.a = Mathf.Max(0, a); t.color = c; yield return null; }
            _toasts.Remove(t);
            UnityEngine.Object.Destroy(t.gameObject);
        }
    }

    /// <summary>用若干细条拼出金色边框与装饰角。</summary>
    public static class Deco
    {
        public static void Border(RectTransform parent, Color color, float thickness = 2f, float inset = 0f)
        {
            Edges(parent, color, thickness, inset);
        }

        public static void Edges(RectTransform parent, Color color, float thickness, float inset)
        {
            Top(parent, color, thickness, inset);
            Bottom(parent, color, thickness, inset);
            Left(parent, color, thickness, inset);
            Right(parent, color, thickness, inset);
        }

        static void Top(RectTransform p, Color c, float t, float i)
        {
            var img = UiFactory.Panel("EdgeT", p, c, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            var r = img.rectTransform; r.pivot = new Vector2(.5f, 1f);
            r.offsetMin = new Vector2(i, -i - t); r.offsetMax = new Vector2(-i, -i);
        }
        static void Bottom(RectTransform p, Color c, float t, float i)
        {
            var img = UiFactory.Panel("EdgeB", p, c, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, Vector2.zero);
            var r = img.rectTransform; r.pivot = new Vector2(.5f, 0f);
            r.offsetMin = new Vector2(i, i); r.offsetMax = new Vector2(-i, i + t);
        }
        static void Left(RectTransform p, Color c, float t, float i)
        {
            var img = UiFactory.Panel("EdgeL", p, c, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, Vector2.zero);
            var r = img.rectTransform; r.pivot = new Vector2(0f, .5f);
            r.offsetMin = new Vector2(i, i); r.offsetMax = new Vector2(i + t, -i);
        }
        static void Right(RectTransform p, Color c, float t, float i)
        {
            var img = UiFactory.Panel("EdgeR", p, c, new Vector2(1, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            var r = img.rectTransform; r.pivot = new Vector2(1f, .5f);
            r.offsetMin = new Vector2(-i - t, i); r.offsetMax = new Vector2(-i, -i);
        }
    }
}

