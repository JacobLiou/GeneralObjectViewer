using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using ObjectViewer.Core.DataSources;
using ObjectViewer.Core.Graph;
using ObjectViewer.Demo.Controls;
using ObjectViewer.Extensions;
using ObjectViewer.Wpf.Controls;
using ObjectViewer.Wpf.ViewModels;

namespace ObjectViewer.Demo;

/// <summary>
/// Shell window: sample library sidebar + tabbed documents (view/edit/save JSON samples)
/// + external data-source import. Documents keep their viewer instance alive across tab
/// switches so expansion state and sessions are preserved.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<SampleDocument> _documents = [];
    private readonly DataSourceImportService _importService = new();
    private readonly StartPage _startPage = new();

    private IReadOnlyList<SampleFile> _samples = [];
    private ICollectionView? _sampleView;
    private string _samplesFolder = SampleLibrary.DefaultFolder;
    private bool _suppressSelectionSync;

    public MainWindow()
    {
        InitializeComponent();
        TabStrip.ItemsSource = _documents;
        _startPage.SampleRequested += sample => _ = OpenSampleAsync(sample);
        _startPage.OpenFileRequested += OpenExternalFile;
        LoadLibrary();
        ShowStartPage();
        StatusVersionText.Text = $"Object Viewer Studio · v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";
        SetStatus("就绪 — 从左侧示例库选择一个示例开始");
    }

    // ---------- Library ----------

    private void LoadLibrary()
    {
        _samplesFolder = SampleLibrary.DefaultFolder;
        (_samples, var errors) = SampleLibrary.Discover(_samplesFolder);

        _sampleView = CollectionViewSource.GetDefaultView(_samples);
        _sampleView.SortDescriptions.Add(new SortDescription("Manifest.Sort", ListSortDirection.Ascending));
        _sampleView.SortDescriptions.Add(new SortDescription("Manifest.Title", ListSortDirection.Ascending));
        _sampleView.GroupDescriptions.Add(new PropertyGroupDescription("Manifest.Category"));
        _sampleView.Filter = FilterSample;
        SampleList.ItemsSource = _sampleView;
        SampleCountText.Text = $"{_samples.Count} 个示例";
        _startPage.ShowSamples(_sampleView.OfType<SampleFile>().ToList());

        if (errors.Count > 0)
            SetStatus($"部分示例加载失败: {string.Join("; ", errors)}");
    }

    private bool FilterSample(object item)
    {
        if (item is not SampleFile sample)
            return false;
        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(query))
            return true;
        return sample.Manifest.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
               || sample.Manifest.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
               || sample.Manifest.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
               || sample.FileName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        => _sampleView?.Refresh();

    private void OnRefreshLibraryClick(object sender, RoutedEventArgs e)
    {
        LoadLibrary();
        SetStatus("示例库已重新扫描");
    }

    private void OnOpenSamplesFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_samplesFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", _samplesFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"无法打开文件夹: {ex.Message}");
        }
    }

    // ---------- Document lifecycle ----------

    private async Task OpenSampleAsync(SampleFile sample)
        => await OpenSamplePathAsync(sample.FilePath);

    private async Task OpenSamplePathAsync(string path)
    {
        var existing = _documents.FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SetCurrent(existing);
            return;
        }

        SampleDocument document;
        try
        {
            document = SampleLibrary.Load(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"加载示例失败:\n{ex.Message}", "Object Viewer Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await ActivateNewDocumentAsync(document);
        SetStatus($"已加载 {document.Title} · {document.Sample.FileSizeText}");
    }

    private async Task ActivateNewDocumentAsync(SampleDocument document)
    {
        var viewer = new ObjectViewerControl();
        await viewer.SetRootAsync(SampleLibrary.BuildRequest(document));
        HookDirtyTracking(document, viewer);
        document.Viewer = viewer;

        var view = new DocumentView();
        view.Bind(document, viewer);
        document.View = view;

        _documents.Add(document);
        SetCurrent(document);
    }

    private void HookDirtyTracking(SampleDocument document, ObjectViewerControl viewer)
    {
        // A successful inline write publishes the "已写入 <member>" status (FR-122);
        // treat it as the document becoming dirty.
        viewer.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ObjectViewerViewModel.StatusText)
                && viewer.ViewModel.StatusText.StartsWith("已写入", StringComparison.Ordinal))
            {
                document.IsDirty = true;
                if (document.IsCurrent)
                    UpdateDocumentStatus(document);
            }
        };
    }

    private void SetCurrent(SampleDocument? document)
    {
        foreach (var doc in _documents)
            doc.IsCurrent = ReferenceEquals(doc, document);

        if (document is null)
        {
            ShowStartPage();
            return;
        }

        DocumentHost.Content = document.View;
        TitleDocumentText.Text = $"{document.Sample.Manifest.Icon}  {document.Sample.Manifest.Title}";
        SaveButton.IsEnabled = document.CanSave;
        SaveAsButton.IsEnabled = document.CanSave;
        ReloadButton.IsEnabled = true;
        UpdateDocumentStatus(document);
        SyncSidebarSelection(document);
    }

    private void ShowStartPage()
    {
        DocumentHost.Content = _startPage;
        TitleDocumentText.Text = string.Empty;
        StatusDocumentText.Text = string.Empty;
        SaveButton.IsEnabled = false;
        SaveAsButton.IsEnabled = false;
        ReloadButton.IsEnabled = false;
    }

    private void UpdateDocumentStatus(SampleDocument document)
        => StatusDocumentText.Text = document.IsDirty
            ? $"{document.FilePath} · ● 未保存"
            : document.FilePath;

    private void SyncSidebarSelection(SampleDocument document)
    {
        var match = _samples.FirstOrDefault(s => string.Equals(s.FilePath, document.FilePath, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return;
        _suppressSelectionSync = true;
        SampleList.SelectedItem = match;
        SampleList.ScrollIntoView(match);
        _suppressSelectionSync = false;
    }

    private async Task CloseDocumentAsync(SampleDocument document)
    {
        if (document.IsDirty)
        {
            var choice = MessageBox.Show(this,
                $"是否保存对 \"{document.Title}\" 的更改?",
                "Object Viewer Studio",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel)
                return;
            if (choice == MessageBoxResult.Yes)
            {
                await SaveDocumentAsync(document);
                if (document.IsDirty)
                    return; // save failed; keep the document open
            }
        }

        _documents.Remove(document);
        if (document.Viewer?.ViewModel.Session is { } session)
            await session.DisposeAsync();
        SetCurrent(_documents.LastOrDefault());
        SetStatus($"已关闭 {document.Title}");
    }

    // ---------- Save / reload ----------

    private async Task SaveDocumentAsync(SampleDocument document)
    {
        if (!document.CanSave)
        {
            SetStatus("该文档为外部导入,只读浏览");
            return;
        }

        try
        {
            await SampleLibrary.SaveAsync(document);
            document.IsDirty = false;
            if (document.IsCurrent)
                UpdateDocumentStatus(document);
            SetStatus($"已保存 {Path.GetFileName(document.FilePath)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败:\n{ex.Message}", "Object Viewer Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (CurrentDocument is { } document)
            await SaveDocumentAsync(document);
    }

    private async void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        if (CurrentDocument is not { CanSave: true } document)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "另存为示例",
            Filter = "JSON 示例 (*.json)|*.json",
            FileName = document.Title + ".json",
            InitialDirectory = _samplesFolder,
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            await SampleLibrary.SaveAsAsync(document, dialog.FileName);
            document.IsDirty = false;
            if (document.IsCurrent)
                UpdateDocumentStatus(document);
            SetStatus($"已保存到 {dialog.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"另存失败:\n{ex.Message}", "Object Viewer Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnReloadClick(object sender, RoutedEventArgs e)
    {
        if (CurrentDocument is not { } document)
            return;

        if (document.IsDirty)
        {
            var choice = MessageBox.Show(this,
                $"重新加载将丢弃对 \"{document.Title}\" 的未保存更改,继续?",
                "Object Viewer Studio",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes)
                return;
        }

        try
        {
            if (document.IsImported)
                await ImportIntoDocumentAsync(document, document.FilePath);
            else
            {
                var fresh = SampleLibrary.Load(document.FilePath);
                document.Data = fresh.Data;
                document.DataType = fresh.DataType;
                await document.Viewer!.SetRootAsync(SampleLibrary.BuildRequest(document));
            }
            document.IsDirty = false;
            if (document.IsCurrent)
                UpdateDocumentStatus(document);
            SetStatus($"已从磁盘重新加载 {document.Title}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"重新加载失败:\n{ex.Message}", "Object Viewer Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- Sidebar / tabs ----------

    private void OnSampleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionSync)
            return;
        if (SampleList.SelectedItem is SampleFile sample)
            _ = OpenSampleAsync(sample);
    }

    private void OnSampleListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SampleList.SelectedItem is SampleFile sample)
        {
            _ = OpenSampleAsync(sample);
            e.Handled = true;
        }
    }

    private async void OnTabCloseClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SampleDocument document)
        {
            e.Handled = true;
            await CloseDocumentAsync(document);
        }
    }

    private SampleDocument? CurrentDocument
        => _documents.FirstOrDefault(d => d.IsCurrent);

    // ---------- External files ----------

    private void OpenExternalFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开数据源或示例文件",
            Filter = "所有支持的文件 (*.json;*.yaml;*.yml;*.xml;*.csv)|*.json;*.yaml;*.yml;*.xml;*.csv|JSON 示例 (*.json)|*.json|数据源 (*.yaml;*.yml;*.xml;*.csv)|*.yaml;*.yml;*.xml;*.csv|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var path = dialog.FileName;
        if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)
            && SampleLibrary.LooksLikeSample(path))
        {
            _ = OpenSamplePathAsync(path);
            return;
        }

        _ = ImportExternalAsync(path);
    }

    private async Task ImportExternalAsync(string path)
    {
        var document = new SampleDocument
        {
            Sample = new SampleFile
            {
                FilePath = path,
                Manifest = new SampleManifest(
                    Title: Path.GetFileNameWithoutExtension(path),
                    Description: "外部数据源导入(一次性快照)— 只读浏览,不可保存。",
                    Category: "导入",
                    Icon: "📥",
                    Sort: 999,
                    TypeName: string.Empty,
                    DeclaredTypeName: null,
                    RootName: Path.GetFileNameWithoutExtension(path),
                    EditPolicy: EditPolicyMode.ReadOnly,
                    ReferenceMode: ReferenceMode.DetectCyclesOnly,
                    PreserveReferences: false,
                    CollectionPageSize: null),
            },
            Data = null,
            DataType = typeof(object),
            FilePath = path,
            IsImported = true,
        };

        try
        {
            await ImportIntoDocumentAsync(document, path);
        }
        catch (DataSourceException ex)
        {
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await ActivateNewDocumentAsync(document);
    }

    private async Task ImportIntoDocumentAsync(SampleDocument document, string path)
    {
        var result = await _importService.ImportFileAsync(path);
        document.Data = result.Value;
        document.DataType = typeof(object);
        await document.Viewer!.SetRootAsync(new InspectionRequest
        {
            RootName = result.RootName,
            Value = result.Value,
            DeclaredType = result.DeclaredType,
            CustomAdapters = ObjectViewerFormatAdapters.All,
        });
    }

    private void OnOpenFileClick(object sender, RoutedEventArgs e)
        => OpenExternalFile();

    // ---------- Window chrome ----------

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e)
        => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        var maximized = WindowState == WindowState.Maximized;
        RootBorder.Padding = maximized ? new Thickness(7) : new Thickness(0);
        MaximizeGlyph.Text = maximized ? "\uE923" : "\uE922";
    }

    // ---------- Keyboard & closing ----------

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;

        if (e.Key == Key.S)
        {
            if (CurrentDocument is { } document)
                _ = SaveDocumentAsync(document);
            e.Handled = true;
        }
        else if (e.Key == Key.O)
        {
            OpenExternalFile();
            e.Handled = true;
        }
        else if (e.Key == Key.W)
        {
            if (CurrentDocument is { } document)
                _ = CloseDocumentAsync(document);
            e.Handled = true;
        }
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        var dirty = _documents.Where(d => d.IsDirty).ToList();
        if (dirty.Count == 0)
            return;

        var choice = MessageBox.Show(this,
            $"有 {dirty.Count} 个未保存的文档,保存后退出?",
            "Object Viewer Studio",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }

        if (choice != MessageBoxResult.Yes)
            return;

        e.Cancel = true; // re-close after the async saves complete
        foreach (var document in dirty)
            await SaveDocumentAsync(document);

        var stillDirty = _documents.Any(d => d.IsDirty);
        if (stillDirty)
            return; // a save failed; let the user decide what to do

        Close();
    }

    private void SetStatus(string text)
        => StatusText.Text = text;
}
