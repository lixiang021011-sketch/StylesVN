using System;
using Styles.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Styles.UI
{
    /// <summary>纯代码构建 uGUI，避免手工维护场景文件；所有控件在移动端都可点。</summary>
    public static class UiFactory
    {
        public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                         Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = anchorMin; r.anchorMax = anchorMax;
            r.offsetMin = offsetMin; r.offsetMax = offsetMax;
            return r;
        }

        public static RectTransform Stretch(string name, Transform parent)
        {
            return Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        public static Image Panel(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax,
                                  Vector2 offsetMin, Vector2 offsetMax)
        {
            var r = Rect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);
            var img = r.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Image StretchImage(string name, Transform parent, Color color)
        {
            var img = Stretch(name, parent).gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static TMP_Text Text(string name, Transform parent, string content, int size, Color color,
                                    TextAlignmentOptions align = TextAlignmentOptions.TopLeft, float lineSpacing = 1.35f)
        {
            var r = Rect(name, parent, new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            var f = FontService.Main;
            if (f != null) t.font = f;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.lineSpacing = lineSpacing;
            t.enableWordWrapping = true;
            t.raycastTarget = false;
            t.richText = true;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        public static Button Button(string name, Transform parent, string label, int fontSize, Action onClick,
                                    Color? bg = null, Color? fg = null, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var r = Rect(name, parent, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            r.pivot = new Vector2(.5f, 1f);
            var img = r.gameObject.AddComponent<Image>();
            img.color = bg ?? new Color(0.05f, 0.11f, 0.10f, 0.92f);
            var btn = r.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, .95f, .8f);
            colors.pressedColor = new Color(.85f, .8f, .65f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var txt = Text(name + "_label", r, label, fontSize, fg ?? UiTheme.Paper, align);
            var tr = txt.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(28, 8); tr.offsetMax = new Vector2(-28, -8);
            txt.alignment = align;
            return btn;
        }

        public static RectTransform LayoutVertical(string name, Transform parent, float spacing, RectOffset padding,
                                                   TextAnchor align = TextAnchor.UpperLeft)
        {
            var r = Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var v = r.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding ?? new RectOffset(0, 0, 0, 0);
            v.childAlignment = align;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            v.childControlHeight = true;
            v.childControlWidth = true;
            return r;
        }

        public static LayoutElement Size(GameObject go, float height, float width = -1f)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            if (width > 0f) { le.preferredWidth = width; le.minWidth = width; }
            return le;
        }

        public static ScrollRect Scroll(string name, Transform parent, out RectTransform content, float spacing = 16f,
                                        RectOffset padding = null)
        {
            var viewport = Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var mask = viewport.gameObject.AddComponent<RectMask2D>();
            mask.padding = new Vector4(4, 4, 4, 4);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();

            // 列表内容必须「上边贴住视口 + 竖向按需撑高」。
            // 以前用 LayoutVertical 生成的内容是四向拉伸的，再叠一个 ContentSizeFitter，
            // 高度会变成「视口高度 + 内容高度」，于是每个列表底下都多出一大块空白。
            var contentRect = Rect("Content", viewport, new Vector2(0, 1), new Vector2(1, 1),
                Vector2.zero, Vector2.zero);
            contentRect.pivot = new Vector2(.5f, 1f);
            var vertical = contentRect.gameObject.AddComponent<VerticalLayoutGroup>();
            vertical.spacing = spacing;
            vertical.padding = padding ?? new RectOffset(0, 0, 0, 0);
            vertical.childAlignment = TextAnchor.UpperLeft;
            vertical.childForceExpandHeight = false;
            vertical.childForceExpandWidth = true;
            vertical.childControlHeight = true;
            vertical.childControlWidth = true;
            content = contentRect;
            var fitter = contentRect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = contentRect;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 40f;
            scroll.inertia = true;
            return scroll;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        public static void SetActive(Transform t, bool on) { if (t != null) t.gameObject.SetActive(on); }
    }

    /// <summary>把 Canvas 内容限制在刘海屏安全区内。</summary>
    public class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rect;
        Rect _last;
        void Awake() { _rect = GetComponent<RectTransform>(); Apply(); }
        void Update() { if (Screen.safeArea != _last) Apply(); }
        void Apply()
        {
            if (_rect == null) return;
            _last = Screen.safeArea;
            // 打包成无窗口 / 后台运行时 Screen 尺寸可能是 0，除零会得到 NaN 锚点，整个界面消失
            if (Screen.width <= 0 || Screen.height <= 0) { _rect.anchorMin = Vector2.zero; _rect.anchorMax = Vector2.one; _rect.offsetMin = Vector2.zero; _rect.offsetMax = Vector2.zero; return; }
            var min = _last.position;
            var max = _last.position + _last.size;
            min.x /= Screen.width; min.y /= Screen.height;
            max.x /= Screen.width; max.y /= Screen.height;
            _rect.anchorMin = min; _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero; _rect.offsetMax = Vector2.zero;
        }
    }
}
