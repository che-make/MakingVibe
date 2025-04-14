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
using WinForms = System.Windows.Forms;

namespace MakingVibe
{
    public partial class MainWindow // : Window // Already declared in main part
    {
        // --- UI Event Handlers: TreeView Panel ---

        private void btnSelectRoot_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Seleccione la carpeta raíz del proyecto",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            string? initialDir = rootPath;
            if (string.IsNullOrEmpty(initialDir) || !_fileSystemService.DirectoryExists(initialDir))
            {
                initialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            if (!string.IsNullOrEmpty(initialDir) && _fileSystemService.DirectoryExists(initialDir))
            {
                dialog.SelectedPath = initialDir;
            }

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                rootPath = dialog.SelectedPath;
                txtCurrentPath.Text = $"Ruta actual: {rootPath}";
                UpdateStatusBarAndButtonStates($"Cargando directorio: {rootPath}...");
                _activeFileExtensionsFilter.Clear(); // Reset filters on root change
                LoadDirectoryTreeUI(rootPath); // Reload UI & Repopulate filters
                _settingsService.SaveLastRootPath(rootPath);
                UpdateStatusBarAndButtonStates("Listo.");
                treeViewFiles.Focus();
            }
        }

        private void btnClearSelection_Click(object sender, RoutedEventArgs e)
        {
            ClearAllSelectionsUI();
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
                if (treeViewItem.Items.Count == 1 && treeViewItem.Items[0] is string loadingText && loadingText == "Cargando...")
                {
                    treeViewItem.Items.Clear();
                    LoadChildrenUI(treeViewItem, fsi.Path, _activeFileExtensionsFilter); // Load children with filter
                }
                UpdateStatusBarAndButtonStates();
            }
            e.Handled = true;
        }

        private void TreeViewItem_Collapsed(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem { Tag: FileSystemItem fsi } treeViewItem && fsi.IsDirectory)
            {
                UpdateStatusBarAndButtonStates();
            }
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
                                 || Fragments.Any(); // Also consider fragments
            ctxCopyText.IsEnabled = canCopyAnything;

            ctxRename.IsEnabled = selectedItems.Count == 1 && selectedFsi != null;
            ctxDelete.IsEnabled = selectedItems.Count > 0;
            ctxRefreshNode.IsEnabled = isItemSelected && selectedFsi != null && selectedFsi.IsDirectory;
            ctxRefreshAll.IsEnabled = !string.IsNullOrEmpty(rootPath); // Tied to main refresh button
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
            selectedItems.Clear();

            var foundExtensions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (!_fileSystemService.DirectoryExists(path))
            {
                MessageBox.Show($"El directorio raíz especificado no existe o no es accesible:\n{path}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                rootPath = null;
                txtCurrentPath.Text = "Ruta actual: (Seleccione una carpeta raíz)";
                _settingsService.SaveLastRootPath(null);
                UpdateStatusBarAndButtonStates("Error al cargar el directorio raíz.");
                FileFilters.Clear(); // Method in FiltersTab partial class
                return;
            }

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
                UpdateFileFiltersUI(foundExtensions);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error inesperado al construir el árbol de directorios:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBarAndButtonStates("Error al mostrar el árbol.");
                FileFilters.Clear();
                Debug.WriteLine($"Error LoadDirectoryTreeUI: {ex}");
            }
            finally
            {
                UpdateStatusBarAndButtonStates();
            }
        }


        /// <summary>
        /// Loads the children UI (TreeViewItems) for a given parent TreeViewItem,
        /// applying file extension filters and collecting found extensions.
        /// </summary>
        private void LoadChildrenUI(TreeViewItem parentTreeViewItem, string directoryPath, HashSet<string> allowedExtensions, Dictionary<string, int>? foundExtensions = null)
        {
            var childrenData = _fileSystemService.GetDirectoryChildren(directoryPath, allowedExtensions);

            parentTreeViewItem.Items.Clear();

            if (childrenData == null)
            {
                parentTreeViewItem.Items.Add(new TreeViewItem { Header = "[Acceso Denegado]", Foreground = Brushes.Gray, IsEnabled = false });
                return;
            }

            var childrenList = childrenData.ToList();
            if (!childrenList.Any())
            {
                return;
            }

            foreach (var fsi in childrenList)
            {
                var childTreeViewItem = CreateTreeViewItemUI(fsi);
                parentTreeViewItem.Items.Add(childTreeViewItem);
                pathToTreeViewItemMap[fsi.Path] = childTreeViewItem;

                if (!fsi.IsDirectory && foundExtensions != null)
                {
                    string ext = string.IsNullOrEmpty(fsi.Type) ? "[Sin extensión]" : fsi.Type;
                    if (foundExtensions.ContainsKey(ext))
                        foundExtensions[ext]++;
                    else
                        foundExtensions[ext] = 1;
                }

                if (fsi.IsDirectory)
                {
                    childTreeViewItem.Items.Add("Cargando...");
                    if (foundExtensions != null)
                    {
                        // Recursively collect ALL extensions for filter list (Method in this partial class)
                        CollectExtensionsRecursive(fsi.Path, foundExtensions);
                    }
                }
            }

            if (parentTreeViewItem.Tag is FileSystemItem parentFsi)
            {
                UpdateParentDirectorySelectionState(parentFsi, childrenList);
            }
        }

        /// <summary>
        /// Recursively scans directories just to collect file extensions for the filter list.
        /// This runs *without* applying the active filter.
        /// </summary>
        private void CollectExtensionsRecursive(string directoryPath, Dictionary<string, int> foundExtensions)
        {
            var allChildren = _fileSystemService.GetDirectoryChildren(directoryPath); // No filter applied here
            if (allChildren == null) return;

            foreach (var item in allChildren)
            {
                if (!item.IsDirectory)
                {
                    string ext = string.IsNullOrEmpty(item.Type) ? "[Sin extensión]" : item.Type;
                    if (foundExtensions.ContainsKey(ext))
                        foundExtensions[ext]++;
                    else
                        foundExtensions[ext] = 1;
                }
                else
                {
                    CollectExtensionsRecursive(item.Path, foundExtensions);
                }
            }
        }

        /// <summary>
        /// Refreshes a specific node in the TreeView UI, reapplying filters.
        /// </summary>
        private void RefreshNodeUI(TreeViewItem nodeToRefresh, FileSystemItem fsi)
        {
            bool wasExpanded = nodeToRefresh.IsExpanded;
            string? parentPath = _fileSystemService.GetDirectoryName(fsi.Path);

            // Enhanced Cleanup
            var pathsToRemove = pathToTreeViewItemMap.Keys
                .Where(k => k.StartsWith(fsi.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                            && !k.Equals(fsi.Path, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var path in pathsToRemove)
            {
                if (pathToTreeViewItemMap.TryGetValue(path, out var childTvi) && childTvi.Tag is FileSystemItem childFsi)
                {
                    selectedItems.Remove(childFsi);
                }
                pathToTreeViewItemMap.Remove(path);
            }

            nodeToRefresh.Items.Clear();
            LoadChildrenUI(nodeToRefresh, fsi.Path, _activeFileExtensionsFilter); // Reload children with current filter
            nodeToRefresh.IsExpanded = wasExpanded;

            // Re-sync checkbox state
            if (nodeToRefresh.Header is Grid headerGrid)
            {
                var checkbox = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                if (checkbox != null)
                {
                    checkbox.IsChecked = selectedItems.Contains(fsi);
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
                IsChecked = selectedItems.Contains(fsi)
            };
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
                ToolTip = fsi.Path,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(nameTextBlock, 2);
            headerGrid.Children.Add(nameTextBlock);

            if (!fsi.IsDirectory && fsi.LineCount.HasValue)
            {
                var lineCountTextBlock = new TextBlock
                {
                    Text = $"({fsi.LineCount} lines)",
                    Foreground = Brushes.Gray,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(lineCountTextBlock, 3);
                headerGrid.Children.Add(lineCountTextBlock);
            }

            var treeViewItem = new TreeViewItem
            {
                Header = headerGrid,
                Tag = fsi
            };

            if (fsi.IsDirectory)
            {
                treeViewItem.Expanded += TreeViewItem_Expanded;
                treeViewItem.Collapsed += TreeViewItem_Collapsed;
            }

            return treeViewItem;
        }


        // --- Selection Handling (UI Related) ---

        private void Checkbox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingParentState) return;

            if (sender is CheckBox { Tag: FileSystemItem fsi } checkbox)
            {
                bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

                bool added = false;
                if (!selectedItems.Contains(fsi))
                {
                    selectedItems.Add(fsi);
                    added = true;
                }

                if (isCtrlPressed && fsi.IsDirectory)
                {
                    SelectDirectoryDescendantsRecursive(fsi, true); // Method in this partial class
                }
                else if (!isCtrlPressed && fsi.IsDirectory)
                {
                    UpdateDirectoryFilesSelection(fsi, true, _activeFileExtensionsFilter); // Method in this partial class
                }

                if (!fsi.IsDirectory || (added && fsi.IsDirectory))
                {
                    UpdateParentDirectorySelectionState(fsi); // Method in this partial class
                }
            }
        }

        private void Checkbox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingParentState) return;

            if (sender is CheckBox { Tag: FileSystemItem fsi } checkbox)
            {
                bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                bool removed = selectedItems.Remove(fsi);

                if (isCtrlPressed && fsi.IsDirectory)
                {
                    SelectDirectoryDescendantsRecursive(fsi, false);
                }
                else if (!isCtrlPressed && fsi.IsDirectory)
                {
                    UpdateDirectoryFilesSelection(fsi, false, _activeFileExtensionsFilter);
                }

                if (!fsi.IsDirectory || (removed && fsi.IsDirectory))
                {
                    UpdateParentDirectorySelectionState(fsi);
                }
            }
        }

        /// <summary>
        /// Recursively selects or deselects all descendant *files AND directories*. (Ctrl+Click)
        /// Ignores UI filters.
        /// </summary>
        private void SelectDirectoryDescendantsRecursive(FileSystemItem directoryFsi, bool select)
        {
            if (!directoryFsi.IsDirectory) return;

            var descendantsToUpdate = new List<FileSystemItem>();
            _fileSystemService.FindAllDescendantsRecursive(directoryFsi, descendantsToUpdate);

            if (!descendantsToUpdate.Any()) return;

            bool collectionChanged = false;
            _isUpdatingParentState = true;
            try
            {
                foreach (var item in descendantsToUpdate)
                {
                    bool currentlySelected = selectedItems.Contains(item);
                    if (select && !currentlySelected) { selectedItems.Add(item); collectionChanged = true; }
                    else if (!select && currentlySelected) { selectedItems.Remove(item); collectionChanged = true; }

                    if (pathToTreeViewItemMap.TryGetValue(item.Path, out var descendantTreeViewItem))
                    {
                        if (descendantTreeViewItem.Header is Grid headerGrid)
                        {
                            var cb = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                            if (cb != null && cb.IsChecked != select) cb.IsChecked = select;
                        }
                    }
                }
            }
            finally { _isUpdatingParentState = false; }

            if (collectionChanged) UpdateStatusBarAndButtonStates();
            UpdateParentDirectorySelectionState(directoryFsi);
        }


        /// <summary>
        /// Updates the selection state for direct child *files* of a directory, respecting filters. (Simple Click)
        /// </summary>
        private void UpdateDirectoryFilesSelection(FileSystemItem directoryFsi, bool select, HashSet<string> allowedExtensions)
        {
            var children = _fileSystemService.GetDirectoryChildren(directoryFsi.Path, allowedExtensions);
            if (children == null) return;

            bool collectionChanged = false;
            _isUpdatingParentState = true;
            try
            {
                foreach (var child in children.Where(c => !c.IsDirectory)) // Only process files
                {
                    bool currentlySelected = selectedItems.Contains(child);
                    if (select && !currentlySelected) { selectedItems.Add(child); collectionChanged = true; }
                    else if (!select && currentlySelected) { selectedItems.Remove(child); collectionChanged = true; }

                    if (pathToTreeViewItemMap.TryGetValue(child.Path, out var childTreeViewItem))
                    {
                        if (childTreeViewItem.Header is Grid headerGrid)
                        {
                            var cb = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                            if (cb != null && cb.IsChecked != select) cb.IsChecked = select;
                        }
                    }
                }
            }
            finally { _isUpdatingParentState = false; }

            if (collectionChanged) UpdateStatusBarAndButtonStates();
            UpdateParentDirectorySelectionState(directoryFsi, children);
        }

        /// <summary>
        /// Updates a parent directory's selection state based on its *visible* children.
        /// </summary>
        private void UpdateParentDirectorySelectionState(FileSystemItem childItem, IEnumerable<FileSystemItem>? cachedChildren = null)
        {
            if (_isUpdatingParentState) return;

            string? parentPath = _fileSystemService.GetDirectoryName(childItem.Path);
            if (string.IsNullOrEmpty(parentPath) || !parentPath.StartsWith(rootPath ?? "__INVALID__", StringComparison.OrdinalIgnoreCase)) return;

            if (!pathToTreeViewItemMap.TryGetValue(parentPath, out var parentTvi)) return;
            if (parentTvi.Tag is not FileSystemItem parentFsi || !parentFsi.IsDirectory) return;

            var visibleChildren = cachedChildren?.ToList()
                                  ?? _fileSystemService.GetDirectoryChildren(parentPath, _activeFileExtensionsFilter)?.ToList();

            bool shouldBeChecked;
            if (visibleChildren == null || !visibleChildren.Any())
            {
                 shouldBeChecked = false; // Uncheck if no visible children
            } else {
                 shouldBeChecked = visibleChildren.All(item => selectedItems.Contains(item));
            }

            if (parentTvi.Header is Grid headerGrid)
            {
                var parentCheckbox = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                if (parentCheckbox != null)
                {
                    _isUpdatingParentState = true;
                    try
                    {
                        if (parentCheckbox.IsChecked != shouldBeChecked) parentCheckbox.IsChecked = shouldBeChecked;

                        bool parentCurrentlySelected = selectedItems.Contains(parentFsi);
                        if (shouldBeChecked && !parentCurrentlySelected) selectedItems.Add(parentFsi);
                        else if (!shouldBeChecked && parentCurrentlySelected) selectedItems.Remove(parentFsi);
                    }
                    finally { _isUpdatingParentState = false; }
                }
            }

            if (!parentPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateParentDirectorySelectionState(parentFsi); // Recurse upwards
            }
            else
            {
                UpdateStatusBarAndButtonStates(); // Update status if root node changed
            }
        }

        /// <summary>
        /// Clears the selection collection and unchecks corresponding checkboxes in the UI.
        /// </summary>
        private void ClearAllSelectionsUI()
        {
            if (selectedItems.Count == 0) return;
            var itemsToUncheckPaths = selectedItems.Select(i => i.Path).ToList();

            _isUpdatingParentState = true;
            try
            {
                selectedItems.Clear();
                foreach (var path in itemsToUncheckPaths)
                {
                    if (pathToTreeViewItemMap.TryGetValue(path, out var treeViewItem))
                    {
                        if (treeViewItem.Header is Grid headerGrid)
                        {
                            var cb = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                            if (cb != null && cb.IsChecked == true) cb.IsChecked = false;
                        }
                    }
                }
            }
            finally { _isUpdatingParentState = false; }

            UpdateStatusBarAndButtonStates("Selección limpiada.");
        }


        // --- TreeView Collapse/Expand All/Current ---

        private void CollapseOrExpandNodes(ItemCollection items, bool expand)
        {
            foreach (object? obj in items)
            {
                if (obj is TreeViewItem tvi && tvi.Tag is FileSystemItem fsi && fsi.IsDirectory)
                {
                    if (tvi.IsExpanded != expand)
                    {
                        tvi.IsExpanded = expand; // Setting true might trigger lazy load
                    }

                    // Recurse regardless of current load state when expanding (lazy load handles it)
                    // Recurse if collapsing and it has items
                    if (tvi.HasItems && (!expand || expand && tvi.IsExpanded)) // Simplified condition
                    {
                         CollapseOrExpandNodes(tvi.Items, expand);
                    }
                }
            }
        }

        private void btnCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            CollapseOrExpandNodes(treeViewFiles.Items, false);
            UpdateStatusBarAndButtonStates("Todos los nodos contraídos.");
        }

        private void btnExpandAll_Click(object sender, RoutedEventArgs e)
        {
            this.Cursor = Cursors.Wait;
            UpdateStatusBarAndButtonStates("Expandiendo nodos...");
            try
            {
                Dispatcher.InvokeAsync(() => {
                    CollapseOrExpandNodes(treeViewFiles.Items, true);
                    UpdateStatusBarAndButtonStates("Nodos expandidos (puede requerir otro clic para niveles cargados dinámicamente).");
                    this.Cursor = Cursors.Arrow;
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"Error expanding nodes: {ex}");
                 MessageBox.Show($"Ocurrió un error al expandir los nodos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 UpdateStatusBarAndButtonStates("Error al expandir nodos.");
                 this.Cursor = Cursors.Arrow;
            }
        }

        private void btnCollapseCurrent_Click(object sender, RoutedEventArgs e)
        {
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
             if (treeViewFiles.SelectedItem is TreeViewItem { Tag: FileSystemItem { IsDirectory: true } fsi } selectedTvi)
            {
                 if (!selectedTvi.IsExpanded)
                 {
                    selectedTvi.IsExpanded = true; // Triggers lazy load if needed
                    UpdateStatusBarAndButtonStates($"'{fsi.Name}' expandido.");
                 }
            }
        }

    } // End partial class MainWindow
} // End namespace MakingVibe