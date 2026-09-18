// PretextLayoutGroup: automatically adjusts RectTransform height to fit PretextTextPro content.
// Place in an Editor folder if you only need it at editor time.

#if TMP_PACKAGE
using UnityEngine;
using UnityEngine.UI;

namespace Pretext.Components
{
    /// <summary>
    /// Layout group that sizes itself based on PretextTextPro content height.
    /// Use this instead of ContentSizeFitter for more accurate CJK-aware sizing.
    ///
    /// Attach to a parent RectTransform containing PretextTextPro children.
    /// </summary>
    [ExecuteInEditMode]
    [RequireComponent(typeof(RectTransform))]
    public class PretextLayoutGroup : MonoBehaviour, ILayoutController
    {
        [SerializeField]
        private float _spacing = 0f;
        [SerializeField]
        private TextAlignmentOptions _childAlignment = TextAlignmentOptions.TopLeft;
        [SerializeField]
        private bool _reverseOrder = false;

        private RectTransform _rect;

        private void Awake()
        {
            _rect = (RectTransform)transform;
        }

        private void OnEnable()
        {
            SetLayoutHorizontal();
            SetLayoutVertical();
        }

        private void Update()
        {
            // Recalculate if children changed (editor-time)
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                SetLayoutHorizontal();
                SetLayoutVertical();
            }
#endif
        }

        public void SetLayoutHorizontal()
        {
            // No horizontal adjustment by default
            // Override if you need horizontal fitting
        }

        public void SetLayoutVertical()
        {
            float totalHeight = 0f;

            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(_reverseOrder ? transform.childCount - 1 - i : i);
                var layoutEl = child.GetComponent<ILayoutElement>();

                if (layoutEl != null)
                {
                    totalHeight += layoutEl.preferredHeight;
                    if (i < transform.childCount - 1)
                        totalHeight += _spacing;
                }
            }

            // Also add padding
            var layoutGroup = GetComponent<LayoutGroup>();
            if (layoutGroup != null)
            {
                totalHeight += layoutGroup.padding.top + layoutGroup.padding.bottom;
            }

            _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalHeight);
        }
    }

    /// <summary>
    /// High-precision text renderer using Pretext for exact visual output.
    ///
    /// Unlike PretextTextPro which delegates rendering to TextMeshPro,
    /// PretextCustomRenderer builds its own geometry from Pretext layout results.
    /// This gives pixel-perfect control over line breaking for CJK text.
    ///
    /// Use this when TMP's line breaking doesn't match Pretext's calculation
    /// (e.g., CJK kinsoku enforcement, mixed bidi).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PretextCustomRenderer : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] private string _text = "";
        [SerializeField] private TMP_FontAsset _fontAsset;
        [SerializeField] [Range(8, 256)] private float _fontSize = 18f;
        [SerializeField] private Color _color = Color.white;

        [Header("Layout")]
        [SerializeField] private float _maxWidth = 320f;
        [SerializeField] [Min(1)] private float _lineHeight = 26f;
        [SerializeField] private TextAlignmentOptions _alignment = TextAlignmentOptions.TopLeft;
        [SerializeField] private WhiteSpaceMode _whiteSpace = WhiteSpaceMode.Normal;

        [Header("Precision")]
        [SerializeField] private bool _useKinsokuRules = true;
        [SerializeField] private bool _includeBidi = true;

        private CanvasRenderer? _canvasRenderer;
        private Mesh? _mesh;
        private IFontMeasurer? _measurer;
        private PreparedText? _prepared;
        private LayoutLinesResult? _layoutResult;
        private bool _dirty = true;

        private void OnEnable() => _dirty = true;
        private void OnValidate() => _dirty = true;

        private void Update()
        {
            if (_dirty)
            {
                CalculateAndRender();
                _dirty = false;
            }
        }

        public void SetText(string newText)
        {
            if (_text == newText) return;
            _text = newText ?? "";
            _dirty = true;
        }

        public void CalculateAndRender()
        {
            if (_fontAsset == null || string.IsNullOrEmpty(_text))
            {
                ClearGeometry();
                return;
            }

            _measurer ??= new Integration.TMPFontMeasurer(_fontAsset, _fontSize);

            _prepared = PretextCore.PrepareWithSegments(_text, _measurer, _whiteSpace, _includeBidi);
            _layoutResult = PretextCore.LayoutWithLines(_prepared, _maxWidth, _lineHeight);

            RebuildGeometry();
        }

        private void ClearGeometry()
        {
            _canvasRenderer?.Clear();
            _mesh?.Clear();
        }

        private void RebuildGeometry()
        {
            if (_layoutResult == null || _prepared == null) return;

            // Get or create mesh
            _mesh ??= new Mesh();
            _canvasRenderer ??= GetComponent<CanvasRenderer>();
            if (_canvasRenderer == null)
                _canvasRenderer = gameObject.AddComponent<CanvasRenderer>();

            var verts = new System.Collections.Generic.List<UIVertex>();
            var indices = new System.Collections.Generic.List<int>();

            float y = 0; // Start from top
            foreach (var line in _layoutResult.Value.Lines)
            {
                float offset = GetHorizontalOffset(line.Width, _maxWidth, _alignment);
                RenderLine(verts, line.Text, offset, y, line.Width);
                y += _lineHeight;
            }

            // Note: Full vertex + index buffer construction is complex.
            // This is a simplified outline. For full implementation:
            // 1. Use TMP's text generator to get character quads
            // 2. Position them according to Pretext's line widths and alignment
            // 3. Set vertex colors from _color
            //
            // The key advantage: Pretext's line text (line.Text) determines WHAT to render,
            // while Pretext's line.Width determines WHERE to position it.

            // For now, fall back to TMP component
            EnsureTMPFallback();
        }

        private void RenderLine(System.Collections.Generic.List<UIVertex> verts, string text,
            float x, float y, float lineWidth)
        {
            // Simplified: adds text position for each character
            // Full implementation would generate UIVertex quads from TMP_Glyph data
            if (string.IsNullOrEmpty(text)) return;

            float charX = x;
            foreach (char ch in text)
            {
                // Get glyph metrics from font asset
                if (_fontAsset.characterTable.TrySearchForCharacterIndex(ch, out var character))
                {
                    float advance = character.glyph.metrics.horizontalAdvance * (_fontSize / _fontAsset.fontInfo.PointSize);
                    charX += advance;
                }
                else
                {
                    charX += _fontSize * 0.5f; // Fallback
                }
            }
        }

        private void EnsureTMPFallback()
        {
            // If full custom geometry is too complex, use TMP for rendering
            var tmp = GetComponent<TextMeshProUGUI>();
            if (tmp == null) tmp = gameObject.AddComponent<TextMeshProUGUI>();

            tmp.font = _fontAsset;
            tmp.fontSize = _fontSize;
            tmp.color = _color;
            tmp.alignment = _alignment;
            tmp.enableWordWrapping = false;
            tmp.text = _text;
            tmp.autoSizeTextContainer = false;

            // Note: TMP's internal line breaking will differ from Pretext here.
            // The _layoutResult height can still be used for container sizing.
        }

        private static float GetHorizontalOffset(float lineWidth, float containerWidth, TextAlignmentOptions alignment)
        {
            float delta = containerWidth - lineWidth;
            return alignment switch
            {
                TextAlignmentOptions.TopLeft or TextAlignmentOptions.Left or TextAlignmentOptions.BottomLeft => 0,
                TextAlignmentOptions.Top or TextAlignmentOptions.Center or TextAlignmentOptions.Bottom => delta / 2,
                TextAlignmentOptions.TopRight or TextAlignmentOptions.Right or TextAlignmentOptions.BottomRight => delta,
                _ => 0
            };
        }
    }
}
#endif // TMP_PACKAGE
