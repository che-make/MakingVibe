/*
 * MakingVibe/MainWindow.xaml.cs
 * Main partial class file.
 * Contains core window logic, service initialization, shared state,
 * event handlers for window lifetime, status bar updates, and command definitions.
 * Includes LOC and Char count updates in status bar.
 */
using MakingVibe.Models;
using MakingVibe.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO; // Needed for File reading for char count
using System.Linq;
using System.Text;
using System.Threading.Tasks; // Potentially for async calculation later
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media; // For VisualTreeHelper
using CheckBox = System.Windows.Controls.CheckBox; // Explicit alias
using ListViewItem = System.Windows.Controls.ListViewItem;
using MessageBox = System.Windows.MessageBox; // Explicit alias
using WinForms = System.Windows.Forms; // Alias for Windows.Forms

namespace MakingVibe
{
    public partial class MainWindow : Window
    {
        // --- Services ---
        private readonly FileSystemService _fileSystemService;
        private readonly AiCopyService _aiCopyService;
        private readonly SettingsService _settingsService;
        private readonly GitService _gitService;

        // --- Shared UI State & Data (Accessible by all partial classes) ---
        private string? rootPath;
        private readonly ObservableCollection<FileSystemItem> selectedItems = new();
        private readonly Dictionary<string, TreeViewItem> pathToTreeViewItemMap = new();
        public ObservableCollection<TextFragment> Fragments { get; set; }
        private readonly string _fragmentsFilePath;
        public ObservableCollection<FileFilterItem> FileFilters { get; set; }
        private HashSet<string> _activeFileExtensionsFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<FileSystemItem> clipboardItems = new();
        private bool isCutOperation;
        private bool _isUpdatingParentState = false; // Flag to prevent recursive selection updates

        public ObservableCollection<SavedPath> SavedPaths { get; set; }

        // Commands
        public static RoutedCommand RefreshCommand = new RoutedCommand();
        public static RoutedCommand DeleteCommand = new RoutedCommand();
        // Add more RoutedCommands if you bind other actions (Copy, Cut, Paste, Rename)

        public MainWindow()
        {
            InitializeComponent();

            // Instantiate services
            _fileSystemService = new FileSystemService();
            _aiCopyService = new AiCopyService(_fileSystemService);
            _settingsService = new SettingsService();
            _gitService = new GitService();

            // *** Initialize Fragments ***
            Fragments = new ObservableCollection<TextFragment>();
            _fragmentsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "makingvibe.fragments.json");
            LoadFragments(); // Method in PromptTab partial class
            listViewFragments.ItemsSource = Fragments;

            // Initialize SavedPaths
            SavedPaths = new ObservableCollection<SavedPath>();
            cmbSavedPaths.ItemsSource = SavedPaths;

            // *** Initialize File Filters ***
            FileFilters = new ObservableCollection<FileFilterItem>();
            listViewFilters.ItemsSource = FileFilters;

            // Bind commands
            CommandBindings.Add(new CommandBinding(RefreshCommand, btnRefresh_Click, CanExecuteRefresh));
            CommandBindings.Add(new CommandBinding(DeleteCommand, btnDelete_Click, CanExecuteDelete)); // Executed in FileOperations partial class
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
            LoadSavedPaths(); // Cargar las rutas guardadas
            // UpdateSavedPathControlsState(); // Called within LoadSavedPaths

            if (!string.IsNullOrEmpty(rootPath) && _fileSystemService.DirectoryExists(rootPath))
            {
                UpdateStatusBarAndButtonStates($"Cargando directorio: {rootPath}...");
                LoadDirectoryTreeUI(rootPath); // Method in TreeView partial class
                UpdateStatusBarAndButtonStates("Listo.");
            }
            else
            {
                txtCurrentPath.Text = "Ruta actual: (Seleccione una carpeta raíz)";
                UpdateStatusBarAndButtonStates("Por favor, seleccione una carpeta raíz.");
                UpdateSavedPathControlsState(); // Update button state even if no root path
                rootPath = null;
            }

            if (rootPath != null)
            {
                treeViewFiles.Focus();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _settingsService.SaveLastRootPath(rootPath);
            SaveFragments(); // Method in PromptTab partial class
            base.OnClosing(e);
        }

        // --- Central UI Update Logic ---

        private void UpdateStatusBarAndButtonStates(string statusMessage = "")
        {
             long totalLines = 0;
             long totalChars = 0;
             int selectedFileCount = 0; // Count only files for LOC/Chars

             // Calculate LOC and Chars for selected text files
             // WARNING: Reading file content here synchronously can impact UI responsiveness
             //          if many large files are selected. Consider async calculation if needed.
             foreach (var item in selectedItems)
             {
                 if (!item.IsDirectory) // Only count for files
                 {
                     selectedFileCount++;
                     if (FileHelper.IsTextFile(item.Path))
                     {
                         // Add line count (already calculated, hopefully)
                         totalLines += item.LineCount ?? 0;

                         // Calculate character count by reading the file
                         try
                         {
                             // Ensure file still exists before reading
                             if (File.Exists(item.Path))
                             {
                                 // Note: File.ReadAllText is simple but reads entire file into memory.
                                 // Consider StreamReader if memory becomes an issue for huge files.
                                 string content = File.ReadAllText(item.Path);
                                 totalChars += content.Length;
                             }
                             else {
                                 // File might have been deleted since selection
                                 Debug.WriteLine($"File not found for char count: {item.Path}");
                             }
                         }
                         catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                         {
                             // Handle potential errors reading the file for char count gracefully
                             Debug.WriteLine($"Error reading file {item.Path} for char count: {ex.Message}");
                         }
                         catch (Exception ex)
                         {
                              // Catch unexpected errors
                              Debug.WriteLine($"Unexpected error reading file {item.Path} for char count: {ex}");
                         }
                     }
                 }
             }

             Action updateAction = () => {
                 // Update status bar text (Main status)
                if (!string.IsNullOrEmpty(statusMessage))
                {
                    statusBarText.Text = statusMessage;
                }
                else if (selectedItems.Count > 0)
                {
                    // Use selectedItems.Count for the general message, but selectedFileCount for details
                    statusBarText.Text = $"{selectedItems.Count} elemento(s) seleccionado(s) ({selectedFileCount} archivo(s)).";
                }
                 else if (!string.IsNullOrEmpty(rootPath))
                 {
                     statusBarText.Text = "Listo.";
                 }
                 else
                 {
                     statusBarText.Text = "Por favor, seleccione una carpeta raíz.";
                 }

                // Update selection count and details (Right side)
                // Use N0 format specifier for thousands separator
                statusSelectionCount.Text = $"Items: {selectedItems.Count:N0}";
                statusTotalLines.Text = $"LOC: {totalLines:N0}";
                statusTotalChars.Text = $"Chars: {totalChars:N0}";


                 // Standard Button States
                 bool hasSelection = selectedItems.Count > 0;
                 bool hasSingleSelection = selectedItems.Count == 1;
                 bool canPaste = clipboardItems.Count > 0 && (treeViewFiles.SelectedItem != null || !string.IsNullOrEmpty(rootPath)); // Simplified paste check

                 // Determine if any fragments are selected
                 bool anyFragmentSelected = false;
                 // Use ItemContainerGenerator only if the list view is visible and rendered
                 if (listViewFragments.IsVisible)
                 {
                     try
                     {
                         foreach (var item in listViewFragments.Items)
                         {
                             // Ensure ItemContainerGenerator has finished before accessing
                             if (listViewFragments.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
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
                                 // else: Container might be null if item is virtualized/not yet generated
                             }
                             else
                             {
                                 // Generator not ready, might need to wait or use data binding
                                 // For simplicity, we might skip this check if it causes issues
                                 // Debug.WriteLine("Fragment List ItemContainerGenerator not ready.");
                             }
                         }
                     } catch (InvalidOperationException ioex) {
                        // ItemContainerGenerator might throw if accessed at the wrong time during updates.
                        Debug.WriteLine($"Warning: Could not check fragment selection state reliably: {ioex.Message}");
                        // Consider alternative logic if this becomes problematic.
                     }
                 } else {
                     // Alternative check if fragments list isn't visible (if TextFragment had IsSelected property)
                     // anyFragmentSelected = Fragments.Any(f => f.IsSelected);
                 }


                 bool canCopyFiles = hasSelection && selectedItems.Any(item => item.IsDirectory || FileHelper.IsTextFile(item.Path));
                 bool canCopyPrompt = !string.IsNullOrWhiteSpace(txtMainPrompt.Text);
                 bool canCopyAiText = canCopyFiles || canCopyPrompt || anyFragmentSelected;
                 bool treeHasRoot = treeViewFiles.HasItems;
                 bool filtersEnabled = !string.IsNullOrEmpty(rootPath) && FileFilters.Any();

                 // Toolbar Buttons
                 btnCopyText.IsEnabled = canCopyAiText;
                 btnCopy.IsEnabled = hasSelection;
                 btnCut.IsEnabled = hasSelection;
                 btnDelete.IsEnabled = hasSelection;
                 btnRename.IsEnabled = hasSingleSelection;
                 btnPaste.IsEnabled = canPaste;
                 btnRefresh.IsEnabled = !string.IsNullOrEmpty(rootPath);

                // TreeView Panel Buttons
                 btnCollapseAll.IsEnabled = treeHasRoot;
                 btnExpandAll.IsEnabled = treeHasRoot;
                 btnClearSelection.IsEnabled = hasSelection;

                 // Filter Tab Buttons
                 btnSelectAllFilters.IsEnabled = filtersEnabled;
                 btnDeselectAllFilters.IsEnabled = filtersEnabled;
                 btnApplyFilters.IsEnabled = !string.IsNullOrEmpty(rootPath);

                 // Collapse/Expand Current Button States (depend on TreeView selection)
                 bool canCollapseCurrent = false;
                 bool canExpandCurrent = false;
                 if (treeViewFiles.SelectedItem is TreeViewItem selectedTvi && selectedTvi.Tag is FileSystemItem selectedFsi && selectedFsi.IsDirectory)
                 {
                     canCollapseCurrent = selectedTvi.IsExpanded;
                     canExpandCurrent = !selectedTvi.IsExpanded && selectedTvi.HasItems; // Only allow expand if it has items (or placeholder)
                 }
                 btnCollapseCurrent.IsEnabled = canCollapseCurrent;
                 btnExpandCurrent.IsEnabled = canExpandCurrent;


                 // Ensure commands re-evaluate CanExecute
                 CommandManager.InvalidateRequerySuggested();
            };

            if (Dispatcher.CheckAccess()) { updateAction(); }
            else { Dispatcher.Invoke(updateAction); }
        }


        // --- Toolbar Refresh Action ---
        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(rootPath) && _fileSystemService.DirectoryExists(rootPath))
            {
                UpdateStatusBarAndButtonStates($"Refrescando: {rootPath}...");
                LoadDirectoryTreeUI(rootPath); // Method in TreeView partial class
                UpdateStatusBarAndButtonStates("Árbol refrescado.");
            }
            else
            {
                MessageBox.Show("No hay una carpeta raíz seleccionada o válida para refrescar.", "Refrescar", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateStatusBarAndButtonStates("Seleccione una carpeta raíz.");
            }
        }


        // --- Git Integration ---
        private void SetWindowTitleFromGit()
        {
            string? commitSubject = _gitService.GetLastCommitSubject();
            this.Title = !string.IsNullOrEmpty(commitSubject)
                ? $"MakingVibe - {commitSubject}"
                : "MakingVibe - (Commit info unavailable)";
        }

        // --- Helper to find visual children (Used by UpdateStatusBarAndButtonStates) ---
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

        // --- Nuevos métodos para gestión de rutas guardadas ---

        /// <summary>
        /// Carga las rutas guardadas y actualiza el ComboBox
        /// </summary>
        private void LoadSavedPaths()
        {
            try
            {
                SavedPaths.Clear();
                var paths = _settingsService.LoadSavedPaths();
                
                foreach (var path in paths)
                {
                    SavedPaths.Add(path);
                }
                
                // Si tenemos una ruta actual, seleccionarla en el combo box si existe
                if (!string.IsNullOrEmpty(rootPath))
                {
                    var matchingPath = SavedPaths.FirstOrDefault(p => p.Path.Equals(rootPath, StringComparison.OrdinalIgnoreCase));
                    if (matchingPath != null)
                    {
                        cmbSavedPaths.SelectedItem = matchingPath;
                    }
                }
                
                // Actualizar estado de los botones de ruta guardada
                UpdateSavedPathControlsState();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading saved paths: {ex.Message}");
            }
        }

        // --- Method deprecated, logic moved to UpdateSavedPathControlsState ---
        // private void UpdateSavePathButtonState()
        // {
        //     btnSavePath.IsEnabled = !string.IsNullOrEmpty(rootPath) && _fileSystemService.DirectoryExists(rootPath);
        // }

        /// <summary>
        /// Manejador del evento de clic en el botón Guardar Ruta
        /// </summary>
        private void btnSavePath_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(rootPath) || !_fileSystemService.DirectoryExists(rootPath))
            {
                MessageBox.Show("Primero seleccione una carpeta raíz válida.", "Guardar Ruta", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Obtener un nombre predeterminado basado en el nombre de la carpeta
            string defaultName = Path.GetFileName(rootPath) ?? rootPath;
            string displayName = defaultName;
            
            // Mostrar diálogo para que el usuario introduzca un nombre personalizado
            var dialog = new InputDialog("Guardar Ruta", "Nombre para la ruta (opcional):", defaultName);
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true)
            {
                displayName = dialog.InputText.Trim();
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = defaultName; // Usar el predeterminado si está vacío
                }
            }
            else
            {
                return; // Usuario canceló
            }
            
            _settingsService.AddSavedPath(rootPath, displayName);
            LoadSavedPaths(); // Refrescar el combo box
            
            UpdateSavedPathControlsState(); // Update delete button state as well
            UpdateStatusBarAndButtonStates($"Ruta '{displayName}' guardada.");
        }

        /// <summary>
        /// Manejador del evento de cambio de selección en el ComboBox de rutas guardadas
        /// </summary>
        private void cmbSavedPaths_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbSavedPaths.SelectedItem is SavedPath selectedPath)
            {
                // Update delete button enabled state whenever selection changes
                UpdateSavedPathControlsState();

                if (string.IsNullOrEmpty(selectedPath.Path) || !_fileSystemService.DirectoryExists(selectedPath.Path))
                {
                    MessageBox.Show($"La ruta '{selectedPath}' ya no existe o no es accesible.", "Ruta Inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
                    
                    // Preguntar si quieren eliminar la ruta inválida
                    if (MessageBox.Show("¿Desea eliminar esta ruta de la lista?", "Eliminar Ruta", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        _settingsService.RemoveSavedPath(selectedPath.Path);
                        LoadSavedPaths(); // Refrescar el combo box
                        // UpdateSavedPathControlsState(); // Called within LoadSavedPaths
                    }
                    
                    return;
                }
                
                // Cambiar a la ruta seleccionada
                rootPath = selectedPath.Path;
                txtCurrentPath.Text = $"Ruta actual: {rootPath}";
                UpdateStatusBarAndButtonStates($"Cargando directorio: {rootPath}...");
                _activeFileExtensionsFilter.Clear(); // Reset filters on root change
                _settingsService.SaveLastRootPath(rootPath);
                LoadDirectoryTreeUI(rootPath);
                UpdateStatusBarAndButtonStates($"Directorio cambiado a '{selectedPath}'.");
                
                // Actualizar la fecha de último uso de la ruta
                _settingsService.AddSavedPath(selectedPath.Path, selectedPath.DisplayName);
            }
        }

        /// <summary>
        /// Handles the click event for the "Delete Saved Path" button.
        /// </summary>
        private void btnDeleteSavedPath_Click(object sender, RoutedEventArgs e)
        {
            if (cmbSavedPaths.SelectedItem is SavedPath selectedPath)
            {
                string displayName = selectedPath.ToString(); // Use the display name from ToString()
                var result = MessageBox.Show($"¿Está seguro de que desea eliminar la ruta guardada '{displayName}'?\n({selectedPath.Path})", 
                                             "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    _settingsService.RemoveSavedPath(selectedPath.Path);
                    LoadSavedPaths(); // Refresh the ComboBox
                    UpdateStatusBarAndButtonStates($"Ruta guardada '{displayName}' eliminada.");
                }
            }
        }
        // --- Selector de ruta (modificar el método existente, no reemplazarlo) ---
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
                
                // Actualizar el estado de los botones de ruta guardada
                UpdateSavedPathControlsState(); 
                
                UpdateStatusBarAndButtonStates("Listo.");
                treeViewFiles.Focus();
            }
        }
        
        /// <summary>
        /// Updates the enabled state of the Save Path and Delete Path buttons.
        /// </summary>
        private void UpdateSavedPathControlsState()
        {
            btnSavePath.IsEnabled = !string.IsNullOrEmpty(rootPath) && _fileSystemService.DirectoryExists(rootPath);
            btnDeleteSavedPath.IsEnabled = cmbSavedPaths.SelectedItem != null;
        }

        // --- Partial Class Methods (Defined in other files) ---
        // LoadDirectoryTreeUI(string path) -> MainWindow.TreeView.cs
        // LoadChildrenUI(...) -> MainWindow.TreeView.cs
        // UpdateFileFiltersUI(...) -> MainWindow.FiltersTab.cs
        // AdjustFilterCount(...) -> MainWindow.FiltersTab.cs
        // ApplyFiltersAndReloadTree() -> MainWindow.FiltersTab.cs
        // ShowPreviewAsync(FileSystemItem? fsi) -> MainWindow.PreviewTab.cs
        // AddCurrentFragment() -> MainWindow.PromptTab.cs
        // LoadFragments() -> MainWindow.PromptTab.cs
        // SaveFragments() -> MainWindow.PromptTab.cs
        // RefreshNodeUI(...) -> MainWindow.TreeView.cs
        // ClearAllSelectionsUI() -> MainWindow.TreeView.cs
        // SelectDirectoryDescendantsRecursive(...) -> MainWindow.TreeView.cs
        // UpdateDirectoryFilesSelection(...) -> MainWindow.TreeView.cs
        // UpdateParentDirectorySelectionState(...) -> MainWindow.TreeView.cs
        // CollapseOrExpandNodes(...) -> MainWindow.TreeView.cs

    } // End partial class MainWindow
} // End namespace MakingVibe