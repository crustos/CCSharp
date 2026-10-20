// TMPro (TextMesh Pro): TMP_Text and its two components, TextMeshProUGUI (on a canvas) and TextMeshPro (in the world), with TMP's fields and
// SetText formatting ("{0:2}" is the value with two decimals).  Nothing draws text yet: the fields are kept.

using System;
using UnityEngine;
using UnityEngine.UI;

namespace TMPro
{
    public enum TextAlignmentOptions
    {
        TopLeft = 257, Top = 258, TopRight = 260, TopJustified = 264,
        Left = 513, Center = 514, Right = 516, Justified = 520,
        BottomLeft = 1025, Bottom = 1026, BottomRight = 1028, BottomJustified = 1032,
        BaselineLeft = 2049, Baseline = 2050, BaselineRight = 2052,
        MidlineLeft = 4097, Midline = 4098, MidlineRight = 4100,
        CaplineLeft = 8193, Capline = 8194, CaplineRight = 8196
    }

    public enum HorizontalAlignmentOptions { Left = 1, Center = 2, Right = 4, Justified = 8, Flush = 16, Geometry = 32 }
    public enum VerticalAlignmentOptions { Top = 256, Middle = 512, Bottom = 1024, Baseline = 2048, Geometry = 4096, Capline = 8192 }

    [Flags]
    public enum FontStyles { Normal = 0, Bold = 1, Italic = 2, Underline = 4, LowerCase = 8, UpperCase = 16, SmallCaps = 32, Strikethrough = 64, Superscript = 128, Subscript = 256, Highlight = 512 }

    public enum TextOverflowModes { Overflow = 0, Ellipsis = 1, Masking = 2, Truncate = 3, ScrollRect = 4, Page = 5, Linked = 6 }
    public enum TextWrappingModes { NoWrap = 0, Normal = 1, PreserveWhitespace = 2, PreserveWhitespaceNoWrap = 3 }

    public class TMP_FontAsset : ScriptableObject { }
    public class TMP_SpriteAsset : ScriptableObject { }

    public abstract class TMP_Text : MaskableGraphic
    {
        protected string m_text = "";

        public TMP_FontAsset font;
        public Material fontSharedMaterial;
        public float fontSize = 36f;
        public float fontSizeMin = 18f;
        public float fontSizeMax = 72f;
        public bool enableAutoSizing;
        public FontStyles fontStyle;
        public TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft;
        public bool richText = true;
        public bool enableWordWrapping = true;
        public TextWrappingModes textWrappingMode = TextWrappingModes.Normal;
        public TextOverflowModes overflowMode;
        public float characterSpacing;
        public float wordSpacing;
        public float lineSpacing;
        public float paragraphSpacing;
        public int maxVisibleCharacters = 99999;
        public int maxVisibleWords = 99999;
        public int maxVisibleLines = 99999;
        public int pageToDisplay = 1;
        public bool isRightToLeftText;
        public bool enableKerning = true;
        public bool extraPadding;
        public Vector4 margin;
        public Color32 faceColor = new Color32(255, 255, 255, 255);
        public Color outlineColor = Color.black;
        public float outlineWidth;
        public TMP_SpriteAsset spriteAsset;

        public virtual string text
        {
            get { return m_text; }
            set { m_text = value ?? ""; }
        }

        public float alpha { get { return color.a; } set { Color c = color; c.a = value; color = c; } }
        public HorizontalAlignmentOptions horizontalAlignment
        {
            get { return (HorizontalAlignmentOptions)((int)alignment & 0xFF); }
            set { alignment = (TextAlignmentOptions)(((int)alignment & 0xFF00) | (int)value); }
        }
        public VerticalAlignmentOptions verticalAlignment
        {
            get { return (VerticalAlignmentOptions)((int)alignment & 0xFF00); }
            set { alignment = (TextAlignmentOptions)(((int)alignment & 0xFF) | (int)value); }
        }

        /// <summary>A guess at the text's size (half an em per character): nothing lays text out here.</summary>
        public float preferredWidth { get { return m_text.Length * fontSize * 0.5f; } }
        public float preferredHeight { get { return fontSize * (1f + lineSpacing / 100f); } }
        public Vector2 GetPreferredValues() { return new Vector2(preferredWidth, preferredHeight); }
        public Vector2 GetPreferredValues(string text) { return new Vector2((text ?? "").Length * fontSize * 0.5f, preferredHeight); }
        public int textInfoCharacterCount { get { return m_text.Length; } }

        public void SetText(string sourceText) { text = sourceText; }
        public void SetText(string sourceText, bool syncTextInputBox) { text = sourceText; }
        public void SetText(string sourceText, float arg0) { text = Format(sourceText, new float[] { arg0 }); }
        public void SetText(string sourceText, float arg0, float arg1) { text = Format(sourceText, new float[] { arg0, arg1 }); }
        public void SetText(string sourceText, float arg0, float arg1, float arg2) { text = Format(sourceText, new float[] { arg0, arg1, arg2 }); }

        /// <summary>TMP's SetText format: "{n}" is argument n, "{n:d}" it with d decimals.</summary>
        static string Format(string source, float[] args)
        {
            if (source == null) return "";
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                int close = c == '{' ? source.IndexOf('}', i) : -1;
                if (close < 0) { b.Append(c); continue; }
                string spec = source.Substring(i + 1, close - i - 1);
                int colon = spec.IndexOf(':');
                int index, decimals = 0;
                if (!int.TryParse(colon < 0 ? spec : spec.Substring(0, colon), out index) || index < 0 || index >= args.Length
                    || (colon >= 0 && !int.TryParse(spec.Substring(colon + 1), out decimals)))
                {
                    b.Append(c);
                    continue;
                }
                b.Append(args[index].ToString("F" + decimals));
                i = close;
            }
            return b.ToString();
        }

        public void ForceMeshUpdate() { }
        public void ForceMeshUpdate(bool ignoreActiveState, bool forceTextReparsing) { }
        public void UpdateMeshPadding() { }
        public void ClearMesh() { }
    }

    public class TextMeshProUGUI : TMP_Text { }

    public class TextMeshPro : TMP_Text
    {
        public int sortingOrder;
        public int sortingLayerID;
    }
}
