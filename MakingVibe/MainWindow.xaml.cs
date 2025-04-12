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
            if (!string.IsNullOrEmpty(initialDir)) // Check if MyDocuments exists
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
                     // Check if the directory actually contains anything before adding dummy node
                     // This avoids the dummy node in empty directories, but requires an extra IO check.
                     // Keep it simple for now: always add dummy node.
                    childTreeViewItem.Items.Add("Cargando...");
                }
            }
        }

        /// <summary>
        /// Refreshes a specific node in the TreeView UI.
        /// </summary>
        private void RefreshNodeUI(TreeViewItem nodeToRefresh, FileSystemItem fsi)
        {
            bool wasExpanded = nodeToRefresh.IsExpanded;
            nodeToRefresh.Items.Clear(); // Clear existing UI children

            // Clear potentially outdated entries from the map for children of this node
            var keysToRemove = pathToTreeViewItemMap.Keys
                                .Where(k => k.StartsWith(fsi.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                                .ToList();
            foreach (var key in keysToRemove)
            {
                pathToTreeViewItemMap.Remove(key);
            }

            // Reload children UI
            LoadChildrenUI(nodeToRefresh, fsi.Path);

            // Restore expansion state if needed
            nodeToRefresh.IsExpanded = wasExpanded;

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
                IsChecked = selectedItems.Contains(fsi) // Sync check state
            };
            checkbox.Checked += Checkbox_Checked;
            checkbox.Unchecked += Checkbox_Unchecked;

            // Add visual indicator [D] or [F]
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

            // Attach lazy loading handler only to directories
            if (fsi.IsDirectory)
            {
                treeViewItem.Expanded += TreeViewItem_Expanded;
            }

            return treeViewItem;
        }


        // --- Selection Handling (UI Related) ---

        private void Checkbox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox { Tag: FileSystemItem fsi } checkbox)
            {
                if (!selectedItems.Contains(fsi))
                {
                    selectedItems.Add(fsi); // Triggers CollectionChanged -> UpdateStatusBarAndButtonStates
                }
            }
        }

        private void Checkbox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox { Tag: FileSystemItem fsi } checkbox)
            {
                selectedItems.Remove(fsi); // Triggers CollectionChanged -> UpdateStatusBarAndButtonStates
            }
        }

        /// <summary>
        /// Clears the selection collection and unchecks corresponding checkboxes in the UI.
        /// </summary>
        private void ClearAllSelectionsUI()
        {
            // Create a copy to avoid issues while modifying the collection being iterated indirectly
            var itemsToUncheck = selectedItems.ToList();
            selectedItems.Clear(); // Clear the backing collection (triggers update)

            // Uncheck the corresponding checkboxes in the UI
            foreach (var item in itemsToUncheck)
            {
                if (pathToTreeViewItemMap.TryGetValue(item.Path, out var treeViewItem))
                {
                    if (treeViewItem.Header is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is CheckBox cb)
                    {
                        cb.IsChecked = false; // Directly uncheck UI element
                    }
                }
            }
            // Status bar update is handled by selectedItems.Clear() via CollectionChanged
        }


        // --- File Preview (UI Related) ---

        /// <summary>
        /// Shows a preview of the selected file or directory information in the TextBox.
        /// </summary>
        private async Task ShowPreviewAsync(FileSystemItem fsi)
        {
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
            bool isDirectorySelected = (treeViewFiles.SelectedItem as TreeViewItem)?.Tag is FileSystemItem fsi && fsi.IsDirectory;
            bool canPaste = clipboardItems.Count > 0 && treeViewFiles.SelectedItem != null; // Simplified: Can paste if *any* item is selected as target parent


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


            this.Cursor = Cursors.Wait;
            statusBarText.Text = "Recopilando archivos de texto...";
            btnCopyText.IsEnabled = false; // Disable button during operation

            try
            {
                // Use Task.Run for the potentially long-running service call
                var result = await Task.Run(() => _aiCopyService.GenerateAiClipboardContentAsync(selectedItems, rootPath));

                if (result == null)
                {
                    MessageBox.Show("No se encontraron archivos de texto válidos en la selección.", "Copiar Texto (AI)", MessageBoxButton.OK, MessageBoxImage.Information);
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
            // Optional: Add visual cue for cut items (e.g., graying out TreeViewItem)
            // ApplyCutVisuals();
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

            // Determine destination directory path
            string destinationDir = targetFsi.IsDirectory ? targetFsi.Path : (_fileSystemService.GetDirectoryName(targetFsi.Path) ?? rootPath ?? "");
             if(string.IsNullOrEmpty(destinationDir))
             {
                  MessageBox.Show($"No se pudo determinar el directorio destino.", "Error al Pegar", MessageBoxButton.OK, MessageBoxImage.Error);
                  return;
             }

            UpdateStatusBarAndButtonStates("Pegando elementos...");
            this.Cursor = Cursors.Wait;
            bool refreshNeeded = false;
            string? refreshParentPath = null; // Track the parent where paste occurred

            try
            {
                int pasteCount = 0;
                var itemsToProcess = clipboardItems.ToList(); // Process a copy

                foreach (var itemToPaste in itemsToProcess)
                {
                    string sourcePath = itemToPaste.Path;
                    string targetName = _fileSystemService.GetFileName(sourcePath);
                    string fullTargetPath = _fileSystemService.CombinePath(destinationDir, targetName);

                    // Basic checks
                    if (string.Equals(sourcePath, fullTargetPath, StringComparison.OrdinalIgnoreCase)) continue; // Pasting onto itself
                    if ((itemToPaste.IsDirectory && !_fileSystemService.DirectoryExists(sourcePath)) || (!itemToPaste.IsDirectory && !_fileSystemService.FileExists(sourcePath)))
                    {
                        Debug.WriteLine($"Source path not found for paste: {sourcePath}"); continue; // Source gone
                    }
                     if (itemToPaste.IsDirectory && destinationDir.StartsWith(sourcePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                     {
                         MessageBox.Show($"No se puede {(isCutOperation ? "mover" : "copiar")} la carpeta '{itemToPaste.Name}' dentro de sí misma o de una subcarpeta.", "Operación Inválida", MessageBoxButton.OK, MessageBoxImage.Warning); continue;
                     }


                    // Handle conflicts
                    bool targetExists = itemToPaste.IsDirectory ? _fileSystemService.DirectoryExists(fullTargetPath) : _fileSystemService.FileExists(fullTargetPath);
                    if (targetExists)
                    {
                        var overwriteResult = MessageBox.Show($"El elemento '{targetName}' ya existe en el destino.\n¿Desea sobrescribirlo?", "Conflicto al Pegar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (overwriteResult == MessageBoxResult.No) continue; // Skip

                        // Try deleting existing target
                        try
                        {
                            var targetToDeleteFsi = new FileSystemItem { Name = targetName, Path = fullTargetPath, Type = "", IsDirectory = itemToPaste.IsDirectory };
                            _fileSystemService.DeleteItem(targetToDeleteFsi);
                        }
                        catch (Exception delEx)
                        {
                            MessageBox.Show($"No se pudo sobrescribir '{targetName}'. Error: {delEx.Message}", "Error al Pegar", MessageBoxButton.OK, MessageBoxImage.Error); continue;
                        }
                    }

                    // Perform Copy/Move using the service
                    try
                    {
                        if (isCutOperation)
                        {
                             _fileSystemService.MoveItem(sourcePath, fullTargetPath, itemToPaste.IsDirectory);
                        }
                        else
                        {
                            _fileSystemService.CopyItem(sourcePath, fullTargetPath, itemToPaste.IsDirectory);
                        }
                        pasteCount++;
                        refreshNeeded = true; // Mark that UI refresh is needed
                        refreshParentPath = destinationDir; // Target directory needs refresh
                    }
                    catch (Exception opEx)
                    {
                        MessageBox.Show($"Error al {(isCutOperation ? "mover" : "copiar")} '{itemToPaste.Name}':\n{opEx.Message}", "Error al Pegar", MessageBoxButton.OK, MessageBoxImage.Error);
                         // Consider breaking or continuing based on requirements
                         break; // Stop on first error
                    }
                } // end foreach

                // Clear clipboard only if Cut was successful and items were processed
                if (isCutOperation && pasteCount > 0) // Check pasteCount > 0 to avoid clearing if all failed
                {
                    clipboardItems.Clear();
                    isCutOperation = false;
                     // Optional: Remove visual cue for cut items
                     // RemoveCutVisuals();
                }

                // Refresh UI if changes were made
                if (refreshNeeded && refreshParentPath != null)
                {
                    if (pathToTreeViewItemMap.TryGetValue(refreshParentPath, out var parentNode))
                    {
                        RefreshNodeUI(parentNode, (FileSystemItem)parentNode.Tag); // Refresh the parent node
                    }
                    else {
                         LoadDirectoryTreeUI(rootPath!); // Fallback to full refresh
                    }
                }

                 UpdateStatusBarAndButtonStates($"{pasteCount} elemento(s) pegado(s) en '{_fileSystemService.GetFileName(destinationDir)}'.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error inesperado durante la operación de pegado:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBarAndButtonStates("Error al pegar.");
                // Consider full refresh on major error
                 if(!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
                UpdateStatusBarAndButtonStates(); // Update buttons etc.
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
                var itemsToDelete = selectedItems.ToList(); // Process a copy
                var parentPathsToRefresh = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    foreach (var item in itemsToDelete)
                    {
                        try
                        {
                            string? parentPath = _fileSystemService.GetDirectoryName(item.Path);
                            _fileSystemService.DeleteItem(item); // Use service

                            // Remove from selection *after* successful deletion
                            // Note: Removing from selectedItems automatically triggers status update
                            selectedItems.Remove(item);

                            // Remove from map
                             pathToTreeViewItemMap.Remove(item.Path); // Remove the item itself

                            // Mark parent for refresh
                            if (parentPath != null) parentPathsToRefresh.Add(parentPath);

                            deleteCount++;
                        }
                        catch (Exception itemEx)
                        {
                            MessageBox.Show($"No se pudo eliminar '{item.Name}'.\nError: {itemEx.Message}\n\nAsegúrese de que no esté en uso.", "Error al Eliminar", MessageBoxButton.OK, MessageBoxImage.Error);
                            // Decide whether to continue or break
                        }
                    }

                    // Refresh parent nodes in the UI
                    if(parentPathsToRefresh.Any())
                    {
                        foreach(var parentPath in parentPathsToRefresh)
                        {
                            if (pathToTreeViewItemMap.TryGetValue(parentPath, out var parentNode))
                            {
                                RefreshNodeUI(parentNode, (FileSystemItem)parentNode.Tag);
                            } else if(string.Equals(parentPath, rootPath, StringComparison.OrdinalIgnoreCase)) {
                                // If parent was root and root node wasn't found (edge case), refresh all
                                if(!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath);
                            }
                        }
                    }
                    else if (deleteCount > 0 && !string.IsNullOrEmpty(rootPath)) {
                         // Fallback refresh if parents weren't found but items were deleted
                         LoadDirectoryTreeUI(rootPath);
                    }


                    UpdateStatusBarAndButtonStates($"{deleteCount} elemento(s) eliminado(s)."); // Update status bar
                }
                catch (Exception ex) // Catch unexpected errors during the loop or refresh
                {
                    MessageBox.Show($"Error inesperado durante la eliminación: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatusBarAndButtonStates("Error al eliminar.");
                    if(!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath); // Try to refresh to consistent state
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                    // UpdateStatusBarAndButtonStates(); // Status update already happens via selectedItems.Remove
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

            // Use the existing RenameDialog
            var dialog = new RenameDialog(itemToRename.Name) { Owner = this };

            if (dialog.ShowDialog() == true)
            {
                string newName = dialog.NewName; // Assumes dialog validates emptiness and invalid chars

                if (newName.Equals(itemToRename.Name, StringComparison.Ordinal)) return; // Name didn't change

                string newPath = _fileSystemService.CombinePath(directory, newName);

                // Check if new name already exists using FileSystemService
                if ((itemToRename.IsDirectory && _fileSystemService.DirectoryExists(newPath)) || (!itemToRename.IsDirectory && _fileSystemService.FileExists(newPath)))
                {
                    MessageBox.Show($"Ya existe un elemento llamado '{newName}' en esta ubicación.", "Renombrar Fallido", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                UpdateStatusBarAndButtonStates($"Renombrando '{itemToRename.Name}'...");
                this.Cursor = Cursors.Wait;

                try
                {
                    // Perform rename using the service
                    _fileSystemService.RenameItem(oldPath, newPath, itemToRename.IsDirectory);

                    // Update the FileSystemItem in memory (important!)
                    string oldName = itemToRename.Name; // Store old name for status message
                    itemToRename.Name = newName;
                    itemToRename.Path = newPath;


                    // Refresh the parent node in the tree view UI
                     if (pathToTreeViewItemMap.TryGetValue(directory, out var parentNode))
                     {
                         // Refresh the parent node which will re-create the child item UI
                         RefreshNodeUI(parentNode, (FileSystemItem)parentNode.Tag);

                         // Optionally try to re-select the renamed item visually
                          Dispatcher.InvokeAsync(() => { // Ensure UI update happens after refresh
                             if(pathToTreeViewItemMap.TryGetValue(newPath, out var newTvi))
                             {
                                 newTvi.IsSelected = true;
                             }
                         }, System.Windows.Threading.DispatcherPriority.Background);

                     }
                     else
                     {
                          if(!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath); // Fallback refresh
                     }

                    // Update the main path-to-item map (remove old, new one added during refresh)
                    pathToTreeViewItemMap.Remove(oldPath);


                    UpdateStatusBarAndButtonStates($"'{oldName}' renombrado a '{newName}'.");
                    // No need for success message box? Status bar is enough.
                    // MessageBox.Show("Elemento renombrado con éxito.", "Renombrar", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al renombrar '{itemToRename.Name}':\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatusBarAndButtonStates($"Error al renombrar '{itemToRename.Name}'.");
                    // Refresh parent or whole tree to ensure consistency after error
                     if (pathToTreeViewItemMap.TryGetValue(directory, out var parentNode))
                     {
                         RefreshNodeUI(parentNode, (FileSystemItem)parentNode.Tag);
                     } else if(!string.IsNullOrEmpty(rootPath)){
                         LoadDirectoryTreeUI(rootPath);
                     }
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                    UpdateStatusBarAndButtonStates(); // Update buttons etc.
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