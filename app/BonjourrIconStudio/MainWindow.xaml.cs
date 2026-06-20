using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using BonjourrIconStudio.Dialogs;
using BonjourrIconStudio.Helpers;
using BonjourrIconStudio.Models;
using BonjourrIconStudio.Services;
using Microsoft.Win32;

namespace BonjourrIconStudio;

public partial class MainWindow : Window
{
    private const double PreviewSize = 640d;
    private const double GlassRimAt128 = 4d;
    private const double GlassOpticalDepthAt128 = 12d;
    private const double GlassMagnification = 1.078d;
    private readonly SettingsService _settingsService = new();
    private readonly TokenVaultService _vaultService = new();
    private readonly ImageProcessor _imageProcessor = new();
    private readonly GitHubService _gitHubService = new();
    private AppSettings _settings;
    private LoadedImage? _loadedImage;
    private string? _sourcePath;
    private string? _currentToken;
    private double _baseScale;
    private double _offsetX;
    private double _offsetY;
    private Point _dragStart;
    private double _dragStartOffsetX;
    private double _dragStartOffsetY;
    private bool _isDragging;
    private bool _updatingGlassUi;

    public ObservableCollection<UploadItem> UploadItems { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        PortablePaths.EnsureFolders();
        _settings = _settingsService.Load();
        ApplySettingsToUi();
        InitializeGlassControls();
        InitializeMask();
        RefreshTokenState();

        SourceInitialized += EnableDarkTitleBar;
    }

    private void InitializeMask()
    {
        var geometry = SquircleGeometry.Create(PreviewSize, PreviewSize);
        MaskedCanvas.Clip = geometry;
        MaskOutline.Data = geometry;
        var opticalDepth = GlassOpticalDepthAt128 * PreviewSize / 128d;
        var innerSize = PreviewSize - opticalDepth * 2d;
        var innerGeometry = SquircleGeometry.Create(innerSize, innerSize).Clone();
        innerGeometry.Transform = new TranslateTransform(opticalDepth, opticalDepth);
        innerGeometry.Freeze();
        var refractionRing = new CombinedGeometry(GeometryCombineMode.Exclude, geometry, innerGeometry);
        refractionRing.Freeze();
        LiquidGlassRefractionCanvas.Clip = refractionRing;
        LiquidGlassBodyOutline.Data = geometry;
        LiquidGlassBodyOutline.Clip = geometry;
        LiquidGlassSpecularOutline.Data = geometry;
        LiquidGlassSpecularOutline.Clip = geometry;
        UpdateLiquidGlassPreview();
    }

    private void InitializeGlassControls()
    {
        _updatingGlassUi = true;
        try
        {
            LiquidGlassCheckBox.IsChecked = _settings.LiquidGlassEnabled;
            LightGlassRadioButton.IsChecked = _settings.LiquidGlassVariant == LiquidGlassVariant.Light;
            DarkGlassRadioButton.IsChecked = _settings.LiquidGlassVariant == LiquidGlassVariant.Dark;
        }
        finally
        {
            _updatingGlassUi = false;
        }

        UpdateLiquidGlassUiState();
    }

    private void LiquidGlassCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _settings is null || _updatingGlassUi) return;
        _settings.LiquidGlassEnabled = LiquidGlassCheckBox.IsChecked == true;
        UpdateLiquidGlassUiState();
        UpdateLiquidGlassPreview();
        _settingsService.Save(_settings);
    }

    private void LiquidGlassVariant_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _settings is null || _updatingGlassUi ||
            sender is not RadioButton { IsChecked: true, Tag: string variantName } ||
            !Enum.TryParse<LiquidGlassVariant>(variantName, out var variant))
            return;

        _settings.LiquidGlassVariant = variant;
        UpdateLiquidGlassPreview();
        UpdateImageVisual();
        _settingsService.Save(_settings);
    }

    private void UpdateLiquidGlassUiState()
    {
        var enabled = LiquidGlassCheckBox.IsChecked == true;
        LiquidGlassOptionsPanel.IsEnabled = enabled;
    }

    private void UpdateLiquidGlassPreview()
    {
        if (LiquidGlassBodyOutline is null || _settings is null) return;
        var enabled = _settings.LiquidGlassEnabled;
        LiquidGlassRefractionCanvas.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        LiquidGlassBodyOutline.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        LiquidGlassSpecularOutline.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        MaskOutline.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;

        var previewScale = PreviewSize / 128d;
        LiquidGlassBodyOutline.StrokeThickness = GlassRimAt128 * previewScale * 2d;
        const double specularWidthAt128 = 0.9d;
        LiquidGlassSpecularOutline.StrokeThickness = specularWidthAt128 * previewScale * 2d;
        RefractionPreviewImage.Opacity = _settings.LiquidGlassVariant == LiquidGlassVariant.Light ? 0.78d : 0.66d;

        if (_settings.LiquidGlassVariant == LiquidGlassVariant.Light)
        {
            LiquidGlassBodyOutline.Stroke = CreateGlassBrush(
                (Color.FromArgb(215, 255, 255, 255), 0d),
                (Color.FromArgb(130, 218, 246, 255), 0.36d),
                (Color.FromArgb(72, 84, 101, 118), 0.76d),
                (Color.FromArgb(170, 25, 30, 38), 1d));
            LiquidGlassSpecularOutline.Stroke = CreateGlassBrush(
                (Color.FromArgb(255, 255, 255, 255), 0d),
                (Color.FromArgb(215, 222, 249, 255), 0.4d),
                (Color.FromArgb(45, 255, 255, 255), 0.72d),
                (Color.FromArgb(125, 18, 22, 29), 1d));
        }
        else
        {
            LiquidGlassBodyOutline.Stroke = CreateGlassBrush(
                (Color.FromArgb(175, 220, 245, 255), 0d),
                (Color.FromArgb(180, 67, 80, 96), 0.34d),
                (Color.FromArgb(220, 16, 22, 30), 0.74d),
                (Color.FromArgb(238, 4, 7, 11), 1d));
            LiquidGlassSpecularOutline.Stroke = CreateGlassBrush(
                (Color.FromArgb(255, 255, 255, 255), 0d),
                (Color.FromArgb(185, 211, 242, 255), 0.4d),
                (Color.FromArgb(48, 53, 66, 81), 0.72d),
                (Color.FromArgb(190, 0, 0, 0), 1d));
        }
    }

    private static LinearGradientBrush CreateGlassBrush(params (Color Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        foreach (var (color, offset) in stops) brush.GradientStops.Add(new GradientStop(color, offset));
        brush.Freeze();
        return brush;
    }

    private void ApplySettingsToUi()
    {
        OwnerTextBox.Text = _settings.RepositoryOwner;
        RepoTextBox.Text = _settings.RepositoryName;
        BranchTextBox.Text = _settings.Branch;
        RepoFolderTextBox.Text = _settings.RepositoryFolder;
        ExportFolderTextBox.Text = _settings.ExportFolder;
        UpdateRepositorySummary();
    }

    private void UpdateRepositorySummary()
    {
        RepoSummaryText.Text = $"{_settings.RepositoryOwner}/{_settings.RepositoryName}\n{_settings.Branch} → /{_settings.RepositoryFolder.Trim('/')}";
    }

    private void RefreshTokenState()
    {
        if (_vaultService.TryLoadWithWindows(out var token))
        {
            _currentToken = token;
            VaultStatusText.Text = "Токен сохранён и разблокирован Windows";
            VaultStatusText.Foreground = (Brush)FindResource("AccentBrush");
            TokenSummaryText.Text = "Токен готов к использованию";
        }
        else if (_vaultService.HasSavedToken)
        {
            _currentToken = null;
            VaultStatusText.Text = "Профиль найден — требуется пароль восстановления";
            VaultStatusText.Foreground = (Brush)FindResource("DangerBrush");
            TokenSummaryText.Text = "Требуется разблокировать профиль в настройках";
        }
        else
        {
            _currentToken = null;
            VaultStatusText.Text = "Токен не сохранён";
            VaultStatusText.Foreground = (Brush)FindResource("MutedTextBrush");
            TokenSummaryText.Text = "Сначала сохраните GitHub-токен в настройках";
        }
    }

    private void ShowEditor_Click(object sender, RoutedEventArgs e) => ShowSection(EditorPanel, EditorNavButton);
    private void ShowPublish_Click(object sender, RoutedEventArgs e) => ShowSection(PublishPanel, PublishNavButton);
    private void ShowSettings_Click(object sender, RoutedEventArgs e) => ShowSection(SettingsPanel, SettingsNavButton);

    private void ShowSection(UIElement panel, System.Windows.Controls.Button activeButton)
    {
        EditorPanel.Visibility = Visibility.Collapsed;
        PublishPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        EditorNavButton.Background = Brushes.Transparent;
        PublishNavButton.Background = Brushes.Transparent;
        SettingsNavButton.Background = Brushes.Transparent;
        panel.Visibility = Visibility.Visible;
        activeButton.Background = new SolidColorBrush(Color.FromRgb(29, 42, 50));
    }

    private void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите изображение",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.webp;*.avif;*.bmp;*.tif;*.tiff|Все файлы|*.*"
        };
        if (dialog.ShowDialog(this) == true) LoadImage(dialog.FileName);
    }

    private void EditorStage_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void EditorStage_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            LoadImage(files[0]);
    }

    private void LoadImage(string path)
    {
        try
        {
            EditorStatusText.Text = "Загружаю изображение…";
            var loaded = _imageProcessor.LoadForEditing(path);
            _loadedImage = loaded;
            _sourcePath = path;
            PreviewImage.Source = loaded.Preview;
            DimmedImage.Source = loaded.Preview;
            RefractionPreviewImage.Source = loaded.Preview;
            DropHint.Visibility = Visibility.Collapsed;
            ExportButton.IsEnabled = true;
            ImageInfoText.Text = $"{Path.GetFileName(path)}  ·  {loaded.Width} × {loaded.Height} px";
            BaseNameTextBox.Text = SuggestBaseName(path);
            ResetImagePosition();
            EditorStatusText.Text = "Перетаскивайте изображение мышью и масштабируйте колёсиком";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось открыть изображение.\n\n{ex.Message}", "Bonjourr Icon Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            EditorStatusText.Text = "Ошибка загрузки изображения";
        }
    }

    private void ResetImagePosition()
    {
        if (_loadedImage is null) return;
        _baseScale = Math.Max(PreviewSize / _loadedImage.Width, PreviewSize / _loadedImage.Height);
        _offsetX = 0;
        _offsetY = 0;
        ZoomSlider.Value = 1;
        UpdateImageVisual();
    }

    private void UpdateImageVisual()
    {
        if (_loadedImage is null) return;

        var scale = _baseScale * ZoomSlider.Value;
        var width = _loadedImage.Width * scale;
        var height = _loadedImage.Height * scale;
        var maxX = Math.Max(0, (width - PreviewSize) / 2d);
        var maxY = Math.Max(0, (height - PreviewSize) / 2d);
        _offsetX = Math.Clamp(_offsetX, -maxX, maxX);
        _offsetY = Math.Clamp(_offsetY, -maxY, maxY);
        var left = (PreviewSize - width) / 2d + _offsetX;
        var top = (PreviewSize - height) / 2d + _offsetY;

        foreach (var image in new[] { PreviewImage, DimmedImage })
        {
            image.Width = width;
            image.Height = height;
            System.Windows.Controls.Canvas.SetLeft(image, left);
            System.Windows.Controls.Canvas.SetTop(image, top);
        }

        var refractedWidth = width * GlassMagnification;
        var refractedHeight = height * GlassMagnification;
        var refractedLeft = PreviewSize / 2d + (left - PreviewSize / 2d) * GlassMagnification;
        var refractedTop = PreviewSize / 2d + (top - PreviewSize / 2d) * GlassMagnification;
        RefractionPreviewImage.Width = refractedWidth;
        RefractionPreviewImage.Height = refractedHeight;
        System.Windows.Controls.Canvas.SetLeft(RefractionPreviewImage, refractedLeft);
        System.Windows.Controls.Canvas.SetTop(RefractionPreviewImage, refractedTop);

        ZoomValueText.Text = $"{ZoomSlider.Value * 100:0}%";
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized) return;
        UpdateImageVisual();
    }

    private void EditorStage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_loadedImage is null) return;
        ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + (e.Delta > 0 ? 0.1 : -0.1), ZoomSlider.Minimum, ZoomSlider.Maximum);
        e.Handled = true;
    }

    private void EditorStage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_loadedImage is null) return;
        _isDragging = true;
        _dragStart = e.GetPosition(EditorStage);
        _dragStartOffsetX = _offsetX;
        _dragStartOffsetY = _offsetY;
        EditorStage.CaptureMouse();
        EditorStage.Cursor = Cursors.SizeAll;
    }

    private void EditorStage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var position = e.GetPosition(EditorStage);
        _offsetX = _dragStartOffsetX + position.X - _dragStart.X;
        _offsetY = _dragStartOffsetY + position.Y - _dragStart.Y;
        UpdateImageVisual();
    }

    private void EditorStage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => StopDragging();
    private void EditorStage_MouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) StopDragging();
    }

    private void StopDragging()
    {
        _isDragging = false;
        EditorStage.ReleaseMouseCapture();
        EditorStage.Cursor = Cursors.Arrow;
    }

    private void FitImage_Click(object sender, RoutedEventArgs e) => ResetImagePosition();
    private void ResetImage_Click(object sender, RoutedEventArgs e) => ResetImagePosition();

    private void ChooseExportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Папка для готовых иконок",
            InitialDirectory = Directory.Exists(ExportFolderTextBox.Text) ? ExportFolderTextBox.Text : PortablePaths.DefaultExportFolder
        };
        if (dialog.ShowDialog(this) != true) return;
        ExportFolderTextBox.Text = dialog.FolderName;
        _settings.ExportFolder = dialog.FolderName;
        _settingsService.Save(_settings);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedImage is null || _sourcePath is null) return;
        var sizes = new List<int>();
        if (Size128CheckBox.IsChecked == true) sizes.Add(128);
        if (Size256CheckBox.IsChecked == true) sizes.Add(256);
        if (Size512CheckBox.IsChecked == true) sizes.Add(512);

        var formats = new List<IconFormat>();
        if (PngCheckBox.IsChecked == true) formats.Add(IconFormat.Png);
        if (WebPCheckBox.IsChecked == true) formats.Add(IconFormat.WebP);
        if (AvifCheckBox.IsChecked == true) formats.Add(IconFormat.Avif);

        var baseName = SanitizeFileName(BaseNameTextBox.Text);
        if (sizes.Count == 0 || formats.Count == 0 || string.IsNullOrWhiteSpace(baseName))
        {
            MessageBox.Show(this, "Выберите хотя бы один размер и формат и укажите название файла.", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ExportButton.IsEnabled = false;
        try
        {
            var crop = new CropState(
                _sourcePath,
                _loadedImage.Width,
                _loadedImage.Height,
                PreviewSize,
                _baseScale * ZoomSlider.Value,
                _offsetX,
                _offsetY);
            var request = new ExportRequest(
                crop,
                ExportFolderTextBox.Text,
                baseName,
                sizes,
                formats,
                _settings.WebPQuality,
                _settings.AvifQuality,
                _settings.LiquidGlassEnabled,
                _settings.LiquidGlassVariant);
            var progress = new Progress<string>(message => EditorStatusText.Text = message);
            var files = await _imageProcessor.ExportAsync(request, progress);

            foreach (var file in files)
            {
                var existing = UploadItems.FirstOrDefault(item => string.Equals(item.LocalPath, file, StringComparison.OrdinalIgnoreCase));
                if (existing is null) UploadItems.Add(new UploadItem { LocalPath = file });
                else existing.Status = "Экспортирован заново";
            }

            EditorStatusText.Text = $"Готово: создано файлов — {files.Count}. Они добавлены в очередь публикации.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось экспортировать изображение.\n\n{ex.Message}", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Error);
            EditorStatusText.Text = "Ошибка экспорта";
        }
        finally
        {
            ExportButton.IsEnabled = true;
        }
    }

    private void AddReadyFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Добавить готовые изображения",
            Filter = "Изображения|*.png;*.webp;*.avif;*.jpg;*.jpeg|Все файлы|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;

        foreach (var file in dialog.FileNames)
        {
            if (UploadItems.All(item => !string.Equals(item.LocalPath, file, StringComparison.OrdinalIgnoreCase)))
                UploadItems.Add(new UploadItem { LocalPath = file });
        }
        PublishStatusText.Text = $"В очереди файлов: {UploadItems.Count}";
    }

    private void RemoveSelectedFiles_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in UploadItems.Where(item => item.IsSelected).ToList()) UploadItems.Remove(item);
        PublishStatusText.Text = UploadItems.Count == 0 ? "Файлы не выбраны" : $"В очереди файлов: {UploadItems.Count}";
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (_currentToken is null)
        {
            if (_vaultService.HasSavedToken)
            {
                var dialog = new RecoveryPasswordDialog(false) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    _currentToken = await _vaultService.UnlockWithRecoveryPasswordAsync(dialog.Password);
                    if (_currentToken is null)
                    {
                        MessageBox.Show(this, "Неверный восстановительный пароль.", "GitHub", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    RefreshTokenState();
                }
                else return;
            }
            else
            {
                MessageBox.Show(this, "Сначала сохраните GitHub-токен в настройках.", "GitHub", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowSection(SettingsPanel, SettingsNavButton);
                return;
            }
        }

        var selected = UploadItems.Where(item => item.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Выберите хотя бы один файл.", "GitHub", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        UploadButton.IsEnabled = false;
        var successCount = 0;
        try
        {
            foreach (var item in selected)
            {
                item.Status = "Загрузка…";
                PublishStatusText.Text = $"Загружаю {item.FileName}…";
                var result = await _gitHubService.UploadAsync(_currentToken!, _settings, item.LocalPath, OverwriteCheckBox.IsChecked == true);
                item.Status = result.Success ? result.PublicUrl ?? "Загружено" : result.Message;
                if (result.Success)
                {
                    successCount++;
                    if (selected.Count == 1 && result.PublicUrl is not null) Clipboard.SetText(result.PublicUrl);
                }
            }

            PublishStatusText.Text = selected.Count == 1 && successCount == 1
                ? "Загружено. Ссылка скопирована в буфер обмена."
                : $"Загружено файлов: {successCount} из {selected.Count}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Ошибка подключения к GitHub.\n\n{ex.Message}", "GitHub", MessageBoxButton.OK, MessageBoxImage.Error);
            PublishStatusText.Text = "Ошибка загрузки";
        }
        finally
        {
            UploadButton.IsEnabled = true;
        }
    }

    private void SaveRepositorySettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.RepositoryOwner = OwnerTextBox.Text.Trim();
        _settings.RepositoryName = RepoTextBox.Text.Trim();
        _settings.Branch = BranchTextBox.Text.Trim();
        _settings.RepositoryFolder = RepoFolderTextBox.Text.Trim().Trim('/');
        _settingsService.Save(_settings);
        UpdateRepositorySummary();
        VaultStatusText.Text = "Настройки репозитория сохранены";
    }

    private async void TestToken_Click(object sender, RoutedEventArgs e)
    {
        var token = string.IsNullOrWhiteSpace(TokenPasswordBox.Password) ? _currentToken : TokenPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(token))
        {
            VaultStatusText.Text = "Введите токен или разблокируйте сохранённый профиль";
            return;
        }

        VaultStatusText.Text = "Проверяю подключение…";
        try
        {
            var result = await _gitHubService.TestConnectionAsync(token, ReadRepositorySettingsFromUi());
            VaultStatusText.Text = result.Message;
            VaultStatusText.Foreground = (Brush)FindResource(result.Success ? "AccentBrush" : "DangerBrush");
        }
        catch (Exception ex)
        {
            VaultStatusText.Text = ex.Message;
            VaultStatusText.Foreground = (Brush)FindResource("DangerBrush");
        }
    }

    private async void SaveToken_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TokenPasswordBox.Password))
        {
            VaultStatusText.Text = "Вставьте новый GitHub-токен";
            return;
        }

        var passwordDialog = new RecoveryPasswordDialog(true) { Owner = this };
        if (passwordDialog.ShowDialog() != true) return;

        try
        {
            await _vaultService.SaveTokenAsync(TokenPasswordBox.Password, passwordDialog.Password);
            TokenPasswordBox.Clear();
            RefreshTokenState();
        }
        catch (Exception ex)
        {
            VaultStatusText.Text = $"Не удалось сохранить токен: {ex.Message}";
            VaultStatusText.Foreground = (Brush)FindResource("DangerBrush");
        }
    }

    private void DeleteToken_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Удалить сохранённый токен и защищённый профиль?", "GitHub-токен", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _vaultService.DeleteToken();
        _currentToken = null;
        RefreshTokenState();
    }

    private void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!_vaultService.HasSavedToken)
        {
            MessageBox.Show(this, "Сначала сохраните GitHub-токен.", "Профиль", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Сохранить защищённый профиль",
            Filter = "Профиль Bonjourr Icon Studio|*.bisp",
            FileName = "bonjourr-icon-studio-profile.bisp"
        };
        if (dialog.ShowDialog(this) != true) return;
        _vaultService.ExportProfile(dialog.FileName);
        VaultStatusText.Text = "Защищённый профиль экспортирован";
    }

    private async void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите защищённый профиль",
            Filter = "Профиль Bonjourr Icon Studio|*.bisp|Все файлы|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            _vaultService.ImportProfile(dialog.FileName);
            var passwordDialog = new RecoveryPasswordDialog(false) { Owner = this };
            if (passwordDialog.ShowDialog() == true)
            {
                _currentToken = await _vaultService.UnlockWithRecoveryPasswordAsync(passwordDialog.Password);
                if (_currentToken is null)
                {
                    VaultStatusText.Text = "Неверный пароль восстановления";
                    VaultStatusText.Foreground = (Brush)FindResource("DangerBrush");
                }
                else RefreshTokenState();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось импортировать профиль.\n\n{ex.Message}", "Профиль", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private AppSettings ReadRepositorySettingsFromUi() => new()
    {
        RepositoryOwner = OwnerTextBox.Text.Trim(),
        RepositoryName = RepoTextBox.Text.Trim(),
        Branch = BranchTextBox.Text.Trim(),
        RepositoryFolder = RepoFolderTextBox.Text.Trim().Trim('/'),
        ExportFolder = _settings.ExportFolder,
        WebPQuality = _settings.WebPQuality,
        AvifQuality = _settings.AvifQuality
    };

    private static string SuggestBaseName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var suffix in new[] { "_128", "_256", "_512" })
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name[..^suffix.Length];
        return SanitizeFileName(name);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(character => invalid.Contains(character) || char.IsWhiteSpace(character) ? '_' : character).ToArray());
        while (cleaned.Contains("__", StringComparison.Ordinal)) cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        return cleaned.Trim('_');
    }

    private void EnableDarkTitleBar(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}
