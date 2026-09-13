using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Styles.Core
{
    /// <summary>剧本中的单条演出指令。字段与 JSON 一一对应，便于写作工具直接产出。</summary>
    [Serializable]
    public class Beat
    {
        public string t;                    // say / chapter / bg / choice / deduce / investigate / ask / note / label / jump / set / if / accuse / ending / wait / bgm / sfx
        public string who;                  // 说话人 id（narr = 旁白）
        public string text;                 // 台词正文，支持 TMP 富文本
        [JsonProperty("id")] public string id;     // 背景 / 标签 id
        public string tag;                  // 章节卡上方的短标签
        public string title;
        public string sub;
        public List<FigureState> figs;      // 立绘状态（为空表示清空）
        public string fx;                   // flash / shake / fade
        public string sfx;
        public string bgm;
        public float wait = 0f;

        // choice
        public string prompt;
        public List<Option> options;

        // deduce
        public string question;
        public List<string> answers;
        public int answer = -1;
        public List<string> wrong;
        public string explain;
        public string give;
        public string insight;
        public int score = 0;

        // investigate / ask
        public string scene;
        public string sceneId;
        public int need = 0;
        public List<string> chars;

        // note
        public string icon;
        public string body;

        // 控制流
        public string target;               // jump 目标标签
        public string cond;                 // 条件表达式，如 clue:cup / flag:trust_mary>=2
        public string set;
        public int value = 0;
        public List<Beat> then;
        public List<Beat> els;
    }

    [Serializable]
    public class FigureState
    {
        public string id;
        public string pos = "mid";          // left / midL / mid / midR / right / center
        public string emotion = "neutral";
        public bool flip = false;
        public bool dim = false;
    }

    [Serializable]
    public class Option
    {
        public string text;
        public string hint;
        public int score = 0;
        public string give;
        [JsonProperty("goto")] public string gotoLabel;   // 跳转到标签（goto 是 C# 关键字）
    }

    [Serializable]
    public class Chapter
    {
        public string id;
        public string title;
        public string act;
        public string summary;
        public List<Beat> beats = new List<Beat>();
    }

    [Serializable]
    public class CharacterInfo
    {
        public string id;
        public string name;
        public string role;
        public string description;
        public List<string> emotions = new List<string>();
        public string color = "#F0E6CD";
    }

    [Serializable]
    public class EvidenceInfo
    {
        public string id;
        public string name;
        public string category;             // 物证 / 证词 / 推论
        public string source;
        public string description;
        public string insight;              // 波洛的批注（推理后解锁）
        public string icon;
    }

    [Serializable]
    public class Hotspot
    {
        public float x;
        public float y;
        public string icon;
        public string clue;
        public string text;
    }

    [Serializable]
    public class InvestigationScene
    {
        public string id;
        public string bg;
        public string title;
        public string hint;
        public string intro;
        public int need = 0;
        public List<Hotspot> spots = new List<Hotspot>();
    }

    [Serializable]
    public class AskLine
    {
        public string who;
        public string text;
    }

    [Serializable]
    public class AskTopic
    {
        public string id;
        public string label;
        public string need;
        public string give;
        public List<AskLine> lines = new List<AskLine>();
    }

    [Serializable]
    public class AskCharacter
    {
        public string id;
        public string name;
        public string role;
        public List<AskTopic> topics = new List<AskTopic>();
    }

    [Serializable]
    public class AccusationQuestion
    {
        public string question;
        public List<string> options = new List<string>();
        public int answer = 0;
        public string explain;
    }

    [Serializable]
    public class FaithRow
    {
        public string item;
        public string source;
        public string kind;
    }

    /// <summary>全部内容数据库的运行时容器。</summary>
    public class ContentDatabase
    {
        public readonly List<Chapter> Chapters = new List<Chapter>();
        public readonly Dictionary<string, CharacterInfo> Characters = new Dictionary<string, CharacterInfo>();
        public readonly Dictionary<string, EvidenceInfo> Evidence = new Dictionary<string, EvidenceInfo>();
        public readonly Dictionary<string, InvestigationScene> Investigations = new Dictionary<string, InvestigationScene>();
        public readonly Dictionary<string, AskCharacter> Interrogations = new Dictionary<string, AskCharacter>();
        public readonly List<AccusationQuestion> Accusation = new List<AccusationQuestion>();
        public readonly List<FaithRow> Faith = new List<FaithRow>();

        public static ContentDatabase Load(IContentSource source)
        {
            var db = new ContentDatabase();
            // 路径相对于 StreamingAssets/content（IContentSource 已带该前缀，避免重复拼接）
            var index = source.ReadJson<ContentIndex>("index.json");
            if (index == null) throw new Exception("[Styles] 缺少 content/index.json，无法启动游戏。");

            foreach (var file in index.chapters)
            {
                var ch = source.ReadJson<Chapter>(file);
                if (ch != null) db.Chapters.Add(ch);
            }
            foreach (var file in index.characters)
            {
                var list = source.ReadJson<List<CharacterInfo>>(file) ?? new List<CharacterInfo>();
                foreach (var c in list) db.Characters[c.id] = c;
            }
            foreach (var file in index.evidence)
            {
                var list = source.ReadJson<List<EvidenceInfo>>(file) ?? new List<EvidenceInfo>();
                foreach (var e in list) db.Evidence[e.id] = e;
            }
            foreach (var file in index.investigations)
            {
                var list = source.ReadJson<List<InvestigationScene>>(file) ?? new List<InvestigationScene>();
                foreach (var s in list) db.Investigations[s.id] = s;
            }
            foreach (var file in index.interrogations)
            {
                var list = source.ReadJson<List<AskCharacter>>(file) ?? new List<AskCharacter>();
                foreach (var a in list) db.Interrogations[a.id] = a;
            }
            db.Accusation.AddRange(source.ReadJson<List<AccusationQuestion>>("accusation.json") ?? new List<AccusationQuestion>());
            db.Faith.AddRange(source.ReadJson<List<FaithRow>>("faith.json") ?? new List<FaithRow>());
            return db;
        }

        public List<Beat> Flatten()
        {
            var all = new List<Beat>();
            foreach (var ch in Chapters)
            {
                all.Add(new Beat { t = "chapter", tag = ch.act, title = ch.title, sub = ch.summary });
                foreach (var b in ch.beats) all.Add(b);
            }
            return all;
        }

        public CharacterInfo Character(string id)
        {
            CharacterInfo c;
            return Characters.TryGetValue(id ?? "", out c) ? c : new CharacterInfo { id = id, name = id ?? "" };
        }
    }

    [Serializable]
    public class ContentIndex
    {
        public List<string> chapters = new List<string>();
        public List<string> characters = new List<string>();
        public List<string> evidence = new List<string>();
        public List<string> investigations = new List<string>();
        public List<string> interrogations = new List<string>();
    }
}
