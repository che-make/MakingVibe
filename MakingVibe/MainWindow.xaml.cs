using MakingVibe.Models; // Use the model namespace
using MakingVibe.Services; // Use the services namespace
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json; // Required for JSON serialization
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input; // For MouseButtonEventArgs, KeyEventArgs, Cursors
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using CheckBox = System.Windows.Controls.CheckBox;
using Clipboard = System.Windows.Clipboard;
using Cursors = System.Windows.Input.Cursors;
//using Cursors = System.Windows.Input.Cursors; // No longer needed due to using System.Windows.Input above
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
//using KeyEventArgs = System.Windows.Input.KeyEventArgs; // No longer needed due to using System.Windows.Input above
using ListViewItem = System.Windows.Controls.ListViewItem;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using RoutedEventArgs = System.Windows.RoutedEventArgs;
using WinForms = System.Windows.Forms; // Alias for FolderBrowserDialog


namespace MakingVibe
{
    public partial class MainWindow : Window
    {
        // --- Services ---
        private readonly FileSystemService _fileSystemService;
        private readonly AiCopyService _aiCopyService;
        private readonly SettingsService _settingsService; // Keep for root path
        private readonly GitService _gitService;

        // --- UI State & Data ---
        private string? rootPath;
        private readonly ObservableCollection<FileSystemItem> selectedItems = new();
        private readonly Dictionary<string, TreeViewItem> pathToTreeViewItemMap = new();
        // *** Collection for Text Fragments ***
        public ObservableCollection<TextFragment> Fragments { get; set; }
        private readonly string _fragmentsFilePath; // Path to save fragments

        // Clipboard state (managed by UI)
        private List<FileSystemItem> clipboardItems = new();
        private bool isCutOperation;

        // --- Flag to prevent recursive updates during programmatic changes ---
        private bool _isUpdatingParentState = false;


        // Commands
        public static RoutedCommand RefreshCommand = new RoutedCommand();
        public static RoutedCommand DeleteCommand = new RoutedCommand();
        // Add more RoutedCommands if you bind other actions (Copy, Cut, Paste, Rename)

        public MainWindow()
        {
            InitializeComponent();

            // Instantiate services
            _fileSystemService = new FileSystemService();
            _aiCopyService = new AiCopyService(_fileSystemService); // Inject dependency
            _settingsService = new SettingsService();
            _gitService = new GitService();

            // *** Initialize Fragments and set file path ***
            Fragments = new ObservableCollection<TextFragment>();
            // Store fragments file in the same directory as settings for simplicity
            _fragmentsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "makingvibe.fragments.json");
            LoadFragments(); // Load fragments on startup
            listViewFragments.ItemsSource = Fragments; // Bind ListView in code-behind (alternative to XAML binding)

            // Bind commands
            CommandBindings.Add(new CommandBinding(RefreshCommand, btnRefresh_Click, CanExecuteRefresh));
            CommandBindings.Add(new CommandBinding(DeleteCommand, btnDelete_Click, CanExecuteDelete));
            // Add more CommandBindings here...

            // Update status bar when selection changes
            selectedItems.CollectionChanged += (s, e) => UpdateStatusBarAndButtonStates();

            // Listen for text changes in the main prompt to update button state
            txtMainPrompt.TextChanged += (s, e) => UpdateStatusBarAndButtonStates();

            // Set initial state
            UpdateStatusBarAndButtonStates("Listo.");
            SetWindowTitleFromGit();
        }

        // --- Command CanExecute Handlers ---
        private void CanExecuteRefresh(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !string.IsNullOrEmpty(rootPath) && _fileSystemService.DirectoryExists(rootPath);
        }

        private void CanExecuteDelete(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = selectedItems.Count > 0;
        }

        // Add CanExecute handlers for other commands if needed...

        // --- Window Load/Close ---

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            rootPath = _settingsService.LoadLastRootPath();

            if (!string.IsNullOrEmpty(rootPath) && _fileSystemService.DirectoryExists(rootPath))
            {
                UpdateStatusBarAndButtonStates($"Cargando directorio: {rootPath}...");
                LoadDirectoryTreeUI(rootPath);
                UpdateStatusBarAndButtonStates("Listo.");
            }
            else
            {
                txtCurrentPath.Text = "Ruta actual: (Seleccione una carpeta raíz)";
                UpdateStatusBarAndButtonStates("Por favor, seleccione una carpeta raíz.");
                rootPath = null; // Ensure rootPath is null if loaded path is invalid
            }
            // Set focus to the tree view initially if root is loaded
             if (rootPath != null)
             {
                treeViewFiles.Focus();
             }
        }

        // Save settings and fragments on closing
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _settingsService.SaveLastRootPath(rootPath);
            SaveFragments(); // *** Save fragments ***
            base.OnClosing(e);
        }


        // --- UI Event Handlers ---

        private void btnSelectRoot_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Seleccione la carpeta raíz del proyecto",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            // Set initial directory
            string? initialDir = rootPath;
            if (string.IsNullOrEmpty(initialDir) || !_fileSystemService.DirectoryExists(initialDir))
            {
                 initialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            if (!string.IsNullOrEmpty(initialDir) && _fileSystemService.DirectoryExists(initialDir)) // Check if initialDir exists before setting
            {
                 dialog.SelectedPath = initialDir; // Use SelectedPath for initial selection
            }


            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                rootPath = dialog.SelectedPath;
                txtCurrentPath.Text = $"Ruta actual: {rootPath}";
                UpdateStatusBarAndButtonStates($"Cargando directorio: {rootPath}...");
                LoadDirectoryTreeUI(rootPath); // Reload UI
                _settingsService.SaveLastRootPath(rootPath); // Save the newly selected path immediately
                UpdateStatusBarAndButtonStates("Listo.");
                treeViewFiles.Focus(); // Set focus after loading
            }
        }

        private void btnClearSelection_Click(object sender, RoutedEventArgs e)
        {
            ClearAllSelectionsUI();
        }

        // Preview handler
        private async void treeViewFiles_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            txtFileContent.Text = string.Empty; // Clear preview initially
            if (e.NewValue is TreeViewItem { Tag: FileSystemItem fsi } selectedTvi) // Use pattern matching and get Tvi
            {
                await ShowPreviewAsync(fsi);
                 // Optionally make preview tab active
                 // if(tabControlMain.SelectedIndex != 0) tabControlMain.SelectedIndex = 0; // Decided against this
            }
            // Handle deselection or selection of non-FSI items
            else if (e.NewValue == null) {
                 await ShowPreviewAsync(null); // Clear preview if nothing is selected
            }
             // Update button states after selection changes, as Collapse/Expand Current depend on SelectedItem
             UpdateStatusBarAndButtonStates();
        }

        // Lazy loading handler
        private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem { Tag: FileSystemItem fsi } treeViewItem && fsi.IsDirectory)
            {
                // Check if it needs loading (contains the dummy item)
                if (treeViewItem.Items.Count == 1 && treeViewItem.Items[0] is string loadingText && loadingText == "Cargando...")
                {
                    treeViewItem.Items.Clear(); // Remove dummy item
                    LoadChildrenUI(treeViewItem, fsi.Path); // Load actual children UI elements
                }
                // Update button states when expansion state changes
                UpdateStatusBarAndButtonStates();
            }
            e.Handled = true; // Prevent bubbling
        }

         // Handler for when an item is collapsed
        private void TreeViewItem_Collapsed(object sender, RoutedEventArgs e)
        {
             if (sender is TreeViewItem { Tag: FileSystemItem fsi } treeViewItem && fsi.IsDirectory)
             {
                 // Update button states when expansion state changes
                 UpdateStatusBarAndButtonStates();
             }
             e.Handled = true;
        }


        // --- Context Menu ---
        private void TreeViewContextMenu_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Note: The logic here primarily enables/disables context menu items based on TreeView selection.
            // The toolbar button's state is handled by UpdateStatusBarAndButtonStates.
            bool isItemSelected = treeViewFiles.SelectedItem != null;
            FileSystemItem? selectedFsi = (treeViewFiles.SelectedItem as TreeViewItem)?.Tag as FileSystemItem;
            bool isTextFileSelected = selectedFsi != null && !selectedFsi.IsDirectory && FileHelper.IsTextFile(selectedFsi.Path);

            // Enable actions based on selection state for the CONTEXT MENU
            // Check if there's something to copy (either selected files or a prompt)
             bool canCopyAnything = (selectedItems.Count > 0 && selectedItems.Any(item => item.IsDirectory || FileHelper.IsTextFile(item.Path)))
                                 || !string.IsNullOrWhiteSpace(txtMainPrompt.Text)
                                 || Fragments.Any(); // Also consider if any fragments exist to be selected
            ctxCopyText.IsEnabled = canCopyAnything;

            // Other context menu items depend only on TreeView selection
            ctxRename.IsEnabled = selectedItems.Count == 1 && selectedFsi != null;
            ctxDelete.IsEnabled = selectedItems.Count > 0;
            ctxRefreshNode.IsEnabled = isItemSelected && selectedFsi != null && selectedFsi.IsDirectory;
            ctxRefreshAll.IsEnabled = !string.IsNullOrEmpty(rootPath);
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
        /// Clears and reloads the entire TreeView UI based on the specified root path.
        /// </summary>
        private void LoadDirectoryTreeUI(string path)
        {
            treeViewFiles.Items.Clear();
            pathToTreeViewItemMap.Clear();
            selectedItems.Clear(); // Clear selection when reloading tree

            if (!_fileSystemService.DirectoryExists(path))
            {
                MessageBox.Show($"El directorio raíz especificado no existe o no es accesible:\n{path}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                rootPath = null; // Reset root path
                txtCurrentPath.Text = "Ruta actual: (Seleccione una carpeta raíz)";
                _settingsService.SaveLastRootPath(null); // Clear setting
                UpdateStatusBarAndButtonStates("Error al cargar el directorio raíz.");
                return;
            }

            try
            {
                var rootDirectoryInfo = new DirectoryInfo(path); // Use DirectoryInfo for root name
                var rootFsi = new FileSystemItem
                {
                    Name = rootDirectoryInfo.Name, // Display root folder name
                    Path = rootDirectoryInfo.FullName,
                    Type = "Directorio Raíz",
                    IsDirectory = true,
                    LineCount = null // Root directory has no line count
                };

                var rootTreeViewItem = CreateTreeViewItemUI(rootFsi);
                treeViewFiles.Items.Add(rootTreeViewItem);
                pathToTreeViewItemMap[rootFsi.Path] = rootTreeViewItem; // Map the root

                // Load initial children UI for the root
                LoadChildrenUI(rootTreeViewItem, rootFsi.Path);
                rootTreeViewItem.IsExpanded = true; // Expand root by default
            }
            catch (Exception ex) // Catch unexpected errors during UI creation
            {
                MessageBox.Show($"Error inesperado al construir el árbol de directorios:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBarAndButtonStates("Error al mostrar el árbol.");
                Debug.WriteLine($"Error LoadDirectoryTreeUI: {ex}");
            }
            finally
            {
                UpdateStatusBarAndButtonStates(); // Ensure buttons/status are correct
            }
        }

        /// <summary>
        /// Loads the children UI (TreeViewItems) for a given parent TreeViewItem.
        /// </summary>
        private void LoadChildrenUI(TreeViewItem parentTreeViewItem, string directoryPath)
        {
            // Use FileSystemService to get the data (now includes line counts)
            var childrenData = _fileSystemService.GetDirectoryChildren(directoryPath);

            parentTreeViewItem.Items.Clear(); // Clear existing items (like "Cargando...")

            if (childrenData == null) // Access denied case from service
            {
                parentTreeViewItem.Items.Add(new TreeViewItem { Header = "[Acceso Denegado]", Foreground = Brushes.Gray, IsEnabled = false });
                return;
            }

            if (!childrenData.Any())
            {
                 // Optionally add a placeholder if the directory is empty
                 // parentTreeViewItem.Items.Add(new TreeViewItem { Header = "[Vacío]", Foreground = Brushes.Gray, IsEnabled = false });
                return;
            }

            // Create TreeViewItems for each child data item
            foreach (var fsi in childrenData)
            {
                var childTreeViewItem = CreateTreeViewItemUI(fsi); // This now handles the line count display
                parentTreeViewItem.Items.Add(childTreeViewItem);
                pathToTreeViewItemMap[fsi.Path] = childTreeViewItem; // Add to map

                // Add dummy node for lazy loading if it's a directory
                if (fsi.IsDirectory)
                {
                    childTreeViewItem.Items.Add("Cargando...");
                }
            }

            // After loading children, check if parent needs auto-selection
            if(parentTreeViewItem.Tag is FileSystemItem parentFsi)
            {
                 UpdateParentDirectorySelectionState(parentFsi, childrenData); // Pass childrenData to avoid redundant fetch
            }
        }

        /// <summary>
        /// Refreshes a specific node in the TreeView UI.
        /// </summary>
        private void RefreshNodeUI(TreeViewItem nodeToRefresh, FileSystemItem fsi)
        {
            bool wasExpanded = nodeToRefresh.IsExpanded;
            string? parentPath = _fileSystemService.GetDirectoryName(fsi.Path); // Get parent path before clearing

            // --- Enhanced Cleanup on Refresh ---
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
            // --- End Enhanced Cleanup ---


            nodeToRefresh.Items.Clear(); // Clear existing UI children

            // Reload children UI (this now includes the parent state check and line counts)
            LoadChildrenUI(nodeToRefresh, fsi.Path);

            // Restore expansion state if needed
            nodeToRefresh.IsExpanded = wasExpanded;

             // Re-sync checkbox state for the refreshed node itself based on selection
             // Need to find the CheckBox within the potentially new header Grid
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
        /// Creates a TreeViewItem UI element for a given FileSystemItem, including line count display.
        /// Uses a Grid for better layout control.
        /// </summary>
        private TreeViewItem CreateTreeViewItemUI(FileSystemItem fsi)
        {
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Checkbox
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Type Indicator
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Name (takes remaining space)
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Line Count

            // 1. Checkbox
            var checkbox = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
                Tag = fsi, // Tag the checkbox with the data item
                IsChecked = selectedItems.Contains(fsi) // Sync check state with backing collection
            };
            checkbox.Checked += Checkbox_Checked;
            checkbox.Unchecked += Checkbox_Unchecked;
            Grid.SetColumn(checkbox, 0);
            headerGrid.Children.Add(checkbox);

            // 2. Type Indicator
            var typeIndicator = new TextBlock
            {
                Text = fsi.IsDirectory ? "[D] " : "[F] ",
                FontWeight = FontWeights.SemiBold,
                Foreground = fsi.IsDirectory ? Brushes.DarkGoldenrod : Brushes.DarkSlateBlue,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(typeIndicator, 1);
            headerGrid.Children.Add(typeIndicator);

            // 3. Name TextBlock
            var nameTextBlock = new TextBlock
            {
                Text = fsi.Name,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = fsi.Path, // Show full path on hover
                TextTrimming = TextTrimming.CharacterEllipsis // Prevent long names overflowing
            };
            Grid.SetColumn(nameTextBlock, 2);
            headerGrid.Children.Add(nameTextBlock);

            // 4. Line Count TextBlock (only for files with a count)
            if (!fsi.IsDirectory && fsi.LineCount.HasValue)
            {
                var lineCountTextBlock = new TextBlock
                {
                    Text = $"({fsi.LineCount} lines)",
                    Foreground = Brushes.Gray,
                    FontSize = 10, // Smaller font size
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right, // Align to the right within its column
                    Margin = new Thickness(8, 0, 0, 0) // Add some space before the count
                };
                Grid.SetColumn(lineCountTextBlock, 3);
                headerGrid.Children.Add(lineCountTextBlock);
            }

            // Create the TreeViewItem
            var treeViewItem = new TreeViewItem
            {
                Header = headerGrid, // Set the Grid as the header
                Tag = fsi // Tag the TreeViewItem itself with the data item
            };

            // Attach event handlers for directories
            if (fsi.IsDirectory)
            {
                treeViewItem.Expanded += TreeViewItem_Expanded;
                treeViewItem.Collapsed += TreeViewItem_Collapsed; // Add collapsed handler
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
                    // --- UPDATED: Call renamed method ---
                    SelectDirectoryDescendantsRecursive(fsi, true);
                }
                else if (!isCtrlPressed && fsi.IsDirectory)
                {
                    UpdateDirectoryFilesSelection(fsi, true);
                }

                if (!fsi.IsDirectory || (added && fsi.IsDirectory))
                {
                    UpdateParentDirectorySelectionState(fsi);
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
                     // --- UPDATED: Call renamed method ---
                     SelectDirectoryDescendantsRecursive(fsi, false);
                 }
                 else if (!isCtrlPressed && fsi.IsDirectory)
                 {
                     UpdateDirectoryFilesSelection(fsi, false);
                 }

                 if (!fsi.IsDirectory || (removed && fsi.IsDirectory))
                 {
                     UpdateParentDirectorySelectionState(fsi);
                 }
            }
        }


        /// <summary>
        /// Recursively selects or deselects all descendant *files AND directories* within a specified directory,
        /// updating the selection collection and the UI checkboxes. (Used for Ctrl+Click)
        /// </summary>
        /// <param name="directoryFsi">The FileSystemItem representing the directory.</param>
        /// <param name="select">True to select descendants, False to deselect descendants.</param>
        // --- RENAMED METHOD ---
        private void SelectDirectoryDescendantsRecursive(FileSystemItem directoryFsi, bool select)
        {
             if (!directoryFsi.IsDirectory) return;

             // --- UPDATED: Use new service method ---
             var descendantsToUpdate = new List<FileSystemItem>();
             _fileSystemService.FindAllDescendantsRecursive(directoryFsi, descendantsToUpdate);
             // --- END UPDATED ---

             if (!descendantsToUpdate.Any()) return;

             bool collectionChanged = false;

             _isUpdatingParentState = true;
             try
             {
                 // --- Process ALL descendants (files and directories) ---
                 foreach (var item in descendantsToUpdate)
                 {
                     bool currentlySelected = selectedItems.Contains(item);
                     if (select && !currentlySelected)
                     {
                         selectedItems.Add(item);
                         collectionChanged = true;
                     }
                     else if (!select && currentlySelected)
                     {
                         selectedItems.Remove(item);
                         collectionChanged = true;
                     }

                     // Update the corresponding UI CheckBox state
                     if (pathToTreeViewItemMap.TryGetValue(item.Path, out var descendantTreeViewItem))
                     {
                          // Find checkbox within the Grid header
                         if (descendantTreeViewItem.Header is Grid headerGrid)
                         {
                             var cb = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                             if (cb != null && cb.IsChecked != select)
                             {
                                 cb.IsChecked = select;
                             }
                         }
                     }
                     // else: Item not visible/expanded in tree, UI update not possible yet.
                 }
                 // --- END Process ALL descendants ---
             }
             finally
             {
                 _isUpdatingParentState = false;
             }


             if (collectionChanged)
             {
                 UpdateStatusBarAndButtonStates();
             }

             UpdateParentDirectorySelectionState(directoryFsi);
        }
        // --- END RENAMED METHOD ---


        /// <summary>
        /// Updates the selection state (both in the collection and UI) for direct child *files* of a directory.
        /// (Used for Simple Click on Directory)
        /// </summary>
        private void UpdateDirectoryFilesSelection(FileSystemItem directoryFsi, bool select)
        {
            var children = _fileSystemService.GetDirectoryChildren(directoryFsi.Path);
            if (children == null) return;

            bool collectionChanged = false;

            _isUpdatingParentState = true;
            try {
                foreach (var child in children)
                {
                    if (!child.IsDirectory) // Only direct files
                    {
                        bool currentlySelected = selectedItems.Contains(child);
                        if (select && !currentlySelected)
                        {
                            selectedItems.Add(child);
                            collectionChanged = true;
                        }
                        else if (!select && currentlySelected)
                        {
                            selectedItems.Remove(child);
                            collectionChanged = true;
                        }

                        if (pathToTreeViewItemMap.TryGetValue(child.Path, out var childTreeViewItem))
                        {
                             // Find checkbox within the Grid header
                             if (childTreeViewItem.Header is Grid headerGrid)
                             {
                                 var cb = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                                 if (cb != null && cb.IsChecked != select)
                                 {
                                     cb.IsChecked = select;
                                 }
                             }
                        }
                    }
                }
            }
            finally {
                 _isUpdatingParentState = false;
            }

            if (collectionChanged)
            {
                UpdateStatusBarAndButtonStates();
            }

            UpdateParentDirectorySelectionState(directoryFsi);
        }


        /// <summary>
        /// Checks if a parent directory should be selected based on the selection state of its direct children.
        /// Updates the parent's checkbox and selection state accordingly.
        /// </summary>
        private void UpdateParentDirectorySelectionState(FileSystemItem childItem, IEnumerable<FileSystemItem>? cachedChildren = null)
        {
            if (_isUpdatingParentState) return;

            string? parentPath = _fileSystemService.GetDirectoryName(childItem.Path);
            if (string.IsNullOrEmpty(parentPath) || !parentPath.StartsWith(rootPath ?? "__INVALID__", StringComparison.OrdinalIgnoreCase)) return; // Stop recursion if we go above root

            if (!pathToTreeViewItemMap.TryGetValue(parentPath, out var parentTvi)) return;
            if (parentTvi.Tag is not FileSystemItem parentFsi || !parentFsi.IsDirectory) return;

            var children = cachedChildren ?? _fileSystemService.GetDirectoryChildren(parentPath);
            if (children == null || !children.Any()) return; // No children or access denied

            var directChildrenList = children.ToList();
            // Parent should be checked if ALL direct children (files and valid dirs that actually exist) are selected
             bool shouldBeChecked = directChildrenList.All(item => selectedItems.Contains(item));


            // Find checkbox within the Grid header
             if (parentTvi.Header is Grid headerGrid)
             {
                 var parentCheckbox = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                 if (parentCheckbox != null)
                 {
                     _isUpdatingParentState = true;
                     try
                     {
                         if (parentCheckbox.IsChecked != shouldBeChecked)
                         {
                             parentCheckbox.IsChecked = shouldBeChecked;
                         }

                         bool parentCurrentlySelected = selectedItems.Contains(parentFsi);
                         if (shouldBeChecked && !parentCurrentlySelected)
                         {
                             selectedItems.Add(parentFsi);
                         }
                         else if (!shouldBeChecked && parentCurrentlySelected)
                         {
                             selectedItems.Remove(parentFsi);
                         }
                     }
                     finally
                     {
                         _isUpdatingParentState = false;
                     }
                 }
             }

            // Recurse upwards only if the parent path is not the root path itself
            if (!parentPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                 UpdateParentDirectorySelectionState(parentFsi); // Recurse upwards
            } else {
                 // If we just updated the root node, refresh the status bar just in case
                  UpdateStatusBarAndButtonStates();
            }
        }


        /// <summary>
        /// Clears the selection collection and unchecks corresponding checkboxes in the UI.
        /// </summary>
        private void ClearAllSelectionsUI()
        {
            var itemsToUncheckPaths = selectedItems.Select(i => i.Path).ToList();
            if (selectedItems.Count == 0) return;

             _isUpdatingParentState = true;
             try {
                selectedItems.Clear();

                foreach (var path in itemsToUncheckPaths)
                {
                    if (pathToTreeViewItemMap.TryGetValue(path, out var treeViewItem))
                    {
                         // Find checkbox within the Grid header
                         if (treeViewItem.Header is Grid headerGrid)
                         {
                             var cb = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                             if (cb != null && cb.IsChecked == true)
                             {
                                 cb.IsChecked = false;
                             }
                         }
                    }
                }
             } finally {
                 _isUpdatingParentState = false;
             }

            UpdateStatusBarAndButtonStates("Selección limpiada.");
        }


        // --- File Preview (UI Related) ---

        /// <summary>
        /// Shows a preview of the selected file or directory information in the TextBox.
        /// </summary>
        private async Task ShowPreviewAsync(FileSystemItem? fsi) // Allow nullable FSI
        {
            if (fsi == null) // Add null check
            {
                 txtFileContent.Text = string.Empty;
                 return;
            }

            if (fsi.IsDirectory)
            {
                txtFileContent.Text = $"Directorio seleccionado: {fsi.Name}\n\nRuta: {fsi.Path}";
                //tabControlMain.SelectedIndex = 0; // Ensure preview tab is selected - Keep current tab
                return;
            }

            if (FileHelper.IsTextFile(fsi.Path))
            {
                // Use service to read content
                var (content, success) = await _fileSystemService.ReadTextFileContentAsync(fsi.Path);
                txtFileContent.Text = content; // Display content or error message from service
            }
            else if (FileHelper.IsImageFile(fsi.Path))
            {
                txtFileContent.Text = $"--- Vista previa de imagen no implementada ---\nArchivo: {fsi.Name}";
                // Future: Could add an Image control here and load the image
            }
            else
            {
                txtFileContent.Text = $"--- No se puede previsualizar este tipo de archivo ---\nTipo: {fsi.Type}\nRuta: {fsi.Path}";
            }
            //tabControlMain.SelectedIndex = 0; // Switch to preview tab - Keep current tab
        }

        // --- Button Actions / Command Handlers ---

        private void UpdateStatusBarAndButtonStates(string statusMessage = "")
        {
             Action updateAction = () => {
                 // Update status bar text
                if (!string.IsNullOrEmpty(statusMessage))
                {
                    statusBarText.Text = statusMessage;
                }
                else if (selectedItems.Count > 0)
                {
                    statusBarText.Text = $"{selectedItems.Count} elemento(s) seleccionado(s).";
                }
                 else if (!string.IsNullOrEmpty(rootPath)) // Check if root path is set
                 {
                     statusBarText.Text = "Listo.";
                 }
                 else
                 {
                     statusBarText.Text = "Por favor, seleccione una carpeta raíz.";
                 }


                // Update selection count
                statusSelectionCount.Text = $"Seleccionados: {selectedItems.Count}";

                 // Standard Button States (based on selectedItems collection)
                 bool hasSelection = selectedItems.Count > 0;
                 bool hasSingleSelection = selectedItems.Count == 1;
                 bool canPaste = clipboardItems.Count > 0 && treeViewFiles.SelectedItem is TreeViewItem; // Simplified paste check

                 // Determine if any fragments are selected via checkbox
                 bool anyFragmentSelected = false;
                 foreach (var item in listViewFragments.Items)
                 {
                     var container = listViewFragments.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
                     if (container != null)
                     {
                         var checkBox = FindVisualChild<CheckBox>(container);
                         if (checkBox != null && checkBox.IsChecked == true)
                         {
                             anyFragmentSelected = true;
                             break;
                         }
                     }
                 }

                 bool canCopyFiles = hasSelection && selectedItems.Any(item => item.IsDirectory || FileHelper.IsTextFile(item.Path));
                 bool canCopyPrompt = !string.IsNullOrWhiteSpace(txtMainPrompt.Text); // Simple check: is main prompt non-empty?
                 bool canCopyAiText = canCopyFiles || canCopyPrompt || anyFragmentSelected; // MODIFIED: Also allow copy if fragments are selected


                 bool treeHasRoot = treeViewFiles.HasItems; // Check if the tree has at least the root item


                 btnCopyText.IsEnabled = canCopyAiText; // **MODIFIED** Use the combined condition
                 btnCopy.IsEnabled = hasSelection;
                 btnCut.IsEnabled = hasSelection;
                 btnDelete.IsEnabled = hasSelection;
                 btnRename.IsEnabled = hasSingleSelection;
                 btnPaste.IsEnabled = canPaste;
                 btnClearSelection.IsEnabled = hasSelection;
                 btnCollapseAll.IsEnabled = treeHasRoot;
                 btnExpandAll.IsEnabled = treeHasRoot;

                 // Collapse/Expand Current Button States (based on treeViewFiles.SelectedItem)
                 bool canCollapseCurrent = false;
                 bool canExpandCurrent = false;
                 if (treeViewFiles.SelectedItem is TreeViewItem selectedTvi && selectedTvi.Tag is FileSystemItem selectedFsi && selectedFsi.IsDirectory)
                 {
                     canCollapseCurrent = selectedTvi.IsExpanded;
                     canExpandCurrent = !selectedTvi.IsExpanded && selectedTvi.HasItems && !(selectedTvi.Items.Count == 1 && selectedTvi.Items[0] is string); // Enable expand only if it HAS items and they are loaded
                 }
                 btnCollapseCurrent.IsEnabled = canCollapseCurrent;
                 btnExpandCurrent.IsEnabled = canExpandCurrent;


                 // Ensure commands associated with buttons re-evaluate their CanExecute status
                 CommandManager.InvalidateRequerySuggested();
            };

            // Ensure updates run on the UI thread
            if (Dispatcher.CheckAccess())
            {
                updateAction();
            }
            else
            {
                Dispatcher.Invoke(updateAction);
            }
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(rootPath) && _fileSystemService.DirectoryExists(rootPath))
            {
                UpdateStatusBarAndButtonStates($"Refrescando: {rootPath}...");
                LoadDirectoryTreeUI(rootPath);
                UpdateStatusBarAndButtonStates("Árbol refrescado.");
            }
            else
            {
                MessageBox.Show("No hay una carpeta raíz seleccionada o válida para refrescar.", "Refrescar", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateStatusBarAndButtonStates("Seleccione una carpeta raíz.");
            }
        }

        // --- AI Text Copy Action (UI Coordination) ---

        // **MODIFIED**: Added logic to handle the case where no files are selected.
        private async void btnCopyText_Click(object sender, RoutedEventArgs e)
        {
            // 1. Get main prompt AND selected fragments text (always needed)
            string mainPrompt = txtMainPrompt.Text.Trim();
            var selectedFragmentsText = new StringBuilder();
            // Iterate through ListView items to find checked fragments
            foreach (var item in listViewFragments.Items)
            {
                if (item is TextFragment fragment) // Get the data context (TextFragment)
                {
                    // Find the corresponding ListViewItem container
                    var container = listViewFragments.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
                    if (container != null)
                    {
                        // Find the CheckBox within the container's template
                        var checkBox = FindVisualChild<CheckBox>(container);
                        if (checkBox != null && checkBox.IsChecked == true)
                        {
                            selectedFragmentsText.AppendLine(fragment.Text);
                            selectedFragmentsText.AppendLine(); // Add blank line between fragments
                        }
                    }
                }
            }
            string fragmentsString = selectedFragmentsText.ToString().TrimEnd(); // Trim trailing newline

            // --- Handle case where NO files are selected (but prompt/fragments might be) ---
            if (selectedItems.Count == 0)
            {
                var promptOnlyOutputBuilder = new StringBuilder();

                // 1. Add Main Prompt (if any)
                if (!string.IsNullOrEmpty(mainPrompt))
                {
                    promptOnlyOutputBuilder.AppendLine(mainPrompt);
                    promptOnlyOutputBuilder.AppendLine(); // Add a blank line for separation
                }

                // 2. Add Selected Fragments (if any)
                if (!string.IsNullOrEmpty(fragmentsString))
                {
                     promptOnlyOutputBuilder.AppendLine(fragmentsString);
                     // No extra blank line needed here if it's the last thing
                }

                string finalPromptOnlyText = promptOnlyOutputBuilder.ToString().Trim(); // Trim potential leading/trailing whitespace

                if (string.IsNullOrEmpty(finalPromptOnlyText))
                {
                    UpdateStatusBarAndButtonStates("Nada seleccionado para copiar.");
                    MessageBox.Show("No hay archivos seleccionados, ni prompt principal, ni fragmentos seleccionados. No hay nada que copiar.", "Copiar Texto (AI)", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                try
                {
                    Clipboard.SetText(finalPromptOnlyText);
                    UpdateStatusBarAndButtonStates("Prompt y/o fragmentos copiados al portapapeles.");
                    MessageBox.Show("El prompt principal y/o los fragmentos seleccionados han sido copiados al portapapeles.", "Texto Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception clipEx)
                {
                     Debug.WriteLine($"Clipboard Error (Prompt/Fragment Only): {clipEx.Message}");
                     MessageBox.Show($"No se pudo copiar el texto al portapapeles.\nError: {clipEx.Message}", "Error al Copiar", MessageBoxButton.OK, MessageBoxImage.Warning);
                     UpdateStatusBarAndButtonStates("Error al copiar texto.");
                }
                return; // Exit the method after copying only the prompt/fragments
            }

            // --- ORIGINAL LOGIC: Files ARE selected (prompt/fragments might also be included) ---
            if (string.IsNullOrEmpty(rootPath))
            {
                 MessageBox.Show("No se ha establecido una carpeta raíz (necesaria para generar el mapa de archivos).", "Copiar Texto (AI)", MessageBoxButton.OK, MessageBoxImage.Error);
                 return;
            }

            // Use a local copy of selected items in case the selection changes during async operation
            var itemsToProcess = selectedItems.ToList();

            this.Cursor = Cursors.Wait;
            statusBarText.Text = "Recopilando archivos de texto...";
            btnCopyText.IsEnabled = false; // Disable button during operation

            try
            {
                // Use Task.Run for the potentially long-running service call
                var result = await Task.Run(() => _aiCopyService.GenerateAiClipboardContentAsync(itemsToProcess, rootPath));

                if (result == null)
                {
                    // This case means files were selected, but NONE were text files.
                    // We still might want to copy the prompt/fragments.
                    var promptOnlyOutputBuilder = new StringBuilder();
                    if (!string.IsNullOrEmpty(mainPrompt)) { promptOnlyOutputBuilder.AppendLine(mainPrompt).AppendLine(); }
                    if (!string.IsNullOrEmpty(fragmentsString)) { promptOnlyOutputBuilder.AppendLine("--- Included Fragments ---").AppendLine(fragmentsString).AppendLine("--- End Fragments ---"); }
                    string finalPromptOnlyText = promptOnlyOutputBuilder.ToString().Trim();

                    if (string.IsNullOrEmpty(finalPromptOnlyText))
                    {
                        MessageBox.Show("No se encontraron archivos de texto válidos en la selección, y el prompt/fragmentos están vacíos.", "Copiar Texto (AI)", MessageBoxButton.OK, MessageBoxImage.Information);
                        UpdateStatusBarAndButtonStates("No se encontraron archivos de texto.");
                    }
                    else
                    {
                        try
                        {
                            Clipboard.SetText(finalPromptOnlyText);
                            UpdateStatusBarAndButtonStates("Prompt y/o fragmentos copiados (sin archivos de texto).");
                            MessageBox.Show("No se encontraron archivos de texto en la selección. Se ha copiado solo el prompt principal y/o los fragmentos seleccionados al portapapeles.", "Texto Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception clipEx)
                        {
                             Debug.WriteLine($"Clipboard Error (Prompt/Fragment Only after file check): {clipEx.Message}");
                             MessageBox.Show($"No se pudo copiar el texto al portapapeles.\nError: {clipEx.Message}", "Error al Copiar", MessageBoxButton.OK, MessageBoxImage.Warning);
                             UpdateStatusBarAndButtonStates("Error al copiar texto.");
                        }
                    }
                    return; // Exit early
                }

                // Build the final combined output (Prompt + Fragments + FileMap + FileContents)
                var finalOutputBuilder = new StringBuilder();

                // 1. Add Main Prompt (if any)
                if (!string.IsNullOrEmpty(mainPrompt))
                {
                    finalOutputBuilder.AppendLine(mainPrompt);
                    finalOutputBuilder.AppendLine(); // Add a blank line for separation
                }

                // 2. Add Selected Fragments (if any)
                if (!string.IsNullOrEmpty(fragmentsString))
                {
                     // Add separator only if main prompt was also present
                     if (!string.IsNullOrEmpty(mainPrompt)) finalOutputBuilder.AppendLine("--- Included Fragments ---");
                     finalOutputBuilder.AppendLine(fragmentsString);
                     finalOutputBuilder.AppendLine(); // Add a blank line after fragments
                }

                // 3. Append the map and contents generated by the service
                finalOutputBuilder.Append(result.Value.Output);

                string finalClipboardText = finalOutputBuilder.ToString();


                // Copy to clipboard (must be done on UI thread)
                 try
                 {
                      Clipboard.SetText(finalClipboardText);
                 }
                 catch (Exception clipEx)
                 {
                      Debug.WriteLine($"Clipboard Error: {clipEx.Message}");
                      MessageBox.Show($"No se pudo copiar al portapapeles. El contenido puede ser demasiado grande.\nError: {clipEx.Message}", "Error al Copiar", MessageBoxButton.OK, MessageBoxImage.Warning);
                      // Don't show the success message if clipboard failed
                      return;
                 }


                UpdateStatusBarAndButtonStates($"Contenido de {result.Value.TextFileCount} archivo(s) copiado al portapapeles.");
                MessageBox.Show($"Se ha copiado el prompt principal, el texto de los fragmentos seleccionados (si los hay), el mapa y el contenido de {result.Value.TextFileCount} archivo(s) de texto al portapapeles.", "Texto Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UpdateStatusBarAndButtonStates("Error al copiar texto.");
                MessageBox.Show($"Error al preparar el texto para la IA:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"Error btnCopyText_Click: {ex}");
            }
            finally
            {
                // Restore cursor and re-evaluate button state on UI thread
                this.Cursor = Cursors.Arrow;
                UpdateStatusBarAndButtonStates(); // Refresh button states potentially
            }
        }


        // --- Standard File Operations (UI Coordination) ---

        private void btnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItems.Count == 0) return;

            clipboardItems = selectedItems.ToList(); // Store selected items for paste operation
            isCutOperation = false;

            UpdateStatusBarAndButtonStates($"Copiado(s) {clipboardItems.Count} elemento(s) al portapapeles.");
        }

        private void btnCut_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItems.Count == 0) return;

            clipboardItems = selectedItems.ToList();
            isCutOperation = true;

            UpdateStatusBarAndButtonStates($"Cortado(s) {clipboardItems.Count} elemento(s) al portapapeles.");
        }

        private void btnPaste_Click(object sender, RoutedEventArgs e)
        {
            if (clipboardItems.Count == 0)
            {
                MessageBox.Show("El portapapeles está vacío.", "Pegar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
             // Use treeViewFiles.SelectedItem to determine target directory more reliably
             TreeViewItem? targetTreeViewItem = treeViewFiles.SelectedItem as TreeViewItem;
            if (targetTreeViewItem == null || targetTreeViewItem.Tag is not FileSystemItem targetFsi)
            {
                MessageBox.Show("Seleccione un elemento (preferiblemente un directorio) en el árbol como destino para pegar.", "Pegar", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string destinationDir = targetFsi.IsDirectory ? targetFsi.Path : (_fileSystemService.GetDirectoryName(targetFsi.Path) ?? rootPath ?? "");
             if(string.IsNullOrEmpty(destinationDir))
             {
                  MessageBox.Show($"No se pudo determinar el directorio destino.", "Error al Pegar", MessageBoxButton.OK, MessageBoxImage.Error);
                  return;
             }

            UpdateStatusBarAndButtonStates("Pegando elementos...");
            this.Cursor = Cursors.Wait;
            bool refreshNeeded = false;
            string? refreshParentPath = null;
            string? sourceRefreshParentPath = null;

            try
            {
                int pasteCount = 0;
                var itemsToProcess = clipboardItems.ToList();

                foreach (var itemToPaste in itemsToProcess)
                {
                    string sourcePath = itemToPaste.Path;
                    string targetName = _fileSystemService.GetFileName(sourcePath);
                    string fullTargetPath = _fileSystemService.CombinePath(destinationDir, targetName);
                    string? sourceParent = isCutOperation ? _fileSystemService.GetDirectoryName(sourcePath) : null;

                    if (string.Equals(sourcePath, fullTargetPath, StringComparison.OrdinalIgnoreCase)) continue;
                    if ((itemToPaste.IsDirectory && !_fileSystemService.DirectoryExists(sourcePath)) || (!itemToPaste.IsDirectory && !_fileSystemService.FileExists(sourcePath)))
                    {
                        Debug.WriteLine($"Source path not found for paste/move: {sourcePath}");
                        MessageBox.Show($"El elemento de origen '{itemToPaste.Name}' ya no existe.", "Error al Pegar/Mover", MessageBoxButton.OK, MessageBoxImage.Warning);
                        continue;
                    }
                     if (itemToPaste.IsDirectory && destinationDir.StartsWith(sourcePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                     {
                         MessageBox.Show($"No se puede {(isCutOperation ? "mover" : "copiar")} la carpeta '{itemToPaste.Name}' dentro de sí misma o de una subcarpeta.", "Operación Inválida", MessageBoxButton.OK, MessageBoxImage.Warning); continue;
                     }

                    bool targetExists = itemToPaste.IsDirectory ? _fileSystemService.DirectoryExists(fullTargetPath) : _fileSystemService.FileExists(fullTargetPath);
                    if (targetExists)
                    {
                        var overwriteResult = MessageBox.Show($"El elemento '{targetName}' ya existe en el destino.\n¿Desea sobrescribirlo?", "Conflicto al Pegar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (overwriteResult == MessageBoxResult.No) continue;

                        try
                        {
                             // We need LineCount = null here as DeleteItem doesn't use it
                            var targetToDeleteFsi = new FileSystemItem { Name = targetName, Path = fullTargetPath, Type = "", IsDirectory = itemToPaste.IsDirectory, LineCount = null };
                            _fileSystemService.DeleteItem(targetToDeleteFsi);
                            if (pathToTreeViewItemMap.TryGetValue(fullTargetPath, out var existingTvi)) {
                                if (existingTvi.Tag is FileSystemItem existingFsi) selectedItems.Remove(existingFsi);
                                pathToTreeViewItemMap.Remove(fullTargetPath);
                            }
                        }
                        catch (Exception delEx)
                        {
                            MessageBox.Show($"No se pudo sobrescribir '{targetName}'. Error: {delEx.Message}", "Error al Pegar", MessageBoxButton.OK, MessageBoxImage.Error); continue;
                        }
                    }

                    try
                    {
                        if (isCutOperation)
                        {
                             _fileSystemService.MoveItem(sourcePath, fullTargetPath, itemToPaste.IsDirectory);
                             pathToTreeViewItemMap.Remove(sourcePath);
                             if(sourceParent != null) sourceRefreshParentPath = sourceParent;
                        }
                        else
                        {
                            _fileSystemService.CopyItem(sourcePath, fullTargetPath, itemToPaste.IsDirectory);
                        }
                        pasteCount++;
                        refreshNeeded = true;
                        refreshParentPath = destinationDir;
                    }
                    catch (Exception opEx)
                    {
                        MessageBox.Show($"Error al {(isCutOperation ? "mover" : "copiar")} '{itemToPaste.Name}':\n{opEx.Message}", "Error al Pegar", MessageBoxButton.OK, MessageBoxImage.Error);
                         break;
                    }
                }

                if (isCutOperation && pasteCount > 0)
                {
                    clipboardItems.Clear();
                    isCutOperation = false;
                }

                if (refreshNeeded && !string.IsNullOrEmpty(rootPath))
                {
                     if (sourceRefreshParentPath != null && pathToTreeViewItemMap.TryGetValue(sourceRefreshParentPath, out var sourceParentNode))
                     {
                         if (sourceParentNode.Tag is FileSystemItem sourceParentFsi) RefreshNodeUI(sourceParentNode, sourceParentFsi);
                     }

                     if (refreshParentPath != null && pathToTreeViewItemMap.TryGetValue(refreshParentPath, out var parentNode))
                     {
                          if (parentNode.Tag is FileSystemItem parentFsi) RefreshNodeUI(parentNode, parentFsi);
                     }
                     else if (refreshParentPath != null && refreshParentPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase)) {
                         LoadDirectoryTreeUI(rootPath); // Refresh root if destination was root
                     }
                     else {
                         LoadDirectoryTreeUI(rootPath); // Fallback to full refresh
                     }
                }

                 UpdateStatusBarAndButtonStates($"{pasteCount} elemento(s) pegado(s) en '{_fileSystemService.GetFileName(destinationDir)}'.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error inesperado durante la operación de pegado:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBarAndButtonStates("Error al pegar.");
                 if(!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
                UpdateStatusBarAndButtonStates();
            }
        }


        private void btnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItems.Count == 0) return;

            string message = selectedItems.Count == 1
                ? $"¿Está seguro de que desea eliminar '{selectedItems.First().Name}'?"
                : $"¿Está seguro de que desea eliminar {selectedItems.Count} elementos seleccionados?";

            if (MessageBox.Show(message, "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                UpdateStatusBarAndButtonStates("Eliminando...");
                this.Cursor = Cursors.Wait;
                int deleteCount = 0;
                var itemsToDelete = selectedItems.ToList();
                var parentPathsToRefresh = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                 var pathsToDelete = itemsToDelete.Select(i => i.Path).ToList();
                 ClearAllSelectionsUI(); // Unchecks UI and clears selectedItems first

                try
                {
                    foreach (var itemPath in pathsToDelete)
                    {
                        // Find the original item from the list (don't rely on selectedItems anymore)
                        var item = itemsToDelete.FirstOrDefault(i => i.Path.Equals(itemPath, StringComparison.OrdinalIgnoreCase));
                        if(item == null) continue; // Should not happen, but safety check

                        try
                        {
                            string? parentPath = _fileSystemService.GetDirectoryName(item.Path);
                            _fileSystemService.DeleteItem(item); // DeleteItem uses the item's path and IsDirectory flag

                            pathToTreeViewItemMap.Remove(item.Path);

                            // Remove descendants from map if it was a directory
                            if (item.IsDirectory) {
                                var descendantPaths = pathToTreeViewItemMap.Keys
                                    .Where(k => k.StartsWith(item.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                                    .ToList();
                                foreach(var descendantPath in descendantPaths) {
                                    // Also try to remove from selectedItems if somehow a child was still selected
                                    if (pathToTreeViewItemMap.TryGetValue(descendantPath, out var descTvi) && descTvi.Tag is FileSystemItem descFsi)
                                    {
                                        selectedItems.Remove(descFsi);
                                    }
                                    pathToTreeViewItemMap.Remove(descendantPath);
                                }
                            }

                            if (parentPath != null && parentPath.StartsWith(rootPath??"__INVALID__", StringComparison.OrdinalIgnoreCase)) // Only refresh parents within the root
                                parentPathsToRefresh.Add(parentPath);
                            deleteCount++;
                        }
                        catch (IOException ioEx)
                        {
                             MessageBox.Show($"No se pudo eliminar '{item.Name}'. Es posible que esté en uso.\nError: {ioEx.Message}", "Error al Eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        catch (UnauthorizedAccessException uaEx)
                        {
                             MessageBox.Show($"No se pudo eliminar '{item.Name}'. Permiso denegado.\nError: {uaEx.Message}", "Error al Eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        catch (Exception itemEx)
                        {
                            MessageBox.Show($"No se pudo eliminar '{item.Name}'.\nError inesperado: {itemEx.Message}", "Error al Eliminar", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }

                    // Refresh Parent Nodes or Full Tree
                    if (parentPathsToRefresh.Any() && !string.IsNullOrEmpty(rootPath))
                    {
                        bool refreshedOk = false;
                        foreach (var parentPath in parentPathsToRefresh)
                        {
                            if (pathToTreeViewItemMap.TryGetValue(parentPath, out var parentNode))
                            {
                                if (parentNode.Tag is FileSystemItem parentFsi)
                                {
                                    RefreshNodeUI(parentNode, parentFsi);
                                    refreshedOk = true;
                                }
                            }
                        }
                        // If a parent wasn't found in the map (maybe it was the root itself) or refresh failed, consider full refresh
                        if (!refreshedOk)
                        {
                             LoadDirectoryTreeUI(rootPath);
                        }
                    }
                    else if (deleteCount > 0 && !string.IsNullOrEmpty(rootPath))
                    {
                        // If no specific parent needed refresh (e.g., deleted item was directly under root)
                        LoadDirectoryTreeUI(rootPath);
                    }


                    UpdateStatusBarAndButtonStates($"{deleteCount} elemento(s) eliminado(s).");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error inesperado durante la eliminación: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatusBarAndButtonStates("Error al eliminar.");
                    if(!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath); // Try refresh on unexpected error
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                    UpdateStatusBarAndButtonStates(); // Final status update
                }
            }
        }

        private void btnRename_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItems.Count != 1) return;

            var itemToRename = selectedItems[0];
            string oldPath = itemToRename.Path;
            string? directory = _fileSystemService.GetDirectoryName(oldPath);

            if (directory == null)
            {
                MessageBox.Show("No se puede determinar el directorio del elemento a renombrar.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dialog = new RenameDialog(itemToRename.Name) { Owner = this };

            if (dialog.ShowDialog() == true)
            {
                string newName = dialog.NewName;
                if (newName.Equals(itemToRename.Name, StringComparison.Ordinal)) return; // No change

                string newPath = _fileSystemService.CombinePath(directory, newName);

                if ((itemToRename.IsDirectory && _fileSystemService.DirectoryExists(newPath)) || (!itemToRename.IsDirectory && _fileSystemService.FileExists(newPath)))
                {
                    MessageBox.Show($"Ya existe un elemento llamado '{newName}' en esta ubicación.", "Renombrar Fallido", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                UpdateStatusBarAndButtonStates($"Renombrando '{itemToRename.Name}'...");
                this.Cursor = Cursors.Wait;
                string oldNameForStatus = itemToRename.Name;

                 // --- Cleanup map and selection before actual rename ---
                 pathToTreeViewItemMap.Remove(oldPath);
                 if(itemToRename.IsDirectory) {
                      var descendantPaths = pathToTreeViewItemMap.Keys
                           .Where(k => k.StartsWith(oldPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                           .ToList();
                      foreach(var descendantPath in descendantPaths) {
                          pathToTreeViewItemMap.Remove(descendantPath);
                      }
                 }
                 selectedItems.Remove(itemToRename); // Remove from selection
                 // --- End Cleanup ---

                try
                {
                    _fileSystemService.RenameItem(oldPath, newPath, itemToRename.IsDirectory);

                    // --- Refresh parent node to show the renamed item ---
                     if (pathToTreeViewItemMap.TryGetValue(directory, out var parentNode))
                     {
                          if (parentNode.Tag is FileSystemItem parentFsi)
                          {
                             RefreshNodeUI(parentNode, parentFsi); // Refresh the parent

                             // --- Try to select the newly renamed item ---
                              Dispatcher.InvokeAsync(() => {
                                  if(pathToTreeViewItemMap.TryGetValue(newPath, out var newTvi))
                                  {
                                      newTvi.IsSelected = true; // Make it the selected item in the TreeView
                                      // Find checkbox and check it if needed
                                      if (newTvi.Header is Grid headerGrid) {
                                          var cb = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                                          if (cb != null) cb.IsChecked = true; // This will re-add to selectedItems via event
                                      }
                                  }
                              }, System.Windows.Threading.DispatcherPriority.Background);
                             // --- End Try select ---

                          } else {
                               if(!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath); // Fallback refresh
                          }

                     }
                     else if (directory.Equals(rootPath, StringComparison.OrdinalIgnoreCase)) {
                          // If parent was root, refresh the whole tree
                          if(!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath);
                     }
                     else
                     {
                           // Fallback refresh if parent not found (shouldn't happen often)
                           if(!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath);
                     }
                     // --- End Refresh ---

                    UpdateStatusBarAndButtonStates($"'{oldNameForStatus}' renombrado a '{newName}'."); // Update status AFTER refresh attempt
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al renombrar '{oldNameForStatus}':\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatusBarAndButtonStates($"Error al renombrar '{oldNameForStatus}'.");
                     // Attempt to refresh the parent node even on error to clean up UI state
                     if (pathToTreeViewItemMap.TryGetValue(directory, out var parentNode))
                     {
                          if (parentNode.Tag is FileSystemItem parentFsi) RefreshNodeUI(parentNode, parentFsi);
                          else if(!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath);
                     } else if(!string.IsNullOrEmpty(rootPath)){
                         LoadDirectoryTreeUI(rootPath);
                     }
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                    UpdateStatusBarAndButtonStates(); // Final status/button update
                }
            }
        }


        // --- Git Integration (using GitService) ---
        private void SetWindowTitleFromGit()
        {
            string? commitSubject = _gitService.GetLastCommitSubject();
            this.Title = !string.IsNullOrEmpty(commitSubject)
                ? $"MakingVibe - {commitSubject}"
                : "MakingVibe - (Commit info unavailable)";
        }

        // --- Fragment Management ---

        private void AddFragment_Click(object sender, RoutedEventArgs e)
        {
            AddCurrentFragment();
        }

        // Handle Enter key in the fragment TEXT input box
        private void TxtNewFragmentText_KeyDown(object sender, KeyEventArgs e)
        {
            // Add fragment on Enter only if Shift is NOT pressed (allowing multi-line input)
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                AddCurrentFragment();
                // Prevent the 'ding' sound and the Enter key from adding a newline
                e.Handled = true;
            }
        }

        private void AddCurrentFragment()
        {
             string newTitle = txtNewFragmentTitle.Text.Trim();
             string newText = txtNewFragmentText.Text.Trim(); // Trim text as well

            // Basic validation: require both title and text
            if (string.IsNullOrWhiteSpace(newTitle))
            {
                 MessageBox.Show("El título del fragmento no puede estar vacío.", "Añadir Fragmento", MessageBoxButton.OK, MessageBoxImage.Warning);
                 txtNewFragmentTitle.Focus();
                 return;
            }
             if (string.IsNullOrWhiteSpace(newText))
            {
                 MessageBox.Show("El texto del fragmento no puede estar vacío.", "Añadir Fragmento", MessageBoxButton.OK, MessageBoxImage.Warning);
                 txtNewFragmentText.Focus();
                 return;
            }

            Fragments.Add(new TextFragment { Title = newTitle, Text = newText });
            txtNewFragmentTitle.Clear();
            txtNewFragmentText.Clear();
            SaveFragments(); // Save after adding
            txtNewFragmentTitle.Focus(); // Set focus back to title for next entry
        }

        private void DeleteFragment_Click(object sender, RoutedEventArgs e)
        {
            // Get selected items from the ListView
            var itemsToRemove = listViewFragments.SelectedItems.Cast<TextFragment>().ToList();

            if (itemsToRemove.Count == 0)
            {
                MessageBox.Show("Seleccione uno o más fragmentos para eliminar.", "Eliminar Fragmento", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Confirmation (optional but recommended)
            if (MessageBox.Show($"¿Está seguro de que desea eliminar {itemsToRemove.Count} fragmento(s) seleccionado(s)?", "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                foreach (var item in itemsToRemove)
                {
                    Fragments.Remove(item);
                }
                SaveFragments(); // Save after deleting
            }
        }


        // --- Fragment Persistence ---

        private void LoadFragments()
        {
            if (!File.Exists(_fragmentsFilePath)) return;

            try
            {
                string json = File.ReadAllText(_fragmentsFilePath);
                // Ensure deserialization handles potential missing properties gracefully if needed
                var loadedFragments = JsonSerializer.Deserialize<List<TextFragment>>(json);
                if (loadedFragments != null)
                {
                    Fragments.Clear(); // Clear existing before loading
                    foreach (var fragment in loadedFragments)
                    {
                        // Basic check if loaded data is valid before adding
                        // Allow empty text, but require title
                        if (!string.IsNullOrEmpty(fragment.Title) && fragment.Text != null)
                        {
                            Fragments.Add(fragment);
                        }
                        else
                        {
                             Debug.WriteLine($"Skipping invalid fragment during load: Title='{fragment.Title}', Text='{fragment.Text}'");
                        }
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                 Debug.WriteLine($"JSON Error loading fragments from {_fragmentsFilePath}: {jsonEx.Message}");
                 MessageBox.Show($"No se pudieron cargar los fragmentos guardados debido a un error en el formato del archivo.\nError: {jsonEx.Message}", "Error al Cargar Fragmentos", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"Error loading fragments from {_fragmentsFilePath}: {ex.Message}");
                 MessageBox.Show($"No se pudieron cargar los fragmentos guardados.\nError: {ex.Message}", "Error al Cargar Fragmentos", MessageBoxButton.OK, MessageBoxImage.Warning);
                 // Consider backing up or deleting the corrupt file here
            }
        }

        private void SaveFragments()
        {
             try
            {
                // Use options for cleaner JSON output
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Fragments, options);
                File.WriteAllText(_fragmentsFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving fragments to {_fragmentsFilePath}: {ex.Message}");
                // Optionally notify the user
                 MessageBox.Show($"No se pudieron guardar los fragmentos.\nError: {ex.Message}", "Error al Guardar Fragmentos", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

         // --- Helper to find visual children (needed for getting CheckBox state) ---
         public static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T correctlyTyped)
                {
                    return correctlyTyped;
                }

                T? childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }


        // --- TreeView Collapse/Expand All ---

        /// <summary>
        /// Recursively collapses or expands TreeViewItems.
        /// </summary>
        /// <param name="items">The ItemCollection to process.</param>
        /// <param name="expand">True to expand, False to collapse.</param>
        private void CollapseOrExpandNodes(ItemCollection items, bool expand)
        {
            foreach (object? obj in items)
            {
                if (obj is TreeViewItem tvi && tvi.Tag is FileSystemItem fsi && fsi.IsDirectory)
                {
                    // Only change IsExpanded if it's different from the target state
                    // to potentially avoid unnecessary event triggers or work.
                    if (tvi.IsExpanded != expand)
                    {
                        tvi.IsExpanded = expand; // Setting to true might trigger lazy loading
                    }

                    // Determine if children are loaded (i.e., not the dummy "Cargando..." node)
                    bool childrenLoaded = tvi.HasItems && !(tvi.Items.Count == 1 && tvi.Items[0] is string);

                    // If expanding: Recurse only if the item is now expanded AND children are loaded OR it hasn't tried loading yet.
                    // This allows expansion to cascade even through not-yet-loaded nodes.
                    if (expand && tvi.IsExpanded && tvi.HasItems) // Check HasItems to avoid recursing empty folders
                    {
                        // We need to ensure children are loaded before recursing *if* they weren't loaded before
                        // Setting IsExpanded=true should trigger loading if needed via the Expanded event.
                        // However, the loading is asynchronous relative to this loop.
                        // Direct recursion might happen before children are available.
                        // A more robust solution would involve awaiting loading, but that complicates this method significantly.
                        // For now, we'll recurse directly. If ExpandAll is clicked again, deeper levels will load.
                        CollapseOrExpandNodes(tvi.Items, expand);
                    }
                    // If collapsing: Always recurse into children (if any exist).
                    else if (!expand && tvi.HasItems)
                    {
                        CollapseOrExpandNodes(tvi.Items, expand);
                    }
                }
            }
        }


        private void btnCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            CollapseOrExpandNodes(treeViewFiles.Items, false);
            UpdateStatusBarAndButtonStates("Todos los nodos contraídos."); // Update status and button states
        }

        private void btnExpandAll_Click(object sender, RoutedEventArgs e)
        {
            // Expansion can take time due to lazy loading, especially on large trees.
            this.Cursor = Cursors.Wait;
            UpdateStatusBarAndButtonStates("Expandiendo nodos..."); // Initial status update
            try
            {
                // Use Dispatcher.InvokeAsync to allow the UI to update the cursor/status
                // before starting the potentially long operation.
                Dispatcher.InvokeAsync(() => {
                    CollapseOrExpandNodes(treeViewFiles.Items, true);
                    // Update status after completion
                    UpdateStatusBarAndButtonStates("Nodos expandidos (puede requerir otro clic para niveles cargados dinámicamente).");
                    this.Cursor = Cursors.Arrow; // Restore cursor on completion
                }, System.Windows.Threading.DispatcherPriority.Background);

            }
            catch (Exception ex) // Catch unexpected errors during the process
            {
                 Debug.WriteLine($"Error expanding nodes: {ex}");
                 MessageBox.Show($"Ocurrió un error al expandir los nodos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 UpdateStatusBarAndButtonStates("Error al expandir nodos.");
                 this.Cursor = Cursors.Arrow; // Ensure cursor is restored on error
            }
            // Note: Cursor is restored inside the Dispatcher call for the success case,
            // and in the catch block for the error case.
        }

        // --- TreeView Collapse/Expand Current ---
        private void btnCollapseCurrent_Click(object sender, RoutedEventArgs e)
        {
            if (treeViewFiles.SelectedItem is TreeViewItem selectedTvi && selectedTvi.Tag is FileSystemItem selectedFsi && selectedFsi.IsDirectory)
            {
                 if (selectedTvi.IsExpanded)
                 {
                    selectedTvi.IsExpanded = false;
                    UpdateStatusBarAndButtonStates($"'{selectedFsi.Name}' contraído."); // Also updates button states
                 }
            }
        }

        private void btnExpandCurrent_Click(object sender, RoutedEventArgs e)
        {
             if (treeViewFiles.SelectedItem is TreeViewItem selectedTvi && selectedTvi.Tag is FileSystemItem selectedFsi && selectedFsi.IsDirectory)
            {
                 if (!selectedTvi.IsExpanded)
                 {
                    selectedTvi.IsExpanded = true; // This will trigger lazy loading if needed via the Expanded event
                    UpdateStatusBarAndButtonStates($"'{selectedFsi.Name}' expandido."); // Also updates button states
                 }
            }
        }


        // --- NEW: ListView Double-Click Handler for Editing Fragments ---
        private void listViewFragments_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Ensure the source of the double click is within a ListViewItem
            var originalSource = e.OriginalSource as DependencyObject;
            ListViewItem? listViewItem = null;

            // Traverse up the visual tree to find the ListViewItem
            while (originalSource != null && originalSource != listViewFragments)
            {
                if (originalSource is ListViewItem item)
                {
                    listViewItem = item;
                    break;
                }
                // Check if the parent is null before getting it
                var parent = VisualTreeHelper.GetParent(originalSource);
                if (parent == null) break; // Stop if we reach the top without finding ListViewItem
                originalSource = parent;
            }

            // Check if we found a ListViewItem and it has a TextFragment context
            if (listViewItem?.DataContext is TextFragment fragmentToEdit)
            {
                // Create and show the dialog
                var dialog = new EditFragmentDialog(fragmentToEdit) { Owner = this };
                bool? dialogResult = dialog.ShowDialog();

                // If the user clicked Save
                if (dialogResult == true)
                {
                    // Get the edited data
                    var editedData = dialog.EditedFragment;

                    // Update the original fragment in the ObservableCollection
                    // Since TextFragment now implements INotifyPropertyChanged,
                    // updating properties should refresh the UI automatically.
                    fragmentToEdit.Title = editedData.Title;
                    fragmentToEdit.Text = editedData.Text;

                    // Persist the changes
                    SaveFragments();

                    // Optional: Provide user feedback
                    UpdateStatusBarAndButtonStates($"Fragmento '{fragmentToEdit.Title}' actualizado.");
                }
                // No action needed if Cancel was clicked
            }
        }
        // --- END NEW ---


    } // End class MainWindow
} // End namespace MakingVibe



