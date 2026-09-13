using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Styles.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Styles.Core
{
    /// <summary>
    /// 框架功能自动体检。
    ///
    /// 平时完全不工作（没带 -styles-selfcheck 就直接把自己关掉）。
    /// 带上这个命令行参数启动成品后，它会：逐个打开所有界面、点击每一个按钮、
    /// 逐步截图、把结果写成 ToolsOut/system_check.json / .txt，然后自己退出。
    ///
    /// 目的不是"跑剧情"，而是把框架的每一条通道都真的走一遍：
    /// 标题 / 对话 / 选项 / 推理 / 调查 / 询问 / 笔记 / 笔记本 / 人物关系图 / 回顾 /
    /// 菜单 / 存档 / 读档 / 设置 / 章节卡 / 结算 / 特效 / 存读档与设置的边界情况 /
    /// 以及剧本里根本没用到的那些解释器指令（wait、label、jump、set、if…）。
    /// </summary>
    public class SystemCheck : MonoBehaviour
    {
        class Row
        {
            public string name;
            public bool ok;
            public string detail = "";
        }

        readonly List<Row> _rows = new List<Row>();
        readonly List<string> _shots = new List<string>();
        readonly List<string> _errorLog = new List<string>();
        readonly List<string> _warnLog = new List<string>();
        readonly List<string> _notes = new List<string>();
        Func<List<AccuseRecord>> _lastAccusation;   // 指认项的结果，结算屏要用
        string _outDir, _shotDir;
        int _shotIndex;
        float _deadline;
        bool _timedOut;

        public static bool Requested
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                return args != null && args.Any(a => a == "-styles-selfcheck");
            }
        }

        void Awake()
        {
            if (!Requested) { enabled = false; return; }
            _outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ToolsOut"));
            // 每次体检一套独立目录，避免和上一次的截图混在一起
            _shotDir = Path.Combine(_outDir, "shots", DateTime.Now.ToString("MMdd-HHmmss"));
            Directory.CreateDirectory(_shotDir);
            Application.targetFrameRate = 60;
        }

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                _errorLog.Add(condition);
            else if (type == LogType.Warning)
                _warnLog.Add(condition);
        }

        void Start()
        {
            if (!Requested) return;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            _deadline = Time.realtimeSinceStartup + 300f;
            Debug.Log("[SystemCheck] 开始框架体检");
            yield return null;

            yield return CheckEnumerator("内容库载入", CheckContent);
            yield return CheckEnumerator("中文字体", CheckFont);
            yield return CheckEnumerator("素材解析（背景/立绘/占位）", CheckAssets);
            yield return CheckEnumerator("解释器：全部指令类型", CheckInterpreterInstructions);
            yield return CheckEnumerator("解释器：条件表达式", CheckConditions);
            yield return CheckEnumerator("解释器：跳转与分支", CheckControlFlow);
            yield return CheckEnumerator("解释器：异常输入", CheckBadInput);
            yield return CheckEnumerator("存档系统", CheckSaveSystem);
            yield return CheckEnumerator("设置系统", CheckSettings);
            yield return CheckEnumerator("音频服务", CheckAudio);
            yield return CheckEnumerator("标题画面", CheckTitle);
            yield return CheckEnumerator("开新游戏（章节卡 → 对话）", CheckNewGame);
            yield return CheckEnumerator("对话推进 / 点击判定", CheckDialogue);
            yield return CheckEnumerator("自动播放 / 快进", CheckAutoPlay);
            yield return CheckEnumerator("HUD 计数器与按钮", CheckHud);
            yield return CheckEnumerator("笔记本", CheckNotebook);
            yield return CheckEnumerator("人物档案与关系图", CheckCodex);
            yield return CheckEnumerator("关系图排版（头像与名字不重叠）", CheckRelationMap);
            yield return CheckEnumerator("对话回顾", CheckBacklog);
            yield return CheckEnumerator("菜单 / 存读档 / 设置面板", CheckMenu);
            yield return CheckEnumerator("选项", CheckChoice);
            yield return CheckEnumerator("推理题", CheckDeduce);
            yield return CheckEnumerator("现场调查", CheckInvestigate);
            yield return CheckEnumerator("询问", CheckAsk);
            yield return CheckEnumerator("知识卡", CheckNote);
            yield return CheckEnumerator("最终指认", CheckAccusation);
            yield return CheckEnumerator("结算报告", CheckEnding);
            yield return CheckEnumerator("演出特效", CheckFx);
            yield return CheckEnumerator("回到标题再开新游戏", CheckRestartPath);
            yield return CheckEnumerator("中途撤退（调查 / 章节卡里返回标题）", CheckAbandonMidway);
            yield return CheckEnumerator("真通关（真实界面从标题玩到结局）", CheckFullPlaythrough);

            WriteReport();
            int bad = _rows.Count(r => !r.ok);
            Debug.Log("[SystemCheck] 完成：" + (_rows.Count - bad) + " 通过 / " + bad + " 失败");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(bad == 0 ? 0 : 1);
        }

        /// <summary>
        /// 逐层手工推进子枚举器。
        ///
        /// 不能把子枚举器直接 yield 给 Unity —— 那样它会被 Unity 的协程机制接管，
        /// 里面的异常会穿透整条协程链、既不进我们的 catch，也会把后续所有体检项一起中断
        /// （第一版就是这么只跑了 3 项就静默结束的）。
        /// 另外 C# 不允许在带 catch 的 try 里 yield，所以异常只能这样人工收集。
        /// </summary>
        IEnumerator CheckEnumerator(string name, Func<IEnumerator> body)
        {
            if (Time.realtimeSinceStartup > _deadline) { _timedOut = true; yield break; }
            int errorsBefore = _errorLog.Count;
            string error = null;

            var stack = new Stack<IEnumerator>();
            try { stack.Push(body()); }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }

            while (error == null && stack.Count > 0)
            {
                var top = stack.Peek();
                bool moved;
                object current = null;
                try
                {
                    moved = top.MoveNext();
                    if (moved) current = top.Current;
                }
                catch (Exception e)
                {
                    error = e.GetType().Name + ": " + e.Message;
                    break;
                }
                if (!moved) { stack.Pop(); continue; }
                if (current is IEnumerator) { stack.Push((IEnumerator)current); continue; }
                yield return current;
            }

            if (error != null)
            {
                Add(name, false, error);
                // 失败项留下的面板必须收掉：实测「现场调查」断言失败后调查面板一直挂着，
                // 紧接着的「询问」项就会抓到那块面板上的文字，把一个小毛病报成两个。
                if (GameUI.Instance != null) GameUI.Instance.ResetForCheck();
                yield return null;   // 让 Destroy 落地，下一项才能在干净界面上开工
                yield break;
            }

            int newErrors = _errorLog.Count - errorsBefore;
            if (newErrors > 0)
                Add(name, false, "执行期间产生 " + newErrors + " 条错误日志：" +
                    string.Join(" | ", _errorLog.Skip(errorsBefore).Take(3).ToArray()));
            else Add(name, true, "");
        }

        void Add(string name, bool ok, string detail)
        {
            _rows.Add(new Row { name = name, ok = ok, detail = detail });
            Debug.Log("[SystemCheck] " + (ok ? "[通过] " : "[失败] ") + name +
                      (string.IsNullOrEmpty(detail) ? "" : "  —— " + detail));
        }

        static void Assert(bool cond, string message)
        {
            if (!cond) throw new Exception(message);
        }

        static ContentDatabase LoadDb()
        {
            return ContentDatabase.Load(new StreamingAssetsContentSource());
        }

        static Button FindButton(string goName)
        {
            if (GameUI.Instance == null) return null;
            return GameUI.Instance.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(b => b.gameObject.name == goName);
        }

        static List<Button> ButtonsUnder(string pathContains)
        {
            return GameUI.Instance.GetComponentsInChildren<Button>(true)
                .Where(b => PathOf(b.transform).Contains(pathContains)).ToList();
        }

        static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var p = t.parent;
            while (p != null) { sb.Insert(0, p.name + "/"); p = p.parent; }
            return sb.ToString();
        }

        static TMP_Text FindText(string goName)
        {
            if (GameUI.Instance == null) return null;
            return GameUI.Instance.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(t => t.gameObject.name == goName);
        }

        static TMP_Text TextAt(string path)
        {
            if (GameUI.Instance == null) return null;
            var t = GameUI.Instance.transform.Find(path);
            return t == null ? null : t.GetComponent<TMP_Text>();
        }

        /// <summary>把「预期之内」的报错从统计里划掉（例如故意喂一个损坏的存档文件）。</summary>
        void ExpectErrors(int count)
        {
            if (count <= 0) return;
            int remove = Mathf.Min(count, _errorLog.Count);
            _errorLog.RemoveRange(_errorLog.Count - remove, remove);
        }

        /// <summary>把 RectTransform 的四角换算成屏幕像素矩形，用来做版面重叠检查。</summary>
        /// <summary>两个矩形真正重叠的面积（像素²）。只贴边不算重叠，避免整数化导致的假报警。</summary>
        static float OverlapArea(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return (w > 0f && h > 0f) ? w * h : 0f;
        }

        static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            float minX = Mathf.Min(Mathf.Min(c[0].x, c[1].x), Mathf.Min(c[2].x, c[3].x));
            float maxX = Mathf.Max(Mathf.Max(c[0].x, c[1].x), Mathf.Max(c[2].x, c[3].x));
            float minY = Mathf.Min(Mathf.Min(c[0].y, c[1].y), Mathf.Min(c[2].y, c[3].y));
            float maxY = Mathf.Max(Mathf.Max(c[0].y, c[1].y), Mathf.Max(c[2].y, c[3].y));
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        static void ForceLayout()
        {
            Canvas.ForceUpdateCanvases();
            if (GameUI.Instance != null)
            {
                var canvas = GameUI.Instance.GetComponentInChildren<Canvas>(true);
                if (canvas != null && canvas.rootCanvas != null)
                {
                    var root = canvas.rootCanvas.GetComponent<RectTransform>();
                    if (root != null) LayoutRebuilder.ForceRebuildLayoutImmediate(root);
                }
            }
            Canvas.ForceUpdateCanvases();
        }

        IEnumerator Shot(string name)
        {
            ForceLayout();
            for (int i = 0; i < 2; i++) yield return new WaitForEndOfFrame();
            string file = string.Format("{0:00}-{1}.png", ++_shotIndex, name);
            string full = Path.Combine(_shotDir, file);
            ScreenCapture.CaptureScreenshot(full);
            for (int i = 0; i < 3; i++) yield return new WaitForEndOfFrame();
            if (File.Exists(full)) _shots.Add(file);
        }

        IEnumerator Click(string goName)
        {
            var b = FindButton(goName);
            Assert(b != null, "找不到按钮：" + goName);
            Assert(b.interactable, "按钮不可点击：" + goName);
            var rt = b.GetComponent<RectTransform>();
            Assert(rt != null && rt.rect.width > 1f && rt.rect.height > 1f,
                "按钮尺寸异常：" + goName + " = " + (rt == null ? "无 RectTransform" : rt.rect.size.ToString()));
            b.onClick.Invoke();
            yield return null;
        }

        static IEnumerator WaitFrames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static IEnumerator WaitSeconds(float s)
        {
            float t = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < t) yield return null;
        }

        // ---------------------------------------------------------------- 内容 / 字体 / 素材

        IEnumerator CheckContent()
        {
            var db = LoadDb();
            Assert(db != null, "内容库为 null");
            Assert(db.Chapters.Count >= 9, "章节数 " + db.Chapters.Count);
            Assert(db.Evidence.Count > 0, "没有证据");
            Assert(db.Characters.Count > 0, "没有人物");
            Assert(db.Investigations.Count > 0, "没有调查场景");
            Assert(db.Interrogations.Count > 0, "没有询问对象");
            Assert(db.Accusation.Count > 0, "没有指认问题");
            Assert(db.Faith.Count > 0, "没有原著对照");
            Assert(db.Flatten().Count > 200, "演出指令过少");
            Assert(db.Character("__不存在__").name == "__不存在__", "未知人物没有回退成 id");
            // 知识卡必须真的有正文：转换脚本以前把原稿的 html 整段丢掉了，
            // 游戏里只剩标题 + 图标 + 「明白了」，看着像坏了，其实是内容缺字段。
            int emptyNotes = 0;
            foreach (var ch in db.Chapters)
                foreach (var b in ch.beats)
                    if (b.t == "note" && string.IsNullOrWhiteSpace(b.body)) emptyNotes++;
            Assert(emptyNotes == 0, "有 " + emptyNotes + " 张知识卡没有正文（body 为空）");
            yield return null;
        }

        IEnumerator CheckFont()
        {
            var font = FontService.Main;
            Assert(font != null, "TMP 字体为 null（Resources/Fonts/StylesSerif SDF 未生成？）");
            Assert(font.atlasTextures != null && font.atlasTextures.Length > 0 && font.atlasTextures[0] != null,
                "字体图集为空 —— 运行时添加字形会崩");
            Assert(font.material != null && font.material.shader != null, "字体材质/着色器缺失");

            // 探针必须挂进 Canvas：TMP 脱离 Canvas 时不排版，characterCount 会是 0
            var canvas = GameUI.Instance.GetComponentInChildren<Canvas>(true);
            Assert(canvas != null, "找不到 Canvas");
            var go = new GameObject("__fontprobe", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(600f, 120f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSize = 32;
            tmp.text = "斯泰尔斯庄园奇案";
            yield return null;
            tmp.ForceMeshUpdate();
            Assert(tmp.textInfo.characterCount == 8, "中文排版字形数 = " + tmp.textInfo.characterCount + "（期望 8）");
            Assert(tmp.preferredHeight > 10f, "中文排版高度异常");
            UnityEngine.Object.Destroy(go);

            // 顺带验一下真正在用的界面文字
            var title = TextAt("Canvas/SafeArea/Title/Title");
            if (title != null && !string.IsNullOrEmpty(title.text))
            {
                title.ForceMeshUpdate();
                Assert(title.textInfo.characterCount > 0, "标题界面的文字没有排版出任何字形（字体没生效）");
            }
            yield return null;
        }

        IEnumerator CheckAssets()
        {
            var db = LoadDb();
            var missing = AssetService.Missing();
            Assert(missing != null, "占位图创建失败");

            var bgIds = new HashSet<string>();
            var figs = new HashSet<string>();
            foreach (var b in db.Flatten())
            {
                if (b.t == "bg" && !string.IsNullOrEmpty(b.id)) bgIds.Add(b.id);
                if (b.t == "investigate")
                {
                    var key = string.IsNullOrEmpty(b.scene) ? b.sceneId : b.scene;
                    InvestigationScene sc;
                    if (db.Investigations.TryGetValue(key ?? "", out sc) && !string.IsNullOrEmpty(sc.bg)) bgIds.Add(sc.bg);
                }
                if (b.figs != null)
                    foreach (var f in b.figs) figs.Add(f.id + "|" + (string.IsNullOrEmpty(f.emotion) ? "neutral" : f.emotion));
            }
            foreach (var id in bgIds) Assert(AssetService.Background(id) != missing, "缺背景图：" + id);
            foreach (var f in figs)
            {
                var p = f.Split('|');
                Assert(AssetService.Portrait(p[0], p[1]) != missing, "缺立绘：" + f);
            }
            Assert(AssetService.Background("title_styles") != missing, "缺标题底图 title_styles");
            Assert(AssetService.Background("__不存在__") == missing, "缺图时没有回退到占位图");
            Assert(AssetService.Portrait("__不存在__", "neutral") == missing, "缺立绘时没有回退到占位图");
            Assert(AssetService.Portrait("poirot", "__没有的表情__") == AssetService.Portrait("poirot", "neutral"),
                "表情缺失时没有回退到 neutral");
            yield return null;
        }

        // ---------------------------------------------------------------- 解释器

        class AutoView : IVnView
        {
            public Action Pending;
            public bool Ended;
            public readonly List<string> Seen = new List<string>();
            public readonly List<string> Errors = new List<string>();
            public string LastHud = "";

            public void ShowSay(Beat b, Action next) { Seen.Add("say:" + b.text); Pending = next; }
            public void ShowChapter(Beat b, Action next) { Seen.Add("chapter"); Pending = next; }
            public void ShowChoice(Beat b, Action<Option> pick)
            {
                Seen.Add("choice");
                if (b.options == null || b.options.Count == 0) { Errors.Add("空选项组"); Pending = () => pick(null); return; }
                // 永远选第一项：选带 goto 的那一项会把剧本送回标签，形成人为死循环
                Pending = () => pick(b.options[0]);
            }
            public void ShowDeduce(Beat b, Action<int> resolve)
            {
                Seen.Add("deduce");
                if (b.answers == null || b.answer < 0 || b.answer >= b.answers.Count)
                {
                    Errors.Add("推理答案越界");
                    Pending = () => resolve(0);
                    return;
                }
                Pending = () => resolve(b.answer);
            }
            public void ShowInvestigate(InvestigationScene sc, Action done) { Seen.Add("investigate"); Pending = done; }
            public void ShowAsk(List<AskCharacter> chars, int need, string title, Action done) { Seen.Add("ask"); Pending = done; }
            public void ShowNote(Beat b, Action done) { Seen.Add("note"); Pending = done; }
            public void ShowAccusation(List<AccusationQuestion> qs, Action<List<AccuseRecord>> done)
            { Seen.Add("accuse"); Pending = () => done(new List<AccuseRecord>()); }
            public void ShowEnding(GameState st, ContentDatabase db) { Seen.Add("ending"); Ended = true; }
            public void SetBackground(string id, bool instant) { Seen.Add("bg:" + id); }
            public void SetFigures(List<FigureState> figs) { }
            public void PlayFx(string fx) { Seen.Add("fx:" + fx); }
            public void Toast(string msg) { }
            public void SetHud(string tag, string title) { LastHud = tag + "/" + title; }
        }

        static ContentDatabase MakeSyntheticDb(params Beat[] beats)
        {
            var db = new ContentDatabase();
            var ch = new Chapter { id = "synthetic", title = "体检", act = "测试", summary = "自动生成" };
            ch.beats.AddRange(beats);
            db.Chapters.Add(ch);
            // 合成的剧本要引用真实素材/场景/人物，否则「调查/询问」这两项会被判成找不到
            try
            {
                var real = LoadDb();
                foreach (var kv in real.Investigations) db.Investigations[kv.Key] = kv.Value;
                foreach (var kv in real.Interrogations) db.Interrogations[kv.Key] = kv.Value;
                foreach (var kv in real.Evidence) db.Evidence[kv.Key] = kv.Value;
                foreach (var kv in real.Characters) db.Characters[kv.Key] = kv.Value;
            }
            catch (Exception) { }
            return db;
        }

        static IEnumerator DriveInterpreter(ContentDatabase db, AutoView view, int maxSteps = 4000)
        {
            var go = new GameObject("__dircheck");
            var dir = go.AddComponent<GameDirector>();
            try
            {
                dir.Initialize(db, view, null);
                dir.StartNewGame();
                int guard = maxSteps;
                int sinceYield = 0;
                int idleFrames = 0;
                while (!view.Ended && guard-- > 0)
                {
                    if (view.Pending == null)
                    {
                        // 带 wait 的指令要靠协程回调，不能一帧没动静就判定卡住
                        if (++idleFrames < 180) { yield return null; continue; }
                        Assert(false, "剧本卡住了：连续 " + idleFrames + " 帧没有任何推进（剩 " + guard + " 步）");
                        yield return null;
                        continue;
                    }
                    idleFrames = 0;
                    var act = view.Pending;
                    view.Pending = null;
                    act();
                    if (++sinceYield >= 8) { sinceYield = 0; yield return null; }
                }
                Assert(view.Ended, "剧本没有走到 ending（剩 " + guard + " 步）");
                Assert(view.Errors.Count == 0, string.Join("；", view.Errors.ToArray()));
            }
            finally
            {
                UnityEngine.Object.Destroy(go);
            }
            yield return null;
        }

        IEnumerator CheckInterpreterInstructions()
        {
            var db = MakeSyntheticDb(
                new Beat { t = "chapter", tag = "测试", title = "全部指令", sub = "自动" },
                new Beat { t = "bg", id = "styles_hall" },
                new Beat { t = "bgm", id = "__没有这首曲子__" },
                new Beat { t = "sfx", id = "__没有这个音效__" },
                new Beat { t = "say", who = "poirot", text = "第一句", fx = "flash" },
                new Beat { t = "fx", fx = "shake" },
                new Beat { t = "fx", fx = "fade" },
                new Beat { t = "set", set = "trust", value = 2 },
                new Beat { t = "wait", wait = 0.05f },
                new Beat { t = "label", id = "L1" },
                new Beat { t = "say", who = "narr", text = "标签之后" },
                new Beat { t = "choice", prompt = "选一个", options = new List<Option> {
                    new Option { text = "给线索", give = "cup", score = 5 },
                    new Option { text = "跳到标签", gotoLabel = "L1" }
                } },
                new Beat { t = "note", title = "知识卡", icon = "◆", body = "正文" },
                new Beat { t = "investigate", scene = "styles_scene_chemist", need = 1 },
                new Beat { t = "ask", chars = new List<string> { "dorcas" }, need = 1, title = "问话" },
                new Beat { t = "deduce", question = "推理", answers = new List<string> { "甲", "乙" }, answer = 1,
                           wrong = new List<string> { "不对" }, explain = "对", score = 10 },
                new Beat { t = "accuse" },
                new Beat { t = "ending" });

            var view = new AutoView();
            yield return DriveInterpreter(db, view);

            Assert(view.Seen.Any(s => s == "chapter"), "章节卡没有出现");
            Assert(view.Seen.Any(s => s.StartsWith("fx:flash")), "say 上的 flash 没有播");
            Assert(view.Seen.Any(s => s == "fx:shake"), "fx 指令 shake 没有播");
            Assert(view.Seen.Any(s => s == "fx:fade"), "fx 指令 fade 没有播");
            Assert(view.Seen.Any(s => s == "note"), "知识卡没有出现");
            Assert(view.Seen.Any(s => s == "investigate"), "调查没有出现");
            Assert(view.Seen.Any(s => s == "ask"), "询问没有出现");
            Assert(view.Seen.Any(s => s == "deduce"), "推理没有出现");
            Assert(view.Seen.Any(s => s == "accuse"), "指认没有出现");
            Assert(view.Seen.Any(s => s == "ending"), "结局没有触发");
            yield return null;
        }

        IEnumerator CheckConditions()
        {
            var go = new GameObject("__condcheck");
            var dir = go.AddComponent<GameDirector>();
            try
            {
                dir.Initialize(MakeSyntheticDb(), new AutoView(), null);
                var st = dir.State;
                st.AddClue("cup");
                st.SetFlag("trust", 2);

                Assert(dir.EvalCondition(""), "空条件应为真");
                Assert(dir.EvalCondition("clue:cup"), "clue: 判定错误");
                Assert(!dir.EvalCondition("clue:nope"), "clue: 不存在时应为假");
                Assert(dir.EvalCondition("notclue:nope"), "notclue: 判定错误");
                Assert(!dir.EvalCondition("notclue:cup"), "notclue: 已存在时应为假");
                Assert(dir.EvalCondition("flag:trust"), "flag: 非零应为真");
                Assert(!dir.EvalCondition("flag:none"), "flag: 未设置应为假");
                Assert(dir.EvalCondition("flag:trust>=2"), ">= 判定错误");
                Assert(!dir.EvalCondition("flag:trust>2"), "> 判定错误");
                Assert(dir.EvalCondition("flag:trust<=2"), "<= 判定错误");
                Assert(!dir.EvalCondition("flag:trust<2"), "< 判定错误");
                Assert(dir.EvalCondition("clue:cup;flag:trust>=1"), "分号连接错误");
                Assert(dir.EvalCondition("clue:cup&&flag:trust>=1"), "&& 连接错误");
                Assert(!dir.EvalCondition("clue:cup;clue:nope"), "多条件中有一条假应为假");
            }
            finally { UnityEngine.Object.Destroy(go); }
            yield return null;
        }

        IEnumerator CheckControlFlow()
        {
            var db = MakeSyntheticDb(
                new Beat { t = "set", set = "go", value = 1 },
                new Beat { t = "if", cond = "flag:go>=1",
                    then = new List<Beat> {
                        new Beat { t = "say", who = "narr", text = "进入分支" },
                        new Beat { t = "if", cond = "clue:cup",
                            then = new List<Beat> { new Beat { t = "say", who = "narr", text = "有线索" } },
                            els = new List<Beat> { new Beat { t = "say", who = "narr", text = "无线索" } } }
                    },
                    els = new List<Beat> { new Beat { t = "say", who = "narr", text = "不该走这里" } } },
                new Beat { t = "jump", target = "SKIP" },
                new Beat { t = "say", who = "narr", text = "不该被念到" },
                new Beat { t = "label", id = "SKIP" },
                new Beat { t = "say", who = "narr", text = "跳转之后" },
                new Beat { t = "ending" });

            var view = new AutoView();
            yield return DriveInterpreter(db, view);
            Assert(view.Seen.Any(s => s.Contains("进入分支")), "if 的 then 没有执行");
            Assert(view.Seen.Any(s => s.Contains("无线索")), "嵌套 if 的 els 没有执行");
            Assert(!view.Seen.Any(s => s.Contains("不该走这里")), "if 的 els 被错误执行");
            Assert(!view.Seen.Any(s => s.Contains("不该被念到")), "jump 没有跳过中间内容");
            Assert(view.Seen.Any(s => s.Contains("跳转之后")), "jump 之后没有继续");
            yield return null;
        }

        IEnumerator CheckBadInput()
        {
            var db = MakeSyntheticDb(
                new Beat { t = "jump", target = "__不存在的标签__" },
                new Beat { t = "investigate", scene = "__不存在的场景__" },
                new Beat { t = "ask", chars = new List<string> { "__不存在的人__" }, need = 1 },
                new Beat { t = "zzz未知指令" },
                new Beat { t = "say", who = "narr", text = "还能继续" },
                new Beat { t = "ending" });
            var view = new AutoView();

            int warningsBefore = _warnLog.Count;
            yield return DriveInterpreter(db, view);
            Assert(view.Seen.Any(s => s.Contains("还能继续")), "异常指令之后剧情没有继续");
            int newWarnings = _warnLog.Count - warningsBefore;
            Assert(newWarnings >= 3, "异常输入只产生了 " + newWarnings + " 条提醒（期望 >= 3）");
            yield return null;
        }

        // ---------------------------------------------------------------- 存档 / 设置 / 音频

        IEnumerator CheckSaveSystem()
        {
            var st = new GameState { chapterId = "ch01", beatIndex = 7, score = 42, bg = "styles_hall" };
            st.AddClue("cup");
            st.SetFlag("trust", 3);
            st.Log("narr", "一句话");

            for (int i = 0; i < SaveSystem.SlotCount; i++)
            {
                SaveSystem.Save(i, st);
                Assert(SaveSystem.Exists(i), "存档槽 " + i + " 写入后不存在");
                var meta = SaveSystem.Meta(i);
                Assert(meta.used, "槽 " + i + " 的 Meta.used 为 false");
                Assert(meta.clues == 1 && meta.score == 42, "槽 " + i + " 摘要不对：" + meta.clues + "/" + meta.score);
                Assert(!string.IsNullOrEmpty(meta.time), "槽 " + i + " 没有时间戳");

                var back = SaveSystem.Load(i);
                Assert(back != null, "槽 " + i + " 读不回来");
                Assert(back.beatIndex == 7 && back.score == 42 && back.chapterId == "ch01", "槽 " + i + " 内容不一致");
                Assert(back.HasClue("cup") && back.Flag("trust") == 3, "槽 " + i + " 线索/标志丢失");
                Assert(back.log != null && back.log.Count == 1, "槽 " + i + " 对话回顾丢失");
                Assert(back.bg == "styles_hall", "槽 " + i + " 背景丢失");
            }
            Assert(SaveSystem.Load(99) == null, "不存在的槽应返回 null");

            SaveSystem.Delete(SaveSystem.SlotCount - 1);
            Assert(!SaveSystem.Exists(SaveSystem.SlotCount - 1), "删除存档失败");

            var p = Path.Combine(Application.persistentDataPath, "saves", "slot3.json");
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, "{ 这不是合法 JSON");
            int before = _errorLog.Count;
            Assert(SaveSystem.Load(3) == null, "损坏的存档应返回 null 而不是抛异常");
            Assert(!SaveSystem.Meta(3).used, "损坏存档的 Meta 应为空");
            ExpectErrors(_errorLog.Count - before);   // 这两条报错是本次故意制造的
            SaveSystem.Delete(3);
            yield return null;
        }

        IEnumerator CheckSettings()
        {
            var s = SettingsService.Current;
            Assert(s != null, "设置对象为 null");
            Assert(s.textSpeed > 0f && s.textSpeed < 1f, "文字速度默认值异常：" + s.textSpeed);
            Assert(s.bgmVolume >= 0f && s.bgmVolume <= 1f, "音量默认值异常");
            Assert(s.quality >= 0 && s.quality <= 3, "画质档位异常：" + s.quality);

            float before = s.textSpeed;
            s.textSpeed = 0.03f;
            SettingsService.Save();
            var file = Path.Combine(Application.persistentDataPath, "settings.json");
            Assert(File.Exists(file), "设置文件没有写出来");
            var reloaded = JsonConvert.DeserializeObject<GameSettings>(File.ReadAllText(file));
            Assert(reloaded != null && Math.Abs(reloaded.textSpeed - 0.03f) < 1e-4f, "设置写入后读回不一致");

            for (int q = 0; q <= 3; q++) { s.quality = q; SettingsService.Apply(); }
            s.quality = 2;
            s.textSpeed = before;
            SettingsService.Save();

            File.WriteAllText(file, "不是 JSON");
            var field = typeof(SettingsService).GetField("_current",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (field != null) field.SetValue(null, null);
            var fresh = SettingsService.Current;
            Assert(fresh != null && fresh.textSpeed > 0f, "损坏的设置文件没有回退到默认值");
            SettingsService.Save();
            yield return null;
        }

        IEnumerator CheckAudio()
        {
            var ad = AudioDirector.Instance;
            Assert(ad != null, "AudioDirector 不存在");
            ad.PlayBgm("__没有的曲子__");
            ad.PlaySfx("__没有的音效__");
            ad.PlayVoice("__没有的语音__");
            ad.ApplyVolumes();
            ad.StopBgm();
            yield return new WaitForSecondsRealtime(0.1f);

            var bgm = Resources.LoadAll<AudioClip>("Audio/BGM");
            var sfx = Resources.LoadAll<AudioClip>("Audio/SFX");
            Add("音频资源清点", true, "BGM " + bgm.Length + " 首 / SFX " + sfx.Length + " 个" +
                (bgm.Length + sfx.Length == 0 ? "（工程里没有任何音频文件，BGM/SFX 指令目前是空转）" : ""));
            yield return null;
        }

        // ---------------------------------------------------------------- 标题 / 开局

        IEnumerator CheckTitle()
        {
            var ui = GameUI.Instance;
            Assert(ui != null, "GameUI 不存在");
            var title = ui.transform.Find("Canvas/SafeArea/Title");
            Assert(title != null, "找不到标题层");
            Assert(title.gameObject.activeSelf, "标题层没有显示");

            foreach (var n in new[] { "M_开始案件调查", "M_继续上次调查", "M_人物与关系图", "M_设置", "M_退出游戏" })
            {
                var b = FindButton(n);
                Assert(b != null, "标题按钮缺失：" + n);
                var r = b.GetComponent<RectTransform>().rect;
                Assert(r.width > 100f && r.height > 40f, "标题按钮尺寸异常：" + n + " " + r.size);
            }
            var bg = title.Find("Art").GetComponent<Image>();
            Assert(bg != null && bg.sprite != null && bg.sprite != AssetService.Missing(), "标题底图没有加载");

            // 版面检查：标语不能被菜单压住，按钮不能跑出屏幕
            ForceLayout();
            var tagline = TextAt("Canvas/SafeArea/Title/Tagline");
            Assert(tagline != null, "标题画面找不到标语");
            var tagRect = WorldRect(tagline.rectTransform);
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            foreach (var n in new[] { "M_开始案件调查", "M_继续上次调查", "M_人物与关系图", "M_设置", "M_退出游戏" })
            {
                var r = WorldRect(FindButton(n).GetComponent<RectTransform>());
                Assert(screen.Overlaps(r) && r.xMin >= -1f && r.xMax <= Screen.width + 1f &&
                       r.yMin >= -1f && r.yMax <= Screen.height + 1f,
                    "标题按钮「" + n + "」跑出屏幕：" + r);
                Assert(!tagRect.Overlaps(r),
                    "标题按钮「" + n + "」与标语文字重叠（按钮 " + r + " / 标语 " + tagRect + "）");
            }
            yield return Shot("标题画面");
        }

        IEnumerator CheckNewGame()
        {
            yield return Click("M_开始案件调查");
            yield return WaitFrames(4);
            var card = GameUI.Instance.transform.Find("Canvas/SafeArea/OverlayLayer/ChapterCard");
            Assert(card != null, "章节卡节点不存在");
            Assert(card.gameObject.activeSelf, "开始游戏后章节卡没有出现");
            // 章节卡是淡入的（0.5s）。点完就开始截图只能拍到一张几乎全黑的图，
            // 报告里那张「章节卡」截图就永远看不出对错 —— 等它亮起来再拍。
            yield return WaitSeconds(0.8f);
            yield return Shot("章节卡");
            yield return WaitSeconds(3.2f);
            var dlg = GameUI.Instance.transform.Find("Canvas/SafeArea/DialogueLayer/Dialogue");
            Assert(dlg != null && dlg.gameObject.activeSelf, "章节卡之后对话框没有出现");
            yield return WaitSeconds(0.6f);
            yield return Shot("对话");
        }

        IEnumerator CheckDialogue()
        {
            var dir = GameDirector.Instance;
            Assert(dir != null, "GameDirector 没有创建");
            int start = dir.State.beatIndex;

            var es = EventSystem.current;
            Assert(es != null, "场景里没有 EventSystem");
            var ped = new PointerEventData(es) { position = new Vector2(Screen.width * 0.5f, Screen.height * 0.80f) };
            var hits = new List<RaycastResult>();
            es.RaycastAll(ped, hits);
            Assert(hits.Count > 0, "在对话框位置什么都点不到");
            Assert(hits[0].gameObject.name == "AdvanceLayer",
                "对话框位置最上层的可点击对象是「" + hits[0].gameObject.name + "」，点这里不会推进剧情");

            var adv = GameUI.Instance.transform.Find("Canvas/SafeArea/AdvanceLayer").GetComponent<Button>();
            for (int i = 0; i < 16 && dir.State.beatIndex < start + 6; i++)
            {
                adv.onClick.Invoke();
                yield return WaitSeconds(0.15f);
            }
            Assert(dir.State.beatIndex > start, "点了继续但剧情没有推进（beatIndex 一直是 " + start + "）");
            var body = FindText("Body");
            Assert(body != null && !string.IsNullOrEmpty(body.text), "对话框正文为空");

            // 姓名牌必须落在对话框里。曾经它被摆到对话框上方几十像素、飘在背景中间。
            var plate = GameUI.Instance.transform.Find("Canvas/SafeArea/DialogueLayer/Dialogue/NamePlate") as RectTransform;
            var box = GameUI.Instance.transform.Find("Canvas/SafeArea/DialogueLayer/Dialogue") as RectTransform;
            Assert(plate != null && box != null, "找不到姓名牌或对话框");
            Assert(plate.gameObject.activeSelf, "说话的姓名牌没有显示");
            var pc = plate.TransformPoint(plate.rect.center);
            var bc = box.TransformPoint(box.rect.center);
            Assert(Mathf.Abs(pc.x - bc.x) < box.rect.width && Mathf.Abs(pc.y - bc.y) < box.rect.height,
                "姓名牌跑到对话框外面了（牌中心 " + pc + " / 框中心 " + bc + "）");
            var nameTxt = plate.GetComponentInChildren<TMP_Text>(true);
            Assert(nameTxt != null && !string.IsNullOrEmpty(nameTxt.text), "姓名牌上没有名字");

            // 打字机中途点一次，应该先把这句显示完，而不是跳到下一句
            int beatBefore = dir.State.beatIndex;
            adv.onClick.Invoke();
            yield return null;
            adv.onClick.Invoke();
            yield return null;
            Assert(dir.State.beatIndex >= beatBefore, "连续点击把剧情推进顺序弄乱了");
            yield return null;
        }

        /// <summary>
        /// 「自动播放 / 快进」这两个开关。上一版它们只是把布尔值翻一下再弹条提示，
        /// 没有任何代码读它 —— 玩家按了完全没反应，而且全程都没有测试项覆盖。
        /// </summary>
        IEnumerator CheckAutoPlay()
        {
            var dir = GameDirector.Instance;
            Assert(dir != null, "GameDirector 不存在");
            Assert(FindButton("Tool_自动") != null, "对话框上没有「自动」按钮");
            Assert(FindButton("Tool_快进") != null, "对话框上没有「快进」按钮");

            yield return Click("Tool_自动");
            int before = dir.State.beatIndex;
            // 等到「真的自己翻了一页」为止（当前这句可能还在打字，所以给足余量）。
            float limit = Time.realtimeSinceStartup + Mathf.Clamp(SettingsService.Current.autoDelay, 0.4f, 8f) + 8f;
            while (dir.State.beatIndex <= before && Time.realtimeSinceStartup < limit) yield return null;
            Assert(dir.State.beatIndex > before,
                "开了自动播放，但一秒钟都不点它也不走（beatIndex 停在 " + before + "）");
            yield return Click("Tool_自动");                 // 关掉
            yield return WaitSeconds(0.4f);

            yield return Click("Tool_快进");
            before = dir.State.beatIndex;
            limit = Time.realtimeSinceStartup + 4f;
            while (dir.State.beatIndex <= before && Time.realtimeSinceStartup < limit) yield return null;
            Assert(dir.State.beatIndex > before, "开了快进，但剧情没有自己往前翻");
            yield return Click("Tool_快进");                 // 关掉
            yield return Shot("自动与快进");
            yield return null;
        }

        IEnumerator CheckHud()
        {
            var clues = FindText("Clues");
            Assert(clues != null, "HUD 上找不到笔记本计数");
            Assert(clues.text.Contains("笔记本"), "计数文本不对：" + clues.text);

            ForceLayout();
            var rt = clues.rectTransform;
            var btnRow = GameUI.Instance.transform.Find("Canvas/SafeArea/HudLayer/HudButtons") as RectTransform;
            Assert(btnRow != null, "找不到 HUD 按钮行");
            float clueRight = rt.TransformPoint(new Vector3(rt.rect.xMax, 0f, 0f)).x;
            float rowLeft = btnRow.TransformPoint(new Vector3(btnRow.rect.xMin, 0f, 0f)).x;
            Assert(clueRight <= rowLeft + 1f,
                "笔记本计数（右边界 " + clueRight.ToString("0") + "px）被 HUD 按钮行（左边界 " + rowLeft.ToString("0") + "px）盖住了");

            foreach (var n in new[] { "Hud_笔记本", "Hud_人物", "Hud_回顾", "Hud_菜单" })
                Assert(FindButton(n) != null, "HUD 按钮缺失：" + n);

            // 按路径取，不能按名字取 —— 弹层标题、标题画面都叫 "Title"
            var tag = TextAt("Canvas/SafeArea/HudLayer/Hud/Tag");
            var title = TextAt("Canvas/SafeArea/HudLayer/Hud/Title");
            Assert(tag != null && !string.IsNullOrEmpty(tag.text), "HUD 章节标签为空");
            Assert(title != null && !string.IsNullOrEmpty(title.text), "HUD 章节标题为空");
            yield return null;
        }

        // ---------------------------------------------------------------- 各种弹层

        static Transform OverlayPanelRoot()
        {
            return GameUI.Instance.transform.Find("Canvas/SafeArea/OverlayLayer/Overlay");
        }

        IEnumerator OpenOverlay(string hudButton)
        {
            yield return Click(hudButton);
            yield return WaitFrames(3);
            var panel = OverlayPanelRoot();
            Assert(panel != null, hudButton + " 没有生成面板");
            Assert(panel.gameObject.activeSelf, hudButton + " 面板没有打开");
            var parent = panel.parent;
            Assert(parent != null && parent == GameUI.Instance.transform.Find("Canvas/SafeArea/OverlayLayer"),
                "弹层挂错了父节点");
            Assert(parent.GetSiblingIndex() == parent.parent.childCount - 1,
                "弹层所在的整层不是最后一个子节点 —— 会被标题等界面盖住（点上去没反应）");
        }

        IEnumerator CloseOverlay()
        {
            var close = GameUI.Instance.transform.Find(
                "Canvas/SafeArea/OverlayLayer/Overlay/Card/TitleBar/Close").GetComponent<Button>();
            Assert(close != null, "弹层没有关闭按钮");
            close.onClick.Invoke();
            yield return WaitFrames(2);
        }

        IEnumerator CheckNotebook()
        {
            // 先塞两条证据进去，否则笔记本只会显示空态，测不到列表与详情
            var st = GameDirector.Instance.State;
            st.AddClue("cup");
            st.AddClue("wax");
            yield return OpenOverlay("Hud_笔记本");
            yield return Shot("笔记本");
            foreach (var tab in new[] { "Tab_全部", "Tab_物证", "Tab_证词", "Tab_推论", "Tab_全部" })
            {
                var b = FindButton(tab);
                Assert(b != null, "笔记本筛选按钮缺失：" + tab);
                b.onClick.Invoke();
                yield return WaitFrames(2);
            }
            var firstClue = ButtonsUnder("Overlay/Card/Body/List").FirstOrDefault();
            Assert(firstClue != null, "笔记本列表里一条证据都没有（前面已经拿过线索了）");
            firstClue.onClick.Invoke();
            yield return WaitFrames(2);
            var detail = FindText("DetailText");
            Assert(detail != null && detail.text.Length > 10, "证据详情没有渲染出来");
            yield return Shot("笔记本-证据详情");
            yield return CloseOverlay();
        }

        IEnumerator CheckCodex()
        {
            yield return OpenOverlay("Hud_人物");
            yield return Shot("人物档案");
            var db = LoadDb();
            int faces = GameUI.Instance.GetComponentsInChildren<Image>(true)
                .Count(i => i.gameObject.name == "Face" && i.sprite != null);
            Assert(faces >= db.Characters.Count, "人物档案里只有 " + faces + " 张头像，期望至少 " + db.Characters.Count);
            int nodes = GameUI.Instance.GetComponentsInChildren<Image>(true)
                .Count(i => i.gameObject.name.StartsWith("Node_"));
            Assert(nodes >= 10, "关系图节点只有 " + nodes + " 个");

            ForceLayout();
            var map = OverlayPanelRoot().Find("Card/Body/RelMap") as RectTransform;
            Assert(map != null, "找不到关系图面板");
            var mapRect = WorldRect(map);
            foreach (var img in GameUI.Instance.GetComponentsInChildren<Image>(true)
                         .Where(i => i.gameObject.name.StartsWith("Node_")))
            {
                var r = WorldRect(img.rectTransform);
                Assert(r.xMin >= mapRect.xMin - 1f && r.xMax <= mapRect.xMax + 1f &&
                       r.yMin >= mapRect.yMin - 1f && r.yMax <= mapRect.yMax + 1f,
                    "关系图节点「" + img.gameObject.name + "」被面板裁切（节点 " + r + " / 面板 " + mapRect + "）");
            }
            yield return CloseOverlay();
        }

        /// <summary>
        /// 关系图排版：头像必须画在自己的方框里，名字底衬不能互相压住、也不能压到别人的头像。
        /// 这几个都是「看一眼就知道丑、但没人量化过」的问题，所以直接量像素。
        /// </summary>
        IEnumerator CheckRelationMap()
        {
            GameUI.Instance.ResetForCheck();
            yield return WaitFrames(2);
            var title = GameUI.Instance.transform.Find("Canvas/SafeArea/Title");
            if (title != null && !title.gameObject.activeSelf)
            {
                yield return Click("Hud_菜单");
                yield return WaitFrames(3);
                yield return Click("Btn_返回标题");
                yield return WaitFrames(4);
            }
            yield return Click("M_人物与关系图");
            yield return WaitFrames(4);
            var mapT = GameUI.Instance.transform.Find("Canvas/SafeArea/OverlayLayer/Overlay/Card/Body/RelMap");
            Assert(mapT != null, "人物档案里找不到关系图面板");
            ForceLayout();
            var map = mapT as RectTransform;
            var mapRect = WorldRect(map);

            var ids = new List<string>();
            var boxes = new List<Rect>();
            foreach (var img in map.GetComponentsInChildren<Image>(true))
            {
                if (!img.gameObject.name.StartsWith("Node_")) continue;
                var box = WorldRect(img.rectTransform);
                ids.Add(img.gameObject.name.Substring("Node_".Length));
                boxes.Add(box);
                Assert(box.xMin >= mapRect.xMin - 1f && box.xMax <= mapRect.xMax + 1f &&
                       box.yMin >= mapRect.yMin - 1f && box.yMax <= mapRect.yMax + 1f,
                    "关系图节点 " + img.gameObject.name + " 跑出面板：" + box.ToString("0") + " / " + mapRect.ToString("0"));
                var face = img.transform.Find("Face") as RectTransform;
                Assert(face != null, img.gameObject.name + " 没有头像层");
                var fr = WorldRect(face);
                Assert(fr.xMin >= box.xMin - 1f && fr.xMax <= box.xMax + 1f && fr.yMin >= box.yMin - 1f && fr.yMax <= box.yMax + 1f,
                    "关系图里 " + img.gameObject.name + " 的头像画到方框外面了：" + fr.ToString("0") + " / 方框 " + box.ToString("0"));
            }
            Assert(boxes.Count >= 10, "关系图节点只有 " + boxes.Count + " 个");

            var chips = new List<Rect>();
            for (int i = 0; i < ids.Count; i++)
            {
                var chip = map.Find("NameChip_" + ids[i]) as RectTransform;
                Assert(chip != null, "关系图里 " + ids[i] + " 没有名字底衬");
                chips.Add(WorldRect(chip));
            }
            for (int a = 0; a < chips.Count; a++)
            {
                for (int b = a + 1; b < chips.Count; b++)
                    Assert(OverlapArea(chips[a], chips[b]) <= 1f,
                        "关系图里两个名字叠在一起：" + chips[a].ToString("0") + " / " + chips[b].ToString("0"));
                for (int b = 0; b < boxes.Count; b++)
                {
                    if (b == a) continue;   // 自己的方框本来就在名字正上方
                    Assert(OverlapArea(chips[a], boxes[b]) <= 1f,
                        "关系图里有名字压在别人的头像上：" + chips[a].ToString("0") + " / 头像 " + boxes[b].ToString("0"));
                }
            }
            yield return Shot("人物档案-关系图");
            GameUI.Instance.ResetForCheck();
            yield return WaitFrames(2);
        }

        IEnumerator CheckBacklog()
        {
            yield return OpenOverlay("Hud_回顾");
            yield return Shot("对话回顾");
            int lines = GameUI.Instance.GetComponentsInChildren<TMP_Text>(true)
                .Count(t => t.gameObject.name.StartsWith("Log"));
            Assert(lines > 0, "对话回顾里一条记录都没有（前面已经念过台词了）");
            yield return CloseOverlay();
        }

        IEnumerator CheckMenu()
        {
            yield return OpenOverlay("Hud_菜单");
            foreach (var n in new[] { "Btn_继续游戏", "Btn_保存进度", "Btn_读取存档", "Btn_设置",
                                       "Btn_证据笔记本", "Btn_人物档案", "Btn_返回标题" })
            {
                var b = FindButton(n);
                Assert(b != null, "菜单缺少按钮：" + n);
                // 按钮必须留在卡片内 —— 7 颗按钮排一行会超出卡片右边缘，最后两颗被切掉
                ForceLayout();
                var card = WorldRect(OverlayPanelRoot().Find("Card") as RectTransform);
                var r = WorldRect(b.GetComponent<RectTransform>());
                Assert(r.xMin >= card.xMin - 1f && r.xMax <= card.xMax + 1f,
                    "菜单按钮「" + n + "」超出卡片范围（按钮 " + r + " / 卡片 " + card + "）");
            }
            yield return Shot("菜单");

            yield return Click("Btn_保存进度");
            yield return WaitFrames(3);
            var slots = ButtonsUnder("Overlay/Card/Body/Host").OrderBy(b => b.gameObject.name).ToList();
            Assert(slots.Count == SaveSystem.SlotCount, "存档槽数量是 " + slots.Count + "，期望 " + SaveSystem.SlotCount);
            yield return Shot("保存进度");
            slots[1].onClick.Invoke();
            yield return WaitFrames(3);
            Assert(SaveSystem.Exists(1), "点了存档槽但没有写盘");
            var meta = SaveSystem.Meta(1);
            Assert(meta.used && meta.beats > 0, "存档摘要异常");
            yield return CloseOverlay();

            yield return Click("Hud_菜单");
            yield return WaitFrames(2);
            yield return Click("Btn_设置");
            yield return WaitFrames(3);
            var sliders = GameUI.Instance.GetComponentsInChildren<Slider>(true);
            Assert(sliders.Length >= 4, "设置里滑条只有 " + sliders.Length + " 条，期望 >= 4");
            foreach (var sl in sliders)
            {
                sl.value = Mathf.Lerp(sl.minValue, sl.maxValue, 0.6f);
                yield return null;
            }
            yield return Shot("设置");
            foreach (var q in new[] { "Q低", "Q中", "Q高", "Q极高" })
            {
                var b = FindButton(q);
                Assert(b != null, "画质档位缺失：" + q);
                b.onClick.Invoke();
                yield return WaitFrames(2);
            }
            yield return CloseOverlay();

            yield return Click("Hud_菜单");
            yield return WaitFrames(2);
            yield return Click("Btn_读取存档");
            yield return WaitFrames(3);
            var loadSlots = ButtonsUnder("Overlay/Card/Body/Host").OrderBy(b => b.gameObject.name).ToList();
            Assert(loadSlots.Count == SaveSystem.SlotCount, "读档槽数量异常");
            loadSlots[1].onClick.Invoke();
            yield return WaitFrames(8);
            var panel = OverlayPanelRoot();
            Assert(panel == null || !panel.gameObject.activeSelf, "读档后面板没有关闭");
            Assert(GameDirector.Instance != null, "读档后解释器不见了");
            var dlg = GameUI.Instance.transform.Find("Canvas/SafeArea/DialogueLayer/Dialogue");
            Assert(dlg != null && dlg.gameObject.activeSelf, "读档后对话框没有恢复");
            yield return Shot("读档之后");
        }

        // ---------------------------------------------------------------- 玩法

        IEnumerator CheckChoice()
        {
            var beat = new Beat
            {
                t = "choice",
                prompt = "体检：选一个",
                options = new List<Option>
                {
                    new Option { text = "甲", give = "cup", score = 3 },
                    new Option { text = "乙（带提示）", hint = "这是第二项" }
                }
            };
            GameUI.Instance.ShowChoice(beat, o => { });
            yield return WaitFrames(3);
            var opts = ButtonsUnder("ChoiceLayer").ToList();
            Assert(opts.Count == 2, "选项按钮数量是 " + opts.Count + "，期望 2");
            Assert(FindButton("Opt1") != null, "带提示的选项没有生成");
            yield return Shot("选项");
            GameUI.Instance.ShowSay(new Beat { t = "say", who = "narr", text = "选项测试结束" }, () => { });
            yield return WaitFrames(2);
            yield return null;
        }

        IEnumerator CheckDeduce()
        {
            var dir = GameDirector.Instance;
            int mistakes0 = dir.State.mistakes;
            int score0 = dir.State.score;
            int resolved = -1;
            var beat = new Beat
            {
                t = "deduce",
                tag = "体检",
                question = "体检：哪一个是对的？",
                answers = new List<string> { "错的这个", "对的那个" },
                answer = 1,
                wrong = new List<string> { "体检：答错了" },
                explain = "体检：解释文本"
            };
            GameUI.Instance.ShowDeduce(beat, i => resolved = i);
            yield return WaitFrames(3);

            var wrong = FindButton("A0");
            var right = FindButton("A1");
            Assert(wrong != null && right != null, "推理选项按钮没有生成");
            yield return Shot("推理题");
            wrong.onClick.Invoke();
            yield return WaitFrames(2);
            Assert(!wrong.interactable, "答错的选项没有变灰");
            Assert(dir.State.mistakes == mistakes0 + 1, "答错没有计失误（" + mistakes0 + " → " + dir.State.mistakes + "）");
            Assert(dir.State.score == Mathf.Max(0, score0 - 3), "答错没有扣分（" + score0 + " → " + dir.State.score + "）");

            right.onClick.Invoke();
            yield return WaitFrames(2);
            var next = FindButton("Next");
            Assert(next != null && next.gameObject.activeSelf, "答对后没有出现「继续」");
            next.onClick.Invoke();
            yield return WaitFrames(3);
            Assert(resolved == 1, "解释器收到的是 " + resolved + "，期望 1");
            yield return null;
        }

        IEnumerator CheckInvestigate()
        {
            var db = LoadDb();
            var sc = db.Investigations.Values.First();
            int done = 0;
            GameUI.Instance.ShowInvestigate(sc, () => done++);
            yield return WaitFrames(4);
            yield return Shot("现场调查");

            var exit = FindButton("Exit");
            Assert(exit != null, "调查里没有「结束搜寻」按钮");
            // 热点不能叠在一起（叠住的那个点等于点不到），也不能跑出屏幕
            ForceLayout();
            var rects = new List<Rect>();
            for (int i = 0; i < sc.spots.Count; i++)
            {
                var rt = FindButton("Hot" + i).GetComponent<RectTransform>();
                var r = WorldRect(rt);
                Assert(r.xMin >= -1f && r.xMax <= Screen.width + 1f && r.yMin >= -1f && r.yMax <= Screen.height + 1f,
                    "调查热点 Hot" + i + " 跑出屏幕：" + r);
                rects.Add(r);
            }
            // 热点的间距是按「参考分辨率」放宽的（108px @1920×1080），跟屏幕像素宽度不成正比：
            // 在 20:9 的 2400×1080 下同样的版面会量出 121px，用「屏幕宽度的 5.5%」当门槛就会误报。
            // 所以换算回参考分辨率再比，并且直接检查两个热点的方框有没有真的压在一起。
            float uiScale = 1f;
            var canvas = GameUI.Instance.GetComponentInChildren<Canvas>();
            if (canvas != null && canvas.scaleFactor > 0f) uiScale = canvas.scaleFactor;
            for (int a = 0; a < rects.Count; a++)
                for (int b = a + 1; b < rects.Count; b++)
                {
                    Assert(!rects[a].Overlaps(rects[b]),
                        "调查热点 Hot" + a + " 与 Hot" + b + " 的方框叠在一起：" +
                        rects[a].ToString("0") + " / " + rects[b].ToString("0"));
                    float gap = Vector2.Distance(rects[a].center, rects[b].center) / uiScale;
                    Assert(gap >= 100f,
                        "调查热点 Hot" + a + " 与 Hot" + b + " 挨得太近（参考分辨率下 " +
                        gap.ToString("0") + "px，期望 >= 100）");
                }

            for (int i = 0; i < sc.spots.Count; i++)
            {
                var b = FindButton("Hot" + i);
                Assert(b != null, "调查热点缺失：Hot" + i);
                b.onClick.Invoke();
                yield return WaitFrames(2);
            }
            Assert(exit.gameObject.activeSelf, "找齐热点后「结束搜寻」仍然不出现");
            yield return Shot("现场调查-找齐");
            exit.onClick.Invoke();
            yield return WaitFrames(3);
            Assert(done == 1, "调查结束回调没有触发");
            yield return null;
        }

        IEnumerator CheckAsk()
        {
            var db = LoadDb();
            // 先把全部证据塞满，确保所有话题都解锁 —— 这样测的是「询问面板本身」，
            // 而不是「玩家手上有哪些线索」
            var st = GameDirector.Instance.State;
            foreach (var k in db.Evidence.Keys.ToList()) st.AddClue(k);

            var chars = db.Interrogations.Values.Take(2).ToList();
            int need = 2;
            int done = 0;
            GameUI.Instance.ShowAsk(chars, need, "体检：询问", () => done++);
            yield return WaitFrames(4);
            yield return Shot("询问");

            foreach (var c in chars) Assert(FindButton("P_" + c.id) != null, "询问里缺少人物：" + c.id);

            // 每次点完话题会切到台词页，必须点「换一个问题」回到列表才能继续
            int asked = 0;
            bool shotLines = false;
            int guard = 24;
            while (asked < need && guard-- > 0)
            {
                Button topic = null;
                foreach (var c in chars)
                {
                    var which = c;
                    // 优先挑没有前置条件的话题，避免撞上「证据不足」的提示
                    foreach (var t in which.topics)
                    {
                        if (!string.IsNullOrEmpty(t.need) && !GameDirector.Instance.State.HasClue(t.need)) continue;
                        if (GameDirector.Instance.State.topics.ContainsKey(which.id + "_" + t.id) &&
                            GameDirector.Instance.State.topics[which.id + "_" + t.id]) continue;
                        var cand = FindButton("T_" + t.id);
                        if (cand != null) { topic = cand; break; }
                    }
                    if (topic != null) break;
                }
                if (topic == null) break;
                topic.onClick.Invoke();
                yield return WaitFrames(3);
                if (!shotLines) { yield return Shot("询问-台词"); shotLines = true; }
                var b2 = FindButton("Back");
                if (b2 != null) { b2.onClick.Invoke(); yield return WaitFrames(3); }
                asked = 0;
                foreach (var c in chars)
                    foreach (var t in c.topics)
                        if (GameDirector.Instance.State.topics.ContainsKey(c.id + "_" + t.id) &&
                            GameDirector.Instance.State.topics[c.id + "_" + t.id]) asked++;
            }
            Assert(asked >= need, "只问到了 " + asked + " 个话题（期望 >= " + need + "）");
            var finish = FindButton("Finish");
            Assert(finish != null, "询问里没有「结束询问」按钮");
            Assert(finish.gameObject.activeSelf, "问够 " + need + " 个之后「结束询问」仍然不出现");
            // 按路径取，不用全局名字搜索：调查面板里的计数也叫 Counter，
            // 只要它还在场景里（比如上一项中途失败留下的）就会被抓错。
            var counter = TextAt("Canvas/SafeArea/OverlayLayer/Ask/Card/Footer/Counter");
            Assert(counter != null && counter.text.Contains("已问"), "询问计数没有刷新：" + (counter == null ? "无" : counter.text));
            finish.onClick.Invoke();
            yield return WaitFrames(3);
            Assert(done == 1, "询问结束回调没有触发");
            yield return null;
        }

        IEnumerator CheckNote()
        {
            // 用剧本里**真实**的知识卡，不是随口造一张：这样截图就是玩家会看到的那一屏
            var db = LoadDb();
            Beat real = null;
            foreach (var ch in db.Chapters)
            {
                foreach (var b in ch.beats) if (b.t == "note") { real = b; break; }
                if (real != null) break;
            }
            Assert(real != null, "剧本里一张知识卡都没有");
            int done = 0;
            GameUI.Instance.ShowNote(real, () => done++);
            yield return WaitFrames(3);
            yield return Shot("知识卡");
            var body = TextAt("Canvas/SafeArea/OverlayLayer/Overlay/Card/Body/Note");
            Assert(body != null && body.text.Length > 20, "知识卡正文没有渲染出来（" +
                (body == null ? "找不到文字层" : "只有 " + body.text.Length + " 个字") + "）");
            yield return Click("Btn_明白了");
            yield return WaitFrames(2);
            Assert(done == 1, "知识卡关闭回调没有触发");
            yield return null;
        }

        IEnumerator CheckAccusation()
        {
            var db = LoadDb();
            var qs = db.Accusation;
            List<AccuseRecord> got = null;
            GameUI.Instance.ShowAccusation(qs, r => got = r);
            // 留一份给「结算报告」用：体检是直接调界面、绕过了 GameDirector，
            // 所以解释器状态里的指认记录还是空的，结算屏得借这份真数据。
            _lastAccusation = () => got;
            yield return WaitFrames(4);
            yield return Shot("最终指认");

            for (int qi = 0; qi < qs.Count; qi++)
            {
                var opt0 = FindButton("O0");
                Assert(opt0 != null, "第 " + (qi + 1) + " 题的选项没有生成");
                opt0.onClick.Invoke();
                yield return WaitFrames(2);
                for (int k = 0; k < qs[qi].options.Count; k++)
                {
                    var o = FindButton("O" + k);
                    if (o != null) Assert(!o.interactable, "选定后其它选项仍可点（第 " + (qi + 1) + " 题）");
                }
                var nx = FindButton("Next");
                Assert(nx != null && nx.gameObject.activeSelf, "第 " + (qi + 1) + " 题没有「继续」");
                nx.onClick.Invoke();
                yield return WaitFrames(3);
            }
            Assert(got != null, "指认结束回调没有触发");
            Assert(got.Count == qs.Count, "指认记录 " + got.Count + " 条，期望 " + qs.Count);
            Assert(got.All(r => !string.IsNullOrEmpty(r.truth) && !string.IsNullOrEmpty(r.why)), "指认记录缺少事实/解释");
            yield return null;
        }

        IEnumerator CheckEnding()
        {
            var db = LoadDb();
            var st = new GameState { score = 12, mistakes = 1, playSeconds = 600f };
            for (int i = 0; i < db.Accusation.Count; i++)
                st.accuse.Add(new AccuseRecord { question = "Q" + i, pick = "A", truth = "A", ok = true, why = "理由" });

            var letters = new List<string>();
            foreach (var wrongCount in new[] { 0, 1, 2, db.Accusation.Count })
            {
                var s2 = new GameState();
                for (int i = 0; i < db.Accusation.Count; i++)
                    s2.accuse.Add(new AccuseRecord { ok = i < db.Accusation.Count - wrongCount });
                letters.Add(GameState.GradeLetter(s2.Grade(db.Accusation.Count)));
            }
            Assert(letters[0] == "S", "全对应该评 S，实际 " + letters[0]);
            Assert(letters[3] == "D", "首题答错应该评 D，实际 " + letters[3]);

            // 用玩家真实的那份状态出结算屏。上一版这里喂的是构造出来的假状态，
            // 于是截图上的「证据 0/36」跟实际游玩根本对不上，等于没验证过结算数字。
            var live = GameDirector.Instance != null ? GameDirector.Instance.State : null;
            if (live == null) live = st;
            if (live.accuse.Count == 0 && _lastAccusation != null && _lastAccusation() != null)
                live.accuse = _lastAccusation();
            Assert(live.clues.Count > 0, "结算屏用的状态里一条证据都没有，截图上的数字没有意义");
            GameUI.Instance.ShowEnding(live, db);
            yield return WaitFrames(4);
            yield return Shot("结算报告");
            foreach (var n in new[] { "Btn_重新调查", "Btn_人物档案", "Btn_返回标题" })
                Assert(FindButton(n) != null, "结算报告缺少按钮：" + n);

            yield return Click("Btn_人物档案");
            yield return WaitFrames(3);
            var panel = OverlayPanelRoot();
            Assert(panel != null && panel.gameObject.activeSelf, "结算里点人物档案没有打开");
            yield return CloseOverlay();
            Assert(FindButton("Hud_菜单") != null, "关掉面板后 HUD 消失，玩家会卡在空画面");
            yield return null;
        }

        IEnumerator CheckFx()
        {
            foreach (var fx in new[] { "flash", "shake", "fade", "", null, "__未知特效__" })
            {
                GameUI.Instance.PlayFx(fx);
                yield return WaitFrames(2);
            }
            yield return WaitSeconds(1.6f);
            var bgLayer = GameUI.Instance.transform.Find("Canvas/SafeArea/Backgrounds") as RectTransform;
            Assert(bgLayer != null, "找不到背景层");
            Assert(bgLayer.anchoredPosition.magnitude < 0.5f,
                "震动结束后背景没有归位（偏移 " + bgLayer.anchoredPosition + "）");
            yield return null;
        }

        IEnumerator CheckRestartPath()
        {
            yield return OpenOverlay("Hud_菜单");
            yield return Click("Btn_返回标题");
            yield return WaitFrames(4);
            var title = GameUI.Instance.transform.Find("Canvas/SafeArea/Title");
            Assert(title.gameObject.activeSelf, "返回标题后标题画面没有出现");

            yield return Click("M_设置");
            yield return WaitFrames(3);
            yield return Click("Btn_读取存档");
            yield return WaitFrames(3);
            var loadSlots = ButtonsUnder("Overlay/Card/Body/Host").OrderBy(b => b.gameObject.name).ToList();
            Assert(loadSlots.Count == SaveSystem.SlotCount, "标题里读档槽数量异常");
            loadSlots[1].onClick.Invoke();
            yield return WaitFrames(8);
            Assert(GameDirector.Instance != null, "从标题读档后解释器不存在");
            Assert(!title.gameObject.activeSelf, "从标题读档后标题画面没有收起");
            Assert(GameUI.Instance.transform.Find("Canvas/SafeArea/DialogueLayer/Dialogue").gameObject.activeSelf,
                "从标题读档后对话框没有出现");
            yield return Shot("标题读档之后");

            var dir = GameDirector.Instance;
            int before = dir.State.beatIndex;
            var adv = GameUI.Instance.transform.Find("Canvas/SafeArea/AdvanceLayer").GetComponent<Button>();
            for (int i = 0; i < 24 && dir.State.beatIndex == before; i++)
            {
                adv.onClick.Invoke();
                yield return WaitSeconds(0.15f);
            }
            Assert(dir.State.beatIndex != before, "读档后点继续没有推进剧情");

            yield return Click("Hud_菜单");
            yield return WaitFrames(2);
            yield return Click("Btn_返回标题");
            yield return WaitFrames(3);
            yield return Click("M_开始案件调查");
            yield return WaitFrames(4);
            Assert(GameDirector.Instance.State.beatIndex < 3,
                "开新游戏后进度不是从头开始：" + GameDirector.Instance.State.beatIndex);
            yield return WaitSeconds(3.2f);
            int beforeNew = GameDirector.Instance.State.beatIndex;
            for (int i = 0; i < 24 && GameDirector.Instance.State.beatIndex == beforeNew; i++)
            {
                GameUI.Instance.transform.Find("Canvas/SafeArea/AdvanceLayer").GetComponent<Button>().onClick.Invoke();
                yield return WaitSeconds(0.15f);
            }
            Assert(GameDirector.Instance.State.beatIndex != beforeNew,
                "回到标题再开新游戏后剧情不动了（Running 标志没有重置）");
            yield return null;
        }

        // ================================================================
        // 真通关：用真实界面从标题一路玩到结局
        //
        // 前面每一项都是在「干净界面」上单独测一个系统，看不出「上一步留下的
        // 状态把后面搞坏」这类冲突（自动存档的进度、调查拿到的证据、指认的记录
        // 会不会中途互相打架）。这一项把整局连着玩一遍：点对话、选选项、答推理、
        // 找热点、问口供、指认凶手，一直打到结算屏；再把界面里真实发生的次数和
        // 「自动通关自检」的期望值对照 —— 对不上就说明真界面上有一环走不通。
        // ================================================================
        RectTransform _solveRoot;                   // 当前正在解的谜题根节点（换根就重置进度）
        RectTransform _askRoot;
        readonly HashSet<string> _askTried = new HashSet<string>();

        static bool Active(Transform t) { return t != null && t.gameObject.activeSelf; }
        static Transform Layer(string name)
        {
            return GameUI.Instance == null ? null
                : GameUI.Instance.transform.Find("Canvas/SafeArea/OverlayLayer/" + name);
        }

        /// <summary>当前屏幕上摆着什么（结算屏单独先判，不在这里）。</summary>
        string ScreenKind()
        {
            if (Active(Layer("ChapterCard"))) return "chapter";
            if (Active(Layer("Deduce"))) return "deduce";
            if (Active(Layer("Investigate"))) return "investigate";
            if (Active(Layer("Ask"))) return "ask";
            if (Active(Layer("Accuse"))) return "accuse";
            if (Active(GameUI.Instance.transform.Find("Canvas/SafeArea/ChoiceLayer"))) return "choice";
            if (Active(OverlayPanelRoot())) return FindButton("Btn_明白了") != null ? "note" : "overlay";
            if (Active(GameUI.Instance.transform.Find("Canvas/SafeArea/DialogueLayer/Dialogue"))) return "say";
            return "wait";
        }

        string StuckReport(GameDirector dir)
        {
            if (dir == null) return "解释器不存在";
            return "界面=" + ScreenKind() + "，章节=" + dir.State.chapterId +
                   "，第 " + dir.State.beatIndex + " 条，当前指令=" + (dir.Current == null ? "无" : dir.Current.t) +
                   "，Running=" + dir.Running + "，对话框=" +
                   Active(GameUI.Instance.transform.Find("Canvas/SafeArea/DialogueLayer/Dialogue"));
        }

        IEnumerator CheckFullPlaythrough()
        {
            var db = LoadDb();
            GameUI.Instance.ResetForCheck();
            yield return WaitFrames(2);

            var title = GameUI.Instance.transform.Find("Canvas/SafeArea/Title");
            if (title != null && !title.gameObject.activeSelf)
            {
                yield return Click("Hud_菜单");
                yield return WaitFrames(3);
                yield return Click("Btn_返回标题");
                yield return WaitFrames(4);
            }
            Assert(title != null && title.gameObject.activeSelf, "没能回到标题画面");
            yield return Click("M_开始案件调查");
            yield return WaitFrames(4);

            int chapters = 0, says = 0, deduces = 0, invests = 0, asks = 0, accuses = 0, notes = 0;
            int guard = 20000, idle = 0, lastBeat = -1, countedBeat = -1;
            bool ended = false;
            float deadline = Time.realtimeSinceStartup + 300f;

            while (guard-- > 0 && Time.realtimeSinceStartup < deadline)
            {
                var dir = GameDirector.Instance;
                if (dir == null) { Assert(false, "通关途中解释器不见了"); yield break; }
                if (FindButton("Btn_重新调查") != null) { ended = true; break; }

                string kind = ScreenKind();
                if (dir.State.beatIndex != lastBeat) { lastBeat = dir.State.beatIndex; idle = 0; }
                else if (++idle > 1500)
                {
                    Assert(false, "剧情卡住了：" + StuckReport(dir));
                    yield break;
                }
                if (dir.State.beatIndex != countedBeat)
                {
                    countedBeat = dir.State.beatIndex;
                    switch (kind)
                    {
                        case "chapter": chapters++; break;
                        case "say": says++; break;
                        case "deduce": deduces++; break;
                        case "investigate": invests++; break;
                        case "ask": asks++; break;
                        case "accuse": accuses++; break;
                        case "note": notes++; break;
                    }
                }

                switch (kind)
                {
                    case "chapter": yield return null; break;                 // 章节卡自己会走完
                    case "note": yield return Click("Btn_明白了"); break;
                    case "overlay":                                          // 不该出现的弹层：走关闭按钮
                    {
                        var close = GameUI.Instance.transform.Find(
                            "Canvas/SafeArea/OverlayLayer/Overlay/Card/TitleBar/Close");
                        if (close != null) { close.GetComponent<Button>().onClick.Invoke(); yield return WaitFrames(2); }
                        else yield return null;
                        break;
                    }
                    case "deduce": yield return SolveDeduce(dir); break;
                    case "investigate": yield return SolveInvestigate(); break;
                    case "ask": yield return SolveAsk(); break;
                    case "accuse": yield return SolveAccuse(db); break;
                    case "choice": yield return ClickFirstChoice(); break;
                    case "say": yield return AdvanceDialogue(); break;
                    default: yield return null; break;
                }
            }

            Assert(ended, "一整局没有走到结局，停在：" + StuckReport(GameDirector.Instance));

            var st = GameDirector.Instance.State;
            _notes.Add("【真通关】章节卡 " + chapters + " · 对白 " + says + " · 推理 " + deduces + " · 调查 " + invests +
                       " · 询问 " + asks + " · 指认 " + accuses + " · 知识卡 " + notes);
            _notes.Add("【真通关】证据 " + st.clues.Count + "/" + db.Evidence.Count + " · 失误 " + st.mistakes +
                       " · 得分 " + st.score + " · 指认 " + st.accuse.Count + " 条、全对 " +
                       (st.accuse.TrueForAll(a => a.ok) ? "是" : "否"));
            yield return Shot("真通关-结算");

            Assert(chapters == 9, "真界面上章节卡出现 " + chapters + " 次，自检期望 9 次");
            Assert(deduces == 8, "真界面上推理题做了 " + deduces + " 题，自检期望 8 题");
            Assert(invests == 5, "真界面上现场调查做了 " + invests + " 场，自检期望 5 场");
            Assert(asks == 3, "真界面上询问做了 " + asks + " 场，自检期望 3 场");
            // 指认是「一个界面里连着问 6 题」，所以界面只出现 1 次、记录要 6 条
            Assert(accuses == 1, "最终指认界面出现了 " + accuses + " 次，期望 1 次");
            Assert(st.accuse.Count == db.Accusation.Count && st.accuse.TrueForAll(a => a.ok),
                "真通关的指认记录不对：" + st.accuse.Count + " 条（期望 " + db.Accusation.Count + " 条且全对）");
            if (st.clues.Count < 35)
                _notes.Add("【注意】真通关拿到 " + st.clues.Count + " 条证据，而自动通关自检能拿 35 条 —— " +
                           "自检的假界面会把所有询问话题都解锁并直接发证据，真界面只能问已解锁的话题，差异值得核对。");
            yield return null;
        }

        IEnumerator SolveDeduce(GameDirector dir)
        {
            var b = dir.Current;
            Assert(b != null && b.t == "deduce", "推理界面出现了，但解释器不在推理指令上（" +
                (b == null ? "null" : b.t) + "）");
            Assert(b.answers != null && b.answer >= 0 && b.answer < b.answers.Count,
                "推理题答案越界：" + b.question);
            yield return Click("A" + b.answer);
            yield return WaitFrames(2);
            var next = FindButton("Next");
            Assert(next != null && next.gameObject.activeSelf, "推理答对之后没有出现「继续」");
            yield return Click("Next");
            yield return WaitFrames(2);
        }

        IEnumerator SolveInvestigate()
        {
            var root = Layer("Investigate") as RectTransform;
            if (_solveRoot != root) { _solveRoot = root; _askTried.Clear(); }
            var hots = root.GetComponentsInChildren<Button>(true)
                .Where(b => b.gameObject.name.StartsWith("Hot")).OrderBy(b => b.gameObject.name).ToList();
            var unclicked = hots.FirstOrDefault(b => !_askTried.Contains("hot:" + b.gameObject.name));
            if (unclicked != null)
            {
                _askTried.Add("hot:" + unclicked.gameObject.name);
                unclicked.onClick.Invoke();
                yield return WaitFrames(2);
                yield break;
            }
            var exit = FindButton("Exit");
            if (exit != null && exit.gameObject.activeSelf) { yield return Click("Exit"); yield return WaitFrames(3); }
            else
            {
                var counter = TextAt("Canvas/SafeArea/OverlayLayer/Investigate/Counter");
                Assert(false, "热点全部点完，「结束搜寻」仍然不出现（" + (counter == null ? "无计数" : counter.text) +
                              "）—— 剧本的 need 与热点数量对不上");
            }
        }

        IEnumerator SolveAsk()
        {
            var finish = FindButton("Finish");
            if (finish != null && finish.gameObject.activeSelf) { yield return Click("Finish"); yield return WaitFrames(3); yield break; }
            var back = FindButton("Back");
            if (back != null && back.gameObject.activeSelf) { yield return Click("Back"); yield return WaitFrames(2); yield break; }
            var root = Layer("Ask");
            if (_askRoot != (root as RectTransform)) { _askRoot = root as RectTransform; _askTried.Clear(); }
            var topic = root.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(b => b.gameObject.name.StartsWith("T_") && !_askTried.Contains(b.gameObject.name));
            if (topic != null)
            {
                _askTried.Add(topic.gameObject.name);
                topic.onClick.Invoke();
                yield return WaitFrames(3);
                yield break;
            }
            var counter = TextAt("Canvas/SafeArea/OverlayLayer/Ask/Card/Footer/Counter");
            Assert(false, "询问里的话题全问过了，但还没问够（" + (counter == null ? "无计数" : counter.text) +
                          "）—— 话题的解锁条件可能太严");
        }

        IEnumerator SolveAccuse(ContentDatabase db)
        {
            var next = FindButton("Next");
            if (next != null && next.gameObject.activeSelf) { yield return Click("Next"); yield return WaitFrames(2); yield break; }
            var qtext = TextAt("Canvas/SafeArea/OverlayLayer/Accuse/Card/Q");
            Assert(qtext != null && !string.IsNullOrEmpty(qtext.text), "指认界面没有题目文字");
            var q = db.Accusation.FirstOrDefault(x => x.question.Trim() == qtext.text.Trim());
            Assert(q != null, "指认题目的文字和数据对不上：" + qtext.text);
            yield return Click("O" + q.answer);
            yield return WaitFrames(2);
        }

        /// <summary>
        /// 玩到一半就撤：HUD 是画在最上层的，所以玩家完全可以在调查 / 推理 / 章节卡演出的
        /// 中途点「菜单 → 返回标题」。这时场上不能留下残局面板，剧情也不能在标题背后接着跑
        /// （章节卡的协程、wait 指令的协程都还挂着，最容易出这种事）。
        /// </summary>
        IEnumerator CheckAbandonMidway()
        {
            var db = LoadDb();
            var title = GameUI.Instance.transform.Find("Canvas/SafeArea/Title");
            var dlg = GameUI.Instance.transform.Find("Canvas/SafeArea/DialogueLayer/Dialogue");

            GameUI.Instance.ResetForCheck();
            yield return WaitFrames(2);
            if (!title.gameObject.activeSelf)
            {
                yield return Click("Hud_菜单");
                yield return WaitFrames(3);
                yield return Click("Btn_返回标题");
                yield return WaitFrames(4);
            }
            yield return Click("M_开始案件调查");
            yield return WaitSeconds(3.4f);
            Assert(dlg.gameObject.activeSelf, "开新游戏后没有进入对话");

            // ---- 调查到一半返回标题 ----
            GameUI.Instance.ShowInvestigate(db.Investigations.Values.First(), () => { });
            yield return WaitFrames(4);
            Assert(Active(Layer("Investigate")), "没有进入现场调查界面");
            yield return Click("Hud_菜单");
            yield return WaitFrames(3);
            Assert(Active(OverlayPanelRoot()), "调查中打不开菜单");
            yield return Click("Btn_返回标题");
            yield return WaitFrames(5);
            Assert(title.gameObject.activeSelf, "调查中途返回标题，标题画面没出来");
            Assert(!Active(Layer("Investigate")), "调查中途返回标题，调查面板还留在屏幕上");
            Assert(!dlg.gameObject.activeSelf, "返回标题后对话框还亮着");

            int beat = GameDirector.Instance == null ? -1 : GameDirector.Instance.State.beatIndex;
            yield return WaitSeconds(1.5f);
            Assert(!dlg.gameObject.activeSelf, "返回标题后剧情自己在背后继续跑了");
            Assert(GameDirector.Instance == null || GameDirector.Instance.State.beatIndex == beat,
                "返回标题后进度还在往前爬：" + beat + " → " + GameDirector.Instance.State.beatIndex);

            // ---- 章节卡刚开头就返回标题（卡片的协程还在等 next）----
            yield return Click("M_开始案件调查");
            yield return WaitFrames(4);
            Assert(Active(Layer("ChapterCard")), "开新游戏后章节卡没有出现");
            yield return Click("Hud_菜单");
            yield return WaitFrames(2);
            yield return Click("Btn_返回标题");
            yield return WaitSeconds(3.6f);                       // 比章节卡整段演出更久
            Assert(title.gameObject.activeSelf, "章节卡中途返回标题，标题画面没出来");
            Assert(!Active(Layer("ChapterCard")), "章节卡还挂在标题画面上");
            Assert(!dlg.gameObject.activeSelf, "章节卡被打断后，剧情又自己在标题背后接着跑了");
            yield return null;
        }

        IEnumerator ClickFirstChoice()
        {
            Assert(FindButton("Opt0") != null, "选项界面没有生成按钮 Opt0");
            yield return Click("Opt0");
            yield return WaitFrames(3);
        }

        IEnumerator AdvanceDialogue()
        {
            GameUI.Instance.transform.Find("Canvas/SafeArea/AdvanceLayer")
                .GetComponent<Button>().onClick.Invoke();
            yield return WaitFrames(2);
        }

        // ---------------------------------------------------------------- 报告

        void WriteReport()
        {
            // ScreenCapture 是异步落盘的，最后再扫一次目录，别漏报截图
            if (Directory.Exists(_shotDir))
                _shots.Clear();
            if (Directory.Exists(_shotDir))
                foreach (var f in Directory.GetFiles(_shotDir, "*.png").OrderBy(f => f))
                    _shots.Add(Path.GetFileName(f));

            int bad = _rows.Count(r => !r.ok);
            var sb = new StringBuilder();
            sb.AppendLine("=== 框架功能体检 ===");
            sb.AppendLine("分辨率 " + Screen.width + "x" + Screen.height +
                          "　时间 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("通过 " + (_rows.Count - bad) + " / 失败 " + bad + (_timedOut ? "（超时中断）" : ""));
            sb.AppendLine();
            foreach (var r in _rows)
                sb.AppendLine((r.ok ? "[通过] " : "[失败] ") + r.name +
                              (string.IsNullOrEmpty(r.detail) ? "" : "  —— " + r.detail));
            sb.AppendLine();
            sb.AppendLine("--- 截图 " + _shots.Count + " 张 ---");
            foreach (var s in _shots) sb.AppendLine(s);
            sb.AppendLine();
            sb.AppendLine("--- 运行期警告 " + _warnLog.Count + " 条 ---");
            foreach (var g in _warnLog.GroupBy(w => w).Select(g => g.Key + "  ×" + g.Count()).Take(60))
                sb.AppendLine(g);
            sb.AppendLine();
            sb.AppendLine("--- 运行期错误 " + _errorLog.Count + " 条 ---");
            foreach (var g in _errorLog.GroupBy(w => w).Select(g => g.Key + "  ×" + g.Count()).Take(60))
                sb.AppendLine(g);

            if (_notes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("--- 备注 ---");
                foreach (var n in _notes) sb.AppendLine(n);
            }

            File.WriteAllText(Path.Combine(_outDir, "system_check.txt"), sb.ToString(), Encoding.UTF8);
            var json = JsonConvert.SerializeObject(new
            {
                screen = Screen.width + "x" + Screen.height,
                time = DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
                passed = _rows.Count - bad,
                failed = bad,
                timedOut = _timedOut,
                results = _rows,
                shots = _shots,
                warnings = _warnLog.GroupBy(w => w).Select(g => new { message = g.Key, count = g.Count() }).ToList(),
                errors = _errorLog.GroupBy(w => w).Select(g => new { message = g.Key, count = g.Count() }).ToList()
            }, Formatting.Indented);
            File.WriteAllText(Path.Combine(_outDir, "system_check.json"), json, Encoding.UTF8);
            Debug.Log(sb.ToString());
        }

    }
}
