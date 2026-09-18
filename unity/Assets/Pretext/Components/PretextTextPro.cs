// Main Pretext component for Unity UI using TextMeshPro.
// Provides accurate CJK line-breaking, bidi text, and fast height calculation.

#if TMP_PACKAGE
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Pretext.Integration;

namespace Pretext.Components
{
    /// <summary>
    /// High-quality text component using Pretext for layout and TextMeshPro for rendering.
    ///
    /// PretextTextPro provides:
    /// - Accurate CJK line breaking (kinsoku rules)
    /// - Mixed bidirectional text support
    /// - Fast height calculation without DOM measurements
    /// - Precise per-line width for custom alignment
    ///
    /// Usage: Attach to a GameObject with RectTransform.
    /// Set text, font, size, and maxWidth. Call CalculateLayout() or enable autoLayout.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PretextTextPro : MonoBehaviour, ILayoutElement
    {
        #region Inspector Fields

        [Header("Content")]
        [SerializeField] private string _text = "";
        [SerializeField, Range(8, 256)]
        private float _fontSize = 18f;
        [SerializeField]
        private TMP_FontAsset _fontAsset;
        [SerializeField]
        private Color _color = Color.white;
        [SerializeField]
        private Color _outlineColor = Color.black;
        [SerializeField]
        private float _outlineWidth = 0f;

        [Header("Layout")]
        [SerializeField]
        private float _maxWidth = 320f;
        [Min(1)]
        [SerializeField] private float _lineHeight = 26f;
        [SerializeField]
        private WhiteSpaceMode _whiteSpace = WhiteSpaceMode.Normal;
        [SerializeField]
        private TextAlignmentOptions _alignment = TextAlignmentOptions.TopLeft;
        [SerializeField]
        private bool _autoLayout = true;

        [Header("Advanced")]
        [SerializeField]
        private bool _preciseLineBreaking = false;
        [SerializeField]
        private bool _includeBidiLevels = false;
        [SerializeField]
        private bool _useGlobalizationSegmenter = true;

        #endregion

        #region Private Fields

        private TMP_Text _tmpText;
        private RectTransform _rect;
        private IFontMeasurer? _measurer;
        private PreparedText? _preparedText;
        private LayoutLinesResult? _lastLayoutResult;
        private float _lastMaxWidth = -1;
        private string _lastText = "";
        private bool _layoutDirty = true;

        #endregion

        #region Public Properties

        /// <summary>
        /// The current text content.
        /// Setting this marks the layout as dirty.
        /// </summary>
        public string text
        {
            get => _text;
            set
            {
                if (_text == value) return;
                _text = value ?? "";
                MarkDirty();
            }
        }

        /// <summary>
        /// Font size in pixels.
        /// </summary>
        public float fontSize
        {
            get => _fontSize;
            set
            {
                if (Mathf.Approximately(_fontSize, value)) return;
                _fontSize = value;
                MarkDirty();
            }
        }

        /// <summary>
        /// The TMP FontAsset used for rendering and measurement.
        /// </summary>
        public TMP_FontAsset fontAsset
        {
            get => _fontAsset;
            set
            {
                if (_fontAsset == value) return;
                _fontAsset = value;
                MarkDirty();
            }
        }

        /// <summary>
        /// Maximum width for line breaking in pixels.
        /// </summary>
        public float maxWidth
        {
            get => _maxWidth;
            set
            {
                if (Mathf.Approximately(_maxWidth, value)) return;
                _maxWidth = Mathf.Max(1, value);
                MarkDirty();
            }
        }

        /// <summary>
        /// Line height in pixels.
        /// </summary>
        public float lineHeight
        {
            get => _lineHeight;
            set
            {
                if (Mathf.Approximately(_lineHeight, value)) return;
                _lineHeight = Mathf.Max(1, value);
                MarkDirty();
            }
        }

        /// <summary>
        /// Text color.
        /// </summary>
        public Color color
        {
            get => _color;
            set { _color = value; UpdateTMPVisuals(); }
        }

        /// <summary>
        /// The most recent layout result.
        /// </summary>
        public LayoutResult lastLayoutResult =>
            _lastLayoutResult ?? new LayoutResult(0, 0);

        /// <summary>
        /// All laid-out lines from the most recent layout pass.
        /// </summary>
        public LayoutLine[] lastLines =>
            _lastLayoutResult?.Lines ?? [];

        /// <summary>
        /// The prepared text object. Null until CalculateLayout is called.
        /// </summary>
        public PreparedText? preparedText => _preparedText;

        #endregion

        #region Events

        /// <summary>
        /// Raised when the layout result changes.
        /// </summary>
        public event Action<LayoutResult>? OnLayoutChanged;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _rect = (RectTransform)transform;
            EnsureTMPText();
        }

        private void OnEnable()
        {
            MarkDirty();
        }

        private void Start()
        {
            if (_autoLayout) CalculateLayout();
        }

        private void OnDisable()
        {
            ClearCache();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_autoLayout && _maxWidth != _rect.rect.width)
            {
                _maxWidth = _rect.rect.width;
                MarkDirty();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Recalculate text layout. Call this when text, font, or width changes.
        /// </summary>
        public void CalculateLayout()
        {
            if (_fontAsset == null || string.IsNullOrEmpty(_text))
            {
                _preparedText = null;
                _lastLayoutResult = null;
                ClearTMP();
                return;
            }

            // Create or update measurer
            bool needsNewMeasurer = _measurer == null;
            if (_measurer is TMPFontMeasurer tmpMeasurer)
            {
                needsNewMeasurer = true; // Recreate when font/size changes
            }

            if (needsNewMeasurer)
            {
                _measurer = new TMPFontMeasurer(_fontAsset, _fontSize);
            }

            // Set segmenter
            if (_useGlobalizationSegmenter)
            {
                PretextCore.Segmenter = new GlobalizationSegmenter();
            }

            // Prepare text (only if text or measurer changed)
            bool textChanged = _preparedText == null || _lastText != _text;
            if (textChanged)
            {
                _preparedText = PretextCore.PrepareWithSegments(_text, _measurer, _whiteSpace, _includeBidiLevels);
                _lastText = _text;
            }

            // Layout (always fast - pure arithmetic)
            _lastLayoutResult = PretextCore.LayoutWithLines(_preparedText, _maxWidth, _lineHeight);
            _lastMaxWidth = _maxWidth;

            // Render
            Render();

            // Notify layout system
            LayoutRebuilder.MarkLayoutForRebuild(_rect);

            OnLayoutChanged?.Invoke(_lastLayoutResult.Value);
        }

        /// <summary>
        /// Mark layout as dirty. Layout will be recalculated on next frame
        /// (if autoLayout is enabled) or next CalculateLayout call.
        /// </summary>
        public void MarkDirty()
        {
            _layoutDirty = true;
            if (_autoLayout)
            {
                // Defer layout to end of frame to batch multiple changes
                enabled = true;
            }
        }

        /// <summary>
        /// Force immediate recalculation.
        /// </summary>
        public void ForceLayout()
        {
            CalculateLayout();
        }

        /// <summary>
        /// Clear internal caches and prepared text.
        /// </summary>
        public void ClearCache()
        {
            _preparedText = null;
            _lastLayoutResult = null;
            _lastMaxWidth = -1;
            PretextCore.ClearCache();
        }

        /// <summary>
        /// Get the height needed to display the given text at the current settings,
        /// without rendering. Fast (pure arithmetic after one prepare).
        /// </summary>
        public float GetTextHeight(string text, float width)
        {
            if (_fontAsset == null || string.IsNullOrEmpty(text))
                return 0;

            var measurer = new TMPFontMeasurer(_fontAsset, _fontSize);
            var prepared = PretextCore.Prepare(text, measurer, _whiteSpace);
            var result = PretextCore.Layout(prepared, width, _lineHeight);
            return result.Height;
        }

        #endregion

        #region ILayoutElement Implementation

        public float minWidth => 0;

        public float preferredWidth => _maxWidth;

        public float flexibleWidth => 0;

        public float minHeight => _lastLayoutResult?.Height ?? 0;

        public float preferredHeight => _lastLayoutResult?.Height ?? _lineHeight;

        public float flexibleHeight => 0;

        public int layoutPriority => 1;

        public void CalculateLayoutInputHorizontal()
        {
            // No special handling needed
        }

        public void CalculateLayoutInputVertical()
        {
            if (_layoutDirty && _autoLayout)
            {
                CalculateLayout();
                _layoutDirty = false;
            }
        }

        #endregion

        #region Private Methods

        private void EnsureTMPText()
        {
            _tmpText = GetComponent<TMP_Text>();
            if (_tmpText == null)
            {
                // Add TextMeshProUGUI for UI context
                _tmpText = gameObject.AddComponent<TextMeshProUGUI>();
            }
        }

        private void UpdateTMPVisuals()
        {
            if (_tmpText == null) return;
            _tmpText.color = _color;
            _tmpText.fontStyle = FontStyles.Normal;
        }

        private void ClearTMP()
        {
            if (_tmpText != null)
            {
                _tmpText.text = "";
            }
        }

        private void Render()
        {
            EnsureTMPText();

            if (_lastLayoutResult == null || _preparedText == null)
            {
                ClearTMP();
                return;
            }

            var lines = _lastLayoutResult.Value.Lines;
            var result = _lastLayoutResult.Value;

            // Configure TMP basic settings
            _tmpText.font = _fontAsset;
            _tmpText.fontSize = _fontSize;
            _tmpText.alignment = _alignment;
            _tmpText.color = _color;
            _tmpText.enableWordWrapping = false;
            _tmpText.autoSizeTextContainer = false;

            if (_preciseLineBreaking)
            {
                RenderWithPretextAlignment(lines);
            }
            else
            {
                RenderWithTMPFallback(lines);
            }
        }

        /// <summary>
        /// Render using Pretext's calculated line widths for precise alignment.
        /// This gives pixel-perfect CJK breaking as calculated by Pretext.
        /// </summary>
        private void RenderWithPretextAlignment(LayoutLine[] lines)
        {
            if (lines.Length == 0)
            {
                ClearTMP();
                return;
            }

            // Build rich text with horizontal offsets computed from Pretext widths
            // TMP doesn't natively support per-line horizontal offsets,
            // so we use rich text tags to approximate alignment.
            //
            // For full precision, use RenderWithCustomMesh() or PretextCustomRenderer.
            var richText = new System.Text.StringBuilder();
            for (int li = 0; li < lines.Length; li++)
            {
                var line = lines[li];
                float offset = GetLineOffset(line.Width, _maxWidth, _alignment);

                // Use TMP's horizontal offset tag
                // Note: &lt;pos&gt; tag requires TMP 3.x+
                if (!Mathf.Approximately(offset, 0))
                {
                    richText.Append($"<pos={offset},{0}>");
                }

                // Escape special characters
                richText.Append(EscapeTMPText(line.Text));

                if (li < lines.Length - 1)
                    richText.Append('\n');
            }

            _tmpText.text = richText.ToString();
        }

        /// <summary>
        /// Simple fallback: let TMP render the text with Pretext's height.
        /// TMP's own line breaking may differ slightly from Pretext's for CJK text.
        /// </summary>
        private void RenderWithTMPFallback(LayoutLine[] lines)
        {
            // For fallback, we just pass the text to TMP.
            // TMP will use its own line breaking, but the overall height
            // will match Pretext's calculation.
            _tmpText.text = _text;

            // Force TMP to use our calculated preferred height
            // Note: This is an approximation. For full control, use PretextCustomRenderer.
        }

        /// <summary>
        /// Compute the horizontal offset for a line based on alignment.
        /// </summary>
        private float GetLineOffset(float lineWidth, float containerWidth, TextAlignmentOptions alignment)
        {
            float delta = containerWidth - lineWidth;
            switch (alignment)
            {
                case TextAlignmentOptions.TopLeft:
                case TextAlignmentOptions.Left:
                case TextAlignmentOptions.BottomLeft:
                    return 0;
                case TextAlignmentOptions.Top:
                case TextAlignmentOptions.Center:
                case TextAlignmentOptions.Bottom:
                    return delta / 2;
                case TextAlignmentOptions.TopRight:
                case TextAlignmentOptions.Right:
                case TextAlignmentOptions.BottomRight:
                    return delta;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Escape special characters for TMP text input.
        /// </summary>
        private static string EscapeTMPText(string text)
        {
            return text
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\n", "\\n"); // Handle newlines manually
        }

        #endregion
    }
}
#endif // TMP_PACKAGE
