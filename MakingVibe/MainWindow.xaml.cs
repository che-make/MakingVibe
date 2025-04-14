/*
 * MakingVibe/MainWindow.xaml.cs
 * Main partial class file.
 * Contains core window logic, service initialization, shared state,
 * event handlers for window lifetime, status bar updates, and command definitions.
 */
using MakingVibe.Models;
using MakingVibe.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media; // For VisualTreeHelper
using CheckBox = System.Windows.Controls.CheckBox; // Explicit alias
using ListViewItem = System.Windows.Controls.ListViewItem;
using MessageBox = System.Windows.MessageBox; // Explicit alias

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
            LoadFragments(); // Load fragments on startup (method in PromptTab partial class)
            listViewFragments.ItemsSource = Fragments;

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
                 else if (!string.IsNullOrEmpty(rootPath))
                 {
                     statusBarText.Text = "Listo.";
                 }
                 else
                 {
                     statusBarText.Text = "Por favor, seleccione una carpeta raíz.";
                 }

                // Update selection count
                statusSelectionCount.Text = $"Seleccionados: {selectedItems.Count}";

                 // Standard Button States
                 bool hasSelection = selectedItems.Count > 0;
                 bool hasSingleSelection = selectedItems.Count == 1;
                 bool canPaste = clipboardItems.Count > 0 && (treeViewFiles.SelectedItem != null || !string.IsNullOrEmpty(rootPath)); // Simplified paste check

                 // Determine if any fragments are selected
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
                     canExpandCurrent = !selectedTvi.IsExpanded && selectedTvi.HasItems;
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

    } // End partial class MainWindow
} // End namespace MakingVibe