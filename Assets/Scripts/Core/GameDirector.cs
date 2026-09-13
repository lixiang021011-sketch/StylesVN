using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Styles.Core
{
    /// <summary>界面层需要实现的接口。GameDirector 只负责解释剧本，不碰具体控件。</summary>
    public interface IVnView
    {
        void ShowSay(Beat b, Action next);
        void ShowChapter(Beat b, Action next);
        void ShowChoice(Beat b, Action<Option> pick);
        void ShowDeduce(Beat b, Action<int> resolve);          // 反复调用直到选对；view 自行处理重试
        void ShowInvestigate(InvestigationScene sc, Action done);
        void ShowAsk(List<AskCharacter> chars, int need, string title, Action done);
        void ShowNote(Beat b, Action done);
        void ShowAccusation(List<AccusationQuestion> qs, Action<List<AccuseRecord>> done);
        void ShowEnding(GameState st, ContentDatabase db);
        void SetBackground(string id, bool instant);
        void SetFigures(List<FigureState> figs);
        void PlayFx(string fx);
        void Toast(string msg);
        void SetHud(string tag, string title);
    }

    /// <summary>剧本解释器：线性推进 + 标签跳转 + 条件分支（分支为短片段，不跨存档）。</summary>
    public class GameDirector : MonoBehaviour
    {
        public static GameDirector Instance { get; private set; }

        public ContentDatabase Db;
        public GameState State = new GameState();
        public IVnView View;
        public bool AutoMode;
        public bool SkipMode;
        public bool Running { get; private set; }

        List<Beat> _beats = new List<Beat>();
        readonly Stack<Frame> _stack = new Stack<Frame>();
        int _index;
        float _saveTimer;

        class Frame
        {
            public List<Beat> beats;
            public int index;
        }

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            // 单例被销毁后不清空引用，下一次 `Instance ?? AddComponent(...)` 会拿到一个「假对象」，
            // 之后所有访问都会抛 MissingReferenceException。
            if (Instance == this) Instance = null;
        }

        public void Initialize(ContentDatabase db, IVnView view, GameState state)
        {
            // 编辑器/测试环境里 AddComponent 不会触发 Awake，这里补一次绑定
            Instance = this;
            Db = db; View = view;
            _beats = db.Flatten();
            if (state != null) State = state;
            _stack.Clear();
            _index = Mathf.Clamp(State.beatIndex, 0, Mathf.Max(0, _beats.Count - 1));
        }

        public void StartNewGame()
        {
            State = new GameState();
            _stack.Clear();
            _index = 0;
            // 从标题画面重开时，上一次游玩还停在「等待界面点击」的状态里，Running 仍是 true；
            // 不在这里清掉，Run() 会直接 return —— 屏幕有界面但剧情永远不动。
            Running = false;
            Run();
        }

        public void Run()
        {
            if (Running) return;
            Running = true;
            Step();
        }

        /// <summary>
        /// 把正在跑的剧情停下来（返回标题、读档、重新开始时调用）。
        /// `wait` 指令的协程挂在解释器自己身上，不一起停掉的话，
        /// 玩家返回标题之后剧情还会在标题背后继续往下走。
        /// </summary>
        public void Stop()
        {
            Running = false;
            StopAllCoroutines();
        }

        void Step()
        {
            Current = Peek();
            if (Current == null) { Running = false; return; }
            State.beatIndex = _index;
            _saveTimer += Time.unscaledDeltaTime;

            switch (Current.t)
            {
                case "chapter": View.SetHud(Current.tag, Current.title); View.ShowChapter(Current, Next); break;
                case "bg": View.SetBackground(Current.id, false); if (!string.IsNullOrEmpty(Current.bgm)) AudioDirector.Instance?.PlayBgm(Current.bgm); Next(); break;
                case "bgm": AudioDirector.Instance?.PlayBgm(Current.id); Next(); break;
                case "sfx": AudioDirector.Instance?.PlaySfx(Current.id); Next(); break;
                case "say":
                    if (!string.IsNullOrEmpty(Current.fx)) View.PlayFx(Current.fx);
                    // say 上也可以挂 give：念到这一句就记进笔记本（用于「台词本身就是证据」的情形）
                    if (!string.IsNullOrEmpty(Current.give)) State.AddClue(Current.give);
                    View.SetFigures(Current.figs);
                    State.Log(Current.who, Current.text);
                    View.ShowSay(Current, Next);
                    break;
                // say 指令上可以直接挂特效（例如某句台词要闪一下），此前从未被读取
                case "fx": View.PlayFx(Current.fx); Next(); break;
                case "choice": View.ShowChoice(Current, OnChoice); break;
                case "deduce": View.ShowDeduce(Current, OnDeduce); break;
                case "investigate": DoInvestigate(); break;
                case "ask": DoAsk(); break;
                case "note": View.ShowNote(Current, Next); break;
                case "label": Next(); break;
                case "jump": JumpTo(Current.target); break;
                case "set": State.SetFlag(Current.set, Current.value); Next(); break;
                case "if": DoBranch(); break;
                case "wait": StartCoroutine(WaitRoutine(Current.wait, Next)); break;
                case "accuse": View.ShowAccusation(Db.Accusation, OnAccusationDone); break;
                case "ending": Running = false; View.ShowEnding(State, Db); SaveSystem.Save(SaveSystem.AutoSlot, State); break;
                default: Debug.LogWarning("[Styles] 未知指令类型: " + Current.t); Next(); break;
            }
        }

        public Beat Current { get; private set; }

        Beat Peek()
        {
            if (_stack.Count > 0)
            {
                var f = _stack.Peek();
                if (f.index < f.beats.Count) return f.beats[f.index];
                _stack.Pop();
                return Peek();
            }
            return _index < _beats.Count ? _beats[_index] : null;
        }

        void Next()
        {
            // 已经停了（返回标题 / 读档 / 结局）就不再往前走：
            // 章节卡、解谜面板这些回调可能比 Stop() 晚一步到达，
            // 不挡住的话剧情会在标题背后自己往下演。
            if (!Running) return;
            if (_stack.Count > 0) { _stack.Peek().index++; }
            else _index++;
            if (State.beatIndex % 8 == 0 || _saveTimer > 20f) { _saveTimer = 0f; SaveSystem.Save(SaveSystem.AutoSlot, State); }
            Step();
        }

        void JumpTo(string label)
        {
            if (string.IsNullOrEmpty(label)) { Next(); return; }
            for (int i = 0; i < _beats.Count; i++)
            {
                if (_beats[i].t == "label" && _beats[i].id == label) { _index = i + 1; Step(); return; }
            }
            Debug.LogWarning("[Styles] 找不到标签: " + label);
            Next();
        }

        IEnumerator WaitRoutine(float seconds, Action then)
        {
            yield return new WaitForSeconds(seconds);
            then();
        }

        void OnChoice(Option opt)
        {
            if (opt != null)
            {
                if (opt.score != 0) State.score += opt.score;
                if (!string.IsNullOrEmpty(opt.give)) State.AddClue(opt.give);
                if (!string.IsNullOrEmpty(opt.gotoLabel)) { JumpTo(opt.gotoLabel); return; }
            }
            Next();
        }

        void OnDeduce(int choice)
        {
            var b = Current;
            if (b == null) { Next(); return; }
            if (choice == b.answer)
            {
                if (b.score != 0) State.score += b.score;
                if (!string.IsNullOrEmpty(b.give)) State.AddClue(b.give);
                if (!string.IsNullOrEmpty(b.insight))
                {
                    State.AddClue(b.insight);
                    if (!State.insights.Contains(b.insight)) State.insights.Add(b.insight);
                    var e = Db.Evidence.ContainsKey(b.insight) ? Db.Evidence[b.insight] : null;
                    if (e != null) e.insight = b.explain;
                }
                Next();
            }
            else
            {
                State.mistakes++;
                State.score = Mathf.Max(0, State.score - 3);
            }
        }

        void DoInvestigate()
        {
            InvestigationScene sc;
            var key = !string.IsNullOrEmpty(Current.scene) ? Current.scene : Current.sceneId;
            if (!Db.Investigations.TryGetValue(key ?? "", out sc))
            {
                // 以前这里静默跳过：剧本写错场景 id 时既没有提示也不报错，查起来非常费劲
                Debug.LogWarning("[Styles] 找不到调查场景: " + key);
                Next();
                return;
            }
            View.SetBackground(sc.bg, false);
            View.SetFigures(null);
            View.ShowInvestigate(sc, () =>
            {
                // 剧本里的调查指令一般只写场景、不写 need，此时用调查场景自己的 need，
                // 否则「没找齐就离开」的扣分永远不会发生。
                int need = Current.need > 0 ? Current.need : sc.need;
                if (need > 0)
                {
                    int found = 0;
                    for (int i = 0; i < sc.spots.Count; i++)
                    {
                        bool hit;
                        if (State.spots.TryGetValue(key + "_" + i, out hit) && hit) found++;
                    }
                    if (found < need) State.score += 4 * (found - need);
                }
                Next();
            });
        }

        void DoAsk()
        {
            var list = new List<AskCharacter>();
            if (Current.chars != null)
                foreach (var id in Current.chars)
                {
                    AskCharacter a;
                    if (Db.Interrogations.TryGetValue(id, out a)) list.Add(a);
                    else Debug.LogWarning("[Styles] 找不到询问对象: " + id);
                }
            if (list.Count == 0)
            {
                Debug.LogWarning("[Styles] 询问指令没有任何有效对象，已跳过");
                Next();
                return;
            }
            View.ShowAsk(list, Current.need, string.IsNullOrEmpty(Current.title) ? "询问" : Current.title, Next);
        }

        void DoBranch()
        {
            bool ok = EvalCondition(Current.cond);
            var seq = ok ? Current.then : Current.els;
            // 关键：if 指令本身必须先被消费掉，再压入分支。
            // 否则分支跑完后 Peek() 会退回父层、而父层的位置还停在同一条 if 上，
            // 于是条件被反复求值、分支被反复压栈 —— 无限递归（剧本里一旦用到 if 就会卡死）。
            if (_stack.Count > 0) _stack.Peek().index++;
            else _index++;
            if (seq == null || seq.Count == 0) { Step(); return; }
            _stack.Push(new Frame { beats = seq, index = 0 });
            Step();
        }

        public bool EvalCondition(string cond)
        {
            if (string.IsNullOrEmpty(cond)) return true;
            // 支持 clue:x / notclue:x / flag:x / flag:x>=n / flag:x<n，用 ; 或 && 连接
            foreach (var partRaw in cond.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var part = partRaw.Trim().Replace("&&", ";");
                foreach (var p in part.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var s = p.Trim();
                    if (s.StartsWith("clue:")) { if (!State.HasClue(s.Substring(5).Trim())) return false; continue; }
                    if (s.StartsWith("notclue:")) { if (State.HasClue(s.Substring(8).Trim())) return false; continue; }
                    if (s.StartsWith("flag:"))
                    {
                        var body = s.Substring(5).Trim();
                        int cmp = body.IndexOf(">=", StringComparison.Ordinal);
                        if (cmp > 0) { if (State.Flag(body.Substring(0, cmp).Trim()) < int.Parse(body.Substring(cmp + 2))) return false; continue; }
                        cmp = body.IndexOf("<=", StringComparison.Ordinal);
                        if (cmp > 0) { if (State.Flag(body.Substring(0, cmp).Trim()) > int.Parse(body.Substring(cmp + 2))) return false; continue; }
                        cmp = body.IndexOf('>');
                        if (cmp > 0) { if (State.Flag(body.Substring(0, cmp).Trim()) <= int.Parse(body.Substring(cmp + 1))) return false; continue; }
                        cmp = body.IndexOf('<');
                        if (cmp > 0) { if (State.Flag(body.Substring(0, cmp).Trim()) >= int.Parse(body.Substring(cmp + 1))) return false; continue; }
                        if (State.Flag(body) == 0) return false;
                        continue;
                    }
                }
            }
            return true;
        }

        void OnAccusationDone(List<AccuseRecord> records)
        {
            State.accuse = records ?? new List<AccuseRecord>();
            Next();
        }

        public void SaveAuto() { SaveSystem.Save(SaveSystem.AutoSlot, State); }

        public void LoadState(GameState st)
        {
            State = st;
            _stack.Clear();
            _index = Mathf.Clamp(st.beatIndex, 0, Mathf.Max(0, _beats.Count - 1));
            View.SetBackground(st.bg, true);
            Running = false;
            Run();
        }

        void Update()
        {
            State.playSeconds += Time.unscaledDeltaTime;
        }
    }
}
