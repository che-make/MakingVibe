using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;

namespace MakingVibe;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private string rootPath;
    private readonly ObservableCollection<FileSystemItem> selectedItems = new();
    private readonly Dictionary<string, FileSystemItem> pathToItemMap = new();
    private string clipboardPath;
    private bool isCutOperation;

    public MainWindow()
    {
        InitializeComponent();
        listViewSelected.ItemsSource = selectedItems;
    }

    private void btnSelectRoot_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog();
        dialog.Description = "Seleccione la carpeta raíz";
        dialog.ShowNewFolderButton = false;

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            rootPath = dialog.SelectedPath;
            txtCurrentPath.Text = $"Ruta actual: {rootPath}";
            LoadDirectoryTree(rootPath);
        }
    }

    private void LoadDirectoryTree(string path)
    {
        try
        {
            treeViewFiles.Items.Clear();
            pathToItemMap.Clear();
            selectedItems.Clear();

            var rootDirectory = new DirectoryInfo(path);
            var rootItem = CreateTreeViewItemForDirectory(rootDirectory);
            rootItem.IsExpanded = true;
            treeViewFiles.Items.Add(rootItem);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al cargar el directorio: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private TreeViewItem CreateTreeViewItemForDirectory(DirectoryInfo directoryInfo)
    {
        var stackPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        
        var checkbox = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
            Tag = directoryInfo.FullName
        };
        checkbox.Checked += Checkbox_Checked;
        checkbox.Unchecked += Checkbox_Unchecked;
        
        var textBlock = new TextBlock
        {
            Text = directoryInfo.Name,
            VerticalAlignment = VerticalAlignment.Center
        };
        
        stackPanel.Children.Add(checkbox);
        stackPanel.Children.Add(textBlock);

        var treeViewItem = new TreeViewItem
        {
            Header = stackPanel,
            Tag = directoryInfo.FullName
        };

        // Crear un objeto FileSystemItem para este directorio
        var fileSystemItem = new FileSystemItem
        {
            Name = directoryInfo.Name,
            Path = directoryInfo.FullName,
            Type = "Directorio",
            IsDirectory = true
        };
        pathToItemMap[directoryInfo.FullName] = fileSystemItem;

        // Agregar un elemento dummy para permitir la expansión
        treeViewItem.Items.Add("Cargando...");

        treeViewItem.Expanded += TreeViewItem_Expanded;
        treeViewItem.Selected += TreeViewItem_Selected;

        return treeViewItem;
    }

    private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        var treeViewItem = sender as TreeViewItem;
        if (treeViewItem?.Tag == null || treeViewItem.Items.Count != 1 || treeViewItem.Items[0] is not string loadingText || loadingText != "Cargando...")
            return;

        treeViewItem.Items.Clear();
        var directoryPath = treeViewItem.Tag.ToString();

        try
        {
            // Cargar directorios
            foreach (var directoryInfo in new DirectoryInfo(directoryPath).GetDirectories().OrderBy(d => d.Name))
            {
                treeViewItem.Items.Add(CreateTreeViewItemForDirectory(directoryInfo));
            }

            // Cargar archivos
            foreach (var fileInfo in new DirectoryInfo(directoryPath).GetFiles().OrderBy(f => f.Name))
            {
                treeViewItem.Items.Add(CreateTreeViewItemForFile(fileInfo));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al expandir el directorio: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private TreeViewItem CreateTreeViewItemForFile(FileInfo fileInfo)
    {
        var stackPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        
        var checkbox = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
            Tag = fileInfo.FullName
        };
        checkbox.Checked += Checkbox_Checked;
        checkbox.Unchecked += Checkbox_Unchecked;
        
        var textBlock = new TextBlock
        {
            Text = fileInfo.Name,
            VerticalAlignment = VerticalAlignment.Center
        };
        
        stackPanel.Children.Add(checkbox);
        stackPanel.Children.Add(textBlock);

        var treeViewItem = new TreeViewItem
        {
            Header = stackPanel,
            Tag = fileInfo.FullName
        };

        // Crear un objeto FileSystemItem para este archivo
        var fileSystemItem = new FileSystemItem
        {
            Name = fileInfo.Name,
            Path = fileInfo.FullName,
            Type = fileInfo.Extension,
            IsDirectory = false
        };
        pathToItemMap[fileInfo.FullName] = fileSystemItem;

        treeViewItem.Selected += TreeViewItem_Selected;

        return treeViewItem;
    }

    private void Checkbox_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkbox && checkbox.Tag is string path && pathToItemMap.TryGetValue(path, out var item))
        {
            if (!selectedItems.Contains(item))
            {
                selectedItems.Add(item);
            }
            UpdateButtonStates();
        }
    }

    private void Checkbox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkbox && checkbox.Tag is string path && pathToItemMap.TryGetValue(path, out var item))
        {
            selectedItems.Remove(item);
            UpdateButtonStates();
        }
    }

    private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem treeViewItem && treeViewItem.Tag is string path)
        {
            try
            {
                if (File.Exists(path) && IsTextFile(path))
                {
                    string content = File.ReadAllText(path);
                    txtFileContent.Text = content;
                    tabControlMain.SelectedIndex = 0; // Switch to preview tab
                }
                else
                {
                    txtFileContent.Text = $"No se puede previsualizar este tipo de archivo: {path}";
                }
            }
            catch (Exception ex)
            {
                txtFileContent.Text = $"Error al leer el archivo: {ex.Message}";
            }
        }
    }

    private bool IsTextFile(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLower();
        return extension switch
        {
            // Basic text files
            ".txt" or ".log" or ".md" or ".csv" or ".tsv" => true,
        
            // Web development
            ".html" or ".htm" or ".css" or ".js" or ".jsx" or ".ts" or ".tsx" or ".json" or ".xml" => true,
        
            // Configuration files
            ".config" or ".ini" or ".yml" or ".yaml" or ".toml" or ".conf" or ".properties" => true,
        
            // Scripts and shell
            ".bat" or ".cmd" or ".ps1" or ".sh" or ".bash" or ".zsh" or ".fish" => true,
        
            // Programming languages
            ".cs" or ".xaml" or ".java" or ".py" or ".rb" or ".php" or ".c" or ".cpp" or ".h" or ".hpp" or 
                ".go" or ".rs" or ".swift" or ".kt" or ".scala" or ".pl" or ".lua" or ".dart" or ".groovy" or
                ".m" or ".r" or ".sql" or ".vb" => true,
        
            // Other common text formats
            ".gitignore" or ".dockerignore" or ".env" or ".editorconfig" => true,
        
            // Default for unrecognized extensions
            _ => false
        };
    }

    private void UpdateButtonStates()
    {
        bool hasSelection = selectedItems.Count > 0;
        btnCopy.IsEnabled = hasSelection;
        btnCut.IsEnabled = hasSelection;
        btnDelete.IsEnabled = hasSelection;
        btnRename.IsEnabled = selectedItems.Count == 1;
        btnPaste.IsEnabled = !string.IsNullOrEmpty(clipboardPath);
        
        // Enable the Copy Text button only if at least one text file is selected
        btnCopyText.IsEnabled = hasSelection && selectedItems.Any(item => !item.IsDirectory && IsTextFile(item.Path));
    }

    private void btnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItems.Count == 0) return;
        
        // Si hay múltiples elementos, usamos el directorio del primero como referencia
        clipboardPath = selectedItems.First().Path;
        isCutOperation = false;
        btnPaste.IsEnabled = true;
        
        MessageBox.Show($"Se {(selectedItems.Count == 1 ? "ha copiado 1 elemento" : $"han copiado {selectedItems.Count} elementos")} al portapapeles", 
            "Copiar", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private bool BuildMinimalFileMap(string currentDir, Dictionary<string, List<string>> dirToFilesMap, 
    int level, System.Text.StringBuilder builder)
{
    // Skip directories not in our path to any selected file
    bool hasSelectedFileInSubtree = dirToFilesMap.ContainsKey(currentDir);
    
    // Check if any subdirectories contain selected files
    var subdirs = Directory.GetDirectories(currentDir);
    var relevantSubdirs = new List<string>();
    
    foreach (var subdir in subdirs)
    {
        if (BuildMinimalFileMap(subdir, dirToFilesMap, level + 1, new System.Text.StringBuilder()))
        {
            relevantSubdirs.Add(subdir);
            hasSelectedFileInSubtree = true;
        }
    }
    
    // If this directory doesn't contain any selected files directly or in subdirectories, skip it
    if (!hasSelectedFileInSubtree)
        return false;
    
    // This directory is relevant, so add it to the map
    string indent = new string(' ', level * 2);
    string dirName;
    
    if (level == 0)
    {
        // Root directory - use the full path
        dirName = currentDir;
        builder.Append($"{indent}{dirName}");
    }
    else
    {
        // Subdirectory - use just the folder name
        dirName = Path.GetFileName(currentDir);
        builder.Append($"{indent}└── {dirName}");
    }
    
    // Only add newline if we have files or subdirs
    bool hasContent = (dirToFilesMap.ContainsKey(currentDir) && dirToFilesMap[currentDir].Count > 0) || 
                      relevantSubdirs.Count > 0;
    
    if (hasContent)
    {
        builder.AppendLine();
    }
    
    // Add subdirectories recursively
    foreach (var subdir in relevantSubdirs)
    {
        BuildMinimalFileMap(subdir, dirToFilesMap, level + 1, builder);
    }
    
    // Add selected files in this directory
    if (dirToFilesMap.ContainsKey(currentDir))
    {
        foreach (var file in dirToFilesMap[currentDir].OrderBy(f => f))
        {
            builder.AppendLine($"{indent}  ├── {file}");
        }
    }
    
    return true;
}

    private void btnCopyText_Click(object sender, RoutedEventArgs e)
{
    if (selectedItems.Count == 0 || !selectedItems.Any(item => !item.IsDirectory && IsTextFile(item.Path)))
    {
        MessageBox.Show("Seleccione al menos un archivo de texto para copiar su contenido.", 
            "Copiar texto", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }
    
    try
    {
        var fileMapBuilder = new System.Text.StringBuilder();
        var fileContentBuilder = new System.Text.StringBuilder();
        
        // Get only the text files from selection
        var selectedTextFiles = selectedItems
            .Where(i => !i.IsDirectory && IsTextFile(i.Path))
            .ToList();
        
        // Generate file map header with only selected files
        fileMapBuilder.AppendLine("<file_map>");
        
        // Build directory tree of only the selected files
        Dictionary<string, List<string>> dirToFilesMap = new();
        
        // Group files by directory
        foreach (var item in selectedTextFiles)
        {
            string dirPath = Path.GetDirectoryName(item.Path);
            string fileName = Path.GetFileName(item.Path);
            
            if (!dirToFilesMap.ContainsKey(dirPath))
                dirToFilesMap[dirPath] = new List<string>();
                
            dirToFilesMap[dirPath].Add(fileName);
        }
        
        // Build tree structure starting from root
        BuildMinimalFileMap(rootPath, dirToFilesMap, 0, fileMapBuilder);
        
        fileMapBuilder.AppendLine("</file_map>");
        
        // Generate file contents
        fileContentBuilder.AppendLine("<file_contents>");
        
        foreach (var item in selectedTextFiles)
        {
            try
            {
                string content = File.ReadAllText(item.Path);
                string relativePath = item.Path.Replace(rootPath, "").TrimStart('\\', '/');
                
                fileContentBuilder.AppendLine($"File: {relativePath}");
                fileContentBuilder.AppendLine($"");
                fileContentBuilder.AppendLine(content);
                fileContentBuilder.AppendLine($"");
                fileContentBuilder.AppendLine();
            }
            catch (Exception ex)
            {
                fileContentBuilder.AppendLine($"Error reading file {item.Path}: {ex.Message}");
            }
        }
        
        fileContentBuilder.AppendLine("</file_contents>");
        
        // Combine both sections
        var result = fileMapBuilder.ToString() + Environment.NewLine + fileContentBuilder.ToString();
        
        // Copy to clipboard
        System.Windows.Clipboard.SetText(result);
        
        int fileCount = selectedTextFiles.Count;
        MessageBox.Show($"Se ha copiado el contenido de {fileCount} archivo(s) al portapapeles.", 
            "Copiar texto", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Error al copiar el texto: {ex.Message}", 
            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

    private void BuildFileMapStructure(string directory, int level, System.Text.StringBuilder builder)
    {
        string indent = new string(' ', level * 2);
        string dirName = Path.GetFileName(directory);
        
        if (string.IsNullOrEmpty(dirName) && level == 0)
        {
            // Root directory
            builder.Append($"{indent}{directory}");
        }
        else
        {
            builder.Append($"{indent}└── {dirName}");
        }
        
        try
        {
            var dirs = Directory.GetDirectories(directory);
            var files = Directory.GetFiles(directory);
            
            if (dirs.Length > 0 || files.Length > 0)
            {
                builder.AppendLine();
            }
            
            // Add directories
            foreach (var dir in dirs.OrderBy(d => d))
            {
                BuildFileMapStructure(dir, level + 1, builder);
            }
            
            // Add files
            foreach (var file in files.OrderBy(f => f))
            {
                string fileName = Path.GetFileName(file);
                builder.AppendLine($"{indent}  ├── {fileName}");
            }
        }
        catch (Exception)
        {
            // If we can't access the directory, just write a placeholder
            builder.AppendLine(" [acceso denegado]");
        }
    }

    private void btnCut_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItems.Count == 0) return;
        
        clipboardPath = selectedItems.First().Path;
        isCutOperation = true;
        btnPaste.IsEnabled = true;
        
        MessageBox.Show($"Se {(selectedItems.Count == 1 ? "ha cortado 1 elemento" : $"han cortado {selectedItems.Count} elementos")} al portapapeles", 
            "Cortar", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void btnPaste_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(clipboardPath) || treeViewFiles.SelectedItem is not TreeViewItem selectedItem || 
            selectedItem.Tag is not string targetPath)
        {
            MessageBox.Show("Seleccione un directorio destino para pegar", "Pegar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string destinationPath = targetPath;
        
        // Verificar si el destino seleccionado es un archivo
        if (File.Exists(destinationPath))
        {
            // Si es un archivo, usamos su directorio padre
            destinationPath = Path.GetDirectoryName(destinationPath);
        }

        try
        {
            foreach (var item in selectedItems)
            {
                string sourcePath = item.Path;
                string fileName = Path.GetFileName(sourcePath);
                string destFile = Path.Combine(destinationPath, fileName);

                if (item.IsDirectory)
                {
                    // Es un directorio
                    if (Directory.Exists(sourcePath))
                    {
                        if (!Directory.Exists(destFile))
                            CopyDirectory(sourcePath, destFile);
                        
                        if (isCutOperation)
                            Directory.Delete(sourcePath, true);
                    }
                }
                else
                {
                    // Es un archivo
                    if (File.Exists(sourcePath))
                    {
                        File.Copy(sourcePath, destFile, true);
                        
                        if (isCutOperation)
                            File.Delete(sourcePath);
                    }
                }
            }

            if (isCutOperation)
            {
                selectedItems.Clear();
                clipboardPath = null;
                btnPaste.IsEnabled = false;
            }

            // Recargar el árbol
            LoadDirectoryTree(rootPath);
            
            MessageBox.Show("Operación completada con éxito", "Pegar", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al pegar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyDirectory(string sourceDir, string destDir)
    {
        // Crear el directorio destino si no existe
        Directory.CreateDirectory(destDir);

        // Copiar todos los archivos
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        // Copiar subdirectorios recursivamente
        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }

    private void btnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItems.Count == 0) return;

        string message = selectedItems.Count == 1
            ? $"¿Está seguro de que desea eliminar '{selectedItems[0].Name}'?"
            : $"¿Está seguro de que desea eliminar {selectedItems.Count} elementos?";

        if (MessageBox.Show(message, "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            try
            {
                foreach (var item in selectedItems.ToList())
                {
                    if (item.IsDirectory)
                    {
                        if (Directory.Exists(item.Path))
                            Directory.Delete(item.Path, true);
                    }
                    else
                    {
                        if (File.Exists(item.Path))
                            File.Delete(item.Path);
                    }
                }

                selectedItems.Clear();
                LoadDirectoryTree(rootPath);
                MessageBox.Show("Elementos eliminados con éxito", "Eliminar", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al eliminar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void btnRename_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItems.Count != 1) return;

        var item = selectedItems[0];
        
        var dialog = new RenameDialog(Path.GetFileName(item.Path));
        if (dialog.ShowDialog() == true)
        {
            try
            {
                string directory = Path.GetDirectoryName(item.Path);
                string newPath = Path.Combine(directory, dialog.NewName);

                if (item.IsDirectory)
                {
                    Directory.Move(item.Path, newPath);
                }
                else
                {
                    File.Move(item.Path, newPath);
                }

                selectedItems.Clear();
                LoadDirectoryTree(rootPath);
                MessageBox.Show("Elemento renombrado con éxito", "Renombrar", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al renombrar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void btnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(rootPath))
        {
            LoadDirectoryTree(rootPath);
        }
    }
}

// Clase para representar un elemento del sistema de archivos (archivo o directorio)
public class FileSystemItem
{
    public string Name { get; set; }
    public string Path { get; set; }
    public string Type { get; set; }
    public bool IsDirectory { get; set; }
}