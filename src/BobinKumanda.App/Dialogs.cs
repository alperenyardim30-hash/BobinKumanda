using System.Windows;

namespace BobinKumanda.App;

/// <summary>
/// İleti kutuları. Betikle arayüz denemesinde (<see cref="Quiet"/>) kutu gösterilmez: deneme kullanıcının ekranında
/// beklemede kalmasın diye ileti günlüğe yazılır ve önceden belirlenen cevap döner.
/// </summary>
internal static class Dialogs
{
    public static bool Quiet { get; set; }

    /// <summary>Gösterilmeyen uyarı ve hata iletileri (betik günlüğüne yazılır).</summary>
    public static List<string> Suppressed { get; } = [];

    public static MessageBoxResult Show(string text, MessageBoxButton buttons, MessageBoxImage image,
        Window? owner = null, MessageBoxResult quietAnswer = MessageBoxResult.OK)
    {
        if (Quiet)
        {
            if (image is MessageBoxImage.Warning or MessageBoxImage.Error)
            {
                Suppressed.Add(text.Replace('\n', ' '));
            }
            return quietAnswer;
        }
        return owner != null
            ? MessageBox.Show(owner, text, App.ProductName, buttons, image)
            : MessageBox.Show(text, App.ProductName, buttons, image);
    }
}
