using System;
using System.Collections.Generic;
using Styles.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Styles.UI
{
    /// <summary>整个游戏的界面根节点：用代码搭建 Canvas，并实现剧本解释器需要的视图接口。</summary>
    public class GameUI : MonoBehaviour, IVnView
    {
        public static GameUI Instance { get; private set; }

        ContentDatabase _db;
        DialogueView _dialogue;
        Overlays _overlays;
        Puzzles _puzzles;
        ChapterCard _chapterCard;
        RectTransform _canvasRoot, _bgLayer, _figureLayer, _advance, _dialogueLayer, _choiceLayer, _overlayLayer, _hudLayer, _toastLayer, _titleLayer;
        Image _bgA, _bgB;
        bool _usingA = true;
        readonly Dictionary<string, Image> _figures = new Dictionary<string, Image>();
        TMP_Text _clueCount;
        Action _pendingNext;
        Sprite _titleArt;

        void Awake()
        {
            Instance = this;
            SettingsService.Apply();
            BuildCanvas();
            BuildTitle();
            try
            {
                _db = ContentDatabase.Load(new StreamingAssetsContentSource());
                Debug.Log("[Styles] 内容载入完成：章节 " + _db.Chapters.Count + " / 证据 " + _db.Evidence.Count);
            }
            catch (Exception e)
            {
                Debug.LogError("[Styles] 内容载入失败：" + e.Message);
            }
            // 第五个参数是「读档」入口：从标题画面直接读档时也要能把游戏跑起来
            _overlays.Bind(_db, new GameState(), BackToTitle, Restart, LoadFromSlot);
        }

        // ---------------------------------------------------------
        // 构建界面
        // ---------------------------------------------------------
        void BuildCanvas()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UiTheme.RefWidth, UiTheme.RefHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }

            _canvasRoot = UiFactory.Stretch("SafeArea", canvasGo.transform);
            _canvasRoot.gameObject.AddComponent<SafeAreaFitter>();

            _bgLayer = UiFactory.Stretch("Backgrounds", _canvasRoot);
            _bgA = UiFactory.StretchImage("BgA", _bgLayer, Color.black);
            _bgB = UiFactory.StretchImage("BgB", _bgLayer, Color.black);
            _bgA.preserveAspect = false; _bgB.preserveAspect = false;
            _bgA.gameObject.SetActive(false);

            _figureLayer = UiFactory.Stretch("Figures", _canvasRoot);
            _figureLayer.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;

            _advance = UiFactory.Stretch("AdvanceLayer", _canvasRoot);
            var advImg = _advance.gameObject.AddComponent<Image>();
            advImg.color = new Color(0, 0, 0, 0);
            advImg.raycastTarget = true;
            var advBtn = _advance.gameObject.AddComponent<Button>();
            advBtn.transition = Selectable.Transition.None;
            advBtn.onClick.AddListener(() => { if (_dialogue != null) _dialogue.Advance(); });

            _dialogueLayer = UiFactory.Stretch("DialogueLayer", _canvasRoot);
            _choiceLayer = UiFactory.Stretch("ChoiceLayer", _canvasRoot);
            var choiceV = _choiceLayer.gameObject.AddComponent<VerticalLayoutGroup>();
            choiceV.spacing = 16; choiceV.childAlignment = TextAnchor.MiddleCenter;
            choiceV.childForceExpandHeight = false; choiceV.childForceExpandWidth = true;
            choiceV.childControlHeight = true; choiceV.childControlWidth = true;
            choiceV.padding = new RectOffset(320, 320, 220, 260);
            _choiceLayer.gameObject.SetActive(false);

            _overlayLayer = UiFactory.Stretch("OverlayLayer", _canvasRoot);
            _hudLayer = UiFactory.Stretch("HudLayer", _canvasRoot);
            _toastLayer = UiFactory.Rect("ToastLayer", _canvasRoot, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-620, -640), new Vector2(-40, -120));
            var tv = _toastLayer.gameObject.AddComponent<VerticalLayoutGroup>();
            tv.spacing = 8; tv.childAlignment = TextAnchor.UpperRight;
            tv.childForceExpandHeight = false; tv.childForceExpandWidth = true;
            tv.childControlHeight = true; tv.childControlWidth = true;

            TMP_Text tag, title, clues;
            _dialogue = new DialogueView(this, _dialogueLayer, _advance, _hudLayer, _choiceLayer, _toastLayer, out tag, out title, out clues);
            _clueCount = clues;
            _dialogue.Root.gameObject.SetActive(false);

            _chapterCard = new ChapterCard(this, _overlayLayer);
            _overlays = new Overlays(this, _overlayLayer);
            _puzzles = new Puzzles(this, _overlayLayer, _dialogue, () => GameDirector.Instance.State, () => _db);
            // 弹层开着的时候不许自动翻页（否则玩家在看笔记本，剧情在背后自己跑）
            _dialogue.AutoBlocked = () => _overlays != null && _overlays.IsOpen;

            BuildHudButtons();
            SetVisibleGame(false);
        }

        void BuildHudButtons()
        {
            var row = UiFactory.Rect("HudButtons", _hudLayer, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-640, -92), new Vector2(-20, -16));
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8; h.childAlignment = TextAnchor.MiddleRight;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            h.childControlWidth = true; h.childControlHeight = true;
            AddHud(row, "笔记本", () => { _overlays.Bind(_db, GameDirector.Instance.State, BackToTitle, Restart); _overlays.ShowNotebook(); });
            AddHud(row, "人物", () => { _overlays.Bind(_db, GameDirector.Instance.State, BackToTitle, Restart); _overlays.ShowCodex(); });
            AddHud(row, "回顾", () => { _overlays.Bind(_db, GameDirector.Instance.State, BackToTitle, Restart); _overlays.ShowBacklog(); });
            AddHud(row, "菜单", OpenMenu);
        }

        void AddHud(RectTransform parent, string label, Action onClick)
        {
            var b = UiFactory.Button("Hud_" + label, parent, label, UiTheme.FontSmall, onClick,
                UiTheme.WithAlpha(UiTheme.Ink, .82f), UiTheme.Paper);
            UiFactory.Size(b.gameObject, 64f, 128f);
        }

        void OpenMenu()
        {
            if (GameDirector.Instance != null)
                _overlays.Bind(_db, GameDirector.Instance.State, BackToTitle, Restart);
            _overlays.ShowMenu();
        }

        void SetVisibleGame(bool on)
        {
            _bgLayer.gameObject.SetActive(on);
            _figureLayer.gameObject.SetActive(on);
            _hudLayer.gameObject.SetActive(on);
        }

        // ---------------------------------------------------------
        // 标题画面
        // ---------------------------------------------------------
        void BuildTitle()
        {
            _titleLayer = UiFactory.Stretch("Title", _canvasRoot);
            var bg = UiFactory.StretchImage("Art", _titleLayer, UiTheme.Ink);
            _titleArt = AssetService.Load("Art/Backgrounds/title_styles");
            if (_titleArt != null && _titleArt != AssetService.Missing()) { bg.sprite = _titleArt; bg.color = Color.white; }

            var shade = UiFactory.StretchImage("Shade", _titleLayer, new Color(0, 0, 0, .45f));
            shade.raycastTarget = false;

            // 版面自上而下分带：主标题 / 副标题 / 标语 / 菜单。
            // 原来标语（0.38~0.52）与菜单（约 0.11~0.57）是重叠的，第一颗按钮压在文字上。
            var title = UiFactory.Text("Title", _titleLayer, "斯泰尔斯庄园奇案", 96, UiTheme.Paper, TextAlignmentOptions.Center, 1.2f);
            title.rectTransform.anchorMin = new Vector2(0, .70f);
            title.rectTransform.anchorMax = new Vector2(1, .86f);
            var sub = UiFactory.Text("Sub", _titleLayer, "THE MYSTERIOUS AFFAIR AT STYLES　·　一九二〇", 26, UiTheme.Gold, TextAlignmentOptions.Center, 1.4f);
            sub.rectTransform.anchorMin = new Vector2(0, .64f);
            sub.rectTransform.anchorMax = new Vector2(1, .70f);
            var tagline = UiFactory.Text("Tagline", _titleLayer,
                "一九一七年七月，埃塞克斯郡。\n一位上了年纪的太太在锁紧的卧室里死去。\n这一次，由你来做赫尔克里·波洛。",
                28, UiTheme.PaperDim, TextAlignmentOptions.Center, 1.6f);
            tagline.rectTransform.anchorMin = new Vector2(0, .50f);
            tagline.rectTransform.anchorMax = new Vector2(1, .64f);

            // 显式定位（轴心先行 + 直接给 anchoredPosition/sizeDelta），
            // 不要「先设偏移再改轴心」——那会把整块面板搬走，菜单就会压到标语上。
            var menu = UiFactory.Rect("Menu", _titleLayer, new Vector2(.5f, 0f), new Vector2(.5f, 0f),
                Vector2.zero, Vector2.zero);
            menu.pivot = new Vector2(.5f, 0);
            menu.anchoredPosition = new Vector2(0f, 92f);
            menu.sizeDelta = new Vector2(520f, 390f);
            var v = menu.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 10; v.childAlignment = TextAnchor.UpperCenter;
            v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            v.childControlHeight = true; v.childControlWidth = true;

            AddTitleButton(menu, "开始案件调查", () =>
            {
                _titleLayer.gameObject.SetActive(false);
                SetVisibleGame(true);
                StartCoroutine(Boot());
            });
            AddTitleButton(menu, "继续上次调查", () =>
            {
                var st = SaveSystem.Load(SaveSystem.AutoSlot) ?? SaveSystem.Load(1);
                if (st == null) { _dialogue.Toast("没有找到存档"); return; }
                _titleLayer.gameObject.SetActive(false);
                SetVisibleGame(true);
                StartCoroutine(Boot(st));
            });
            AddTitleButton(menu, "人物与关系图", () =>
            {
                _overlays.Bind(_db, new GameState(), BackToTitle, Restart);
                _overlays.ShowCodex();
            });
            AddTitleButton(menu, "设置", () =>
            {
                _overlays.Bind(_db, new GameState(), BackToTitle, Restart);
                _overlays.ShowMenu();
            });
            AddTitleButton(menu, "退出游戏", Quit);
        }

        void AddTitleButton(RectTransform parent, string label, Action onClick)
        {
            var b = UiFactory.Button("M_" + label, parent, label, UiTheme.FontButton, onClick,
                UiTheme.WithAlpha(UiTheme.Ink, .78f), UiTheme.Paper);
            UiFactory.Size(b.gameObject, 70f);
        }

        System.Collections.IEnumerator Boot(GameState loaded = null)
        {
            if (_db == null)
            {
                _dialogue.Toast("内容载入失败，请检查 StreamingAssets/content");
                yield break;
            }
            // 注意：UnityEngine.Object 不能和 ?? 一起用 —— 被销毁的对象在 C# 里不是 null，
            // ?? 会把它当成有效值返回，之后一路 MissingReferenceException。
            var dir = GameDirector.Instance != null ? GameDirector.Instance : gameObject.AddComponent<GameDirector>();
            dir.Initialize(_db, this, loaded);
            _overlays.Bind(_db, dir.State, BackToTitle, Restart);
            if (loaded == null) dir.StartNewGame(); else dir.LoadState(loaded);
            _clueCount.text = "笔记本 " + dir.State.clues.Count;
        }

        void BackToTitle()
        {
            EndCurrentFlow();
            SetVisibleGame(false);
            _dialogue.SetVisible(false);
            _titleLayer.gameObject.SetActive(true);
        }

        /// <summary>
        /// 离开当前这一局之前先把场面收干净：
        /// HUD 是画在最上层的，玩家可以在推理 / 调查 / 询问 / 选项 / 章节卡演出的**中途**
        /// 打开菜单点「返回标题」；以前这么做会把调查面板、选项框、章节卡整个留在标题画面上，
        /// 而且章节卡的协程还会接着把剧情往下推。这里统一停剧情 + 撤面板 + 关卡片。
        /// </summary>
        void EndCurrentFlow()
        {
            if (GameDirector.Instance != null) GameDirector.Instance.Stop();
            ClearPuzzles();
            if (_choiceLayer != null) _choiceLayer.gameObject.SetActive(false);
            _chapterCard.Hide();
            _dialogue.CancelAuto();
        }

        /// <summary>把解谜类界面（推理 / 调查 / 询问 / 指认）从场上撤掉。</summary>
        void ClearPuzzles()
        {
            if (_overlayLayer == null) return;
            foreach (var n in new[] { "Deduce", "Investigate", "Ask", "Accuse" })
            {
                var t = _overlayLayer.Find(n);
                if (t != null) { t.gameObject.SetActive(false); UnityEngine.Object.Destroy(t.gameObject); }
            }
        }

        /// <summary>从任意弹层读档：保证 GameDirector 存在、标题画面收起，再把状态装进去。</summary>
        void LoadFromSlot(GameState st)
        {
            if (st == null || _db == null) return;
            EndCurrentFlow();
            _titleLayer.gameObject.SetActive(false);
            SetVisibleGame(true);
            _dialogue.SetVisible(false);
            var dir = GameDirector.Instance != null ? GameDirector.Instance : gameObject.AddComponent<GameDirector>();
            dir.Initialize(_db, this, st);
            _overlays.Bind(_db, dir.State, BackToTitle, Restart, LoadFromSlot);
            dir.LoadState(st);
            _clueCount.text = "笔记本 " + dir.State.clues.Count;
        }

        void Restart()
        {
            _overlays.Close();
            EndCurrentFlow();
            var dir = GameDirector.Instance;
            if (dir != null) { dir.State = new GameState(); dir.LoadState(dir.State); }
        }

        /// <summary>
        /// 体检专用（正常游玩路径不会调用它）：某一项检查中途抛异常时，把界面收回干净状态。
        /// 否则失败项的残骸会污染后面每一项 —— 实测「现场调查」断言失败留下的调查面板，
        /// 会让紧随其后的「询问」也被误判成失败，一个毛病报成两个。
        /// </summary>
        public void ResetForCheck()
        {
            _overlays.Close();
            _dialogue.AutoMode = false;
            _dialogue.SkipMode = false;
            EndCurrentFlow();
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------------------------------------------------
        // IVnView 实现
        // ---------------------------------------------------------
        public void ShowSay(Beat b, Action next)
        {
            _dialogue.ShowSay(b, _db, next);
            _clueCount.text = "笔记本 " + GameDirector.Instance.State.clues.Count;
        }

        public void ShowChapter(Beat b, Action next) { _chapterCard.Show(b, next); }

        public void ShowChoice(Beat b, Action<Option> pick) { _dialogue.ShowChoices(b, pick); }

        public void ShowDeduce(Beat b, Action<int> resolve)
        {
            // 答错要算数：以前这里什么都不做，导致失误数永远是 0、也不扣分。
            _puzzles.ShowDeduce(b, resolve, _ =>
            {
                var st = GameDirector.Instance != null ? GameDirector.Instance.State : null;
                if (st == null) return;
                st.mistakes++;
                st.score = Mathf.Max(0, st.score - 3);
                _clueCount.text = "笔记本 " + st.clues.Count;
            });
        }

        public void ShowInvestigate(InvestigationScene sc, Action done)
        {
            string key = sc.id;
            _puzzles.ShowInvestigate(sc, key, () =>
            {
                _clueCount.text = "笔记本 " + GameDirector.Instance.State.clues.Count;
                done();
            });
        }

        public void ShowAsk(List<AskCharacter> chars, int need, string title, Action done)
        {
            _puzzles.ShowAsk(chars, need, title, () =>
            {
                _clueCount.text = "笔记本 " + GameDirector.Instance.State.clues.Count;
                done();
            });
        }

        public void ShowNote(Beat b, Action done)
        {
            _overlays.Bind(_db, GameDirector.Instance.State, BackToTitle, Restart);
            _overlays.OpenNote(b, done);
        }

        public void ShowAccusation(List<AccusationQuestion> qs, Action<List<AccuseRecord>> done)
        {
            _puzzles.ShowAccusation(qs, done);
        }

        public void ShowEnding(GameState st, ContentDatabase db)
        {
            _overlays.Bind(db, st, BackToTitle, Restart);
            _overlays.ShowEnding(st, db, Restart);
            _dialogue.SetVisible(false);
        }

        public void SetBackground(string id, bool instant)
        {
            GameDirector.Instance.State.bg = id;
            var sprite = AssetService.Background(id);
            var target = _usingA ? _bgB : _bgA;
            var current = _usingA ? _bgA : _bgB;
            target.sprite = sprite;
            target.color = sprite == AssetService.Missing() ? UiTheme.Hex("#1B2A26") : Color.white;
            target.gameObject.SetActive(true);
            if (instant) { target.color = new Color(1, 1, 1, 1); current.gameObject.SetActive(false); }
            else StartCoroutine(Crossfade(current, target));
            _usingA = !_usingA;
        }

        System.Collections.IEnumerator Crossfade(Image from, Image to)
        {
            float t = 0f;
            var c = to.color; c.a = 0f; to.color = c;
            while (t < .5f)
            {
                t += Time.unscaledDeltaTime;
                var cc = to.color; cc.a = Mathf.Clamp01(t / .5f); to.color = cc;
                yield return null;
            }
            if (from != to) from.gameObject.SetActive(false);
        }

        public void SetFigures(List<FigureState> figs)
        {
            var keep = new List<string>();
            if (figs != null)
                foreach (var f in figs) keep.Add(f.id + "#" + f.pos + "#" + f.emotion + "#" + f.flip);
            var toRemove = new List<string>();
            foreach (var kv in _figures) if (!keep.Contains(kv.Key)) toRemove.Add(kv.Key);
            foreach (var k in toRemove) { Destroy(_figures[k].gameObject); _figures.Remove(k); }
            if (figs == null) return;
            foreach (var f in figs)
            {
                var key = f.id + "#" + f.pos + "#" + f.emotion + "#" + f.flip;
                Image img;
                if (!_figures.TryGetValue(key, out img))
                {
                    var rect = UiFactory.Rect("Fig_" + key, _figureLayer, new Vector2(PosX(f.pos), 0), new Vector2(PosX(f.pos), 0),
                        Vector2.zero, Vector2.zero);
                    img = rect.gameObject.AddComponent<Image>();
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    _figures[key] = img;
                }
                img.sprite = AssetService.Portrait(f.id, f.emotion);
                img.color = img.sprite == AssetService.Missing()
                    ? UiTheme.WithAlpha(UiTheme.Wine, .55f)
                    : (f.dim ? new Color(.62f, .62f, .62f, 1f) : Color.white);
                var r = img.rectTransform;
                r.sizeDelta = new Vector2(UiTheme.RefHeight * 0.52f * 0.66f, UiTheme.RefHeight * 0.52f);
                r.anchoredPosition = new Vector2(0, r.sizeDelta.y * .5f + 20f);
                r.localScale = new Vector3(f.flip ? -1f : 1f, 1f, 1f);
            }
        }

        static float PosX(string pos)
        {
            switch (pos)
            {
                case "left": return .18f;
                case "midL": return .34f;
                case "mid": return .5f;
                case "midR": return .66f;
                case "right": return .82f;
                default: return .5f;
            }
        }

        public void PlayFx(string fx)
        {
            if (string.IsNullOrEmpty(fx)) return;
            if (SettingsService.Current.reduceMotion && fx == "shake") return;
            switch (fx)
            {
                case "flash": StartCoroutine(Flash()); break;
                case "shake": StartCoroutine(Shake()); break;
                case "fade": StartCoroutine(FadeOut()); break;
                default: break;
            }
        }

        System.Collections.IEnumerator Flash()
        {
            var go = UiFactory.StretchImage("Flash", _canvasRoot, Color.white);
            go.raycastTarget = false;
            float t = 0f;
            while (t < .28f) { t += Time.unscaledDeltaTime; var c = Color.white; c.a = 1f - t / .28f; go.color = c; yield return null; }
            Destroy(go.gameObject);
        }

        /// <summary>镜头震动：轻微上下位移，用来强调「这句话有冲击力」。</summary>
        System.Collections.IEnumerator Shake()
        {
            var layers = new[] { _bgLayer, _figureLayer };
            var original = new System.Collections.Generic.List<Vector2>();
            foreach (var l in layers) original.Add(l.anchoredPosition);
            float t = 0f, dur = .45f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float damp = 1f - t / dur;
                float dx = Mathf.Sin(t * 62f) * 14f * damp;
                float dy = Mathf.Sin(t * 47f) * 9f * damp;
                for (int i = 0; i < layers.Length; i++)
                    if (layers[i] != null) layers[i].anchoredPosition = original[i] + new Vector2(dx, dy);
                yield return null;
            }
            for (int i = 0; i < layers.Length; i++)
                if (layers[i] != null) layers[i].anchoredPosition = original[i];
        }

        /// <summary>黑场一闪：用来分隔「死亡 / 时间跳跃」这种段落。</summary>
        System.Collections.IEnumerator FadeOut()
        {
            var go = UiFactory.StretchImage("Fade", _canvasRoot, new Color(0, 0, 0, 0));
            go.raycastTarget = false;
            float t = 0f;
            while (t < .35f)
            {
                t += Time.unscaledDeltaTime;
                go.color = new Color(0, 0, 0, Mathf.Clamp01(t / .35f));
                yield return null;
            }
            yield return new WaitForSecondsRealtime(.35f);
            t = 0f;
            while (t < .6f)
            {
                t += Time.unscaledDeltaTime;
                go.color = new Color(0, 0, 0, 1f - Mathf.Clamp01(t / .6f));
                yield return null;
            }
            Destroy(go.gameObject);
        }

        public void Toast(string msg) { _dialogue.Toast(msg); }
        public void SetHud(string tag, string title)
        {
            var t = _hudLayer.Find("Hud/Tag");
            var n = _hudLayer.Find("Hud/Title");
            if (t) t.GetComponent<TMP_Text>().text = tag ?? "";
            if (n) n.GetComponent<TMP_Text>().text = title ?? "";
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && !_overlays.IsOpen) OpenMenu();
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                if (!_overlays.IsOpen && _dialogue.Root.gameObject.activeSelf) _dialogue.Advance();
            }
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) _dialogue.SkipMode = true;
            else if (Input.GetKeyUp(KeyCode.LeftControl) || Input.GetKeyUp(KeyCode.RightControl)) _dialogue.SkipMode = false;
        }
    }
}
