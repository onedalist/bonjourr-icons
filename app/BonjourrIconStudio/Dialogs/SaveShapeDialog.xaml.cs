using System.Windows;

namespace BonjourrIconStudio.Dialogs;

public partial class SaveShapeDialog : Window
{
    private readonly IReadOnlyCollection<string> _existingNames;

    public string ShapeName => NameTextBox.Text.Trim();

    public SaveShapeDialog(IReadOnlyCollection<string> existingNames)
    {
        InitializeComponent();
        _existingNames = existingNames;
        Loaded += (_, _) => NameTextBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = ShapeName;
        if (string.IsNullOrWhiteSpace(name))
        {
            ValidationText.Text = "Введите название формы.";
            return;
        }

        if (_existingNames.Contains(name, StringComparer.CurrentCultureIgnoreCase))
        {
            ValidationText.Text = "Форма с таким названием уже существует.";
            return;
        }

        DialogResult = true;
    }
}
