using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel; // Required for INotifyPropertyChanged (optional but good practice)
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text; // For StringBuilder
using System.Threading.Tasks; // For Task, async, await
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input; // For Commands and Cursor
using System.Windows.Media; // For Brushes etc.
// Use specific using for FolderBrowserDialog to avoid conflicts
using WinForms = System.Windows.Forms;

namespace MakingVibe
{
    public partial class MainWindow : Window
    {
        private string? rootPath;
        // Use ObservableCollection for automatic UI updates when items are added/removed
        private readonly ObservableCollection<FileSystemItem> selectedItems = new();
        // Keep track of TreeViewItems for easier manipulation (like unchecking)
        private readonly Dictionary<string, TreeViewItem> pathToTreeViewItemMap = new();

        private string? clipboardPath; // Path for copy/cut
        private bool isCutOperation;
        private List<FileSystemItem> clipboardItems = new(); // Store actual items for copy/cut

        // List of folders to ignore
        private readonly HashSet<string> ignoredFolderNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".vscode", ".idea",
            "bin", "obj",
            "node_modules", "__pycache__",
            "target", // Common for Rust/Java
            "build"   // Common for C++/CMake etc.
        };

        // List of file extensions considered as text for the AI copy feature
        private readonly HashSet<string> textFileExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            // Basic text files
            ".txt", ".log", ".md", ".csv", ".tsv", ".rtf",
            // Web development
            ".html", ".htm", ".css", ".js", ".jsx", ".ts", ".tsx", ".json", ".xml", ".yaml", ".yml", ".svg", ".vue", ".svelte",
            // Configuration files
            ".config", ".ini", ".toml", ".conf", ".properties", ".env", ".editorconfig", ".csproj", ".sln", ".xaml", ".gradle", ".settings", ".props",
            // Scripts and shell
            ".bat", ".cmd", ".ps1", ".sh", ".bash", ".zsh", ".fish", ".py", ".rb", ".php", ".pl", ".lua", ".tcl",
            // Programming languages
            ".cs", ".java", ".c", ".cpp", ".h", ".hpp", ".go", ".rs", ".swift", ".kt", ".scala", ".dart", ".groovy", ".m", ".r", ".sql", ".vb", ".fs", ".pas",
            // Other common text formats
            ".gitignore", ".dockerignore", ".gitattributes", ".sql", ".readme", ".inf", ".tex"
            // Add more as needed
        };

        // Commands for Key Bindings
        public static RoutedCommand RefreshCommand = new RoutedCommand();
        public static RoutedCommand DeleteCommand = new RoutedCommand();

        public MainWindow()
        {
            InitializeComponent();
            // Bind commands
            CommandBindings.Add(new CommandBinding(RefreshCommand, btnRefresh_Click, (s, e) => e.CanExecute = !string.IsNullOrEmpty(rootPath)));
            CommandBindings.Add(new CommandBinding(DeleteCommand, btnDelete_Click, (s, e) => e.CanExecute = selectedItems.Count > 0));

            // Update status bar when selection changes
            selectedItems.CollectionChanged += (s, e) => UpdateStatusBarAndButtonStates();

            // Set initial status
            UpdateStatusBarAndButtonStates("Listo.");
            SetWindowTitleFromGit();
        }

        // --- Window Load/Close ---

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            if (!string.IsNullOrEmpty(rootPath) && Directory.Exists(rootPath))
            {
                UpdateStatusBarAndButtonStates($"Cargando directorio: {rootPath}...");
                LoadDirectoryTree(rootPath); // Load last path if valid
                UpdateStatusBarAndButtonStates("Listo.");
            }
            else
            {
                txtCurrentPath.Text = "Ruta actual: (Seleccione una carpeta raíz)";
                UpdateStatusBarAndButtonStates("Por favor, seleccione una carpeta raíz.");
            }
        }

        private void LoadSettings()
        {
            // Consider using a more robust settings mechanism if needed
            try
            {
                // Simple way using built-in settings (add reference to System.Configuration if not present)
                // You might need to add a Settings.settings file to your project properties
                // rootPath = MakingVibe.Properties.Settings.Default.LastRootPath;
                // For simplicity without adding Settings file now, use a simple file:
                string settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "makingvibe.settings");
                if (File.Exists(settingsFile))
                {
                    rootPath = File.ReadAllText(settingsFile)?.Trim();
                }

            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"Error loading settings: {ex.Message}");
                 rootPath = null; // Reset if error
            }
        }

        private void SaveSettings()
        {
             try
            {
                // MakingVibe.Properties.Settings.Default.LastRootPath = rootPath;
                // MakingVibe.Properties.Settings.Default.Save();
                string settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "makingvibe.settings");
                if (!string.IsNullOrEmpty(rootPath))
                {
                    File.WriteAllText(settingsFile, rootPath);
                }
                else if(File.Exists(settingsFile))
                {
                    File.Delete(settingsFile); // Clear setting if no path
                }
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        // --- UI Event Handlers ---

        private void btnSelectRoot_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Seleccione la carpeta raíz del proyecto",
                UseDescriptionForTitle = true, // Use description as title
                ShowNewFolderButton = true     // Allow creating new folders
            };

            // Try setting initial directory
             if (!string.IsNullOrEmpty(rootPath) && Directory.Exists(rootPath))
             {
                 dialog.SelectedPath = rootPath;
             }
             else if (!string.IsNullOrEmpty(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))) // Fallback
             {
                  dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
             }


            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                rootPath = dialog.SelectedPath;
                txtCurrentPath.Text = $"Ruta actual: {rootPath}";
                UpdateStatusBarAndButtonStates($"Cargando directorio: {rootPath}...");
                LoadDirectoryTree(rootPath);
                SaveSettings(); // Save the newly selected path
                UpdateStatusBarAndButtonStates("Listo.");
            }
        }

        private void btnClearSelection_Click(object sender, RoutedEventArgs e)
        {
            ClearAllSelections();
        }

        private void treeViewFiles_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Renamed from TreeViewItem_Selected to avoid confusion with item selection *event* vs *state*
            // This handles showing the preview when an item is clicked in the tree
            if (e.NewValue is TreeViewItem treeViewItem && treeViewItem.Tag is FileSystemItem fsi)
            {
                ShowPreview(fsi);
            }
            else
            {
                txtFileContent.Text = string.Empty; // Clear preview if selection is lost or invalid
            }
        }

        private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            // Handles lazy loading when a node is expanded
            if (sender is TreeViewItem treeViewItem && treeViewItem.Tag is FileSystemItem fsi && fsi.IsDirectory)
            {
                // Check if it needs loading (contains the dummy item)
                if (treeViewItem.Items.Count == 1 && treeViewItem.Items[0] is string loadingText && loadingText == "Cargando...")
                {
                    treeViewItem.Items.Clear(); // Remove dummy item
                    LoadChildren(treeViewItem, fsi.Path);
                }
            }
            e.Handled = true; // Prevent bubbling
        }

         // --- Context Menu ---
        private void TreeViewContextMenu_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Enable/Disable context menu items based on the current selection
            bool isItemSelected = treeViewFiles.SelectedItem != null;
            bool isTextFileSelected = false;
            FileSystemItem? selectedFsi = null;

            if (isItemSelected && treeViewFiles.SelectedItem is TreeViewItem tvi && tvi.Tag is FileSystemItem fsi)
            {
                 selectedFsi = fsi;
                 isTextFileSelected = !fsi.IsDirectory && IsTextFile(fsi.Path);
            }

            // Enable actions based on selection state
            ctxCopyText.IsEnabled = selectedItems.Count > 0 && selectedItems.Any(item => item.IsDirectory || IsTextFile(item.Path));
            ctxRename.IsEnabled = isItemSelected && selectedItems.Count == 1 && selectedFsi != null; // Only rename single selection
            ctxDelete.IsEnabled = selectedItems.Count > 0;
            ctxRefreshNode.IsEnabled = isItemSelected && selectedFsi != null && selectedFsi.IsDirectory; // Only refresh directories
            ctxRefreshAll.IsEnabled = !string.IsNullOrEmpty(rootPath);
        }

        private void CtxRefreshNode_Click(object sender, RoutedEventArgs e)
        {
            // Refreshes the selected node in the tree
            if (treeViewFiles.SelectedItem is TreeViewItem selectedTreeViewItem &&
                selectedTreeViewItem.Tag is FileSystemItem fsi && fsi.IsDirectory)
            {
                // Clear existing children and reload
                selectedTreeViewItem.Items.Clear();
                // Re-add the dummy item to trigger reload on next expand if desired, or load directly
                LoadChildren(selectedTreeViewItem, fsi.Path);
                 // Optionally re-expand if it was expanded
                 selectedTreeViewItem.IsExpanded = true;
                UpdateStatusBarAndButtonStates($"Nodo '{fsi.Name}' refrescado.");
            }
        }

        // --- Core Logic: Tree Loading ---

        private void LoadDirectoryTree(string path)
        {
            try
            {
                treeViewFiles.Items.Clear();
                pathToTreeViewItemMap.Clear();
                selectedItems.Clear(); // Clear selection when reloading tree

                if (!Directory.Exists(path))
                {
                    System.Windows.MessageBox.Show($"El directorio raíz especificado no existe:\n{path}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    rootPath = null;
                    txtCurrentPath.Text = "Ruta actual: (Seleccione una carpeta raíz)";
                    SaveSettings();
                    UpdateStatusBarAndButtonStates("Error al cargar el directorio raíz.");
                    return;
                }


                var rootDirectoryInfo = new DirectoryInfo(path);
                var rootFsi = new FileSystemItem
                {
                    Name = rootDirectoryInfo.Name,
                    Path = rootDirectoryInfo.FullName,
                    Type = "Directorio Raíz",
                    IsDirectory = true
                };

                var rootTreeViewItem = CreateTreeViewItem(rootFsi);
                treeViewFiles.Items.Add(rootTreeViewItem);

                // Load initial children for the root
                LoadChildren(rootTreeViewItem, rootFsi.Path);
                rootTreeViewItem.IsExpanded = true; // Expand root by default

            }
            catch (UnauthorizedAccessException)
            {
                System.Windows.MessageBox.Show($"No se tienen permisos para acceder a la carpeta:\n{path}", "Acceso Denegado", MessageBoxButton.OK, MessageBoxImage.Warning);
                 UpdateStatusBarAndButtonStates("Error de permisos al cargar.");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error inesperado al cargar el directorio:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBarAndButtonStates("Error al cargar.");
            }
            finally
            {
                 // Ensure button states are correct even if loading fails partially
                UpdateStatusBarAndButtonStates();
            }
        }

        // Loads the children (files and subdirectories) for a given TreeViewItem representing a directory
        private void LoadChildren(TreeViewItem parentTreeViewItem, string directoryPath)
        {
            try
            {
                 // Get directories, filter ignored ones, and sort
                var directories = Directory.GetDirectories(directoryPath)
                                          .Where(d => !ignoredFolderNames.Contains(Path.GetFileName(d)))
                                          .Select(d => new DirectoryInfo(d))
                                          .OrderBy(d => d.Name);

                foreach (var dirInfo in directories)
                {
                    var dirFsi = new FileSystemItem { Name = dirInfo.Name, Path = dirInfo.FullName, Type = "Directorio", IsDirectory = true };
                    var dirTreeViewItem = CreateTreeViewItem(dirFsi);
                    // Add dummy node for lazy loading
                    dirTreeViewItem.Items.Add("Cargando...");
                    parentTreeViewItem.Items.Add(dirTreeViewItem);
                }

                 // Get files, sort, and add
                var files = Directory.GetFiles(directoryPath)
                                    .Select(f => new FileInfo(f))
                                    .OrderBy(f => f.Name);

                foreach (var fileInfo in files)
                {
                    var fileFsi = new FileSystemItem { Name = fileInfo.Name, Path = fileInfo.FullName, Type = fileInfo.Extension, IsDirectory = false };
                    var fileTreeViewItem = CreateTreeViewItem(fileFsi);
                    parentTreeViewItem.Items.Add(fileTreeViewItem);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Add a node indicating access denied, but don't crash
                 parentTreeViewItem.Items.Add(new TreeViewItem { Header = "[Acceso Denegado]", Foreground = System.Windows.Media.Brushes.Gray, IsEnabled = false });
                 Debug.WriteLine($"Access denied loading children for: {directoryPath}");
            }
            catch (Exception ex)
            {
                 // Log error, maybe add an error node
                parentTreeViewItem.Items.Add(new TreeViewItem { Header = "[Error al Cargar]", Foreground = System.Windows.Media.Brushes.Red, IsEnabled = false });
                Debug.WriteLine($"Error loading children for {directoryPath}: {ex.Message}");
            }
        }


        // Creates a TreeViewItem for a FileSystemItem (file or directory)
        private TreeViewItem CreateTreeViewItem(FileSystemItem fsi)
        {
            var stackPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

            var checkbox = new System.Windows.Controls.CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
                Tag = fsi // Tag the checkbox directly with the FileSystemItem
            };
            checkbox.Checked += Checkbox_Checked;
            checkbox.Unchecked += Checkbox_Unchecked;
            // Sync checkbox state if the item is already in selectedItems (e.g., after refresh)
            checkbox.IsChecked = selectedItems.Contains(fsi);


            // Add simple visual indicator [D] or [F]
            var typeIndicator = new TextBlock
            {
                 Text = fsi.IsDirectory ? "[D] " : "[F] ",
                 FontWeight = FontWeights.SemiBold,
                 Foreground = fsi.IsDirectory ? System.Windows.Media.Brushes.DarkGoldenrod : System.Windows.Media.Brushes.DarkSlateBlue,
                 VerticalAlignment = VerticalAlignment.Center
            };

            var textBlock = new TextBlock
            {
                Text = fsi.Name,
                VerticalAlignment = VerticalAlignment.Center
            };

            stackPanel.Children.Add(checkbox);
            stackPanel.Children.Add(typeIndicator);
            stackPanel.Children.Add(textBlock);

            var treeViewItem = new TreeViewItem
            {
                Header = stackPanel,
                Tag = fsi // Tag the TreeViewItem itself with the FileSystemItem
            };

             // Store mapping for easy access
             pathToTreeViewItemMap[fsi.Path] = treeViewItem;

             // Add event handlers only if needed here (Expanded is handled above)
             if (fsi.IsDirectory)
             {
                 treeViewItem.Expanded += TreeViewItem_Expanded;
             }
             // SelectedItemChanged on TreeView handles preview now

            return treeViewItem;
        }

        // --- Selection Handling ---

        private void Checkbox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkbox && checkbox.Tag is FileSystemItem fsi)
            {
                if (!selectedItems.Contains(fsi))
                {
                    selectedItems.Add(fsi);
                }
                // No automatic recursive checking visualization - keep UI simple.
                // Recursive selection is handled during the "Copy Text" action.
            }
            // UpdateStatusBarAndButtonStates(); // Called by CollectionChanged
        }

        private void Checkbox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkbox && checkbox.Tag is FileSystemItem fsi)
            {
                selectedItems.Remove(fsi);
            }
             // UpdateStatusBarAndButtonStates(); // Called by CollectionChanged
        }

        private void ClearAllSelections()
        {
            // Create a copy to avoid issues while modifying the collection
            var itemsToUncheck = selectedItems.ToList();
            selectedItems.Clear(); // Clear the backing collection

            // Uncheck the corresponding checkboxes in the UI
            foreach(var item in itemsToUncheck)
            {
                if (pathToTreeViewItemMap.TryGetValue(item.Path, out var treeViewItem))
                {
                     if (treeViewItem.Header is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is System.Windows.Controls.CheckBox cb)
                     {
                         cb.IsChecked = false;
                     }
                }
            }
             // UpdateStatusBarAndButtonStates(); // Called by CollectionChanged
        }


        // --- File Preview ---

        private void ShowPreview(FileSystemItem fsi)
        {
            if (fsi == null)
            {
                txtFileContent.Text = string.Empty;
                return;
            }

            if (fsi.IsDirectory)
            {
                 txtFileContent.Text = $"Directorio seleccionado: {fsi.Name}\n\nRuta: {fsi.Path}";
                 return;
            }

            try
            {
                if (IsTextFile(fsi.Path))
                {
                    // Basic check for very large files to avoid freezing UI on read
                    var fileInfo = new FileInfo(fsi.Path);
                    if (fileInfo.Length > 5 * 1024 * 1024) // 5 MB limit for preview
                    {
                         txtFileContent.Text = $"--- El archivo es demasiado grande para la vista previa ({fileInfo.Length / 1024.0 / 1024.0:F2} MB) ---";
                    }
                    else
                    {
                        string content = File.ReadAllText(fsi.Path);
                        txtFileContent.Text = content;
                    }
                }
                 else if (IsImageFile(fsi.Path)) // Basic image check (optional)
                 {
                     txtFileContent.Text = $"--- Vista previa de imagen no implementada ---\nArchivo: {fsi.Name}";
                     // Could add an Image control here later
                 }
                else
                {
                    txtFileContent.Text = $"--- No se puede previsualizar este tipo de archivo ---\nTipo: {fsi.Type}\nRuta: {fsi.Path}";
                }
                tabControlMain.SelectedIndex = 0; // Switch to preview tab
            }
             catch (IOException ioEx) // More specific catch for file access issues
             {
                  txtFileContent.Text = $"Error de E/S al leer el archivo:\n{ioEx.Message}\n\nAsegúrese de que el archivo no esté en uso por otro programa.";
                  Debug.WriteLine($"IO Error previewing {fsi.Path}: {ioEx}");
             }
            catch (Exception ex)
            {
                txtFileContent.Text = $"Error inesperado al leer el archivo:\n{ex.Message}";
                 Debug.WriteLine($"Error previewing {fsi.Path}: {ex}");
            }
        }

        private bool IsTextFile(string filePath)
        {
             if (string.IsNullOrEmpty(filePath)) return false;
             string extension = Path.GetExtension(filePath); // Includes the dot "."
             if (string.IsNullOrEmpty(extension)) return false; // File has no extension

             // Handle files with no extension but common names like 'Dockerfile', 'LICENSE'
             string fileName = Path.GetFileName(filePath);
             if (fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Equals("LICENSE", StringComparison.OrdinalIgnoreCase) ||
                  fileName.Equals("README", StringComparison.OrdinalIgnoreCase))
             {
                 return true;
             }

             return textFileExtensions.Contains(extension);
        }

        private bool IsImageFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch {
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tiff" => true,
                _ => false
            };
        }

        // --- Button Actions ---

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


             // Update button states based on selection
            bool hasSelection = selectedItems.Count > 0;
            bool hasSingleSelection = selectedItems.Count == 1;
            bool canPaste = clipboardItems.Count > 0 && treeViewFiles.SelectedItem != null; // Can paste if something is copied and a target is selected

            // AI Copy Button: Enabled if any selected item is a text file OR a directory (because directories imply potential text files within)
            bool canCopyText = hasSelection && selectedItems.Any(item => item.IsDirectory || IsTextFile(item.Path));

             btnCopyText.IsEnabled = canCopyText;
             btnCopy.IsEnabled = hasSelection;
             btnCut.IsEnabled = hasSelection;
             btnDelete.IsEnabled = hasSelection;
             btnRename.IsEnabled = hasSingleSelection;
             btnPaste.IsEnabled = canPaste;
             btnClearSelection.IsEnabled = hasSelection;

             // Update command CanExecute status (for keyboard shortcuts)
             CommandManager.InvalidateRequerySuggested();
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(rootPath) && Directory.Exists(rootPath))
            {
                UpdateStatusBarAndButtonStates($"Refrescando: {rootPath}...");
                 LoadDirectoryTree(rootPath);
                 UpdateStatusBarAndButtonStates("Árbol refrescado.");
            }
            else
            {
                System.Windows.MessageBox.Show("No hay una carpeta raíz seleccionada o válida para refrescar.", "Refrescar", MessageBoxButton.OK, MessageBoxImage.Information);
                 UpdateStatusBarAndButtonStates("Seleccione una carpeta raíz.");
            }
        }

        // --- AI Text Copy Action (Async) ---

        private async void btnCopyText_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItems.Count == 0)
            {
                System.Windows.MessageBox.Show("No hay archivos o carpetas seleccionados.", "Copiar Texto (AI)", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Provide visual feedback for potentially long operation
            this.Cursor = System.Windows.Input.Cursors.Wait;
            statusBarText.Text = "Recopilando archivos de texto...";
            btnCopyText.IsEnabled = false; // Disable button during operation


            try
            {
                // Use Task.Run to perform file searching and reading off the UI thread
                var (fileMap, fileContents, textFileCount) = await Task.Run(() => CollectTextFilesAndContent());

                if (textFileCount == 0)
                {
                    System.Windows.MessageBox.Show("No se encontraron archivos de texto válidos en la selección.", "Copiar Texto (AI)", MessageBoxButton.OK, MessageBoxImage.Information);
                    return; // Exit early
                }

                // Combine map and content
                var resultBuilder = new StringBuilder();
                resultBuilder.AppendLine("<file_map>");
                resultBuilder.Append(fileMap); // Append generated map
                resultBuilder.AppendLine("</file_map>");
                resultBuilder.AppendLine();
                resultBuilder.AppendLine("<file_contents>");
                resultBuilder.Append(fileContents); // Append generated content
                resultBuilder.AppendLine("</file_contents>");

                // Copy to clipboard (must be done on UI thread)
                System.Windows.Clipboard.SetText(resultBuilder.ToString());

                UpdateStatusBarAndButtonStates($"Contenido de {textFileCount} archivo(s) copiado al portapapeles.");
                System.Windows.MessageBox.Show($"Se ha copiado el mapa y el contenido de {textFileCount} archivo(s) de texto al portapapeles.", "Copiar Texto (AI)", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UpdateStatusBarAndButtonStates("Error al copiar texto.");
                System.Windows.MessageBox.Show($"Error al preparar el texto para la IA:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"Error btnCopyText_Click: {ex}");
            }
            finally
            {
                 // Restore cursor and re-enable button on UI thread
                this.Cursor = System.Windows.Input.Cursors.Arrow;
                btnCopyText.IsEnabled = true; // Re-enable based on current selection state
                UpdateStatusBarAndButtonStates(); // Refresh button states potentially
            }
        }

        private (StringBuilder fileMap, StringBuilder fileContents, int textFileCount) CollectTextFilesAndContent()
        {
             var allTextFiles = new List<FileSystemItem>();

             // Recursively find all text files within selected items
             foreach (var selectedItem in selectedItems)
             {
                 FindTextFilesRecursive(selectedItem, allTextFiles);
             }

             // Remove duplicates just in case (e.g., selecting a folder and a file within it)
             var uniqueTextFiles = allTextFiles
                 .DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                 .OrderBy(f => f.Path) // Sort for consistent output
                 .ToList();

             if (uniqueTextFiles.Count == 0)
             {
                 return (new StringBuilder(), new StringBuilder(), 0);
             }

             // Build the minimal file map based *only* on the found text files
             var fileMapBuilder = BuildMinimalFileMapForFiles(uniqueTextFiles);

             // Build the file contents section
             var fileContentBuilder = new StringBuilder();
             foreach (var textFile in uniqueTextFiles)
             {
                 try
                 {
                     string content = File.ReadAllText(textFile.Path);
                     // Use Path.GetRelativePath for cleaner relative paths
                     string relativePath = Path.GetRelativePath(rootPath!, textFile.Path); // rootPath should be non-null here

                     fileContentBuilder.AppendLine($"File: {relativePath.Replace('\\', '/')}"); // Use forward slashes
                     fileContentBuilder.AppendLine($"");
                     fileContentBuilder.AppendLine(content);
                     fileContentBuilder.AppendLine($""); // Extra newline for separation
                     fileContentBuilder.AppendLine();
                 }
                 catch (Exception ex)
                 {
                     // Log error for specific file but continue with others
                      string relativePath = Path.GetRelativePath(rootPath!, textFile.Path);
                     fileContentBuilder.AppendLine($"File: {relativePath.Replace('\\', '/')}");
                     fileContentBuilder.AppendLine($"");
                     fileContentBuilder.AppendLine($"### Error reading file: {ex.Message} ###");
                     fileContentBuilder.AppendLine($"");
                     fileContentBuilder.AppendLine();
                      Debug.WriteLine($"Error reading content for {textFile.Path}: {ex.Message}");
                 }
             }

            return (fileMapBuilder, fileContentBuilder, uniqueTextFiles.Count);
        }

        // Recursive helper to find all text files starting from a selected item
        private void FindTextFilesRecursive(FileSystemItem item, List<FileSystemItem> collectedFiles)
        {
            if (!item.IsDirectory)
            {
                if (IsTextFile(item.Path) && !collectedFiles.Any(f => f.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    collectedFiles.Add(item);
                }
                return; // Stop recursion if it's a file
            }

            // It's a directory, explore its contents
            try
            {
                // Process files in the current directory
                foreach (var fileInfo in new DirectoryInfo(item.Path).GetFiles().Where(f => IsTextFile(f.FullName)))
                {
                     var fileFsi = new FileSystemItem { Name = fileInfo.Name, Path = fileInfo.FullName, Type = fileInfo.Extension, IsDirectory = false };
                      if (!collectedFiles.Any(f => f.Path.Equals(fileFsi.Path, StringComparison.OrdinalIgnoreCase)))
                      {
                           collectedFiles.Add(fileFsi);
                      }
                }

                // Process subdirectories recursively, respecting ignored list
                foreach (var dirInfo in new DirectoryInfo(item.Path).GetDirectories())
                {
                     if (!ignoredFolderNames.Contains(dirInfo.Name))
                     {
                         var dirFsi = new FileSystemItem { Name = dirInfo.Name, Path = dirInfo.FullName, Type = "Directorio", IsDirectory = true };
                         FindTextFilesRecursive(dirFsi, collectedFiles);
                     }
                }
            }
            catch (UnauthorizedAccessException)
            {
                 Debug.WriteLine($"Access denied during recursive text file search in: {item.Path}");
                 // Skip this directory silently in the background task
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"Error during recursive text file search in {item.Path}: {ex.Message}");
                // Log and continue if possible
            }
        }


       // Builds the <file_map> structure based ONLY on the paths of the provided text files.
        private StringBuilder BuildMinimalFileMapForFiles(List<FileSystemItem> textFiles)
        {
            var mapBuilder = new StringBuilder();
            if (textFiles.Count == 0 || string.IsNullOrEmpty(rootPath)) return mapBuilder;

            // Create a set of all directory paths required to reach the text files
            var requiredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in textFiles)
            {
                string? dir = Path.GetDirectoryName(file.Path);
                while (dir != null && dir.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) && dir.Length >= rootPath.Length)
                {
                    requiredDirs.Add(dir);
                    if (dir.Equals(rootPath, StringComparison.OrdinalIgnoreCase)) break; // Stop at root
                    dir = Path.GetDirectoryName(dir);
                }
            }
             requiredDirs.Add(rootPath); // Ensure root is always included if files are present

            // Group files by their directory
            var dirToFileMap = textFiles.GroupBy(f => Path.GetDirectoryName(f.Path) ?? "")
                                        .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Name).ToList(), StringComparer.OrdinalIgnoreCase);

            // Build the tree structure recursively
            BuildMapRecursive(rootPath, 0, mapBuilder, requiredDirs, dirToFileMap);

            return mapBuilder;
        }

        // Recursive helper for building the file map string
        private void BuildMapRecursive(string currentDirPath, int level, StringBuilder builder, HashSet<string> requiredDirs, Dictionary<string, List<FileSystemItem>> dirToFileMap)
        {
            // Get relative path segment for display name
            string displayName = level == 0 ? currentDirPath : Path.GetFileName(currentDirPath);
            string indent = new string(' ', level * 2);
            string prefix = level == 0 ? "" : "└── ";

            builder.AppendLine($"{indent}{prefix}{displayName}");

             try
             {
                 // Add relevant subdirectories first, sorted alphabetically
                 var subDirs = Directory.GetDirectories(currentDirPath)
                     .Where(d => requiredDirs.Contains(d)) // Only include dirs that are required (lead to a selected file)
                     .OrderBy(d => d)
                     .ToList();

                 foreach (var subDir in subDirs)
                 {
                     BuildMapRecursive(subDir, level + 1, builder, requiredDirs, dirToFileMap);
                 }

                 // Add files in the current directory that were selected, sorted alphabetically
                 if (dirToFileMap.TryGetValue(currentDirPath, out var filesInDir))
                 {
                     foreach (var file in filesInDir) // Already sorted when dictionary was created
                     {
                         builder.AppendLine($"{indent}  ├── {file.Name}");
                     }
                 }
             }
             catch (Exception ex)
             {
                  builder.AppendLine($"{indent}  [Error accessing: {ex.Message}]");
                  Debug.WriteLine($"Error accessing directory {currentDirPath} during map build: {ex.Message}");
             }
        }


        // --- Standard File Operations (Copy, Cut, Paste, Delete, Rename) ---
        // These remain largely the same, but we update status bar and use clipboardItems

        private void btnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItems.Count == 0) return;

            clipboardItems = selectedItems.ToList(); // Copy the list of selected items
            isCutOperation = false;
            clipboardPath = null; // Clear single path clipboard concept

            UpdateStatusBarAndButtonStates($"Copiado(s) {clipboardItems.Count} elemento(s) al portapapeles.");
            // No message box needed, status bar is enough
        }

        private void btnCut_Click(object sender, RoutedEventArgs e)
        {
             if (selectedItems.Count == 0) return;

            clipboardItems = selectedItems.ToList();
            isCutOperation = true;
            clipboardPath = null;

            UpdateStatusBarAndButtonStates($"Cortado(s) {clipboardItems.Count} elemento(s) al portapapeles.");
             // Optional: Visually indicate cut items (e.g., gray out in tree) - adds complexity
        }

        private void btnPaste_Click(object sender, RoutedEventArgs e)
        {
            if (clipboardItems.Count == 0)
            {
                System.Windows.MessageBox.Show("El portapapeles está vacío.", "Pegar", MessageBoxButton.OK, MessageBoxImage.Information);
                 return;
            }
            if (treeViewFiles.SelectedItem is not TreeViewItem selectedTreeViewItem || selectedTreeViewItem.Tag is not FileSystemItem targetFsi)
            {
                System.Windows.MessageBox.Show("Seleccione un directorio destino válido en el árbol para pegar.", "Pegar", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string destinationPath;
            // Determine destination directory: if selected item is a file, use its parent dir, otherwise use the dir itself
            if (!targetFsi.IsDirectory)
            {
                destinationPath = Path.GetDirectoryName(targetFsi.Path);
                 if (destinationPath == null)
                 {
                     System.Windows.MessageBox.Show($"No se pudo determinar el directorio destino desde '{targetFsi.Name}'.", "Error al Pegar", MessageBoxButton.OK, MessageBoxImage.Error);
                     return;
                 }
            }
            else
            {
                destinationPath = targetFsi.Path;
            }


            UpdateStatusBarAndButtonStates("Pegando elementos...");
            this.Cursor = System.Windows.Input.Cursors.Wait;

            try
            {
                int pasteCount = 0;
                var itemsToProcess = clipboardItems.ToList(); // Process a copy

                foreach (var itemToPaste in itemsToProcess)
                {
                    string sourcePath = itemToPaste.Path;
                    // Check if source still exists, especially for Cut operations
                    if ((itemToPaste.IsDirectory && !Directory.Exists(sourcePath)) || (!itemToPaste.IsDirectory && !File.Exists(sourcePath)))
                    {
                         Debug.WriteLine($"Source path not found for paste: {sourcePath}");
                         continue; // Skip if source is gone
                    }

                    string targetName = Path.GetFileName(sourcePath);
                    string fullTargetPath = Path.Combine(destinationPath, targetName);

                    // Prevent copying/moving item into itself
                    if (itemToPaste.IsDirectory && destinationPath.StartsWith(sourcePath, StringComparison.OrdinalIgnoreCase))
                    {
                        System.Windows.MessageBox.Show($"No se puede { (isCutOperation ? "mover" : "copiar") } la carpeta '{itemToPaste.Name}' dentro de sí misma.", "Operación Inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
                         continue;
                    }

                    // Handle potential naming conflicts (simple overwrite warning or rename prompt - adding simple overwrite here)
                    bool targetExists = itemToPaste.IsDirectory ? Directory.Exists(fullTargetPath) : File.Exists(fullTargetPath);
                    if (targetExists)
                    {
                         var overwriteResult = System.Windows.MessageBox.Show($"El elemento '{targetName}' ya existe en el destino.\n¿Desea sobrescribirlo?", "Conflicto al Pegar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                         if (overwriteResult == MessageBoxResult.No)
                         {
                             continue; // Skip this item
                         }
                         // If Yes, delete existing target first
                         try
                         {
                              if (itemToPaste.IsDirectory) Directory.Delete(fullTargetPath, true);
                              else File.Delete(fullTargetPath);
                         }
                         catch(Exception delEx)
                         {
                             System.Windows.MessageBox.Show($"No se pudo sobrescribir '{targetName}'. Error: {delEx.Message}", "Error al Pegar", MessageBoxButton.OK, MessageBoxImage.Error);
                              continue;
                         }
                    }


                    try
                    {
                         if (itemToPaste.IsDirectory)
                         {
                             // Use custom CopyDirectory for better control or switch to FileSystem.MoveDirectory/CopyDirectory if using Microsoft.VisualBasic assembly
                             if (isCutOperation)
                             {
                                 Directory.Move(sourcePath, fullTargetPath); // Move directory
                             }
                             else
                             {
                                 CopyDirectory(sourcePath, fullTargetPath); // Copy directory
                             }
                         }
                         else // It's a file
                         {
                             if (isCutOperation)
                             {
                                 File.Move(sourcePath, fullTargetPath); // Move file
                             }
                             else
                             {
                                 File.Copy(sourcePath, fullTargetPath, true); // Copy file (overwrite true, though we handled conflict above)
                             }
                         }
                         pasteCount++;
                    }
                    catch (Exception opEx)
                    {
                        System.Windows.MessageBox.Show($"Error al {(isCutOperation ? "mover" : "copiar")} '{itemToPaste.Name}':\n{opEx.Message}", "Error al Pegar", MessageBoxButton.OK, MessageBoxImage.Error);
                          // Decide whether to continue with other items or stop
                          break; // Stop on first error for simplicity
                    }
                } // end foreach itemToPaste


                 // Clear clipboard if Cut operation was successful for all items moved
                if (isCutOperation)
                {
                    clipboardItems.Clear();
                    clipboardPath = null;
                    isCutOperation = false; // Reset operation type
                }

                // Refresh the parent node where items were pasted
                if (pathToTreeViewItemMap.TryGetValue(destinationPath, out var parentNode))
                {
                     parentNode.Items.Clear(); // Clear children
                     LoadChildren(parentNode, destinationPath); // Reload
                     parentNode.IsExpanded = true; // Ensure it's visible
                }
                else {
                     LoadDirectoryTree(rootPath); // Fallback to full refresh if parent node not found easily
                }


                UpdateStatusBarAndButtonStates($"{pasteCount} elemento(s) pegado(s) en '{Path.GetFileName(destinationPath)}'.");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error inesperado durante la operación de pegado:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBarAndButtonStates("Error al pegar.");
            }
            finally
            {
                 this.Cursor = System.Windows.Input.Cursors.Arrow;
                 UpdateStatusBarAndButtonStates(); // Update buttons etc.
            }
        }


        // Helper to copy directory contents recursively
        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true); // Overwrite if exists (conflict handled before calling)
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                // Skip ignored directories during copy as well
                if (!ignoredFolderNames.Contains(Path.GetFileName(subDir)))
                {
                    string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                    CopyDirectory(subDir, destSubDir);
                }
            }
        }


        private void btnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItems.Count == 0) return;

            string message = selectedItems.Count == 1
                ? $"¿Está seguro de que desea eliminar '{selectedItems.First().Name}'?"
                : $"¿Está seguro de que desea eliminar {selectedItems.Count} elementos seleccionados?";

             // Ask for confirmation
            if (System.Windows.MessageBox.Show(message, "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                UpdateStatusBarAndButtonStates("Eliminando...");
                this.Cursor = System.Windows.Input.Cursors.Wait;
                int deleteCount = 0;
                var itemsToDelete = selectedItems.ToList(); // Process a copy

                try
                {
                    foreach (var item in itemsToDelete)
                    {
                        try
                        {
                            if (item.IsDirectory)
                            {
                                if (Directory.Exists(item.Path))
                                    Directory.Delete(item.Path, true); // Recursive delete
                            }
                            else
                            {
                                if (File.Exists(item.Path))
                                    File.Delete(item.Path);
                            }

                            // Remove from selection and map *after* successful deletion
                            selectedItems.Remove(item);
                            pathToTreeViewItemMap.Remove(item.Path);
                            deleteCount++;
                        }
                        catch (Exception itemEx)
                        {
                            System.Windows.MessageBox.Show($"No se pudo eliminar '{item.Name}'.\nError: {itemEx.Message}\n\nAsegúrese de que no esté en uso.", "Error al Eliminar", MessageBoxButton.OK, MessageBoxImage.Error);
                             // Optionally break or continue
                             // break;
                        }
                    }

                     // Refresh UI - More efficient to remove nodes directly if possible
                     // Find parent nodes of deleted items and refresh them, or just reload the whole tree if simpler
                     LoadDirectoryTree(rootPath); // Simple full refresh after deletion

                     UpdateStatusBarAndButtonStates($"{deleteCount} elemento(s) eliminado(s).");
                 }
                 catch (Exception ex) // Catch unexpected errors during the loop or refresh
                 {
                     System.Windows.MessageBox.Show($"Error inesperado durante la eliminación: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                     UpdateStatusBarAndButtonStates("Error al eliminar.");
                     LoadDirectoryTree(rootPath); // Try to refresh to consistent state
                 }
                 finally
                 {
                    this.Cursor = System.Windows.Input.Cursors.Arrow;
                     UpdateStatusBarAndButtonStates(); // Update button states
                 }
            }
        }

        private void btnRename_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItems.Count != 1) return;

            var itemToRename = selectedItems[0];
            string oldName = itemToRename.Name;
            string oldPath = itemToRename.Path;
            string? directory = Path.GetDirectoryName(oldPath);

            if (directory == null)
            {
                System.Windows.MessageBox.Show("No se puede determinar el directorio del elemento a renombrar.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 return;
            }

            var dialog = new RenameDialog(oldName);
            dialog.Owner = this; // Set owner for proper dialog behavior

            if (dialog.ShowDialog() == true)
            {
                string newName = dialog.NewName.Trim();

                // Basic validation
                if (string.IsNullOrWhiteSpace(newName))
                {
                    System.Windows.MessageBox.Show("El nombre no puede estar vacío.", "Nombre Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                     return;
                }
                if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    System.Windows.MessageBox.Show("El nombre contiene caracteres no válidos.", "Nombre Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                 if (newName.Equals(oldName, StringComparison.Ordinal)) // Use Ordinal for case-sensitive check if needed, else OrdinalIgnoreCase
                 {
                     // Name didn't change
                     return;
                 }


                string newPath = Path.Combine(directory, newName);

                // Check if new name already exists
                if ((itemToRename.IsDirectory && Directory.Exists(newPath)) || (!itemToRename.IsDirectory && File.Exists(newPath)))
                {
                    System.Windows.MessageBox.Show($"Ya existe un elemento llamado '{newName}' en esta ubicación.", "Renombrar Fallido", MessageBoxButton.OK, MessageBoxImage.Warning);
                     return;
                }


                UpdateStatusBarAndButtonStates($"Renombrando '{oldName}'...");
                this.Cursor = System.Windows.Input.Cursors.Wait;

                try
                {
                    if (itemToRename.IsDirectory)
                    {
                        Directory.Move(oldPath, newPath);
                    }
                    else
                    {
                        File.Move(oldPath, newPath);
                    }

                     // Update the item in the selection list *if* it was selected
                    itemToRename.Name = newName;
                    itemToRename.Path = newPath;

                     // Refresh the parent node in the tree view
                    if (pathToTreeViewItemMap.TryGetValue(directory, out var parentNode))
                    {
                         parentNode.Items.Clear();
                         LoadChildren(parentNode, directory);
                         parentNode.IsExpanded = true; // Keep it expanded
                    }
                    else
                    {
                         LoadDirectoryTree(rootPath); // Fallback refresh
                    }

                     // Update maps (remove old, add new)
                     if(pathToTreeViewItemMap.Remove(oldPath, out var oldTvi))
                     {
                         // If the renamed item is visible (i.e., parent was expanded), update its TreeViewItem
                         if(pathToTreeViewItemMap.ContainsKey(newPath)) // Should exist after reload
                         {
                             var newTvi = pathToTreeViewItemMap[newPath];
                             // Potentially re-select the renamed item
                             newTvi.IsSelected = true;
                         }
                     }


                    UpdateStatusBarAndButtonStates($"'{oldName}' renombrado a '{newName}'.");
                    System.Windows.MessageBox.Show("Elemento renombrado con éxito.", "Renombrar", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error al renombrar '{oldName}':\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatusBarAndButtonStates($"Error al renombrar '{oldName}'.");
                     LoadDirectoryTree(rootPath); // Refresh to undo partial changes visually
                }
                finally
                {
                     this.Cursor = System.Windows.Input.Cursors.Arrow;
                      UpdateStatusBarAndButtonStates(); // Update buttons etc.
                }
            }
        }


        // --- Git Integration (Minor Change: Added Null Check) ---
        private void SetWindowTitleFromGit()
        {
            string? commitSubject = GetLastCommitSubject(); // Can return null
            if (!string.IsNullOrEmpty(commitSubject))
            {
                this.Title = $"MakingVibe - {commitSubject}";
            }
            else
            {
                this.Title = "MakingVibe - (Commit info unavailable)";
            }
        }

        private string? GetLastCommitSubject() // Return nullable string
        {
            string? repoPath = FindGitRepositoryRoot(AppDomain.CurrentDomain.BaseDirectory);
            if (repoPath == null)
            {
                Debug.WriteLine("Git repository root not found.");
                return null;
            }

            try
            {
                 // Assumes git is in PATH
                ProcessStartInfo startInfo = new ProcessStartInfo("git", "log -1 --pretty=%s")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = repoPath // Use found repo root
                };

                 using Process? process = Process.Start(startInfo); // Process can be null
                 if (process == null) {
                     Debug.WriteLine("Failed to start git process.");
                     return null;
                 }

                 string output = process.StandardOutput.ReadToEnd(); // Read output first
                 process.WaitForExit(); // Then wait

                 if (process.ExitCode == 0)
                 {
                     return output.Trim();
                 }
                 else
                 {
                      Debug.WriteLine($"Git command failed with exit code {process.ExitCode}");
                      return null;
                 }
            }
            catch (Win32Exception) // Catch if 'git' command is not found
            {
                 Debug.WriteLine("Error running git command. Is git installed and in PATH?");
                 return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting git commit info: {ex.Message}");
                return null;
            }
        }

         // Helper to find the .git directory upwards from a starting path
        private string? FindGitRepositoryRoot(string startingPath)
        {
            DirectoryInfo? currentDir = new DirectoryInfo(startingPath);
            while (currentDir != null)
            {
                if (Directory.Exists(Path.Combine(currentDir.FullName, ".git")))
                {
                    return currentDir.FullName;
                }
                currentDir = currentDir.Parent;
            }
            return null; // Not found
        }

    } // End class MainWindow

    // Class to represent a file system item (file or directory)
    // Keep it simple, no INotifyPropertyChanged unless needed for ListView binding
    public class FileSystemItem : IEquatable<FileSystemItem>
    {
        public required string Name { get; set; }
        public required string Path { get; set; }
        public required string Type { get; set; }
        public bool IsDirectory { get; set; }

        // Implement IEquatable for correct functioning in collections like HashSet or Distinct()
        public bool Equals(FileSystemItem? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            // Compare by Path for uniqueness
            return string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as FileSystemItem);
        }

        public override int GetHashCode()
        {
            // Use Path's hash code (case-insensitive)
            return StringComparer.OrdinalIgnoreCase.GetHashCode(Path ?? string.Empty);
        }

        public static bool operator ==(FileSystemItem? left, FileSystemItem? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(FileSystemItem? left, FileSystemItem? right)
        {
            return !Equals(left, right);
        }
    }
} // End namespace MakingVibe