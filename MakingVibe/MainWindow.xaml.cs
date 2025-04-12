using MakingVibe.Models; // Use the model namespace
using MakingVibe.Services; // Use the services namespace
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using CheckBox = System.Windows.Controls.CheckBox;
using Clipboard = System.Windows.Clipboard;
using Cursors = System.Windows.Input.Cursors;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using WinForms = System.Windows.Forms; // Alias for FolderBrowserDialog


namespace MakingVibe
{
    public partial class MainWindow : Window
    {
        // --- Services ---
        private readonly FileSystemService _fileSystemService;
        private readonly AiCopyService _aiCopyService;
        private readonly SettingsService _settingsService;
        private readonly GitService _gitService;

        // --- UI State & Data ---
        private string? rootPath;
        private readonly ObservableCollection<FileSystemItem> selectedItems = new();
        private readonly Dictionary<string, TreeViewItem> pathToTreeViewItemMap = new();

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

            // Bind commands
            CommandBindings.Add(new CommandBinding(RefreshCommand, btnRefresh_Click, CanExecuteRefresh));
            CommandBindings.Add(new CommandBinding(DeleteCommand, btnDelete_Click, CanExecuteDelete));
            // Add more CommandBindings here...

            // Update status bar when selection changes
            selectedItems.CollectionChanged += (s, e) => UpdateStatusBarAndButtonStates();

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
        }

        // Save settings on closing (optional, could save on path change)
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _settingsService.SaveLastRootPath(rootPath);
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
            if (e.NewValue is TreeViewItem { Tag: FileSystemItem fsi }) // Use pattern matching
            {
                await ShowPreviewAsync(fsi);
            }
            // Handle deselection or selection of non-FSI items
            else if (e.NewValue == null) {
                 await ShowPreviewAsync(null); // Clear preview if nothing is selected
            }
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
            }
            e.Handled = true; // Prevent bubbling
        }

        // --- Context Menu ---
        private void TreeViewContextMenu_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            bool isItemSelected = treeViewFiles.SelectedItem != null;
            FileSystemItem? selectedFsi = (treeViewFiles.SelectedItem as TreeViewItem)?.Tag as FileSystemItem;
            bool isTextFileSelected = selectedFsi != null && !selectedFsi.IsDirectory && FileHelper.IsTextFile(selectedFsi.Path);

            // Enable actions based on selection state
            ctxCopyText.IsEnabled = selectedItems.Count > 0 && selectedItems.Any(item => item.IsDirectory || FileHelper.IsTextFile(item.Path));
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
                    IsDirectory = true
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
            // Use FileSystemService to get the data
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
                var childTreeViewItem = CreateTreeViewItemUI(fsi);
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

            // Reload children UI (this now includes the parent state check)
            LoadChildrenUI(nodeToRefresh, fsi.Path);

            // Restore expansion state if needed
            nodeToRefresh.IsExpanded = wasExpanded;

             // Re-sync checkbox state for the refreshed node itself based on selection
             if (nodeToRefresh.Header is StackPanel sp && sp.Children[0] is CheckBox cb) {
                 cb.IsChecked = selectedItems.Contains(fsi);
             }


            UpdateStatusBarAndButtonStates($"Nodo '{fsi.Name}' refrescado.");
        }

        /// <summary>
        /// Creates a TreeViewItem UI element for a given FileSystemItem.
        /// </summary>
        private TreeViewItem CreateTreeViewItemUI(FileSystemItem fsi)
        {
            var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var checkbox = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
                Tag = fsi, // Tag the checkbox with the data item
                IsChecked = selectedItems.Contains(fsi) // Sync check state with backing collection
            };
            checkbox.Checked += Checkbox_Checked;
            checkbox.Unchecked += Checkbox_Unchecked;

            var typeIndicator = new TextBlock
            {
                Text = fsi.IsDirectory ? "[D] " : "[F] ",
                FontWeight = FontWeights.SemiBold,
                Foreground = fsi.IsDirectory ? Brushes.DarkGoldenrod : Brushes.DarkSlateBlue,
                VerticalAlignment = VerticalAlignment.Center
            };

            var textBlock = new TextBlock
            {
                Text = fsi.Name,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = fsi.Path // Show full path on hover
            };

            stackPanel.Children.Add(checkbox);
            stackPanel.Children.Add(typeIndicator);
            stackPanel.Children.Add(textBlock);

            var treeViewItem = new TreeViewItem
            {
                Header = stackPanel,
                Tag = fsi // Tag the TreeViewItem itself with the data item
            };

            if (fsi.IsDirectory)
            {
                treeViewItem.Expanded += TreeViewItem_Expanded;
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
                         if (descendantTreeViewItem.Header is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is CheckBox cb)
                         {
                             if (cb.IsChecked != select) cb.IsChecked = select;
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
                            if (childTreeViewItem.Header is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is CheckBox cb)
                            {
                                if (cb.IsChecked != select) cb.IsChecked = select;
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
            if (string.IsNullOrEmpty(parentPath)) return;

            if (!pathToTreeViewItemMap.TryGetValue(parentPath, out var parentTvi)) return;
            if (parentTvi.Tag is not FileSystemItem parentFsi || !parentFsi.IsDirectory) return;

            var children = cachedChildren ?? _fileSystemService.GetDirectoryChildren(parentPath);
            if (children == null) return;

            var directChildren = children.ToList();
            // --- MODIFY PARENT CHECK LOGIC: Check based on ALL direct children (files AND directories) ---
            var directChildFilesAndDirs = directChildren.Where(c => !c.IsDirectory || Directory.Exists(c.Path)).ToList(); // Include valid directories

            bool shouldBeChecked = false;
            if (directChildFilesAndDirs.Any())
            {
                // Parent should be checked if ALL direct children (files and valid dirs) are selected
                shouldBeChecked = directChildFilesAndDirs.All(item => selectedItems.Contains(item));
            }
            // If no direct children, parent state isn't auto-managed by this method.
            // --- END MODIFY PARENT CHECK LOGIC ---


            if (parentTvi.Header is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is CheckBox parentCheckbox)
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

            UpdateParentDirectorySelectionState(parentFsi); // Recurse upwards
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
                        if (treeViewItem.Header is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is CheckBox cb)
                        {
                            if (cb.IsChecked == true) cb.IsChecked = false;
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
                tabControlMain.SelectedIndex = 0; // Ensure preview tab is selected
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
            tabControlMain.SelectedIndex = 0; // Switch to preview tab
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
                else
                {
                    statusBarText.Text = "Listo.";
                }

                // Update selection count
                statusSelectionCount.Text = $"Seleccionados: {selectedItems.Count}";

                // Update button states based on selection and clipboard
                bool hasSelection = selectedItems.Count > 0;
                bool hasSingleSelection = selectedItems.Count == 1;
                // Paste enabled if clipboard has items and *any* valid target is selected
                bool canPaste = clipboardItems.Count > 0
                                && treeViewFiles.SelectedItem is TreeViewItem { Tag: FileSystemItem fsi } ;
                                // We'll determine the actual target dir inside btnPaste_Click

                // AI Copy Button: Enabled if any selected item is a text file OR a directory
                bool canCopyText = hasSelection && selectedItems.Any(item => item.IsDirectory || FileHelper.IsTextFile(item.Path));

                btnCopyText.IsEnabled = canCopyText;
                btnCopy.IsEnabled = hasSelection;
                btnCut.IsEnabled = hasSelection;
                btnDelete.IsEnabled = hasSelection;
                btnRename.IsEnabled = hasSingleSelection;
                btnPaste.IsEnabled = canPaste;
                btnClearSelection.IsEnabled = hasSelection;

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

        private async void btnCopyText_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("No hay archivos o carpetas seleccionados.", "Copiar Texto (AI)", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(rootPath))
            {
                 MessageBox.Show("No se ha establecido una carpeta raíz.", "Copiar Texto (AI)", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show("No se encontraron archivos de texto válidos en la selección.", "Copiar Texto (AI)", MessageBoxButton.OK, MessageBoxImage.Information);
                    UpdateStatusBarAndButtonStates("No se encontraron archivos de texto."); // Update status
                    return; // Exit early
                }

                // Copy to clipboard (must be done on UI thread)
                Clipboard.SetText(result.Value.Output);

                UpdateStatusBarAndButtonStates($"Contenido de {result.Value.TextFileCount} archivo(s) copiado al portapapeles.");
                MessageBox.Show($"Se ha copiado el mapa y el contenido de {result.Value.TextFileCount} archivo(s) de texto al portapapeles.", "Copiar Texto (AI)", MessageBoxButton.OK, MessageBoxImage.Information);
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
            if (treeViewFiles.SelectedItem is not TreeViewItem selectedTreeViewItem || selectedTreeViewItem.Tag is not FileSystemItem targetFsi)
            {
                MessageBox.Show("Seleccione un directorio destino válido en el árbol para pegar.", "Pegar", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                            var targetToDeleteFsi = new FileSystemItem { Name = targetName, Path = fullTargetPath, Type = "", IsDirectory = itemToPaste.IsDirectory };
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
                         LoadDirectoryTreeUI(rootPath);
                     }
                     else {
                         LoadDirectoryTreeUI(rootPath);
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
                        var item = itemsToDelete.FirstOrDefault(i => i.Path.Equals(itemPath, StringComparison.OrdinalIgnoreCase));
                        if(item == null) continue;

                        try
                        {
                            string? parentPath = _fileSystemService.GetDirectoryName(item.Path);
                            _fileSystemService.DeleteItem(item);

                            pathToTreeViewItemMap.Remove(item.Path);

                            if (item.IsDirectory) {
                                var descendantPaths = pathToTreeViewItemMap.Keys
                                    .Where(k => k.StartsWith(item.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                                    .ToList();
                                foreach(var descendantPath in descendantPaths) {
                                    pathToTreeViewItemMap.Remove(descendantPath);
                                }
                            }

                            if (parentPath != null) parentPathsToRefresh.Add(parentPath);
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

                    if(parentPathsToRefresh.Any() && !string.IsNullOrEmpty(rootPath))
                    {
                        bool refreshedOk = false;
                        foreach(var parentPath in parentPathsToRefresh)
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
                         if (!refreshedOk && parentPathsToRefresh.Any(p => p.Equals(rootPath, StringComparison.OrdinalIgnoreCase))) {
                            LoadDirectoryTreeUI(rootPath);
                            refreshedOk = true;
                         }
                         else if (!refreshedOk && deleteCount > 0) {
                             LoadDirectoryTreeUI(rootPath);
                         }
                    }
                    else if (deleteCount > 0 && !string.IsNullOrEmpty(rootPath)) {
                         LoadDirectoryTreeUI(rootPath);
                    }

                    UpdateStatusBarAndButtonStates($"{deleteCount} elemento(s) eliminado(s).");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error inesperado durante la eliminación: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatusBarAndButtonStates("Error al eliminar.");
                    if(!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath);
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                    UpdateStatusBarAndButtonStates();
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
                if (newName.Equals(itemToRename.Name, StringComparison.Ordinal)) return;

                string newPath = _fileSystemService.CombinePath(directory, newName);

                if ((itemToRename.IsDirectory && _fileSystemService.DirectoryExists(newPath)) || (!itemToRename.IsDirectory && _fileSystemService.FileExists(newPath)))
                {
                    MessageBox.Show($"Ya existe un elemento llamado '{newName}' en esta ubicación.", "Renombrar Fallido", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                UpdateStatusBarAndButtonStates($"Renombrando '{itemToRename.Name}'...");
                this.Cursor = Cursors.Wait;
                string oldNameForStatus = itemToRename.Name;

                 pathToTreeViewItemMap.Remove(oldPath);
                 if(itemToRename.IsDirectory) {
                      var descendantPaths = pathToTreeViewItemMap.Keys
                           .Where(k => k.StartsWith(oldPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                           .ToList();
                      foreach(var descendantPath in descendantPaths) {
                          pathToTreeViewItemMap.Remove(descendantPath);
                      }
                 }
                 selectedItems.Remove(itemToRename);

                try
                {
                    _fileSystemService.RenameItem(oldPath, newPath, itemToRename.IsDirectory);

                     if (pathToTreeViewItemMap.TryGetValue(directory, out var parentNode))
                     {
                          if (parentNode.Tag is FileSystemItem parentFsi)
                          {
                             RefreshNodeUI(parentNode, parentFsi);

                              Dispatcher.InvokeAsync(() => {
                                  if(pathToTreeViewItemMap.TryGetValue(newPath, out var newTvi))
                                  {
                                      newTvi.IsSelected = true;
                                      if (newTvi.Tag is FileSystemItem newFsi && !selectedItems.Contains(newFsi))
                                      {
                                          if (newTvi.Header is StackPanel sp && sp.Children[0] is CheckBox cb) cb.IsChecked = true;
                                      }
                                  }
                              }, System.Windows.Threading.DispatcherPriority.Background);
                          } else {
                               if(!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath);
                          }

                     }
                     else if (directory.Equals(rootPath, StringComparison.OrdinalIgnoreCase)) {
                          if(!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath);
                     }
                     else
                     {
                           if(!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath);
                     }

                    UpdateStatusBarAndButtonStates($"'{oldNameForStatus}' renombrado a '{newName}'.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al renombrar '{oldNameForStatus}':\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatusBarAndButtonStates($"Error al renombrar '{oldNameForStatus}'.");
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
                    UpdateStatusBarAndButtonStates();
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

    } // End class MainWindow
} // End namespace MakingVibe