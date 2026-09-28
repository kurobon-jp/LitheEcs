using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LitheEcs.Unity.EntityVisualize.Editor
{
    [CustomEditor(typeof(EntitiesHierarchyWindow))]
    internal class EntityInspector : UnityEditor.Editor
    {
        private static readonly Dictionary<Type, FieldInfo[]> ComponentFields = new();
        private static readonly Dictionary<Type, string> TypeNames = new();
        private static readonly Dictionary<Type, Color> TypeColors = new();
        private static readonly Dictionary<Type, bool> FoldoutStates = new();
        private static readonly Dictionary<string, bool> NestedFoldoutStates = new();

        private const int MaximumNestingDepth = 5;

        private EntitiesHierarchyWindow _window;
        private EntityDiagnostics _entity;
        private EntityDiagnosticsSnapshot _snapshot;

        protected override void OnHeaderGUI()
        {
            _window = (EntitiesHierarchyWindow)target;
            _entity = _window.SelectedEntity;
            if (!EditorApplication.isPlaying || !_entity.Entity.IsAlive)
            {
                _snapshot = null;
                return;
            }

            _snapshot = _entity.Entity.World.CreateEntityDiagnosticsSnapshot(_entity.Entity);
            if (!_snapshot.IsAlive) return;

            DrawCustomHeader($"Entity {_entity.Entity.Index}:{_entity.Entity.Version}");
            DrawComponents();
            GUILayout.Space(10);
        }

        public override bool RequiresConstantRepaint()
        {
            if (!EditorApplication.isPlaying) return false;
            var window = _window != null ? _window : target as EntitiesHierarchyWindow;
            return window != null && window.SelectedEntity.Entity.IsAlive;
        }

        public override void OnInspectorGUI()
        {
            // Addressablesの影響を受けるのでOnInspectorGUIでは何も描画しない
        }

        private void DrawCustomHeader(string title)
        {
            var rect = GUILayoutUtility.GetRect(1, 48);
            rect.xMin = 0;
            rect.xMax += 4;
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 24,
                padding = new RectOffset(8, 8, 8, 8)
            };
            var labelRect = new Rect(rect.x + 6, rect.y + 2, rect.width - 40, rect.height);
            EditorGUI.LabelField(labelRect, title, style);
        }

        private void DrawComponents()
        {
            if (_snapshot == null) return;
            foreach (var component in _snapshot.Components)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                var fields = GetComponentFields(component.ComponentType);
                if (DrawComponentHeader(component.ComponentType, fields.Length != 0))
                {
                    for (var i = 0; i < fields.Length; i++)
                    {
                        var field = fields[i];
                        var path = $"{component.ComponentType.FullName}.{field.Name}";
                        DrawValue(ObjectNames.NicifyVariableName(field.Name), field.GetValue(component.Value),
                            field.FieldType, path, 0);
                    }
                }

                EditorGUILayout.EndVertical();
            }
        }

        private static bool DrawComponentHeader(Type componentType, bool hasContents)
        {
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var colorRect = new Rect(rect.x, rect.y, 4, rect.height);
            EditorGUI.DrawRect(colorRect, GetTypeColor(componentType));
            rect.xMin += 9;

            if (!hasContents)
            {
                EditorGUI.LabelField(rect, GetTypeName(componentType), EditorStyles.boldLabel);
                return false;
            }

            if (!FoldoutStates.TryGetValue(componentType, out var expanded)) expanded = true;
            var nextExpanded = EditorGUI.Foldout(rect, expanded, GetTypeName(componentType), true,
                EditorStyles.foldoutHeader);
            if (nextExpanded != expanded) FoldoutStates[componentType] = nextExpanded;
            return nextExpanded;
        }

        private static Color GetTypeColor(Type type)
        {
            if (TypeColors.TryGetValue(type, out var color)) return color;

            var name = type.FullName ?? type.Name;
            var hash = 2166136261u;
            for (var i = 0; i < name.Length; i++)
            {
                hash ^= name[i];
                hash *= 16777619u;
            }

            var hue = hash % 360u / 360f;
            color = Color.HSVToRGB(hue, 0.65f, EditorGUIUtility.isProSkin ? 0.9f : 0.7f);
            TypeColors.Add(type, color);
            return color;
        }

        private static FieldInfo[] GetComponentFields(Type componentType)
        {
            if (ComponentFields.TryGetValue(componentType, out var fields)) return fields;
            fields = componentType.GetFields(BindingFlags.Instance | BindingFlags.Public);
            ComponentFields.Add(componentType, fields);
            return fields;
        }

        private static string GetTypeName(Type type)
        {
            if (TypeNames.TryGetValue(type, out var typeName)) return typeName;
            if (!type.IsGenericType)
            {
                typeName = type.Name;
            }
            else
            {
                var name = type.Name;
                var genericMarkerIndex = name.IndexOf('`');
                if (genericMarkerIndex >= 0) name = name.Substring(0, genericMarkerIndex);
                var arguments = type.GetGenericArguments();
                var argumentNames = new string[arguments.Length];
                for (var i = 0; i < arguments.Length; i++) argumentNames[i] = GetTypeName(arguments[i]);
                typeName = $"{name}<{string.Join(", ", argumentNames)}>";
            }

            TypeNames.Add(type, typeName);
            return typeName;
        }

        private static void DrawValue(string label, object value, Type valueType, string path, int depth)
        {
            if (typeof(UnityEngine.Object).IsAssignableFrom(valueType))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(label, value as UnityEngine.Object, valueType, true);
                }

                return;
            }

            if (value == null)
            {
                EditorGUILayout.LabelField(label, "null");
                return;
            }

            if (valueType == typeof(LayerMask))
            {
                EditorGUILayout.LabelField(label, ((LayerMask)value).value.ToString());
                return;
            }

            var runtimeType = value.GetType();
            var fields = GetComponentFields(runtimeType);
            if (depth >= MaximumNestingDepth || IsLeafType(runtimeType) || fields.Length == 0)
            {
                EditorGUILayout.LabelField(label, value.ToString());
                return;
            }

            var expanded = NestedFoldoutStates.GetValueOrDefault(path, true);
            var nextExpanded = EditorGUILayout.Foldout(expanded, $"{label} ({GetTypeName(runtimeType)})", true);
            if (nextExpanded != expanded) NestedFoldoutStates[path] = nextExpanded;
            if (!nextExpanded) return;

            EditorGUI.indentLevel++;
            for (var i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                DrawValue(ObjectNames.NicifyVariableName(field.Name), field.GetValue(value), field.FieldType,
                    $"{path}.{field.Name}", depth + 1);
            }

            EditorGUI.indentLevel--;
        }

        private static bool IsLeafType(Type type)
        {
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
                   type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Vector2) ||
                   type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Quaternion) ||
                   type == typeof(Color) || type == typeof(Color32) || type == typeof(Rect) ||
                   type == typeof(Bounds);
        }
    }
}
