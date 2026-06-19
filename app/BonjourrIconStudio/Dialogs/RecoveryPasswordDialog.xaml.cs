using System.Windows;

namespace BonjourrIconStudio.Dialogs;

public partial class RecoveryPasswordDialog : Window
{
    private readonly bool _creating;

    public string Password => PasswordInput.Password;

    public RecoveryPasswordDialog(bool creating)
    {
        InitializeComponent();
        _creating = creating;

        if (creating)
        {
            Title = "Создание защищённого профиля";
            TitleText.Text = "Придумайте пароль восстановления";
            DescriptionText.Text = "Он понадобится только после переустановки Windows или при переносе профиля. Храните его отдельно от файла профиля.";
            PasswordInput.ToolTip = "Минимум 8 символов";
            ConfirmationInput.ToolTip = "Повторите пароль";
        }
        else
        {
            Title = "Восстановление профиля";
            TitleText.Text = "Введите пароль восстановления";
            DescriptionText.Text = "Windows больше не может автоматически открыть этот профиль. После успешного ввода он будет привязан к текущей системе.";
            ConfirmationInput.Visibility = Visibility.Collapsed;
        }

        Loaded += (_, _) => PasswordInput.Focus();
    }

    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (PasswordInput.Password.Length < 8)
        {
            ShowError("Пароль должен содержать не менее 8 символов.");
            return;
        }

        if (_creating && PasswordInput.Password != ConfirmationInput.Password)
        {
            ShowError("Пароли не совпадают.");
            return;
        }

        DialogResult = true;
    }
}
