using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace LitheEcs.Unity.EntityVisualize.Editor
{
    internal sealed class EntitiesHierarchyWindow : EditorWindow
    {
        private TreeView _treeView;
        private IList<TreeViewItemData<object>> _rootItems;
        private readonly Dictionary<int, ArchetypeGroup> _groups = new();
        private readonly List<ArchetypeGroup> _orderedGroups = new();
        private List<int> _selectionIds;
        private World _snapshotWorld;
        private int _snapshotStructuralVersion = -1;
        private bool _filterDirty = true;
        private ToolbarMenu _toolbarMenu;
        private ToolbarSearchField _searchField;
        private World _selectedWorld;
        private EntityListDiagnosticsSnapshot _snapshot;

        private readonly StringBuilder _entityTextBuilder = new(256);

        [SerializeField] private string _searchText;

        internal EntityDiagnostics SelectedEntity { get; private set; }

        private void OnEnable()
        {
            EntityVisualizer.OnRegistered -= OnWorldRegistered;
            EntityVisualizer.OnRegistered += OnWorldRegistered;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EntityVisualizer.OnRegistered -= OnWorldRegistered;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void CreateGUI()
        {
            _treeView = new TreeView
            {
                viewDataKey = "tree-view",
                focusable = true,
                makeItem = () =>
                {
                    var label = new Label();
                    var doubleClickable = new Clickable(OnDoubleClick);
                    doubleClickable.activators.Clear();
                    doubleClickable.activators.Add(new ManipulatorActivationFilter
                        { button = MouseButton.LeftMouse, clickCount = 2 });
                    label.AddManipulator(doubleClickable);
                    return label;
                }
            };
            _treeView.bindItem = (e, i) =>
            {
                var item = _treeView.GetItemDataForIndex<object>(i);
                e.Q<Label>().text = item is ArchetypeGroup group
                    ? $"{group.Label} ({group.Entities.Count})"
                    : FormatEntityId((EntityDiagnostics)item);
            };
            _treeView.selectionChanged += OnSelectionChanged;

            var toolbar = new Toolbar();
            _toolbarMenu = new ToolbarMenu();
            _toolbarMenu.text = "World";
            _toolbarMenu.variant = ToolbarMenu.Variant.Popup;
            toolbar.Add(_toolbarMenu);
            _searchField = new ToolbarSearchField();
            _searchField.RegisterValueChangedCallback(x => OnSearchTextChanged(x.newValue));
            _searchField.value = _searchText;
            toolbar.Add(_searchField);
            rootVisualElement.Add(toolbar);
            rootVisualElement.Add(_treeView);
            if (EditorApplication.isPlaying)
            {
                OnPlayEditor();
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    EditorApplication.delayCall += OnPlayEditor;
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    OnStopEditor();
                    EntityVisualizer.Clear();
                    break;
            }
        }

        private void OnPlayEditor()
        {
            if (_toolbarMenu == null) return;
            _toolbarMenu.menu.ClearItems();
            foreach (var pair in EntityVisualizer.Worlds)
            {
                OnWorldRegistered(pair.Key, pair.Value);
            }
        }

        private void OnWorldRegistered(string name, World world)
        {
            if (_toolbarMenu == null) return;
            var status = DropdownMenuAction.Status.Normal;
            if (_toolbarMenu.menu.MenuItems().Count == 0)
            {
                status = DropdownMenuAction.Status.Checked;
                _selectedWorld = world;
            }

            _toolbarMenu.menu.AppendAction(name, _ => { OnSwitchWorld(world); }, status: status);
        }

        private void OnDoubleClick()
        {
            var inspectorWindowType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.InspectorWindow");
            var inspectorWindow = GetWindow(inspectorWindowType);
            inspectorWindow.Focus();
        }

        private void OnSearchTextChanged(string text)
        {
            _searchText = text;
            _filterDirty = true;
        }

        private void OnSelectionChanged(IEnumerable<object> selections)
        {
            foreach (var selection in selections)
            {
                if (selection is not EntityDiagnostics entity) continue;
                OnEntitySelected(entity);
                break;
            }
        }

        private void OnEntitySelected(EntityDiagnostics entity)
        {
            SelectedEntity = entity;
            Selection.activeObject = !entity.Entity.IsAlive || !EditorApplication.isPlaying ? null : this;
            EditorUtility.SetDirty(this);
        }

        private static string FormatEntityId(in EntityDiagnostics entity) =>
            $"Entity: {entity.Entity.Index}";

        private bool MatchesSearch(in EntityDiagnostics entity, string searchText)
        {
            if (string.IsNullOrEmpty(searchText)) return true;
            _entityTextBuilder.Clear();
            _snapshot.AppendFormattedEntity(_entityTextBuilder, entity);
            return ContainsOrdinalIgnoreCase(_entityTextBuilder, searchText);
        }

        private static bool ContainsOrdinalIgnoreCase(StringBuilder text, string value)
        {
            if (value.Length > text.Length) return false;
            var lastStartIndex = text.Length - value.Length;
            for (var startIndex = 0; startIndex <= lastStartIndex; startIndex++)
            {
                var matched = true;
                for (var valueIndex = 0; valueIndex < value.Length; valueIndex++)
                {
                    if (char.ToUpperInvariant(text[startIndex + valueIndex]) ==
                        char.ToUpperInvariant(value[valueIndex])) continue;
                    matched = false;
                    break;
                }

                if (matched) return true;
            }

            return false;
        }

        private void Update()
        {
            if (EntityVisualizer.Worlds.Count == 0 || !EditorApplication.isPlaying ||
                _treeView == null || _selectedWorld == null) return;

            _rootItems ??= new List<TreeViewItemData<object>>();

            var structuralVersion = _selectedWorld.StructuralVersion;
            if (_snapshot == null || !ReferenceEquals(_snapshotWorld, _selectedWorld) ||
                _snapshotStructuralVersion != structuralVersion)
            {
                Profiler.BeginSample("## CreateEntityDiagnosticsSnapshot");
                _snapshot = _selectedWorld.CreateEntityDiagnosticsSnapshot();
                Profiler.EndSample();
                _snapshotWorld = _selectedWorld;
                _snapshotStructuralVersion = structuralVersion;
                _filterDirty = true;
            }

            if (!_filterDirty) return;

            _rootItems.Clear();
            var searchText = _searchText?.Trim();
            var selectedEntity = SelectedEntity.Entity;
            var selectedItemId = -1;
            _groups.Clear();
            foreach (var entity in _snapshot.Entities)
            {
                if (!MatchesSearch(entity, searchText)) continue;
                if (!_groups.TryGetValue(entity.ArchetypeIndex, out var group))
                {
                    group = new ArchetypeGroup(entity.ArchetypeIndex, FormatArchetype(entity));
                    _groups.Add(entity.ArchetypeIndex, group);
                }
                group.Entities.Add(entity);
            }

            _orderedGroups.Clear();
            _orderedGroups.AddRange(_groups.Values);
            _orderedGroups.Sort(static (left, right) =>
                left.ArchetypeIndex.CompareTo(right.ArchetypeIndex));
            foreach (var group in _orderedGroups)
            {
                var children = new List<TreeViewItemData<object>>(group.Entities.Count);
                foreach (var entity in group.Entities)
                {
                    var itemId = entity.Entity.Index;
                    children.Add(new TreeViewItemData<object>(itemId, entity));
                    if (entity.Entity == selectedEntity) selectedItemId = itemId;
                }
                _rootItems.Add(new TreeViewItemData<object>(group.TreeId, group, children));
            }

            _treeView.SetRootItems(_rootItems);
            _treeView.RefreshItems();
            if (selectedItemId >= 0)
            {
                if (_selectionIds == null) _selectionIds = new List<int>(1);
                _selectionIds.Clear();
                _selectionIds.Add(selectedItemId);
                _treeView.SetSelectionByIdWithoutNotify(_selectionIds);
            }
            else if (selectedEntity.World != null &&
                     (!ReferenceEquals(selectedEntity.World, _selectedWorld) || !selectedEntity.IsAlive))
            {
                SelectedEntity = default;
                if (Selection.activeObject == this) Selection.activeObject = null;
            }

            _filterDirty = false;
        }

        private void OnStopEditor()
        {
            Selection.activeObject = null;
            _snapshot = null;
            _snapshotWorld = null;
            _selectedWorld = null;
            _snapshotStructuralVersion = -1;
            _filterDirty = true;
            if (_rootItems == null) return;
            _rootItems?.Clear();
            if (_treeView == null) return;
            _treeView?.ClearSelection();
            _treeView?.CollapseAll();
            _treeView?.SetRootItems(_rootItems);
            _treeView?.Rebuild();
        }

        private void OnDestroy()
        {
            OnStopEditor();
        }

        private void OnSwitchWorld(World world)
        {
            _selectedWorld = world;
            _filterDirty = true;
        }

        private string FormatArchetype(in EntityDiagnostics entity)
        {
            if (entity.ArchetypeIndex < 0) return "Archetype: <Empty>";
            if (!string.IsNullOrEmpty(entity.ArchetypeAlias)) return entity.ArchetypeAlias;
            var typeIds = _snapshot.GetComponentTypeIds(entity);
            _entityTextBuilder.Clear();
            _entityTextBuilder.Append("Archetype: ");
            for (var i = 0; i < typeIds.Length; i++)
            {
                if (i != 0) _entityTextBuilder.Append(", ");
                AppendTypeName(_entityTextBuilder, _snapshot.GetComponentType(typeIds[i]));
            }
            return _entityTextBuilder.ToString();
        }

        private static void AppendTypeName(StringBuilder builder, Type type)
        {
            if (!type.IsGenericType)
            {
                builder.Append(type.Name);
                return;
            }

            var name = type.Name;
            var arityStart = name.IndexOf('`');
            builder.Append(arityStart >= 0 ? name.Substring(0, arityStart) : name);
            builder.Append('<');
            var arguments = type.GetGenericArguments();
            for (var i = 0; i < arguments.Length; i++)
            {
                if (i != 0) builder.Append(", ");
                AppendTypeName(builder, arguments[i]);
            }
            builder.Append('>');
        }

        private sealed class ArchetypeGroup
        {
            internal readonly int ArchetypeIndex;
            internal readonly int TreeId;
            internal readonly string Label;
            internal readonly List<EntityDiagnostics> Entities = new();

            internal ArchetypeGroup(int archetypeIndex, string label)
            {
                ArchetypeIndex = archetypeIndex;
                // Keep group IDs disjoint from non-negative Entity indices.
                TreeId = int.MinValue + archetypeIndex + 1;
                Label = label;
            }
        }

        [MenuItem("Window/LitheEcs/Entities Hierarchy")]
        private static void ShowWindow()
        {
            GetWindow<EntitiesHierarchyWindow>("Entities Hierarchy");
        }
    }
}
