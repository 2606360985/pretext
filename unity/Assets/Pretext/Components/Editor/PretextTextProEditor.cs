// Custom Inspector for PretextTextPro with live layout preview.
// Place in an Editor folder.

#if UNITY_EDITOR && TMP_PACKAGE
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Pretext.Components;

namespace Pretext.Components.Editor
{
    /// <summary>
    /// Custom Inspector for PretextTextPro with live preview and validation.
    /// </summary>
    [CustomEditor(typeof(PretextTextPro))]
    public class PretextTextProEditor : UnityEditor.Editor
    {
        private PretextTextPro _target;
        private bool _showLayoutInfo = true;
        private bool _showPreview = true;

        private static readonly GUIContent LayoutInfoIcon = EditorGUIUtility.IconContent("d_GroupElement Icon");
        private static readonly GUIContent PreviewIcon = EditorGUIUtility.IconContent("d_Animation.EventMarker Icon");

        private void OnEnable()
        {
            _target = (PretextTextPro)target;
        }

        public override void OnInspectorGUI()
        {
            // Draw default inspector but skip the script field
            DrawPropertiesExcluding(serializedObject, "m_Script");

            EditorGUILayout.Space();
            DrawLayoutInfo();
            EditorGUILayout.Space();
            DrawPreview();
            EditorGUILayout.Space();
            DrawValidation();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawLayoutInfo()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _showLayoutInfo = EditorGUILayout.Foldout(_showLayoutInfo, "Layout Info", true);
            if (!_showLayoutInfo)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUI.BeginDisabledGroup(true);

            // Force a layout calculation
            EditorGUI.EndDisabledGroup();

            if (_target.preparedText != null)
            {
                var result = _target.lastLayoutResult;

                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.LabelField("Line Count", result.LineCount.ToString());
                EditorGUILayout.LabelField("Total Height", $"{result.Height:F1}px");
                EditorGUILayout.LabelField("Max Width", $"{_target.maxWidth:F1}px");
                EditorGUILayout.LabelField("Line Height", $"{_target.lineHeight:F1}px");
                EditorGUILayout.LabelField("Segments Prepared", _target.preparedText.Widths.Length.ToString());
                EditorGUILayout.LabelField("Bidi Levels", _target.preparedText.SegLevels?.Length.ToString() ?? "N/A");
                EditorGUI.EndDisabledGroup();

                // Show per-line info
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Lines:", EditorStyles.boldLabel);

                foreach (var line in _target.lastLines)
                {
                    string preview = line.Text.Length > 40
                        ? line.Text[..40] + "..."
                        : line.Text;
                    EditorGUILayout.LabelField($"  [{line.Width:F1}px] \"{preview}\"");
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No layout data yet. Enter Play Mode or click Calculate Layout.",
                    MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Calculate Layout"))
            {
                _target.ForceLayout();
                EditorUtility.SetDirty(_target);
            }
            if (GUILayout.Button("Clear Cache"))
            {
                _target.ClearCache();
                EditorUtility.SetDirty(_target);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawPreview()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _showPreview = EditorGUILayout.Foldout(_showPreview, "Preview", true);
            if (!_showPreview)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.HelpBox(
                "Text preview is shown in Game View during Play Mode.\n" +
                "Enable Auto Layout for live updates.",
                MessageType.None);

            EditorGUILayout.Space(4);

            // Color picker for text
            EditorGUI.BeginChangeCheck();
            var colorRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(colorRect, new GUIContent("Preview Color"));
            colorRect.x += EditorGUIUtility.labelWidth;
            colorRect.width -= EditorGUIUtility.labelWidth;
            Color newColor = EditorGUI.ColorField(colorRect, _target.color);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_target, "Change PretextTextPro Color");
                _target.color = newColor;
                _target.ForceLayout();
                EditorUtility.SetDirty(_target);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawValidation()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            bool hasIssues = false;

            if (_target.fontAsset == null)
            {
                EditorGUILayout.HelpBox("Font Asset is required. Assign a TextMeshPro Font Asset.", MessageType.Warning);
                hasIssues = true;
            }

            if (_target.maxWidth <= 0)
            {
                EditorGUILayout.HelpBox("Max Width must be greater than 0.", MessageType.Warning);
                hasIssues = true;
            }

            if (_target.lineHeight < 1)
            {
                EditorGUILayout.HelpBox("Line Height must be at least 1.", MessageType.Warning);
                hasIssues = true;
            }

            if (!hasIssues)
            {
                EditorGUILayout.HelpBox("No issues detected.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }
    }

    /// <summary>
    /// Validation editor for PretextTextPro scene-wide checks.
    /// </summary>
    [InitializeOnLoad]
    public class PretextTextProSceneValidation
    {
        static PretextTextProSceneValidation()
        {
            // Register scene validation
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpening += (path, mode) => { };
        }
    }
}
#endif // UNITY_EDITOR && TMP_PACKAGE
