/*
 * MakingVibe/MainWindow.xaml.cs
 * Main partial class file.
 * Contains core window logic, service initialization, shared state,
 * event handlers for window lifetime, status bar updates, and command definitions.
 * Includes LOC and Char count updates in status bar.
 * ADDED: WebView2 initialization and CSS injection.
 * FIXED: Use textContent instead of innerHTML for CSS injection due to Trusted Types.
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
using System.Threading.Tasks; // For async operations AND WebView2
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media; // For VisualTreeHelper and Brushes
using Microsoft.Web.WebView2.Wpf; // For WebView2
using Microsoft.Web.WebView2.Core; // For CoreWebView2 events and methods
using System.Text.Json; // For JSON serialization used in CSS injection
using Brushes = System.Windows.Media.Brushes; // Explicit alias
using CheckBox = System.Windows.Controls.CheckBox; // Explicit alias
using HorizontalAlignment = System.Windows.HorizontalAlignment;
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

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            rootPath = _settingsService.LoadLastRootPath();
            LoadSavedPaths(); // Cargar las rutas guardadas

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

            // Initialize WebView2 AFTER other UI loading might be done
            await InitializeWebViewAsync();

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
             int selectedFileCount = 0;

             foreach (var item in selectedItems)
             {
                 if (!item.IsDirectory)
                 {
                     selectedFileCount++;
                     if (FileHelper.IsTextFile(item.Path))
                     {
                         totalLines += item.LineCount ?? 0;
                         try
                         {
                             if (File.Exists(item.Path))
                             {
                                 string content = File.ReadAllText(item.Path);
                                 totalChars += content.Length;
                             }
                             else {
                                 Debug.WriteLine($"File not found for char count: {item.Path}");
                             }
                         }
                         catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                         {
                             Debug.WriteLine($"Error reading file {item.Path} for char count: {ex.Message}");
                         }
                         catch (Exception ex)
                         {
                              Debug.WriteLine($"Unexpected error reading file {item.Path} for char count: {ex}");
                         }
                     }
                 }
             }

             Action updateAction = () => {
                if (!string.IsNullOrEmpty(statusMessage)) { statusBarText.Text = statusMessage; }
                else if (selectedItems.Count > 0) { statusBarText.Text = $"{selectedItems.Count} elemento(s) seleccionado(s) ({selectedFileCount} archivo(s))."; }
                else if (!string.IsNullOrEmpty(rootPath)) { statusBarText.Text = "Listo."; }
                else { statusBarText.Text = "Por favor, seleccione una carpeta raíz."; }

                statusSelectionCount.Text = $"Items: {selectedItems.Count:N0}";
                statusTotalLines.Text = $"LOC: {totalLines:N0}";
                statusTotalChars.Text = $"Chars: {totalChars:N0}";

                 bool hasSelection = selectedItems.Count > 0;
                 bool hasSingleSelection = selectedItems.Count == 1;
                 bool canPaste = clipboardItems.Count > 0 && (treeViewFiles.SelectedItem != null || !string.IsNullOrEmpty(rootPath));
                 bool anyFragmentSelected = false;
                 if (listViewFragments.IsVisible)
                 {
                     try
                     {
                         foreach (var item in listViewFragments.Items)
                         {
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
                             }
                         }
                     } catch (InvalidOperationException ioex) {
                        Debug.WriteLine($"Warning: Could not check fragment selection state reliably: {ioex.Message}");
                     }
                 }

                 bool canCopyFiles = hasSelection && selectedItems.Any(item => item.IsDirectory || FileHelper.IsTextFile(item.Path));
                 bool canCopyPrompt = !string.IsNullOrWhiteSpace(txtMainPrompt.Text);
                 bool canCopyAiText = canCopyFiles || canCopyPrompt || anyFragmentSelected;
                 bool treeHasRoot = treeViewFiles.HasItems;
                 bool filtersEnabled = !string.IsNullOrEmpty(rootPath) && FileFilters.Any();

                 btnCopyText.IsEnabled = canCopyAiText;
                 btnCopy.IsEnabled = hasSelection;
                 btnCut.IsEnabled = hasSelection;
                 btnDelete.IsEnabled = hasSelection;
                 btnRename.IsEnabled = hasSingleSelection;
                 btnPaste.IsEnabled = canPaste;
                 btnRefresh.IsEnabled = !string.IsNullOrEmpty(rootPath);

                 btnCollapseAll.IsEnabled = treeHasRoot;
                 btnExpandAll.IsEnabled = treeHasRoot;
                 btnClearSelection.IsEnabled = hasSelection;

                 btnSelectAllFilters.IsEnabled = filtersEnabled;
                 btnDeselectAllFilters.IsEnabled = filtersEnabled;
                 btnApplyFilters.IsEnabled = !string.IsNullOrEmpty(rootPath);

                 bool canCollapseCurrent = false;
                 bool canExpandCurrent = false;
                 if (treeViewFiles.SelectedItem is TreeViewItem selectedTvi && selectedTvi.Tag is FileSystemItem selectedFsi && selectedFsi.IsDirectory)
                 {
                     canCollapseCurrent = selectedTvi.IsExpanded;
                     canExpandCurrent = !selectedTvi.IsExpanded && selectedTvi.HasItems;
                 }
                 btnCollapseCurrent.IsEnabled = canCollapseCurrent;
                 btnExpandCurrent.IsEnabled = canExpandCurrent;

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

                if (!string.IsNullOrEmpty(rootPath))
                {
                    var matchingPath = SavedPaths.FirstOrDefault(p => p.Path.Equals(rootPath, StringComparison.OrdinalIgnoreCase));
                    if (matchingPath != null)
                    {
                        cmbSavedPaths.SelectedItem = matchingPath;
                    }
                }
                UpdateSavedPathControlsState();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading saved paths: {ex.Message}");
            }
        }

        private void btnSavePath_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(rootPath) || !_fileSystemService.DirectoryExists(rootPath))
            {
                MessageBox.Show("Primero seleccione una carpeta raíz válida.", "Guardar Ruta", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string defaultName = Path.GetFileName(rootPath) ?? rootPath;
            string displayName = defaultName;

            var dialog = new InputDialog("Guardar Ruta", "Nombre para la ruta (opcional):", defaultName);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                displayName = dialog.InputText.Trim();
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = defaultName;
                }
            }
            else
            {
                return; // User cancelled
            }

            _settingsService.AddSavedPath(rootPath, displayName);
            LoadSavedPaths(); // Refresh combo box
            UpdateSavedPathControlsState(); // Update delete button state
            UpdateStatusBarAndButtonStates($"Ruta '{displayName}' guardada.");
        }

        private void cmbSavedPaths_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbSavedPaths.SelectedItem is SavedPath selectedPath)
            {
                UpdateSavedPathControlsState(); // Update delete button state

                if (string.IsNullOrEmpty(selectedPath.Path) || !_fileSystemService.DirectoryExists(selectedPath.Path))
                {
                    MessageBox.Show($"La ruta '{selectedPath}' ya no existe o no es accesible.", "Ruta Inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
                    if (MessageBox.Show("¿Desea eliminar esta ruta de la lista?", "Eliminar Ruta", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        _settingsService.RemoveSavedPath(selectedPath.Path);
                        LoadSavedPaths();
                    }
                    return;
                }

                rootPath = selectedPath.Path;
                txtCurrentPath.Text = $"Ruta actual: {rootPath}";
                UpdateStatusBarAndButtonStates($"Cargando directorio: {rootPath}...");
                _activeFileExtensionsFilter.Clear();
                _settingsService.SaveLastRootPath(rootPath);
                LoadDirectoryTreeUI(rootPath);
                UpdateStatusBarAndButtonStates($"Directorio cambiado a '{selectedPath}'.");
                _settingsService.AddSavedPath(selectedPath.Path, selectedPath.DisplayName); // Update last used
            }
            else
            {
                UpdateSavedPathControlsState(); // Handle selection cleared
            }
        }

        private void btnDeleteSavedPath_Click(object sender, RoutedEventArgs e)
        {
            if (cmbSavedPaths.SelectedItem is SavedPath selectedPath)
            {
                string displayName = selectedPath.ToString();
                var result = MessageBox.Show($"¿Está seguro de que desea eliminar la ruta guardada '{displayName}'?\n({selectedPath.Path})",
                                             "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    _settingsService.RemoveSavedPath(selectedPath.Path);
                    LoadSavedPaths();
                    UpdateStatusBarAndButtonStates($"Ruta guardada '{displayName}' eliminada.");
                }
            }
        }

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
                _activeFileExtensionsFilter.Clear();
                LoadDirectoryTreeUI(rootPath);
                _settingsService.SaveLastRootPath(rootPath);
                UpdateSavedPathControlsState();
                LoadSavedPaths(); // Reload paths to select the newly chosen one if saved
                UpdateStatusBarAndButtonStates("Listo.");
                treeViewFiles.Focus();
            }
        }

        private void UpdateSavedPathControlsState()
        {
            btnSavePath.IsEnabled = !string.IsNullOrEmpty(rootPath) && _fileSystemService.DirectoryExists(rootPath);
            btnDeleteSavedPath.IsEnabled = cmbSavedPaths.SelectedItem != null;
        }

        // --- START: WebView2 Initialization & CSS Injection ---
        private async Task InitializeWebViewAsync()
        {
            try
            {
                Debug.WriteLine("Initializing WebView2...");
                await webViewAiStudio.EnsureCoreWebView2Async(null);
                Debug.WriteLine("WebView2 Core Initialized.");

                if (webViewAiStudio.CoreWebView2 != null)
                {
                    webViewAiStudio.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                    Debug.WriteLine("Attached NavigationCompleted event handler.");
                }
                 else {
                    Debug.WriteLine("Error: CoreWebView2 is null after EnsureCoreWebView2Async completed.");
                    ShowWebViewError("No se pudo inicializar el núcleo de WebView2.");
                 }

                UpdateStatusBarAndButtonStates("WebView AI Studio listo.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebView2 Initialization Error: {ex}");
                MessageBox.Show($"Error inicializando el componente WebView2:\n{ex.Message}\n\nAsegúrese de que el Runtime de WebView2 esté instalado en su sistema.\nPuede descargarlo desde el sitio de Microsoft.",
                    "Error de WebView2", MessageBoxButton.OK, MessageBoxImage.Error);
                ShowWebViewError($"No se pudo cargar WebView2.\nError: {ex.Message}\nAsegúrese de que el runtime de WebView2 esté instalado.");
                UpdateStatusBarAndButtonStates("Error al inicializar WebView2.");
            }
        }

        private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
             if (sender is not CoreWebView2 coreWebView) return;

            if (e.IsSuccess)
            {
                Debug.WriteLine($"Navigation successful to: {coreWebView.Source}");
                await Dispatcher.InvokeAsync(async () =>
                {
                     await InjectCustomCssAsync(webViewAiStudio);
                     UpdateStatusBarAndButtonStates("AI Studio cargado y CSS inyectado.");
                });
            }
            else
            {
                Debug.WriteLine($"Navigation failed: {e.WebErrorStatus} to {coreWebView.Source}");
                 await Dispatcher.InvokeAsync(() => {
                    UpdateStatusBarAndButtonStates($"Error de navegación en AI Studio: {e.WebErrorStatus}");
                    // ShowWebViewError($"No se pudo cargar la página.\nError: {e.WebErrorStatus}");
                 });
            }
        }

        // --- MODIFIED --- InjectCustomCssAsync to use textContent
        private async Task InjectCustomCssAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null)
            {
                Debug.WriteLine("InjectCustomCssAsync: CoreWebView2 not available. Cannot inject CSS.");
                return;
            }

            string css = """
                code {
                    max-height: 100px !important;
                    overflow-y: scroll !important;
                    display: block !important;
                }
                """;

            string escapedCss = JsonSerializer.Serialize(css);

            // --- MODIFICATION --- Use textContent instead of innerHTML
            // This avoids the TrustedHTML violation as textContent is generally safer.
            string script = $$"""
                try {
                    const existingStyle = document.getElementById('makingvibe-custom-code-style');
                    if (!existingStyle) {
                        const style = document.createElement('style');
                        style.id = 'makingvibe-custom-code-style';
                        style.type = 'text/css';
                        style.textContent = {{escapedCss}}; // Use textContent here!
                        document.head.appendChild(style);
                        console.log('MakingVibe: Custom CSS for <code> injected successfully using textContent.');
                    } else {
                         console.log('MakingVibe: Custom CSS for <code> already injected.');
                    }
                } catch (error) {
                    // Log the specific error for easier debugging in browser DevTools
                    console.error('MakingVibe: Error injecting custom CSS:', error.name, error.message, error.stack);
                }
                """;

            try
            {
                Debug.WriteLine("Executing CSS injection script (using textContent)...");
                await webView.ExecuteScriptAsync(script);
                Debug.WriteLine("CSS injection script executed.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error executing CSS injection script: {ex.Message}");
            }
        }

        private void ShowWebViewError(string message)
        {
            Action action = () => {
                var aiStudioTab = tabControlMain.Items.OfType<TabItem>().FirstOrDefault(ti => ti.Header?.ToString() == "AI Studio");
                if (aiStudioTab?.Content is Grid grid)
                {
                    var webView = grid.Children.OfType<WebView2>().FirstOrDefault();
                    if (webView != null) webView.Visibility = Visibility.Collapsed;

                    var existingError = grid.Children.OfType<TextBlock>().FirstOrDefault(tb => tb.Name == "WebViewErrorText");
                    if (existingError != null)
                    {
                        grid.Children.Remove(existingError);
                    }

                    var errorTextBlock = new TextBlock
                    {
                        Name = "WebViewErrorText",
                        Text = message,
                        Foreground = Brushes.Red,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(20)
                    };
                    grid.Children.Add(errorTextBlock);
                }
                else {
                     Debug.WriteLine("ShowWebViewError: Could not find AI Studio TabItem or its Grid content.");
                }
            };

             if (Dispatcher.CheckAccess()) { action(); }
             else { Dispatcher.Invoke(action); }
        }
        // --- END: WebView2 Initialization & CSS Injection ---


        // --- Partial Class Methods (Defined in other files) ---
        // [List of partial methods remains the same]
        // ...

    } // End partial class MainWindow
} // End namespace MakingVibe