using System.Windows;

namespace Torch.Server.Views;

/// <summary>
/// Interaction logic for ImportWorldConfigDialog.xaml
/// </summary>
partial class ImportWorldConfigDialog : Window
{
    public object SelectedWorld => worldList.SelectedItem;

    public ImportWorldConfigDialog()
    {
        InitializeComponent();
    }

    void Button_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
