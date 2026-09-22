using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OhMyHarness.Core;
using SkiaSharp;
using Windows.Storage.Pickers;
using static OhMyHarness.App.UiText;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    readonly Image brandLogo = new() { Width = 28, Height = 28, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock brandName = new() { Text = BrandingAssets.DefaultName, FontSize = 20, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Foreground = FluentDesign.Primary };
    string DisplayApplicationName => BrandingAssets.DisplayName(FeatureSettings.Read(state.FeaturesJson).ApplicationName);
    static string DefaultLogo => Path.Combine(AppContext.BaseDirectory, "Assets", "logo-32.png");
    void ApplyBrandingIcon(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        var custom = Path.Combine(PortableStorage.Root, "branding", "window-icon.ico");
        var path = !string.IsNullOrWhiteSpace(FeatureSettings.Read(state.FeaturesJson).LogoPath) && File.Exists(custom)
            ? custom : Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        try { if (File.Exists(path)) window.AppWindow.SetIcon(path); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Window icon: " + ex.Message); }
    }

    static byte[] ReadLogo(string path)
    {
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > 10 * 1024 * 1024) throw new IOException("Logo : fichier vide ou supérieur à 10 Mo. / Empty logo or larger than 10 MB.");
        var data = File.ReadAllBytes(path);
        using var codec = SKCodec.Create(new MemoryStream(data));
        if (codec == null || codec.Info.Width is <= 0 or > 4096 || codec.Info.Height is <= 0 or > 4096)
            throw new IOException("Image invalide ou supérieure à 4096 × 4096 pixels. / Invalid image or larger than 4096 × 4096 pixels.");
        return data;
    }
    static byte[] LogoPng(byte[] bytes)
    {
        using var bitmap = SKBitmap.Decode(bytes) ?? throw new IOException("Logo invalide / Invalid logo.");
        var scale = Math.Min(1d, 256d / Math.Max(bitmap.Width, bitmap.Height));
        using var resized = bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear));
        using var image = SKImage.FromBitmap(resized); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    static BitmapImage LogoImage(byte[] png)
    {
        var image = new BitmapImage();
        using var stream = new MemoryStream(png); using var source = stream.AsRandomAccessStream(); image.SetSource(source);
        return image;
    }
    void ApplyBranding()
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        Title = brandName.Text = DisplayApplicationName;
        ToolTipService.SetToolTip(brandName, DisplayApplicationName);
        if (settingsWindow != null) settingsWindow.Title = DisplayApplicationName + " · " + T("Réglages");
        if (tasksWindow != null) tasksWindow.Title = DisplayApplicationName + " · " + WorkflowText("Tâches planifiées", "Scheduled tasks");
        byte[]? png = null;
        try { png = LogoPng(ReadLogo(string.IsNullOrWhiteSpace(config.LogoPath) ? DefaultLogo : BrandingAssets.Resolve(config.LogoPath))); ToolTipService.SetToolTip(brandLogo, DisplayApplicationName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            try { png = LogoPng(ReadLogo(DefaultLogo)); } catch { }
            ToolTipService.SetToolTip(brandLogo, WorkflowText("Logo indisponible : ", "Logo unavailable: ") + ex.Message);
        }
        brandLogo.Source = png == null ? null : LogoImage(png);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
                if (config.LogoPath.Length > 0 && png != null)
                {
                    var folder = Path.Combine(PortableStorage.Root, "branding"); Directory.CreateDirectory(folder);
                    icon = Path.Combine(folder, "window-icon.ico");
                    using var output = File.Create(icon); using var writer = new BinaryWriter(output);
                    using var bitmap = SKBitmap.Decode(png);
                    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
                    writer.Write((byte)(bitmap.Width == 256 ? 0 : bitmap.Width)); writer.Write((byte)(bitmap.Height == 256 ? 0 : bitmap.Height));
                    writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                    writer.Write(png.Length); writer.Write(22); writer.Write(png);
                }
                if (File.Exists(icon)) { AppWindow.SetIcon(icon); settingsWindow?.AppWindow.SetIcon(icon); tasksWindow?.AppWindow.SetIcon(icon); }
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Window icon: " + ex.Message); }
        }
    }
    (StackPanel Panel, Func<FeatureSettings, Task> Save) BuildBrandingSettings()
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        var name = new TextBox { Header = WorkflowText("Nom affiché de l’application", "Application display name"), Text = config.ApplicationName, MaxLength = 80, PlaceholderText = BrandingAssets.DefaultName };
        var path = new TextBox { Header = WorkflowText("Logo personnalisé", "Custom logo"), Text = config.LogoPath, IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
        var preview = new Image { Width = 64, Height = 64, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        var error = Label("", 12); error.Tag = null;
        var choose = new Button { Content = WorkflowText("Charger un logo…", "Choose logo…") };
        var reset = new Button { Content = WorkflowText("Logo d’origine", "Default logo") };
        var defaults = new Button { Content = WorkflowText("Rétablir le nom et le logo", "Reset name and logo") };
        string? selectedFile = null; byte[]? selectedData = null;
        void Preview(string value)
        {
            try { preview.Source = LogoImage(LogoPng(ReadLogo(value))); error.Text = ""; }
            catch (Exception ex) { preview.Source = null; error.Text = ex.Message; }
        }
        try { Preview(config.LogoPath.Length == 0 ? DefaultLogo : BrandingAssets.Resolve(config.LogoPath)); }
        catch (ArgumentException ex) { error.Text = ex.Message; }
        choose.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileOpenPicker(); foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".ico" }) picker.FileTypeFilter.Add(extension);
                InitializePicker(picker, settingsWindow ?? this);
                var file = await picker.PickSingleFileAsync(); if (file == null) return;
                var data = ReadLogo(file.Path); var image = LogoImage(LogoPng(data));
                selectedFile = file.Path; selectedData = data; preview.Source = image; error.Text = "";
                path.Text = BrandingAssets.RelativePath(file.Path) ?? WorkflowText("Copie dans branding/ à l’enregistrement · ", "Copy to branding/ on save · ") + Path.GetFileName(file.Path);
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        void ResetLogo() { selectedFile = null; selectedData = null; path.Text = ""; Preview(DefaultLogo); }
        reset.Click += (_, _) => ResetLogo();
        defaults.Click += (_, _) => { name.Text = BrandingAssets.DefaultName; ResetLogo(); };
        var details = new StackPanel { Spacing = 12 };
        foreach (var item in new UIElement[] { name, path, preview, Row(choose, reset), defaults, error,
            Label(WorkflowText("PNG, JPEG, WebP, BMP ou ICO · 10 Mo maximum. Les logos externes sont copiés dans branding/. Les chemins sont relatifs au dossier de l’exécutable : copiez le dossier complet pour conserver votre personnalisation.", "PNG, JPEG, WebP, BMP or ICO · Maximum 10 MB. External logos are copied into branding/. Paths are relative to the executable folder: copy the whole folder to keep your customization."), 12) }) details.Children.Add(item);
        var panel = new StackPanel(); panel.Children.Add(new Expander { Header = WorkflowText("Nom et logo de l’application", "Application name and logo"), Content = details, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        return (panel, async value =>
        {
            value.ApplicationName = BrandingAssets.DisplayName(name.Text);
            value.LogoPath = selectedFile != null && selectedData != null ? await BrandingAssets.SaveLogoAsync(selectedFile, selectedData) : path.Text;
        });
    }
}
