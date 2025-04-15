/*
 * MakingVibe/MainWindow.TreeView.cs
 * Partial class file for TreeView related logic.
 * Handles loading, node expansion/collapse, selection, context menu, and related UI updates.
 */
using MakingVibe.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MakingVibe.Services;
using Brushes = System.Windows.Media.Brushes;
using CheckBox = System.Windows.Controls.CheckBox;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using WinForms = System.Windows.Forms; // Correct alias

namespace MakingVibe
{
    public partial class MainWindow // : Window // Already declared in main part
    {
        // --- UI Event Handlers: TreeView Panel ---

        // btnSelectRoot_Click is defined in MainWindow.xaml.cs

        private void btnClearSelection_Click(object sender, RoutedEventArgs e)
        {
            ClearAllSelectionsUI(); // Call the method defined below
        }

        // --- UI Event Handlers: TreeView Events ---

        private async void treeViewFiles_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            txtFileContent.Text = string.Empty; // Clear preview initially
            FileSystemItem? selectedFsi = null;

            if (e.NewValue is TreeViewItem { Tag: FileSystemItem fsi } selectedTvi)
            {
                selectedFsi = fsi;
            }

            await ShowPreviewAsync(selectedFsi); // Call preview update (method in PreviewTab partial)
            UpdateStatusBarAndButtonStates(); // Update button states based on new selection
        }

        private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem { Tag: FileSystemItem fsi } treeViewItem && fsi.IsDirectory)
            {
                // Check if it contains the placeholder "Cargando..."
                if (treeViewItem.Items.Count == 1 && treeViewItem.Items[0] is string loadingText && loadingText == "Cargando...")
                {
                    treeViewItem.Items.Clear(); // Remove placeholder
                    LoadChildrenUI(treeViewItem, fsi.Path, _activeFileExtensionsFilter); // Load actual children with filter
                }
                // Note: UpdateStatusBarAndButtonStates is likely called within LoadChildrenUI or its callers if needed.
                // If not, uncomment the line below.
                // UpdateStatusBarAndButtonStates();
            }
            e.Handled = true; // Prevent event bubbling
        }


        private void TreeViewItem_Collapsed(object sender, RoutedEventArgs e)
        {
            // No specific action needed on collapse by default, but placeholder is there
            // If (sender is TreeViewItem { Tag: FileSystemItem fsi } treeViewItem && fsi.IsDirectory)
            // {
            //     UpdateStatusBarAndButtonStates();
            // }
            e.Handled = true;
        }

        // --- Context Menu Logic ---
        private void TreeViewContextMenu_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            bool isItemSelected = treeViewFiles.SelectedItem != null;
            FileSystemItem? selectedFsi = (treeViewFiles.SelectedItem as TreeViewItem)?.Tag as FileSystemItem;
            bool isTextFileSelected = selectedFsi != null && !selectedFsi.IsDirectory && FileHelper.IsTextFile(selectedFsi.Path);

            // Check conditions for AI copy for context menu separately
             bool canCopyAnything = (selectedItems.Count > 0 && selectedItems.Any(item => item.IsDirectory || FileHelper.IsTextFile(item.Path)))
                                 || !string.IsNullOrWhiteSpace(txtMainPrompt.Text)
                                 || Fragments.Any(frag => IsFragmentSelected(frag)); // Check if any fragment is actually selected
            ctxCopyText.IsEnabled = canCopyAnything;

            ctxRename.IsEnabled = selectedItems.Count == 1 && selectedFsi != null;
            ctxDelete.IsEnabled = selectedItems.Count > 0;
            ctxRefreshNode.IsEnabled = isItemSelected && selectedFsi != null && selectedFsi.IsDirectory;
            ctxRefreshAll.IsEnabled = !string.IsNullOrEmpty(rootPath); // Tied to main refresh button
        }

        // Helper method to check if a fragment is selected in the ListView
        private bool IsFragmentSelected(TextFragment fragment)
        {
            if (!listViewFragments.IsVisible) return false; // Can't check if not visible

            try
            {
                if (listViewFragments.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                {
                    var container = listViewFragments.ItemContainerGenerator.ContainerFromItem(fragment) as System.Windows.Controls.ListViewItem;
                    if (container != null)
                    {
                        var checkBox = FindVisualChild<CheckBox>(container);
                        return checkBox != null && checkBox.IsChecked == true;
                    }
                }
            }
            catch (InvalidOperationException ioex)
            {
                Debug.WriteLine($"Warning: Could not check fragment selection state reliably: {ioex.Message}");
            }
            return false; // Default to false if container not ready or error
        }


        private void CtxRefreshNode_Click(object sender, RoutedEventArgs e)
        {
            if (treeViewFiles.SelectedItem is TreeViewItem { Tag: FileSystemItem fsi } selectedTreeViewItem && fsi.IsDirectory)
            {
                RefreshNodeUI(selectedTreeViewItem, fsi);
            }
        }

        // --- Core UI Logic: Tree Loading & Manipulation ---

        /// <summary>
        /// Clears and reloads the entire TreeView UI based on the specified root path,
        /// applying the currently active file extension filters. Also updates the FileFilters list.
        /// </summary>
        private void LoadDirectoryTreeUI(string path)
        {
            treeViewFiles.Items.Clear();
            pathToTreeViewItemMap.Clear();
            ClearAllSelectionsUI(false); // Clear selection without updating status yet

            var foundExtensions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (!_fileSystemService.DirectoryExists(path))
            {
                MessageBox.Show($"El directorio raíz especificado no existe o no es accesible:\n{path}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                rootPath = null;
                txtCurrentPath.Text = "Ruta actual: (Seleccione una carpeta raíz)";
                _settingsService.SaveLastRootPath(null);
                FileFilters.Clear(); // Method in FiltersTab partial class
                UpdateStatusBarAndButtonStates("Error al cargar el directorio raíz."); // Update status *after* clearing
                return;
            }

            UpdateStatusBarAndButtonStates($"Cargando: {path}..."); // Indicate loading
            try
            {
                var rootDirectoryInfo = new DirectoryInfo(path);
                var rootFsi = new FileSystemItem
                {
                    Name = rootDirectoryInfo.Name,
                    Path = rootDirectoryInfo.FullName,
                    Type = "Directorio Raíz",
                    IsDirectory = true,
                    LineCount = null
                };

                var rootTreeViewItem = CreateTreeViewItemUI(rootFsi);
                treeViewFiles.Items.Add(rootTreeViewItem);
                pathToTreeViewItemMap[rootFsi.Path] = rootTreeViewItem;

                // Load initial children UI, applying filter and collecting extensions
                LoadChildrenUI(rootTreeViewItem, rootFsi.Path, _activeFileExtensionsFilter, foundExtensions);
                rootTreeViewItem.IsExpanded = true;

                // Update the filter UI (method in FiltersTab partial class)
                UpdateFileFiltersUI(foundExtensions); // This will trigger its own status update if needed

                // Update status bar after loading is complete
                UpdateStatusBarAndButtonStates("Listo.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error inesperado al construir el árbol de directorios:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                FileFilters.Clear();
                UpdateStatusBarAndButtonStates("Error al mostrar el árbol.");
                Debug.WriteLine($"Error LoadDirectoryTreeUI: {ex}");
            }
            // No finally block needed for status update as it's handled in try/catch or by UpdateFileFiltersUI
        }


        /// <summary>
        /// Loads the children UI (TreeViewItems) for a given parent TreeViewItem,
        /// applying file extension filters and collecting found extensions.
        /// </summary>
        private void LoadChildrenUI(TreeViewItem parentTreeViewItem, string directoryPath, HashSet<string> allowedExtensions, Dictionary<string, int>? foundExtensions = null)
        {
            IEnumerable<FileSystemItem>? childrenData = null;
            try {
                 childrenData = _fileSystemService.GetDirectoryChildren(directoryPath, allowedExtensions);
            } catch (Exception ex) {
                 Debug.WriteLine($"Error getting children for {directoryPath}: {ex.Message}");
                 parentTreeViewItem.Items.Clear(); // Clear placeholder if any
                 parentTreeViewItem.Items.Add(new TreeViewItem { Header = $"[Error: {ex.Message}]", Foreground = Brushes.Red, IsEnabled = false });
                 return;
            }


            parentTreeViewItem.Items.Clear(); // Clear placeholder before adding children

            if (childrenData == null) // Indicates access denied from service
            {
                parentTreeViewItem.Items.Add(new TreeViewItem { Header = "[Acceso Denegado]", Foreground = Brushes.Gray, IsEnabled = false });
                return;
            }

            var childrenList = childrenData.ToList(); // Materialize the list
            if (!childrenList.Any())
            {
                 // No children, maybe add a placeholder? Optional.
                 // parentTreeViewItem.Items.Add(new TreeViewItem { Header = "[Vacío]", Foreground = Brushes.Gray, IsEnabled = false });
                return; // Exit if no children found
            }

            foreach (var fsi in childrenList)
            {
                var childTreeViewItem = CreateTreeViewItemUI(fsi);
                parentTreeViewItem.Items.Add(childTreeViewItem);
                pathToTreeViewItemMap[fsi.Path] = childTreeViewItem;

                // Collect extensions if the dictionary is provided (only during initial load)
                if (foundExtensions != null && !fsi.IsDirectory)
                {
                    string ext = string.IsNullOrEmpty(fsi.Type) ? "[Sin extensión]" : fsi.Type;
                    foundExtensions.TryGetValue(ext, out int currentCount);
                    foundExtensions[ext] = currentCount + 1;
                }

                // Add placeholder for subdirectories for lazy loading
                if (fsi.IsDirectory)
                {
                     // Check if directory actually has children before adding placeholder?
                     // This adds overhead. Lazy loading handles empty dirs fine.
                    childTreeViewItem.Items.Add("Cargando..."); // Placeholder for lazy loading

                    // Recursively collect ALL extensions for filter list (only during initial load)
                    if (foundExtensions != null)
                    {
                         CollectExtensionsRecursive(fsi.Path, foundExtensions);
                    }
                }
            }

            // Update parent checkbox state after loading children
            if (parentTreeViewItem.Tag is FileSystemItem parentFsi)
            {
                 UpdateParentDirectorySelectionState(parentFsi, childrenList);
            }
        }

        /// <summary>
        /// Recursively scans directories just to collect file extensions for the filter list.
        /// This runs *without* applying the active filter. Only called during initial load.
        /// </summary>
        private void CollectExtensionsRecursive(string directoryPath, Dictionary<string, int> foundExtensions)
        {
            IEnumerable<FileSystemItem>? allChildren = null;
            try {
                 allChildren = _fileSystemService.GetDirectoryChildren(directoryPath); // No filter applied here
            } catch (Exception ex) {
                 Debug.WriteLine($"Error getting children for extension collection in {directoryPath}: {ex.Message}");
                 return; // Skip this directory on error
            }

            if (allChildren == null) return; // Access denied

            foreach (var item in allChildren)
            {
                if (!item.IsDirectory)
                {
                    string ext = string.IsNullOrEmpty(item.Type) ? "[Sin extensión]" : item.Type;
                    foundExtensions.TryGetValue(ext, out int currentCount);
                    foundExtensions[ext] = currentCount + 1;
                }
                else
                {
                    // Recurse into subdirectory
                    CollectExtensionsRecursive(item.Path, foundExtensions);
                }
            }
        }

        /// <summary>
        /// Refreshes a specific node in the TreeView UI, reapplying filters.
        /// </summary>
        private void RefreshNodeUI(TreeViewItem nodeToRefresh, FileSystemItem fsi)
        {
            if (!fsi.IsDirectory) return; // Should only refresh directories

            bool wasExpanded = nodeToRefresh.IsExpanded;
            string? parentPath = _fileSystemService.GetDirectoryName(fsi.Path);

            UpdateStatusBarAndButtonStates($"Refrescando nodo '{fsi.Name}'...");

            // --- Enhanced Cleanup: Remove descendants from map and selection ---
            var pathsToRemove = pathToTreeViewItemMap.Keys
                .Where(k => k.StartsWith(fsi.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .ToList(); // Materialize keys to avoid modification issues during iteration

            foreach (var path in pathsToRemove)
            {
                if (pathToTreeViewItemMap.TryGetValue(path, out var childTvi) && childTvi.Tag is FileSystemItem childFsi)
                {
                    // Remove from selection silently (don't trigger parent updates yet)
                    _isUpdatingParentState = true;
                    selectedItems.Remove(childFsi);
                    _isUpdatingParentState = false;
                }
                pathToTreeViewItemMap.Remove(path);
            }
            // --- End Cleanup ---

            nodeToRefresh.Items.Clear(); // Clear existing items UI
            LoadChildrenUI(nodeToRefresh, fsi.Path, _activeFileExtensionsFilter); // Reload children with current filter
            nodeToRefresh.IsExpanded = wasExpanded; // Restore expanded state

            // Re-sync this node's checkbox state (might have changed if all children got deselected during cleanup)
            if (nodeToRefresh.Header is Grid headerGrid)
            {
                var checkbox = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                if (checkbox != null)
                {
                     // Use the main selection list to determine the correct state
                     bool shouldBeChecked = selectedItems.Contains(fsi);
                     // Update checkbox only if state differs to avoid event loops
                     if (checkbox.IsChecked != shouldBeChecked) {
                         _isUpdatingParentState = true; // Prevent triggering events while syncing UI
                         checkbox.IsChecked = shouldBeChecked;
                         _isUpdatingParentState = false;
                     }
                }
            }

            UpdateStatusBarAndButtonStates($"Nodo '{fsi.Name}' refrescado.");
        }

        /// <summary>
        /// Creates a TreeViewItem UI element for a given FileSystemItem.
        /// </summary>
        private TreeViewItem CreateTreeViewItemUI(FileSystemItem fsi)
        {
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Checkbox
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Type Indicator
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Name
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Line Count

            var checkbox = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
                Tag = fsi,
                // Check if the item is already in the selected list when creating UI
                IsChecked = selectedItems.Contains(fsi)
            };
            // Wire up events *after* setting initial IsChecked state
            checkbox.Checked += Checkbox_Checked;
            checkbox.Unchecked += Checkbox_Unchecked;
            Grid.SetColumn(checkbox, 0);
            headerGrid.Children.Add(checkbox);

            var typeIndicator = new TextBlock
            {
                Text = fsi.IsDirectory ? "[D] " : "[F] ",
                FontWeight = FontWeights.SemiBold,
                Foreground = fsi.IsDirectory ? Brushes.DarkGoldenrod : Brushes.DarkSlateBlue,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(typeIndicator, 1);
            headerGrid.Children.Add(typeIndicator);

            var nameTextBlock = new TextBlock
            {
                Text = fsi.Name,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = fsi.Path, // Show full path on hover
                TextTrimming = TextTrimming.CharacterEllipsis // Ellipsis if too long
            };
            Grid.SetColumn(nameTextBlock, 2);
            headerGrid.Children.Add(nameTextBlock);

            // Display line count only for non-directories where it has a value
            if (!fsi.IsDirectory && fsi.LineCount.HasValue)
            {
                var lineCountTextBlock = new TextBlock
                {
                    Text = $"({fsi.LineCount} lines)",
                    Foreground = Brushes.Gray,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right, // Align to the right of the name column
                    Margin = new Thickness(8, 0, 0, 0) // Add some space before the count
                };
                Grid.SetColumn(lineCountTextBlock, 3);
                headerGrid.Children.Add(lineCountTextBlock);
            }

            var treeViewItem = new TreeViewItem
            {
                Header = headerGrid,
                Tag = fsi // Store the data object in the Tag
            };

            // Add expand/collapse handlers only for directories
            if (fsi.IsDirectory)
            {
                treeViewItem.Expanded += TreeViewItem_Expanded;
                treeViewItem.Collapsed += TreeViewItem_Collapsed;
                // Placeholder added in LoadChildrenUI instead of here
            }

            return treeViewItem;
        }


        // --- Selection Handling (UI Related) ---

        private void Checkbox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingParentState) return; // Prevent re-entrancy
            if (sender is not CheckBox { Tag: FileSystemItem fsi } checkbox) return;

            bool isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            bool added = false;

            _isUpdatingParentState = true; // Lock during modification
            try {
                if (!selectedItems.Contains(fsi)) {
                    selectedItems.Add(fsi);
                    added = true;
                }

                // Apply child selection logic based on modifier key
                if (fsi.IsDirectory) {
                    if (isCtrlPressed) {
                        SelectDirectoryDescendantsRecursive(fsi, true); // Ctrl+Click selects all descendants
                    } else {
                        UpdateDirectoryFilesSelection(fsi, true, _activeFileExtensionsFilter); // Normal click selects child files
                    }
                }
            } finally {
                _isUpdatingParentState = false; // Release lock
            }


            // Update parent state only if this item was newly added or it's a file
            if (added || !fsi.IsDirectory) {
                UpdateParentDirectorySelectionState(fsi); // Update parent checkbox based on children
            }
             UpdateStatusBarAndButtonStates(); // Update counts and button states
        }

        private void Checkbox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingParentState) return; // Prevent re-entrancy
            if (sender is not CheckBox { Tag: FileSystemItem fsi } checkbox) return;

            bool isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            bool removed = false;

            _isUpdatingParentState = true; // Lock during modification
            try {
                removed = selectedItems.Remove(fsi);

                // Apply child deselection logic based on modifier key
                if (fsi.IsDirectory) {
                    if (isCtrlPressed) {
                        SelectDirectoryDescendantsRecursive(fsi, false); // Ctrl+Click deselects all descendants
                    } else {
                        UpdateDirectoryFilesSelection(fsi, false, _activeFileExtensionsFilter); // Normal click deselects child files
                    }
                }
             } finally {
                 _isUpdatingParentState = false; // Release lock
             }

            // Update parent state only if this item was actually removed or it's a file
            if (removed || !fsi.IsDirectory) {
                 UpdateParentDirectorySelectionState(fsi); // Update parent checkbox based on children
            }
             UpdateStatusBarAndButtonStates(); // Update counts and button states
        }


        /// <summary>
        /// Recursively selects or deselects all descendant *files AND directories* found by the service. (Ctrl+Click)
        /// Respects ignored items list from the service. Updates UI checkboxes.
        /// </summary>
        private void SelectDirectoryDescendantsRecursive(FileSystemItem directoryFsi, bool select)
        {
            if (!directoryFsi.IsDirectory) return;

            var descendantsToUpdate = new List<FileSystemItem>();
             // Use the service method that finds *all* descendants (files and dirs), respecting ignores
            _fileSystemService.FindAllDescendantsRecursive(directoryFsi, descendantsToUpdate);

            if (!descendantsToUpdate.Any()) return;

            bool collectionChanged = false;
            // Lock prevents recursive calls to UpdateParentDirectorySelectionState during bulk update
            _isUpdatingParentState = true;
            try
            {
                foreach (var item in descendantsToUpdate)
                {
                    bool currentlySelected = selectedItems.Contains(item);
                    if (select && !currentlySelected) {
                        selectedItems.Add(item);
                        collectionChanged = true;
                    } else if (!select && currentlySelected) {
                        selectedItems.Remove(item);
                        collectionChanged = true;
                    }

                    // Update the checkbox UI if the item exists in the current TreeView map
                    if (pathToTreeViewItemMap.TryGetValue(item.Path, out var descendantTreeViewItem))
                    {
                        if (descendantTreeViewItem.Header is Grid headerGrid)
                        {
                            var cb = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                            // Update UI only if needed to prevent event cycles
                            if (cb != null && cb.IsChecked != select) cb.IsChecked = select;
                        }
                    }
                }
            }
            finally { _isUpdatingParentState = false; } // Release lock

            // No need to update status bar here, handled by caller (Checkbox_Checked/Unchecked)
            // Don't call UpdateParentDirectorySelectionState here; let the initial check/uncheck call handle it once after recursion.
        }


        /// <summary>
        /// Updates the selection state for direct child *files* of a directory, respecting filters. (Simple Click)
        /// Updates UI checkboxes.
        /// </summary>
        private void UpdateDirectoryFilesSelection(FileSystemItem directoryFsi, bool select, HashSet<string> allowedExtensions)
        {
            IEnumerable<FileSystemItem>? children = null;
            try {
                 children = _fileSystemService.GetDirectoryChildren(directoryFsi.Path, allowedExtensions);
            } catch (Exception ex) {
                 Debug.WriteLine($"Error getting children for file selection update in {directoryFsi.Path}: {ex.Message}");
                 return; // Cannot proceed if children cannot be retrieved
            }

            if (children == null) return; // Access denied

            bool collectionChanged = false;
             // Lock prevents recursive calls to UpdateParentDirectorySelectionState during bulk update
            _isUpdatingParentState = true;
            try
            {
                 // Only process direct child FILES that are visible with the current filter
                foreach (var child in children.Where(c => !c.IsDirectory))
                {
                    bool currentlySelected = selectedItems.Contains(child);
                    if (select && !currentlySelected) {
                        selectedItems.Add(child);
                        collectionChanged = true;
                    } else if (!select && currentlySelected) {
                        selectedItems.Remove(child);
                        collectionChanged = true;
                    }

                    // Update the checkbox UI if the item exists in the current TreeView map
                    if (pathToTreeViewItemMap.TryGetValue(child.Path, out var childTreeViewItem))
                    {
                        if (childTreeViewItem.Header is Grid headerGrid)
                        {
                            var cb = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                            // Update UI only if needed to prevent event cycles
                            if (cb != null && cb.IsChecked != select) cb.IsChecked = select;
                        }
                    }
                }
            }
            finally { _isUpdatingParentState = false; } // Release lock

            // No need to update status bar here, handled by caller (Checkbox_Checked/Unchecked)
            // Do not call UpdateParentDirectorySelectionState here directly, let caller handle it.
        }

        /// <summary>
        /// Updates a parent directory's checkbox state based on its *visible* children's selection state.
        /// Recurses up the tree.
        /// </summary>
        /// <param name="childItem">The item whose parent needs checking.</param>
        /// <param name="cachedChildren">Optional: Pre-fetched children of the parent (optimization).</param>
        private void UpdateParentDirectorySelectionState(FileSystemItem childItem, IEnumerable<FileSystemItem>? cachedChildren = null)
        {
            if (_isUpdatingParentState) return; // Prevent re-entrancy during updates

            string? parentPath = _fileSystemService.GetDirectoryName(childItem.Path);
            // Stop recursion if we reach the root or go outside the loaded root
            if (string.IsNullOrEmpty(parentPath) || !parentPath.StartsWith(rootPath ?? "__INVALID__", StringComparison.OrdinalIgnoreCase) || parentPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                // Reached the top-level item or went outside root, no parent UI element to update in the TreeView itself for the actual root.
                // Status bar will be updated by the initial check/uncheck event handler.
                return;
            }


            if (!pathToTreeViewItemMap.TryGetValue(parentPath, out var parentTvi)) {
                 Debug.WriteLine($"Parent TreeViewItem not found in map for path: {parentPath}");
                 return; // Parent UI node not found
            }
            if (parentTvi.Tag is not FileSystemItem parentFsi || !parentFsi.IsDirectory) {
                 Debug.WriteLine($"Parent TreeViewItem tag is not a valid directory FileSystemItem for path: {parentPath}");
                 return; // Parent UI node data is invalid
            }

            // Get the currently VISIBLE children of the parent node using the active filter
            var visibleChildren = cachedChildren?.ToList() // Use cache if provided
                                   ?? _fileSystemService.GetDirectoryChildren(parentPath, _activeFileExtensionsFilter)?.ToList();


            bool shouldBeChecked;
            if (visibleChildren == null || !visibleChildren.Any())
            {
                // If parent directory has no visible children (due to filters or being empty),
                // its selection state should reflect whether ITSELF is selected, not based on children.
                // However, standard behavior is often to uncheck if empty. Let's uncheck.
                 shouldBeChecked = false;
            } else {
                // Parent should be checked if ALL its visible children are checked
                 shouldBeChecked = visibleChildren.All(item => selectedItems.Contains(item));
            }

            // Update the parent's checkbox UI and selection state
            if (parentTvi.Header is Grid headerGrid)
            {
                var parentCheckbox = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                if (parentCheckbox != null)
                {
                    _isUpdatingParentState = true; // Lock before modifying UI and collection
                    try
                    {
                        // Sync checkbox UI if needed
                        if (parentCheckbox.IsChecked != shouldBeChecked) {
                             parentCheckbox.IsChecked = shouldBeChecked;
                        }

                        // Sync parent's state in the selectedItems collection
                        bool parentCurrentlySelected = selectedItems.Contains(parentFsi);
                        if (shouldBeChecked && !parentCurrentlySelected) {
                            selectedItems.Add(parentFsi);
                        } else if (!shouldBeChecked && parentCurrentlySelected) {
                            selectedItems.Remove(parentFsi);
                        }
                    }
                    finally { _isUpdatingParentState = false; } // Release lock
                }
            }

             // Recurse up to the next parent
             UpdateParentDirectorySelectionState(parentFsi);
        }


        /// <summary>
        /// Clears the selection collection and unchecks corresponding checkboxes in the UI.
        /// </summary>
        /// <param name="updateStatus">Whether to update the status bar after clearing.</param>
        private void ClearAllSelectionsUI(bool updateStatus = true) // Added parameter
        {
            if (selectedItems.Count == 0) return;

            // Get paths *before* clearing the collection
            var itemsToUncheckPaths = selectedItems.Select(i => i.Path).ToList();

            _isUpdatingParentState = true; // Prevent Checkbox events during UI update
            try
            {
                selectedItems.Clear(); // Clear the underlying data collection first

                // Now update the UI based on the paths we captured
                foreach (var path in itemsToUncheckPaths)
                {
                    if (pathToTreeViewItemMap.TryGetValue(path, out var treeViewItem))
                    {
                        if (treeViewItem.Header is Grid headerGrid)
                        {
                            var cb = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                            // Only uncheck if it's currently checked
                            if (cb != null && cb.IsChecked == true)
                            {
                                cb.IsChecked = false;
                            }
                        }
                    }
                }
            }
            finally { _isUpdatingParentState = false; } // Release lock

            if (updateStatus) {
                 UpdateStatusBarAndButtonStates("Selección limpiada.");
            }
        }


        // --- TreeView Collapse/Expand All/Current ---

        /// <summary>
        /// Recursively expands or collapses TreeViewItems.
        /// </summary>
        private void CollapseOrExpandNodes(ItemCollection items, bool expand)
        {
            foreach (object? obj in items)
            {
                 // Ensure the item is a TreeViewItem with a Directory Tag
                if (obj is TreeViewItem tvi && tvi.Tag is FileSystemItem { IsDirectory: true } /*fsi*/)
                {
                    // Set the desired state (this might trigger lazy loading if expanding)
                    if (tvi.IsExpanded != expand)
                    {
                        tvi.IsExpanded = expand;
                    }

                    // Recurse ONLY if the node is now expanded and has items (or placeholder)
                    // Checking HasItems prevents infinite loops on empty dirs
                    // We only need to recurse if expanding or if collapsing an already expanded node
                    if (expand && tvi.IsExpanded && tvi.HasItems)
                    {
                        CollapseOrExpandNodes(tvi.Items, expand);
                    }
                    else if (!expand && tvi.HasItems) // If collapsing, always recurse if it has items
                    {
                         CollapseOrExpandNodes(tvi.Items, expand);
                    }
                }
            }
        }


        private void btnCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            // Iterate through top-level items and collapse recursively
            CollapseOrExpandNodes(treeViewFiles.Items, false);
            UpdateStatusBarAndButtonStates("Todos los nodos contraídos.");
        }

        private void btnExpandAll_Click(object sender, RoutedEventArgs e)
        {
            this.Cursor = Cursors.Wait;
            UpdateStatusBarAndButtonStates("Expandiendo nodos (puede tardar)...");
            // Use Dispatcher to allow UI to update before potentially long operation
            Dispatcher.InvokeAsync(() => {
                try {
                    CollapseOrExpandNodes(treeViewFiles.Items, true);
                    UpdateStatusBarAndButtonStates("Nodos expandidos (puede requerir carga dinámica adicional).");
                } catch (Exception ex) {
                    Debug.WriteLine($"Error expanding nodes: {ex}");
                    MessageBox.Show($"Ocurrió un error al expandir los nodos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatusBarAndButtonStates("Error al expandir nodos.");
                } finally {
                    this.Cursor = Cursors.Arrow;
                }
            }, System.Windows.Threading.DispatcherPriority.Background); // Lower priority
        }

        private void btnCollapseCurrent_Click(object sender, RoutedEventArgs e)
        {
            // Collapse the currently selected TreeViewItem if it's an expanded directory
            if (treeViewFiles.SelectedItem is TreeViewItem { Tag: FileSystemItem { IsDirectory: true } fsi } selectedTvi)
            {
                 if (selectedTvi.IsExpanded)
                 {
                    selectedTvi.IsExpanded = false;
                    UpdateStatusBarAndButtonStates($"'{fsi.Name}' contraído.");
                 }
            }
        }

        private void btnExpandCurrent_Click(object sender, RoutedEventArgs e)
        {
            // Expand the currently selected TreeViewItem if it's a collapsed directory
             if (treeViewFiles.SelectedItem is TreeViewItem { Tag: FileSystemItem { IsDirectory: true } fsi } selectedTvi)
            {
                 if (!selectedTvi.IsExpanded)
                 {
                    selectedTvi.IsExpanded = true; // This will trigger lazy load if needed via TreeViewItem_Expanded
                    UpdateStatusBarAndButtonStates($"'{fsi.Name}' expandido.");
                 }
            }
        }

    } // End partial class MainWindow
} // End namespace MakingVibe