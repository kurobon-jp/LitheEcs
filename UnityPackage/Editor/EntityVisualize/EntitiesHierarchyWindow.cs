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
        private IList<TreeViewItemData<EntityDiagnostics>> _rootItems;
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
                var entity = _treeView.GetItemDataForIndex<EntityDiagnostics>(i);
                e.Q<Label>().text = FormatEntity(entity);
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

        private string FormatEntity(in EntityDiagnostics entity)
        {
            if (_snapshot == null) return entity.ToString();
            _entityTextBuilder.Clear();
            _snapshot.AppendFormattedEntity(_entityTextBuilder, entity);
            return _entityTextBuilder.ToString();
        }

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

            _rootItems ??= new List<TreeViewItemData<EntityDiagnostics>>();

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
            foreach (var entity in _snapshot.Entities)
            {
                if (!MatchesSearch(entity, searchText)) continue;
                var itemId = entity.Entity.Index;
                _rootItems.Add(new TreeViewItemData<EntityDiagnostics>(itemId, entity));
                if (entity.Entity == selectedEntity) selectedItemId = itemId;
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

        [MenuItem("Window/LitheEcs/Entities Hierarchy")]
        private static void ShowWindow()
        {
            GetWindow<EntitiesHierarchyWindow>("Entities Hierarchy");
        }
    }
}
